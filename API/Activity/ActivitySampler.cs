using BZAPI.Configuration;
using BZAPI.Storage;
using Microsoft.Extensions.Options;

namespace BZAPI.Activity;

/// <summary>
/// Periodically records aggregate multiplayer activity after the lobby watcher has produced a real
/// snapshot. The sampler stores counts only; it never copies player identities or lobby contents.
/// </summary>
public sealed class ActivitySampler : BackgroundService
{
    private readonly ILobbyStore _lobbies;
    private readonly IChatStore _chat;
    private readonly IActivityStore _activity;
    private readonly ActivityOptions _options;
    private readonly ILogger<ActivitySampler> _logger;

    public ActivitySampler(
        ILobbyStore lobbies,
        IChatStore chat,
        IActivityStore activity,
        IOptions<ActivityOptions> options,
        ILogger<ActivitySampler> logger)
    {
        _lobbies = lobbies;
        _chat = chat;
        _activity = activity;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Aggregate activity sampling is disabled.");
            return;
        }

        var interval = _options.SamplingInterval <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(5)
            : _options.SamplingInterval;

        // Give the websocket watcher a brief opportunity to populate its first snapshot. If it has
        // not connected yet, SampleOnce simply skips the zero/unknown state.
        await DelaySafely(TimeSpan.FromSeconds(10), stoppingToken);
        SampleOnce();

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                SampleOnce();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private void SampleOnce()
    {
        var snapshot = _lobbies.Current;
        if (snapshot.LastUpdatedUtc is null)
        {
            return;
        }

        _activity.Add(ActivitySnapshotBuilder.Build(snapshot, _chat));
    }

    private static async Task DelaySafely(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal startup cancellation.
        }
    }
}
