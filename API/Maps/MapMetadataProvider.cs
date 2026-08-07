using System.Text.Json;
using System.Text.Json.Serialization;
using BZAPI.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BZAPI.Maps;

public sealed record BZ98MapMetadata(
    string MapFile,
    string ModId,
    bool IsStock,
    string? Title,
    string? ImageUrl,
    string? Description,
    int? MinPlayers,
    int? MaxPlayers,
    string? TypeCode,
    string? TypeLabel,
    string? ModeCode,
    string? ModeLabel,
    string? CustomTypeCode,
    string? CustomTypeName);

public interface IMapMetadataProvider
{
    /// <summary>
    /// Resolves public metadata for a map/mod pair. Missing data and provider failures return null
    /// and are negatively cached so map enrichment can never become a critical dependency.
    /// </summary>
    Task<BZ98MapMetadata?> GetMapAsync(
        string mapFile,
        string modId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Uses the public BZ98R map metadata service consumed by MultiplayerSessionList. The service knows
/// stock map IDs (mod 0) as well as many Workshop/custom maps and supplies the display title,
/// preview image, player limits, and actual map game mode that the Rebellion lobby payload omits.
/// </summary>
public sealed class MapMetadataProvider : IMapMetadataProvider
{
    private const string StockModId = "0";

    private readonly IMemoryCache _cache;
    private readonly HttpClient _httpClient;
    private readonly MapMetadataOptions _options;
    private readonly ILogger<MapMetadataProvider> _logger;

    public MapMetadataProvider(
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IOptions<MapMetadataOptions> options,
        ILogger<MapMetadataProvider> logger)
    {
        _cache = cache;
        _httpClient = httpClientFactory.CreateClient(nameof(MapMetadataProvider));
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BZ98MapMetadata?> GetMapAsync(
        string mapFile,
        string modId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedMap = mapFile.Trim();
        var normalizedMod = string.IsNullOrWhiteSpace(modId) ? StockModId : modId.Trim();
        if (normalizedMap.Length == 0 || string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return null;
        }

        var cacheKey = $"bz98:map:{normalizedMod.ToLowerInvariant()}:{normalizedMap.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out BZ98MapMetadata? cached))
        {
            return cached;
        }

        BZ98MapMetadata? result = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_options.RequestTimeout > TimeSpan.Zero)
            {
                timeout.CancelAfter(_options.RequestTimeout);
            }

            var baseUrl = _options.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/getdata2.php?map={Uri.EscapeDataString(normalizedMap)}&mods={Uri.EscapeDataString(normalizedMod)}";
            using var response = await _httpClient.GetAsync(url, timeout.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var payload = await JsonSerializer.DeserializeAsync<MapDataEnvelope>(stream, cancellationToken: timeout.Token);
            result = BuildResult(payload?.Map, normalizedMap, normalizedMod, baseUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Map metadata lookup for {MapFile}/{ModId} exceeded the configured timeout of {Timeout}.",
                normalizedMap,
                normalizedMod,
                _options.RequestTimeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or UriFormatException)
        {
            _logger.LogWarning(ex, "Failed to fetch map metadata for {MapFile}/{ModId}.", normalizedMap, normalizedMod);
        }

        _cache.Set(
            cacheKey,
            result,
            result is null ? _options.FailureCacheDuration : _options.CacheDuration);

        return result;
    }

    private static BZ98MapMetadata? BuildResult(
        MapDataMap? map,
        string mapFile,
        string modId,
        string baseUrl)
    {
        if (map is null)
        {
            return null;
        }

        var typeCode = FirstNonEmpty(map.BzcpTypeFix, map.BzcpAutoTypeFix, map.Type);
        var modeCode = FirstNonEmpty(map.BzcpTypeOverride, map.BzcpAutoTypeOverride, typeCode);
        var customTypeCode = Clean(map.CustomType);
        var customTypeName = Clean(map.CustomTypeName);

        var modeLabel = customTypeName ?? ModeLabel(modeCode);
        var title = Clean(map.Title);
        var imageUrl = BuildAssetUrl(baseUrl, map.Image);

        // A response with no useful public map fields is treated as a miss rather than publishing
        // an object that implies authoritative recognition.
        if (title is null && imageUrl is null && modeCode is null && map.Min <= 0 && map.Max <= 0)
        {
            return null;
        }

        return new BZ98MapMetadata(
            mapFile,
            modId,
            string.Equals(modId, StockModId, StringComparison.Ordinal),
            title,
            imageUrl,
            Clean(map.Description),
            map.Min > 0 ? map.Min : null,
            map.Max > 0 ? map.Max : null,
            typeCode,
            TypeLabel(typeCode),
            modeCode,
            modeLabel,
            customTypeCode,
            customTypeName);
    }

    private static string? BuildAssetUrl(string baseUrl, string? path)
    {
        var cleaned = Clean(path);
        if (cleaned is null)
        {
            return null;
        }

        if (Uri.TryCreate(cleaned, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root) ||
            !Uri.TryCreate(root, cleaned.TrimStart('/'), out var combined))
        {
            return null;
        }

        return combined.Scheme == Uri.UriSchemeHttp || combined.Scheme == Uri.UriSchemeHttps
            ? combined.ToString()
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(Clean).FirstOrDefault(value => value is not null);

    private static string? Clean(string? value)
    {
        var cleaned = value?.Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string? TypeLabel(string? code) => code?.ToUpperInvariant() switch
    {
        "D" => "Deathmatch",
        "S" => "Strategy",
        "K" => "Deathmatch",
        "M" => "Strategy",
        "A" => "Deathmatch",
        "X" => "Other",
        _ => null
    };

    private static string? ModeLabel(string? code) => code?.ToUpperInvariant() switch
    {
        "A" => "Action MPI",
        "C" => "Custom",
        "D" => "Deathmatch",
        "F" => "Capture the Flag",
        "G" => "Race",
        "K" => "King of the Hill",
        "L" => "Loot",
        "M" => "Mission MPI",
        "P" => "Pilot Deathmatch",
        "Q" => "Squad Deathmatch",
        "R" => "Capture the Relic",
        "S" => "Strategy",
        "W" => "Wingman Strategy",
        "X" => "Other",
        _ => null
    };

    private sealed class MapDataEnvelope
    {
        [JsonPropertyName("map")]
        public MapDataMap? Map { get; init; }
    }

    private sealed class MapDataMap
    {
        [JsonPropertyName("min")]
        public int Min { get; init; }

        [JsonPropertyName("max")]
        public int Max { get; init; }

        [JsonPropertyName("custom_type")]
        public string? CustomType { get; init; }

        [JsonPropertyName("custom_type_name")]
        public string? CustomTypeName { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("bzcp_type_fix")]
        public string? BzcpTypeFix { get; init; }

        [JsonPropertyName("bzcp_type_override")]
        public string? BzcpTypeOverride { get; init; }

        [JsonPropertyName("bzcp_auto_type_fix")]
        public string? BzcpAutoTypeFix { get; init; }

        [JsonPropertyName("bzcp_auto_type_override")]
        public string? BzcpAutoTypeOverride { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("image")]
        public string? Image { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}
