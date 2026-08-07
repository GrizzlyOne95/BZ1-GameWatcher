using BZAPI.Controllers;
using BZAPI.Maps;
using BZAPI.Models.Responses;
using BZAPI.Steam;
using BZAPI.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BZAPI.Tests;

public sealed class EnrichmentFallbackTests
{
    [Fact]
    public async Task Stock_lobby_remains_available_without_external_enrichment()
    {
        var lobby = FixtureLoader.LoadLobby("game-stock-waiting.json");
        var workshop = new StubWorkshopProvider(null);
        var maps = new StubMapProvider(null);
        var controller = CreateController(lobby, workshop, maps);

        var responses = await GetResponsesAsync(controller);
        var response = Assert.Single(responses);

        Assert.Equal("dm01.bzn", response.Stats?.MapFile);
        Assert.Equal("0", response.Stats?.Mod);
        Assert.Null(response.Workshop);
        Assert.Null(response.Map);
        Assert.Empty(workshop.Requests);
        Assert.Equal(new[] { ("dm01.bzn", "0") }, maps.Requests);
    }

    [Fact]
    public async Task Numeric_workshop_and_map_metadata_can_enrich_the_base_lobby()
    {
        var lobby = FixtureLoader.LoadLobby("game-workshop.json");
        var workshop = new StubWorkshopProvider(new SteamWorkshopItem(
            "2898000000",
            "Synthetic Workshop Item",
            "https://example.invalid/workshop-preview.jpg",
            "76561198000000999",
            null,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            42));
        var maps = new StubMapProvider(new BZ98MapMetadata(
            "custom01.bzn",
            "2898000000",
            false,
            "Synthetic Custom Map",
            "https://example.invalid/map-preview.jpg",
            "Fixture-only enrichment",
            2,
            10,
            "M",
            "Strategy",
            "M",
            "Mission MPI",
            null,
            null));
        var controller = CreateController(lobby, workshop, maps);

        var responses = await GetResponsesAsync(controller);
        var response = Assert.Single(responses);

        Assert.Equal(new ulong[] { 2898000000 }, workshop.Requests);
        Assert.Equal(new[] { ("custom01.bzn", "2898000000") }, maps.Requests);
        Assert.Equal("Synthetic Workshop Item", response.Workshop?.Title);
        Assert.Equal("Synthetic Custom Map", response.Map?.Title);
        Assert.Equal("Mission MPI", response.Map?.ModeLabel);
        Assert.Equal("custom01.bzn", response.Stats?.MapFile);
        Assert.Equal("2898000000", response.Stats?.Mod);
    }

    [Fact]
    public async Task Workshop_and_map_enrichment_misses_never_remove_the_lobby()
    {
        var lobby = FixtureLoader.LoadLobby("game-workshop.json");
        var workshop = new StubWorkshopProvider(null);
        var maps = new StubMapProvider(null);
        var controller = CreateController(lobby, workshop, maps);

        var responses = await GetResponsesAsync(controller);
        var response = Assert.Single(responses);

        Assert.Single(workshop.Requests);
        Assert.Single(maps.Requests);
        Assert.Null(response.Workshop);
        Assert.Null(response.Map);
        Assert.Equal("custom01.bzn", response.Stats?.MapFile);
        Assert.Equal("2898000000", response.Stats?.Mod);
    }

    private static BZ98LobbyController CreateController(
        BZAPI.Models.BZ98Lobby lobby,
        ISteamWorkshopProvider workshopProvider,
        IMapMetadataProvider mapProvider)
    {
        var store = new LobbyStore();
        store.Replace([lobby]);

        return new BZ98LobbyController(
            store,
            new EmptyChatStore(),
            workshopProvider,
            mapProvider,
            NullLogger<BZ98LobbyController>.Instance);
    }

    private static async Task<LobbyResponse[]> GetResponsesAsync(BZ98LobbyController controller)
    {
        var action = await controller.GetLobbies(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        return Assert.IsType<LobbyResponse[]>(ok.Value);
    }

    private sealed class StubWorkshopProvider(SteamWorkshopItem? result) : ISteamWorkshopProvider
    {
        public List<ulong> Requests { get; } = [];

        public Task<SteamWorkshopItem?> GetItemAsync(
            ulong publishedFileId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(publishedFileId);
            return Task.FromResult(result);
        }
    }

    private sealed class StubMapProvider(BZ98MapMetadata? result) : IMapMetadataProvider
    {
        public List<(string MapFile, string ModId)> Requests { get; } = [];

        public Task<BZ98MapMetadata?> GetMapAsync(
            string mapFile,
            string modId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add((mapFile, modId));
            return Task.FromResult(result);
        }
    }

    private sealed class EmptyChatStore : IChatStore
    {
        public IReadOnlyList<ChatMessageSnapshot> GetRecent(int lobbyId) => [];

        public void Add(ChatMessageSnapshot message)
        {
        }

        public void SetObserverUserId(int lobbyId, string? userId)
        {
        }

        public bool IsObserverUser(int lobbyId, string? userId) => false;

        public void RemoveLobby(int lobbyId)
        {
        }
    }
}
