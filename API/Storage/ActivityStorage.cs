using System.Text.Json;
using BZAPI.Configuration;
using BZAPI.Models;
using Microsoft.Extensions.Options;

namespace BZAPI.Storage;

/// <summary>Privacy-safe aggregate activity at one point in time.</summary>
public sealed record ActivitySample(
    DateTimeOffset TimeUtc,
    int PlayersOnline,
    int ActiveGames,
    int GamesInProgress,
    int WaitingRoomUsers);

public interface IActivityStore
{
    IReadOnlyList<ActivitySample> GetSince(DateTimeOffset sinceUtc);
    DateTimeOffset? FirstSampleUtc { get; }
    DateTimeOffset? LastSampleUtc { get; }
    string StorageKind { get; }
    bool IsDurable { get; }
    void Add(ActivitySample sample);
}

/// <summary>
/// Keeps at most the configured retention window of aggregate samples. No player identifiers,
/// names, chat text, network addresses, or lobby metadata are stored here.
/// </summary>
public sealed class ActivityStore : IActivityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly object _sync = new();
    private readonly List<ActivitySample> _samples = [];
    private readonly TimeSpan _retention;
    private readonly string? _persistencePath;
    private readonly bool _persistenceIsDurable;
    private readonly ILogger<ActivityStore> _logger;

    public ActivityStore(IOptions<ActivityOptions> options, ILogger<ActivityStore> logger)
    {
        var configured = options.Value;
        _retention = configured.Retention <= TimeSpan.Zero
            ? TimeSpan.FromDays(30)
            : configured.Retention;
        _persistencePath = string.IsNullOrWhiteSpace(configured.PersistencePath)
            ? null
            : configured.PersistencePath.Trim();
        _persistenceIsDurable = _persistencePath is not null && configured.PersistenceIsDurable;
        _logger = logger;

        if (configured.PersistenceIsDurable && _persistencePath is null)
        {
            _logger.LogWarning(
                "Activity persistence was marked durable but no persistence path was configured; using memory-only history.");
        }
        else if (_persistencePath is not null && !_persistenceIsDurable)
        {
            _logger.LogInformation(
                "Activity history is file-backed at {ActivityPersistencePath}, but the path is not declared durable.",
                _persistencePath);
        }

        LoadPersistedSamples();
    }

    public string StorageKind => _persistencePath is null ? "memory" : "file";

    public bool IsDurable => _persistenceIsDurable;

    public DateTimeOffset? FirstSampleUtc
    {
        get
        {
            lock (_sync)
            {
                return _samples.Count == 0 ? null : _samples[0].TimeUtc;
            }
        }
    }

    public DateTimeOffset? LastSampleUtc
    {
        get
        {
            lock (_sync)
            {
                return _samples.Count == 0 ? null : _samples[^1].TimeUtc;
            }
        }
    }

    public IReadOnlyList<ActivitySample> GetSince(DateTimeOffset sinceUtc)
    {
        lock (_sync)
        {
            return _samples
                .Where(sample => sample.TimeUtc >= sinceUtc)
                .ToArray();
        }
    }

    public void Add(ActivitySample sample)
    {
        lock (_sync)
        {
            var normalized = sample with { TimeUtc = sample.TimeUtc.ToUniversalTime() };

            if (_samples.Count > 0 && _samples[^1].TimeUtc == normalized.TimeUtc)
            {
                _samples[^1] = normalized;
            }
            else
            {
                _samples.Add(normalized);
            }

            TrimLocked(normalized.TimeUtc);
            PersistLocked();
        }
    }

    private void LoadPersistedSamples()
    {
        if (_persistencePath is null || !File.Exists(_persistencePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_persistencePath);
            var samples = JsonSerializer.Deserialize<List<ActivitySample>>(json, JsonOptions) ?? [];
            var now = DateTimeOffset.UtcNow;

            _samples.AddRange(samples
                .Where(sample => sample.TimeUtc >= now - _retention && sample.TimeUtc <= now + TimeSpan.FromMinutes(5))
                .OrderBy(sample => sample.TimeUtc));

            TrimLocked(now);
            _logger.LogInformation(
                "Loaded {ActivitySampleCount} activity samples from {ActivityPersistencePath}.",
                _samples.Count,
                _persistencePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not load activity history from {ActivityPersistencePath}.", _persistencePath);
        }
    }

    private void TrimLocked(DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc - _retention;
        var removeCount = 0;

        while (removeCount < _samples.Count && _samples[removeCount].TimeUtc < cutoff)
        {
            removeCount++;
        }

        if (removeCount > 0)
        {
            _samples.RemoveRange(0, removeCount);
        }
    }

    private void PersistLocked()
    {
        if (_persistencePath is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_persistencePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _persistencePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_samples, JsonOptions));
            File.Move(temporaryPath, _persistencePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not persist activity history to {ActivityPersistencePath}.", _persistencePath);
        }
    }
}

public static class ActivitySnapshotBuilder
{
    public static ActivitySample Build(LobbySnapshot snapshot, IChatStore chatStore, DateTimeOffset? nowUtc = null)
    {
        var lobbies = snapshot.Lobbies;
        var gameLobbies = lobbies.Where(lobby => !lobby.IsChat).ToList();
        var chatLobbies = lobbies.Where(lobby => lobby.IsChat).ToList();

        var gamePlayerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lobby in gameLobbies)
        {
            foreach (var user in lobby.Users?.Values.Where(user => user is not null) ?? [])
            {
                gamePlayerKeys.Add(UserKey(lobby.Id, user));
            }
        }

        var waitingRoomUsers = 0;
        foreach (var lobby in chatLobbies)
        {
            waitingRoomUsers += lobby.Users?.Values.Count(user =>
                user is not null && !chatStore.IsObserverUser(lobby.Id, user.Id)) ?? 0;
        }

        return new ActivitySample(
            (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            gamePlayerKeys.Count,
            gameLobbies.Count,
            gameLobbies.Count(lobby => string.Equals(lobby.MetaData?.Launched, "1", StringComparison.Ordinal)),
            waitingRoomUsers);
    }

    private static string UserKey(int lobbyId, BZ98User user)
    {
        if (!string.IsNullOrWhiteSpace(user.Id))
        {
            return user.Id;
        }

        return $"{lobbyId}:{user.Name ?? "unknown"}";
    }
}
