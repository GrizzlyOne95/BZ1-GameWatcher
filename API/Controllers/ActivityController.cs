using BZAPI.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BZAPI.Controllers;

[Route("api/activity")]
[ApiController]
public sealed class ActivityController(
    ILobbyStore lobbyStore,
    IChatStore chatStore,
    IActivityStore activityStore,
    IActivityEventStore activityEventStore) : ControllerBase
{
    private readonly ILobbyStore _lobbyStore = lobbyStore;
    private readonly IChatStore _chatStore = chatStore;
    private readonly IActivityStore _activityStore = activityStore;
    private readonly IActivityEventStore _activityEventStore = activityEventStore;

    [HttpGet]
    [ProducesResponseType(typeof(ActivityResponse), StatusCodes.Status200OK)]
    public ActionResult<ActivityResponse> GetActivity([FromQuery] string range = "24h")
    {
        var window = ParseRange(range);
        var now = DateTimeOffset.UtcNow;
        var since = now - window.Duration;
        var snapshot = _lobbyStore.Current;
        var current = snapshot.LastUpdatedUtc is null
            ? null
            : ActivitySnapshotBuilder.Build(snapshot, _chatStore, now);
        var raw = _activityStore.GetSince(since);
        var chartSamples = Downsample(raw, window.BucketSize);
        var recentEvents = _activityEventStore.GetSince(since, 100);

        var peakPlayers = raw.Count == 0 ? 0 : raw.Max(sample => sample.PlayersOnline);
        var peakGames = raw.Count == 0 ? 0 : raw.Max(sample => sample.ActiveGames);
        var averagePlayers = raw.Count == 0 ? 0 : raw.Average(sample => sample.PlayersOnline);

        if (current is not null)
        {
            peakPlayers = Math.Max(peakPlayers, current.PlayersOnline);
            peakGames = Math.Max(peakGames, current.ActiveGames);
            averagePlayers = raw.Count == 0
                ? current.PlayersOnline
                : ((averagePlayers * raw.Count) + current.PlayersOnline) / (raw.Count + 1);
        }

        return Ok(new ActivityResponse
        {
            Range = window.Name,
            RequestedSinceUtc = since,
            HistoryStartedUtc = _activityStore.FirstSampleUtc,
            LastHistoricalSampleUtc = _activityStore.LastSampleUtc,
            LobbyDataUpdatedUtc = snapshot.LastUpdatedUtc,
            HistoryStorage = _activityStore.StorageKind,
            DurableHistory = _activityStore.IsDurable,
            EventHistoryStartedUtc = _activityEventStore.FirstEventUtc,
            LastEventUtc = _activityEventStore.LastEventUtc,
            EventHistoryStorage = _activityEventStore.StorageKind,
            DurableEventHistory = _activityEventStore.IsDurable,
            Current = current is null ? null : ActivitySampleResponse.From(current),
            Summary = new ActivitySummaryResponse
            {
                PeakPlayers = peakPlayers,
                AveragePlayers = Math.Round(averagePlayers, 1),
                PeakActiveGames = peakGames,
                HistoricalSampleCount = raw.Count
            },
            Samples = chartSamples.Select(ActivitySampleResponse.From).ToArray(),
            RecentEvents = recentEvents.Select(LobbyActivityEventResponse.From).ToArray()
        });
    }

    /// <summary>
    /// Returns a lightweight privacy-safe transition feed for clients that do not need the chart data.
    /// No player identities, chat text, or network fields are retained in this journal.
    /// </summary>
    [HttpGet("events")]
    [ProducesResponseType(typeof(ActivityEventsResponse), StatusCodes.Status200OK)]
    public ActionResult<ActivityEventsResponse> GetEvents(
        [FromQuery] string range = "24h",
        [FromQuery] int limit = 100)
    {
        var window = ParseRange(range);
        var since = DateTimeOffset.UtcNow - window.Duration;
        var events = _activityEventStore.GetSince(since, Math.Clamp(limit, 1, 500));

        return Ok(new ActivityEventsResponse
        {
            Range = window.Name,
            RequestedSinceUtc = since,
            HistoryStartedUtc = _activityEventStore.FirstEventUtc,
            LastEventUtc = _activityEventStore.LastEventUtc,
            HistoryStorage = _activityEventStore.StorageKind,
            DurableHistory = _activityEventStore.IsDurable,
            Events = events.Select(LobbyActivityEventResponse.From).ToArray()
        });
    }

    /// <summary>
    /// Exports the retained aggregate activity window for backup/migration. The activity store
    /// contains counts only — never player names/IDs, chat text, lobby metadata, or network data.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(ActivityExportResponse), StatusCodes.Status200OK)]
    public ActionResult<ActivityExportResponse> ExportActivity()
    {
        var samples = _activityStore.GetSince(DateTimeOffset.MinValue);
        return Ok(new ActivityExportResponse
        {
            ExportedAtUtc = DateTimeOffset.UtcNow,
            HistoryStartedUtc = _activityStore.FirstSampleUtc,
            LastHistoricalSampleUtc = _activityStore.LastSampleUtc,
            HistoryStorage = _activityStore.StorageKind,
            DurableHistory = _activityStore.IsDurable,
            Samples = samples.Select(ActivitySampleResponse.From).ToArray()
        });
    }

    private static ActivityRange ParseRange(string? range) => range?.Trim().ToLowerInvariant() switch
    {
        "7d" => new("7d", TimeSpan.FromDays(7), TimeSpan.FromMinutes(30)),
        "30d" => new("30d", TimeSpan.FromDays(30), TimeSpan.FromHours(2)),
        _ => new("24h", TimeSpan.FromHours(24), TimeSpan.FromMinutes(5))
    };

    private static IReadOnlyList<ActivitySample> Downsample(
        IReadOnlyList<ActivitySample> samples,
        TimeSpan bucketSize)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        var bucketTicks = bucketSize.Ticks;
        return samples
            .GroupBy(sample => sample.TimeUtc.UtcDateTime.Ticks / bucketTicks)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var items = group.ToArray();
                var bucketStartTicks = group.Key * bucketTicks;
                var bucketStart = new DateTimeOffset(new DateTime(bucketStartTicks, DateTimeKind.Utc));

                return new ActivitySample(
                    bucketStart,
                    (int)Math.Round(items.Average(sample => sample.PlayersOnline)),
                    (int)Math.Round(items.Average(sample => sample.ActiveGames)),
                    (int)Math.Round(items.Average(sample => sample.GamesInProgress)),
                    (int)Math.Round(items.Average(sample => sample.WaitingRoomUsers)));
            })
            .ToArray();
    }

    private sealed record ActivityRange(string Name, TimeSpan Duration, TimeSpan BucketSize);
}

