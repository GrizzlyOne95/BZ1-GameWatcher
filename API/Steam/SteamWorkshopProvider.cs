using System.Globalization;
using System.Text.Json;
using BZAPI.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BZAPI.Steam;

public sealed record SteamWorkshopItem(
    string PublishedFileId,
    string Title,
    string? PreviewUrl,
    string? CreatorSteamId,
    uint? ConsumerAppId,
    DateTimeOffset? UpdatedUtc,
    long? Subscriptions)
{
    public string WorkshopUrl => $"https://steamcommunity.com/sharedfiles/filedetails/?id={PublishedFileId}";
    public string? CreatorProfileUrl => string.IsNullOrWhiteSpace(CreatorSteamId)
        ? null
        : $"https://steamcommunity.com/profiles/{CreatorSteamId}/";
}

public interface ISteamWorkshopProvider
{
    /// <summary>
    /// Resolves public Steam Workshop metadata for a numeric published-file ID. Missing, invalid,
    /// cross-app, and failed lookups return null and are negatively cached.
    /// </summary>
    Task<SteamWorkshopItem?> GetItemAsync(ulong publishedFileId, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the public Workshop title/preview/creator metadata exposed by Steam's
/// ISteamRemoteStorage/GetPublishedFileDetails endpoint and caches it aggressively. This endpoint
/// does not require the configured Steam Web API key.
/// </summary>
public sealed class SteamWorkshopProvider : ISteamWorkshopProvider
{
    private const string Endpoint =
        "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    private readonly IMemoryCache _cache;
    private readonly HttpClient _httpClient;
    private readonly SteamOptions _options;
    private readonly ILogger<SteamWorkshopProvider> _logger;

    public SteamWorkshopProvider(
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IOptions<SteamOptions> options,
        ILogger<SteamWorkshopProvider> logger)
    {
        _cache = cache;
        _httpClient = httpClientFactory.CreateClient(nameof(SteamWorkshopProvider));
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SteamWorkshopItem?> GetItemAsync(
        ulong publishedFileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"steam:workshop:{publishedFileId}";
        if (_cache.TryGetValue(cacheKey, out SteamWorkshopItem? cached))
        {
            return cached;
        }

        SteamWorkshopItem? item = null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_options.WorkshopRequestTimeout > TimeSpan.Zero)
            {
                timeout.CancelAfter(_options.WorkshopRequestTimeout);
            }

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["itemcount"] = "1",
                ["publishedfileids[0]"] = publishedFileId.ToString(CultureInfo.InvariantCulture)
            });

            using var response = await _httpClient.PostAsync(Endpoint, form, timeout.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            item = Parse(document.RootElement, publishedFileId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Workshop lookup for {PublishedFileId} exceeded the configured timeout of {Timeout}.",
                publishedFileId,
                _options.WorkshopRequestTimeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to fetch Workshop item {PublishedFileId}.", publishedFileId);
        }

        _cache.Set(
            cacheKey,
            item,
            item is null ? _options.WorkshopFailureCacheDuration : _options.WorkshopCacheDuration);

        return item;
    }

    private SteamWorkshopItem? Parse(JsonElement root, ulong requestedId)
    {
        if (!root.TryGetProperty("response", out var response) ||
            !response.TryGetProperty("publishedfiledetails", out var details) ||
            details.ValueKind != JsonValueKind.Array ||
            details.GetArrayLength() == 0)
        {
            return null;
        }

        var detail = details[0];
        if (ReadInt64(detail, "result") is not 1)
        {
            return null;
        }

        var returnedId = ReadString(detail, "publishedfileid");
        if (returnedId is null ||
            !ulong.TryParse(returnedId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId) ||
            parsedId != requestedId)
        {
            return null;
        }

        var title = ReadString(detail, "title")?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var consumerAppId = ReadUInt32(detail, "consumer_app_id");
        if (_options.AppId != 0 && consumerAppId is not null && consumerAppId != _options.AppId)
        {
            _logger.LogDebug(
                "Ignoring Workshop item {PublishedFileId}: consumer app {ConsumerAppId} does not match configured app {SteamAppId}.",
                requestedId,
                consumerAppId,
                _options.AppId);
            return null;
        }

        var updatedUnix = ReadInt64(detail, "time_updated");
        DateTimeOffset? updatedUtc = updatedUnix is > 0
            ? DateTimeOffset.FromUnixTimeSeconds(updatedUnix.Value)
            : null;

        return new SteamWorkshopItem(
            returnedId,
            title,
            ReadString(detail, "preview_url"),
            ReadString(detail, "creator"),
            consumerAppId,
            updatedUtc,
            ReadInt64(detail, "subscriptions"));
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static long? ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static uint? ReadUInt32(JsonElement element, string propertyName)
    {
        var value = ReadInt64(element, propertyName);
        return value is >= 0 and <= uint.MaxValue ? (uint)value.Value : null;
    }
}
