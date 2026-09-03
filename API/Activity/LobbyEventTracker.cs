using BZAPI.Configuration;
using BZAPI.Models;
using BZAPI.Storage;
using Microsoft.Extensions.Options;

namespace BZAPI.Activity;

/// <summary>
/// Converts authoritative lobby-store publications into privacy-safe lobby-level state transitions.
/// The first snapshot after process start establishes a baseline and deliberately emits no events.
/// </summary>
public sealed class LobbyEventTracker(
    ILobbyStore lobbies,
    IActivityEventStore events,
    IOptions<ActivityOptions> options,
    ILogger<LobbyEventTracker> logger) : IHostedService
{
    private readonly ILobbyStore _lobbies = lobbies;
    private readonly IActivityEventStore _events = events;
    private readonly ActivityOptions _options = options.Value;
    private readonly ILogger<LobbyEventTracker> _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Lobby-state event tracking is disabled with aggregate activity history.");
            return Task.CompletedTask;
        }

        _lobbies.SnapshotChanged += OnSnapshotChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _lobbies.SnapshotChanged -= OnSnapshotChanged;
        return Task.CompletedTask;
    }

    private void OnSnapshotChanged(LobbySnapshot previous, LobbySnapshot current)
    {
        // Avoid reporting every already-open game as newly created after a service restart.
        if (previous.LastUpdatedUtc is null)
        {
            return;
        }

        try
        {
            var timeUtc = (current.LastUpdatedUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
            var previousGames = previous.Lobbies
                .Where(lobby => !lobby.IsChat)
                .ToDictionary(lobby => lobby.Id);
            var currentGames = current.Lobbies
                .Where(lobby => !lobby.IsChat)
                .ToDictionary(lobby => lobby.Id);

            foreach (var previousLobby in previousGames.Values)
            {
                if (!currentGames.ContainsKey(previousLobby.Id))
                {
                    AddEvent(timeUtc, previousLobby, LobbyActivityEventTypes.LobbyClosed);
                }
            }

            foreach (var currentLobby in currentGames.Values)
            {
                if (!previousGames.TryGetValue(currentLobby.Id, out var previousLobby))
                {
                    AddEvent(timeUtc, currentLobby, LobbyActivityEventTypes.LobbyOpened);
                    continue;
                }

                TrackExistingLobby(timeUtc, previousLobby, currentLobby);
            }
        }
        catch (Exception ex)
        {
            // Event history must never break the live lobby watcher. A malformed/partial lobby can
            // cost us one journal update, but must not prevent the authoritative snapshot publishing.
            _logger.LogWarning(ex, "Could not derive lobby-state events from the latest snapshot.");
        }
    }

    private void TrackExistingLobby(DateTimeOffset timeUtc, BZ98Lobby previous, BZ98Lobby current)
    {
        var previousEnded = string.Equals(previous.MetaData?.GameEnded, "1", StringComparison.Ordinal);
        var currentEnded = string.Equals(current.MetaData?.GameEnded, "1", StringComparison.Ordinal);
        var previousLaunched = string.Equals(previous.MetaData?.Launched, "1", StringComparison.Ordinal);
        var currentLaunched = string.Equals(current.MetaData?.Launched, "1", StringComparison.Ordinal);

        if (!previousLaunched && currentLaunched && !currentEnded)
        {
            AddEvent(timeUtc, current, LobbyActivityEventTypes.GameLaunched);
        }

        if (!previousEnded && currentEnded)
        {
            AddEvent(timeUtc, current, LobbyActivityEventTypes.GameEnded);
        }

        var previousMap = Normalize(previous.Stats?.MapFile);
        var currentMap = Normalize(current.Stats?.MapFile);
        if (!string.Equals(previousMap, currentMap, StringComparison.OrdinalIgnoreCase)
            && (previousMap is not null || currentMap is not null))
        {
            AddEvent(
                timeUtc,
                current,
                LobbyActivityEventTypes.MapChanged,
                fromValue: previousMap,
                toValue: currentMap);
        }

        if (previous.UserCount != current.UserCount)
        {
            AddEvent(
                timeUtc,
                current,
                LobbyActivityEventTypes.PlayerCountChanged,
                fromCount: previous.UserCount,
                toCount: current.UserCount);
        }

        if (previous.IsLocked != current.IsLocked)
        {
            AddEvent(timeUtc, current, current.IsLocked
                ? LobbyActivityEventTypes.LobbyLocked
                : LobbyActivityEventTypes.LobbyUnlocked);
        }

        if (previous.IsPrivate != current.IsPrivate)
        {
            AddEvent(timeUtc, current, current.IsPrivate
                ? LobbyActivityEventTypes.LobbyMadePrivate
                : LobbyActivityEventTypes.LobbyMadePublic);
        }
    }

    private void AddEvent(
        DateTimeOffset timeUtc,
        BZ98Lobby lobby,
        string type,
        int? fromCount = null,
        int? toCount = null,
        string? fromValue = null,
        string? toValue = null)
    {
        _events.Add(new LobbyActivityEvent(
            0,
            timeUtc,
            lobby.Id,
            type,
            DisplayName(lobby),
            Normalize(lobby.Stats?.MapFile),
            Normalize(lobby.Stats?.Mod),
            fromCount,
            toCount,
            fromValue,
            toValue));
    }

    private static string DisplayName(BZ98Lobby lobby)
    {
        var rawName = Normalize(lobby.MetaData?.Name);
        if (rawName is null)
        {
            return $"Lobby {lobby.Id}";
        }

        var display = System.Text.RegularExpressions.Regex.Replace(
            rawName,
            "^~game~(?:pub|pri)~\\*?~",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return string.IsNullOrWhiteSpace(display) ? rawName : display;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