public sealed class ActivityResponse
{
    public string Range { get; init; } = "24h";
    public DateTimeOffset RequestedSinceUtc { get; init; }
    public DateTimeOffset? HistoryStartedUtc { get; init; }
    public DateTimeOffset? LastHistoricalSampleUtc { get; init; }
    public DateTimeOffset? LobbyDataUpdatedUtc { get; init; }
    public string HistoryStorage { get; init; } = "memory";
    public bool DurableHistory { get; init; }
    public DateTimeOffset? EventHistoryStartedUtc { get; init; }
    public DateTimeOffset? LastEventUtc { get; init; }
    public string EventHistoryStorage { get; init; } = "memory";
    public bool DurableEventHistory { get; init; }
    public ActivitySampleResponse? Current { get; init; }
    public ActivitySummaryResponse Summary { get; init; } = new();
    public IReadOnlyList<ActivitySampleResponse> Samples { get; init; } = [];
    public IReadOnlyList<LobbyActivityEventResponse> RecentEvents { get; init; } = [];
}

public sealed class ActivityEventsResponse
{
    public string Range { get; init; } = "24h";
    public DateTimeOffset RequestedSinceUtc { get; init; }
    public DateTimeOffset? HistoryStartedUtc { get; init; }
    public DateTimeOffset? LastEventUtc { get; init; }
    public string HistoryStorage { get; init; } = "memory";
    public bool DurableHistory { get; init; }
    public IReadOnlyList<LobbyActivityEventResponse> Events { get; init; } = [];
}

public sealed class ActivityExportResponse
{
    public DateTimeOffset ExportedAtUtc { get; init; }
    public DateTimeOffset? HistoryStartedUtc { get; init; }
    public DateTimeOffset? LastHistoricalSampleUtc { get; init; }
    public string HistoryStorage { get; init; } = "memory";
    public bool DurableHistory { get; init; }
    public IReadOnlyList<ActivitySampleResponse> Samples { get; init; } = [];
}

public sealed class ActivitySummaryResponse
{
    public int PeakPlayers { get; init; }
    public double AveragePlayers { get; init; }
    public int PeakActiveGames { get; init; }
    public int HistoricalSampleCount { get; init; }
}

public sealed class ActivitySampleResponse
{
    public DateTimeOffset TimeUtc { get; init; }
    public int PlayersOnline { get; init; }
    public int ActiveGames { get; init; }
    public int GamesInProgress { get; init; }
    public int WaitingRoomUsers { get; init; }

    public static ActivitySampleResponse From(ActivitySample sample) => new()
    {
        TimeUtc = sample.TimeUtc,
        PlayersOnline = sample.PlayersOnline,
        ActiveGames = sample.ActiveGames,
        GamesInProgress = sample.GamesInProgress,
        WaitingRoomUsers = sample.WaitingRoomUsers
    };
}

public sealed class LobbyActivityEventResponse
{
    public long Sequence { get; init; }
    public DateTimeOffset TimeUtc { get; init; }
    public int LobbyId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string? LobbyName { get; init; }
    public string? MapFile { get; init; }
    public string? Mod { get; init; }
    public int? FromCount { get; init; }
    public int? ToCount { get; init; }
    public string? FromValue { get; init; }
    public string? ToValue { get; init; }

    public static LobbyActivityEventResponse From(LobbyActivityEvent activityEvent) => new()
    {
        Sequence = activityEvent.Sequence,
        TimeUtc = activityEvent.TimeUtc,
        LobbyId = activityEvent.LobbyId,
        Type = activityEvent.Type,
        LobbyName = activityEvent.LobbyName,
        MapFile = activityEvent.MapFile,
        Mod = activityEvent.Mod,
        FromCount = activityEvent.FromCount,
        ToCount = activityEvent.ToCount,
        FromValue = activityEvent.FromValue,
        ToValue = activityEvent.ToValue
    };
}
