using System.Text.Json;
using BZAPI.Models.Responses;
using BZAPI.Protocol;
using BZAPI.Storage;
using Xunit;

namespace BZAPI.Tests;

public sealed class ProtocolFixtureTests
{
    [Fact]
    public void Auth_type_is_authoritative_for_web_gog_and_steam_users()
    {
        var lobby = FixtureLoader.LoadLobby("users-mixed-auth-types.json");

        var web = Assert.IsType<BZAPI.Models.BZ98User>(lobby.Users!["B1000002"]);
        Assert.Equal("web", web.AuthType);
        Assert.False(web.IsSteam);
        Assert.False(web.IsGOG);

        var gog = Assert.IsType<BZAPI.Models.BZ98User>(lobby.Users["G1000001"]);
        Assert.Equal("gog", gog.AuthType);
        Assert.True(gog.IsGOG);
        Assert.False(gog.IsSteam);

        var steam = Assert.IsType<BZAPI.Models.BZ98User>(lobby.Users["S76561198000000004"]);
        Assert.Equal("steam", steam.AuthType);
        Assert.True(steam.IsSteam);
        Assert.False(steam.IsGOG);
    }

    [Fact]
    public void Host_snapshot_works_for_steam_and_web_owners()
    {
        var steamLobby = FixtureLoader.LoadLobby("game-stock-waiting.json");
        var webLobby = FixtureLoader.LoadLobby("chat-default-web-owner.json");

        Assert.Equal("S76561198000000001", steamLobby.Host?.Id);
        Assert.Equal("steam", steamLobby.Host?.AuthType);
        Assert.True(steamLobby.Host?.IsSteam);

        Assert.Equal("B1000002", webLobby.Host?.Id);
        Assert.Equal("web", webLobby.Host?.AuthType);
        Assert.False(webLobby.Host?.IsSteam);
        Assert.False(webLobby.Host?.IsGOG);
    }

    [Fact]
    public void Full_game_settings_tuple_maps_all_known_fields()
    {
        var lobby = FixtureLoader.LoadLobby("game-stock-waiting.json");
        var stats = Assert.IsType<BZAPI.Models.BZ98LobbyData>(lobby.Stats);
        var attributes = Assert.IsType<BZAPI.Models.BZ98LobbyDataAttributes>(stats.Attributes);

        // 0 metadata version
        Assert.Equal(1, stats.MetaDataVersion);
        // 1 map filename
        Assert.Equal("dm01.bzn", stats.MapFile);
        // 2 CRC32
        Assert.Equal("89ABCDEF", stats.CRC32);
        // 3 mod / Workshop ID
        Assert.Equal("0", stats.Mod);
        // 4 sync join
        Assert.True(stats.SyncJoin);
        // 5 satellite
        Assert.True(attributes.Satellite);
        // 6 barracks
        Assert.False(attributes.Barracks);
        // 7 time limit
        Assert.Equal(20, stats.TimeLimit);
        // 8 lives
        Assert.Equal("5", attributes.Lives);
        // 9 player limit
        Assert.Equal(8, stats.PlayerLimit);
        // 10 sniper
        Assert.True(attributes.Sniper);
        // 11 kill limit
        Assert.Equal(30, stats.KillLimit);
        // 12 splinter
        Assert.False(attributes.Splinter);
    }

    [Fact]
    public void Truncated_game_settings_keep_unreported_fields_unknown()
    {
        var lobby = FixtureLoader.LoadLobby("game-partial-metadata.json");
        var stats = Assert.IsType<BZAPI.Models.BZ98LobbyData>(lobby.Stats);
        var attributes = Assert.IsType<BZAPI.Models.BZ98LobbyDataAttributes>(stats.Attributes);

        Assert.Equal(1, stats.MetaDataVersion);
        Assert.Equal("partial.bzn", stats.MapFile);
        Assert.Equal("00FFAA", stats.CRC32);
        Assert.Null(stats.Mod);
        Assert.Null(stats.SyncJoin);
        Assert.Null(stats.TimeLimit);
        Assert.Null(stats.PlayerLimit);
        Assert.Null(stats.KillLimit);
        Assert.Null(attributes.Satellite);
        Assert.Null(attributes.Barracks);
        Assert.Null(attributes.Lives);
        Assert.Null(attributes.Sniper);
        Assert.Null(attributes.Splinter);
    }

    [Fact]
    public void Game_type_one_is_not_labeled_strategy_without_map_enrichment()
    {
        var lobby = FixtureLoader.LoadLobby("game-stock-waiting.json");
        var response = lobby.ToResponse();

        Assert.Equal("1", response.MetaData?.GameType);
        Assert.Null(response.Map);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("Strategy", json);
    }

    [Fact]
    public void Launch_and_ended_values_preserve_reported_tristate()
    {
        var waiting = FixtureLoader.LoadLobby("game-stock-waiting.json").ToResponse();
        var launched = FixtureLoader.LoadLobby("game-stock-launched.json").ToResponse();
        var unknown = FixtureLoader.LoadLobby("game-partial-metadata.json").ToResponse();

        Assert.Equal("0", waiting.MetaData?.Launched);
        Assert.Null(waiting.MetaData?.GameEnded);

        Assert.Equal("1", launched.MetaData?.Launched);
        Assert.Equal("0", launched.MetaData?.GameEnded);

        Assert.Null(unknown.MetaData?.Launched);
        Assert.Null(unknown.MetaData?.GameEnded);
    }

