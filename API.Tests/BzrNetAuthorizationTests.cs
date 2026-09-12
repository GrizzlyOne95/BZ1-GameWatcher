using BZAPI.Websocket;
using Newtonsoft.Json.Linq;
using Xunit;

namespace API.Tests;

/// <summary>
/// Game Watcher documents itself as an openly identified observer. That promise is only kept if the
/// name reaches the lobby server's top-level user record, which happens in Authorization and not in
/// a later SetPlayerData update. These tests pin the payload so the identity cannot quietly regress
/// into the metadata bag again and leave the watcher showing as `unknown` to other players.
/// </summary>
public sealed class BzrNetAuthorizationTests
{
    private static JObject Content(string? playerName) =>
        (JObject)JObject.Parse(BzrNetAuthorization.Serialize(playerName))["content"]!;

    [Fact]
    public void DeclaredNameTravelsInTheAuthorizationPayload()
    {
        var content = Content("BZ1 Game Watcher (read-only)");

        Assert.Equal("BZ1 Game Watcher (read-only)", content["name"]?.ToString());
        Assert.Equal("BZ1 Game Watcher (read-only)", content["playerName"]?.ToString());
    }

    [Fact]
    public void AuthorizationKeepsTheAnonymousWebSessionFields()
    {
        var envelope = JObject.Parse(BzrNetAuthorization.Serialize("BZ1 Game Watcher"));
        var content = (JObject)envelope["content"]!;

        Assert.Equal("Authorization", envelope["type"]?.ToString());
        Assert.Equal("web", content["authtype"]?.ToString());
        Assert.Equal(string.Empty, content["key"]?.ToString());
        Assert.Equal("0", content["id"]?.ToString());
        Assert.Equal("0.0", content["apiVer"]?.ToString());
        Assert.Equal("2.2.301", content["clientVersion"]?.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankNameSendsNoNameRatherThanAnEmptyOne(string? playerName)
    {
        var content = Content(playerName);

        // An empty string would claim a blank display name upstream. Omitting the fields keeps the
        // historical anonymous payload available as a deliberate configuration choice.
        Assert.False(content.ContainsKey("name"));
        Assert.False(content.ContainsKey("playerName"));
    }

    [Fact]
    public void SurroundingWhitespaceIsNotDeclaredAsPartOfTheName()
    {
        Assert.Equal("BZ1 Game Watcher", Content("  BZ1 Game Watcher  ")["name"]?.ToString());
    }
}
