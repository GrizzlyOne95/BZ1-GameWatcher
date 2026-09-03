using System.Text.Json;
using BZAPI.Activity;
using BZAPI.Configuration;
using BZAPI.Models;
using BZAPI.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace API.Tests;

public sealed class LobbyEventTrackerTests
{
    [Fact]
    public async Task TracksLobbyLevelTransitionsWithoutPersistingPlayerIdentities()
    {
        var lobbyStore = new LobbyStore();
        var eventStore = CreateEventStore();
        var tracker = new LobbyEventTracker(
            lobbyStore,
            eventStore,
            Options.Create(new ActivityOptions()),
            NullLogger<LobbyEventTracker>.Instance);

        await tracker.StartAsync(CancellationToken.None);

        // The first authoritative snapshot establishes a baseline rather than manufacturing an
        // "opened" event for every lobby that survived a process restart.
        lobbyStore.Replace([GameLobby(42, 1, "0", "cell.bzn", false, "PilotOne")]);
        Assert.Empty(eventStore.GetSince(DateTimeOffset.MinValue));

        lobbyStore.AddOrUpdate(GameLobby(42, 2, "0", "cell.bzn", false, "PilotOne", "PilotTwo"));
        lobbyStore.AddOrUpdate(GameLobby(42, 2, "1", "cell.bzn", false, "PilotOne", "PilotTwo"));
        lobbyStore.AddOrUpdate(GameLobby(42, 2, "1", "bunker.bzn", true, "PilotOne", "PilotTwo"));
        lobbyStore.Remove(42);

        var events = eventStore.GetSince(DateTimeOffset.MinValue, 100).Reverse().ToArray();

        Assert.Contains(events, activityEvent =>
            activityEvent.Type == LobbyActivityEventTypes.PlayerCountChanged
            && activityEvent.FromCount == 1
            && activityEvent.ToCount == 2);
        Assert.Contains(events, activityEvent => activityEvent.Type == LobbyActivityEventTypes.GameLaunched);
        Assert.Contains(events, activityEvent =>
            activityEvent.Type == LobbyActivityEventTypes.MapChanged
            && activityEvent.FromValue == "cell.bzn"
            && activityEvent.ToValue == "bunker.bzn");
        Assert.Contains(events, activityEvent => activityEvent.Type == LobbyActivityEventTypes.LobbyLocked);
        Assert.Contains(events, activityEvent => activityEvent.Type == LobbyActivityEventTypes.LobbyClosed);

        var serialized = JsonSerializer.Serialize(events);
        Assert.DoesNotContain("PilotOne", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("PilotTwo", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("steamCleanId", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ipAddress", serialized, StringComparison.OrdinalIgnoreCase);

        await tracker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task IgnoresChatLobbyChangesAndRecordsNewGameLobbiesAfterBaseline()
    {
        var lobbyStore = new LobbyStore();
        var eventStore = CreateEventStore();
        var tracker = new LobbyEventTracker(
            lobbyStore,
            eventStore,
            Options.Create(new ActivityOptions()),
            NullLogger<LobbyEventTracker>.Instance);

        await tracker.StartAsync(CancellationToken.None);

        lobbyStore.Replace([ChatLobby(1004, 1)]);
        lobbyStore.AddOrUpdate(ChatLobby(1004, 2));
        lobbyStore.AddOrUpdate(GameLobby(77, 1, "0", "hills.bzn", false, "Pilot"));

        var events = eventStore.GetSince(DateTimeOffset.MinValue, 100);
        var opened = Assert.Single(events);
        Assert.Equal(LobbyActivityEventTypes.LobbyOpened, opened.Type);
        Assert.Equal(77, opened.LobbyId);
        Assert.Equal("hills.bzn", opened.MapFile);

        await tracker.StopAsync(CancellationToken.None);
    }

    private static ActivityEventStore CreateEventStore() => new(
        Options.Create(new ActivityOptions
        {
            EventRetention = TimeSpan.FromDays(30),
            EventMaxEntries = 1000
        }),
        NullLogger<ActivityEventStore>.Instance);

    private static BZ98Lobby GameLobby(
        int id,
        int userCount,
        string launched,
        string mapFile,
        bool isLocked,
        params string[] playerNames)
    {
        return new BZ98Lobby
        {
            Id = id,
            IsChat = false,
            IsLocked = isLocked,
            UserCount = userCount,
            MemberLimit = 8,
            MetaData = new BZ98MetaData
            {
                Name = $"~game~pub~~Test Lobby {id}",
                Launched = launched,
                GameEnded = "0"
            },
            Stats = new BZ98LobbyData
            {
                MapFile = mapFile,
                Mod = "stock"
            },
            Users = playerNames
                .Select((name, index) => new BZ98User
                {
                    Id = $"S{index + 1}",
                    Name = name,
                    SteamCleanId = $"7656119800000000{index}",
                    IPAddress = $"192.0.2.{index + 1}"
                })
                .ToDictionary(user => user.Id!, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static BZ98Lobby ChatLobby(int id, int userCount) => new()
    {
        Id = id,
        IsChat = true,
        UserCount = userCount,
        MemberLimit = 20000,
        MetaData = new BZ98MetaData { Name = "default" }
    };
}
