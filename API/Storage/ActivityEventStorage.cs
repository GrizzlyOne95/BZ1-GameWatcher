using System.Text.Json;
using BZAPI.Configuration;
using Microsoft.Extensions.Options;

namespace BZAPI.Storage;

/// <summary>
/// Privacy-safe discrete change observed in a public game lobby. Deliberately excludes player names,
/// IDs, Steam IDs, chat text, IP addresses, and other user-level history.
/// </summary>
public sealed record LobbyActivityEvent(
    long Sequence,
    DateTimeOffset TimeUtc,
    int LobbyId,
    string Type,
    string? LobbyName,
    string? MapFile,
    string? Mod,
    int? FromCount = null,
    int? ToCount = null,
    string? FromValue = null,
    string? ToValue = null);

public static class LobbyActivityEventTypes
{
    public const string LobbyOpened = "LobbyOpened";
    public const string LobbyClosed = "LobbyClosed";
    public const string GameLaunched = "GameLaunched";
    public const string GameEnded = "GameEnded";
    public const string MapChanged = "MapChanged";
    public const string PlayerCountChanged = "PlayerCountChanged";
    public const string LobbyLocked = "LobbyLocked";
    public const string LobbyUnlocked = "LobbyUnlocked";
    public const string LobbyMadePrivate = "LobbyMadePrivate";
    public const string LobbyMadePublic = "LobbyMadePublic";
}

public interface IActivityEventStore
{
    IReadOnlyList<LobbyActivityEvent> GetSince(DateTimeOffset sinceUtc, int limit = 100);
    DateTimeOffset? FirstEventUtc { get; }
    DateTimeOffset? LastEventUtc { get; }
    string StorageKind { get; }
    bool IsDurable { get; }
    void Add(LobbyActivityEvent activityEvent);
}

/// <summary>
/// Retains a bounded journal of lobby-level state transitions. The schema intentionally contains no
/// user-level identity fields, so persistence cannot silently turn the live player list into a tracking log.
/// </summary>
public sealed class ActivityEventStore : IActivityEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly object _sync = new();
    private readonly List<LobbyActivityEvent> _events = [];
    private readonly TimeSpan _retention;
    private readonly int _maxEntries;
    private readonly string? _persistencePath;
    private readonly bool _persistenceIsDurable;
    private readonly ILogger<ActivityEventStore> _logger;
    private long _nextSequence = 1;

    public ActivityEventStore(IOptions<ActivityOptions> options, ILogger<ActivityEventStore> logger)
    {
        var configured = options.Value;
        _retention = configured.EventRetention <= TimeSpan.Zero
            ? TimeSpan.FromDays(30)
            : configured.EventRetention;
        _maxEntries = configured.EventMaxEntries <= 0 ? 10_000 : configured.EventMaxEntries;
        _persistencePath = string.IsNullOrWhiteSpace(configured.EventPersistencePath)
            ? null
            : configured.EventPersistencePath.Trim();
        _persistenceIsDurable = _persistencePath is not null && configured.EventPersistenceIsDurable;
        _logger = logger;

        if (configured.EventPersistenceIsDurable && _persistencePath is null)
        {
            _logger.LogWarning(
                "Lobby-event persistence was marked durable but no event persistence path was configured; using memory-only history.");
        }
        else if (_persistencePath is not null && !_persistenceIsDurable)
        {
            _logger.LogInformation(
                "Lobby-event history is file-backed at {EventPersistencePath}, but the path is not declared durable.",
                _persistencePath);
        }

        LoadPersistedEvents();
    }

    public string StorageKind => _persistencePath is null ? "memory" : "file";

    public bool IsDurable => _persistenceIsDurable;

    public DateTimeOffset? FirstEventUtc
    {
        get
        {
            lock (_sync)
            {
                return _events.Count == 0 ? null : _events[0].TimeUtc;
            }
        }
    }

    public DateTimeOffset? LastEventUtc
    {
        get
        {
            lock (_sync)
            {
                return _events.Count == 0 ? null : _events[^1].TimeUtc;
            }
        }
    }

    public IReadOnlyList<LobbyActivityEvent> GetSince(DateTimeOffset sinceUtc, int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);

        lock (_sync)
        {
            return _events
                .Where(activityEvent => activityEvent.TimeUtc >= sinceUtc)
                .TakeLast(limit)
                .Reverse()
                .ToArray();
        }
    }

    public void Add(LobbyActivityEvent activityEvent)
    {
        lock (_sync)
        {
            var normalized = activityEvent with
            {
                Sequence = _nextSequence++,
                TimeUtc = activityEvent.TimeUtc.ToUniversalTime()
            };

            _events.Add(normalized);
            TrimLocked(normalized.TimeUtc);
            PersistLocked();
        }
    }

    private void LoadPersistedEvents()
    {
        if (_persistencePath is null || !File.Exists(_persistencePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_persistencePath);
            var events = JsonSerializer.Deserialize<List<LobbyActivityEvent>>(json, JsonOptions) ?? [];
            var now = DateTimeOffset.UtcNow;

            _events.AddRange(events
                .Where(activityEvent => activityEvent.TimeUtc >= now - _retention && activityEvent.TimeUtc <= now + TimeSpan.FromMinutes(5))
                .OrderBy(activityEvent => activityEvent.TimeUtc)
                .ThenBy(activityEvent => activityEvent.Sequence));

            TrimLocked(now);
            _nextSequence = _events.Count == 0 ? 1 : _events.Max(activityEvent => activityEvent.Sequence) + 1;

            _logger.LogInformation(
                "Loaded {LobbyEventCount} lobby-state events from {EventPersistencePath}.",
                _events.Count,
                _persistencePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not load lobby-event history from {EventPersistencePath}.", _persistencePath);
        }
    }

    private void TrimLocked(DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc - _retention;
        var removeCount = 0;

        while (removeCount < _events.Count && _events[removeCount].TimeUtc < cutoff)
        {
            removeCount++;
        }

        if (removeCount > 0)
        {
            _events.RemoveRange(0, removeCount);
        }

        if (_events.Count > _maxEntries)
        {
            _events.RemoveRange(0, _events.Count - _maxEntries);
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
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_events, JsonOptions));
            File.Move(temporaryPath, _persistencePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not persist lobby-event history to {EventPersistencePath}.", _persistencePath);
        }
    }
}
