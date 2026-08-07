using BZAPI.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BZAPI.Controllers;

[Route("api/activity")]
[ApiController]
public sealed class ActivityController(
    ILobbyStore lobbyStore,
    IChatStore chatStore,
    IActivityStore activityStore) : ControllerBase
{
    private readonly ILobbyStore _lobbyStore = lobbyStore;
    private readonly IChatStore _chatStore = chatStore;
    private readonly IActivityStore _activityStore = activityStore;

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
            Current = current is null ? null : ActivitySampleResponse.From(current),
            Summary = new ActivitySummaryResponse
            {
                PeakPlayers = peakPlayers,
                AveragePlayers = Math.Round(averagePlayers, 1),
                PeakActiveGames = peakGames,
                HistoricalSampleCount = raw.Count
            },
            Samples = chartSamples.Select(ActivitySampleResponse.From).ToArray()
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
    public ActivitySampleResponse? Current { get; init; }
    public ActivitySummaryResponse Summary { get; init; } = new();
    public IReadOnlyList<ActivitySampleResponse> Samples { get; init; } = [];
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