    [Fact]
    public void Password_state_is_reduced_to_nullable_boolean()
    {
        var open = FixtureLoader.LoadLobby("game-stock-waiting.json").ToResponse();
        var passworded = FixtureLoader.LoadLobby("game-passworded.json").ToResponse();

        Assert.False(open.HasPassword);
        Assert.True(passworded.HasPassword);
        Assert.DoesNotContain(
            typeof(LobbyResponse).GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(property.Name, nameof(LobbyResponse.HasPassword), StringComparison.Ordinal));
    }

    [Fact]
    public void Host_snapshot_survives_optional_owner_filtering()
    {
        var lobby = FixtureLoader.LoadLobby("chat-default-web-owner.json");

        var removed = BZ98ProtocolParser.FilterPublicUsers(
            lobby,
            (_, user) => user.Id == "B1000002");

        Assert.Equal(1, removed);
        Assert.NotNull(lobby.Host);
        Assert.Equal("B1000002", lobby.Host!.Id);
        Assert.Equal("web", lobby.Host.AuthType);
        Assert.False(lobby.Users!.ContainsKey("B1000002"));
        Assert.Equal(1, lobby.UserCount);
        Assert.Equal("1", lobby.MetaData?.UserCount);
    }

    [Fact]
    public void Read_only_observer_does_not_inflate_visible_participant_count()
    {
        var lobby = FixtureLoader.LoadLobby("chat-default-web-owner.json");

        var removed = BZ98ProtocolParser.FilterPublicUsers(
            lobby,
            (_, user) => user.Id == "B9000001");

        var users = lobby.Users!;
        Assert.Equal(1, removed);
        Assert.Single(users);
        Assert.True(users.ContainsKey("B1000002"));
        Assert.Equal(1, lobby.UserCount);
        Assert.Equal("1", lobby.MetaData?.UserCount);
    }

    [Fact]
    public void Public_chat_history_maps_only_safe_fields()
    {
        var lobby = FixtureLoader.LoadLobby("chat-default-web-owner.json");
        var chat = new[]
        {
            new ChatMessageSnapshot(
                lobby.Id,
                "Synthetic Speaker",
                "B1000002",
                "Fixture hello",
                new DateTimeOffset(2026, 8, 7, 12, 16, 0, TimeSpan.Zero))
        };

        var response = lobby.ToResponse(chat);
        var message = Assert.Single(response.RecentChat);

        Assert.Equal("Synthetic Speaker", message.Author);
        Assert.Equal("B1000002", message.SpeakerId);
        Assert.Equal("Fixture hello", message.Text);
        Assert.Equal(new DateTimeOffset(2026, 8, 7, 12, 16, 0, TimeSpan.Zero), message.TimeUtc);

        Assert.Equal(
            new[] { "Author", "SpeakerId", "Text", "TimeUtc" },
            typeof(ChatMessageResponse).GetProperties().Select(property => property.Name).Order().ToArray());
    }

    [Fact]
    public void Public_response_contract_cannot_expose_upstream_network_addresses()
    {
        var lobby = FixtureLoader.LoadLobby("users-mixed-auth-types.json");
        var response = lobby.ToResponse();
        var userProperties = typeof(UserResponse).GetProperties().Select(property => property.Name).ToArray();
        var lobbyProperties = typeof(LobbyResponse).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("IPAddress", userProperties);
        Assert.DoesNotContain("WANAddress", userProperties);
        Assert.DoesNotContain("LanAddresses", userProperties);
        Assert.DoesNotContain("IPAddress", lobbyProperties);
        Assert.DoesNotContain("WANAddress", lobbyProperties);
        Assert.DoesNotContain("LanAddresses", lobbyProperties);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var lowerJson = json.ToLowerInvariant();
        Assert.DoesNotContain("ipaddress", lowerJson);
        Assert.DoesNotContain("wanaddress", lowerJson);
        Assert.DoesNotContain("lanaddresses", lowerJson);
        Assert.DoesNotContain("192.0.2.", json);
        Assert.DoesNotContain("198.51.100.", json);
        Assert.DoesNotContain("203.0.113.", json);
        Assert.DoesNotContain("2001:db8", lowerJson);
    }

    [Fact]
    public void Unknown_and_malformed_optional_fields_do_not_break_base_lobby_mapping()
    {
        var exception = Record.Exception(() =>
        {
            var lobby = FixtureLoader.LoadLobby("malformed-or-unknown-fields.json");
            var response = lobby.ToResponse();

            Assert.Equal(106, response.Id);
            Assert.Equal("unexpected", response.MetaData?.GameType);
            Assert.Empty(response.Users);
            Assert.Null(response.Stats?.MetaDataVersion);
            Assert.Null(response.Stats?.SyncJoin);
            Assert.Null(response.Stats?.TimeLimit);
        });

        Assert.Null(exception);
    }
}
