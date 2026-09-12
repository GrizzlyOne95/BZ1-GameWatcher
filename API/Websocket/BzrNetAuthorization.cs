using Newtonsoft.Json;

namespace BZAPI.Websocket;

/// <summary>
/// Builds the BZRNet <c>Authorization</c> payload shared by the lounge watcher and every read-only
/// chat observer.
/// </summary>
/// <remarks>
/// Public identity has to travel in this message, not in a later <c>SetPlayerData</c> update.
/// <c>SetPlayerData</c> writes the server-side user *metadata* bag: after sending it the admin
/// roster still showed Game Watcher as <c>unknown</c> with <c>{"name":"BZ1 Game Watcher
/// (read-only)", ...}</c> in the metadata column, and the lobby <c>userPack</c> omitted us
/// entirely. Accounts that do appear under their own name — the community <c>!BRIDGE</c> bridge is
/// the visible example — carry <c>name</c>/<c>playerName</c> in the Authorization content, which is
/// also what <c>Battlezone_LobbyMonitor</c> sends. Game Watcher documents itself as an openly
/// identified observer, so the name has to land where other players actually read it.
/// </remarks>
internal static class BzrNetAuthorization
{
    /// <summary>
    /// Client version reported to the lobby server. Matches the BZ98R build the public Web client
    /// advertises so Game Watcher is not classified as an unknown-version client.
    /// </summary>
    public const string ClientVersion = "2.2.301";

    /// <summary>
    /// Builds the Authorization message for an anonymous Web session.
    /// </summary>
    /// <param name="playerName">
    /// Public identity to declare. A blank value sends no name at all rather than an empty one,
    /// which keeps the previous anonymous behavior available as an explicit configuration choice.
    /// </param>
    public static WebsocketAuthMessage Create(string? playerName)
    {
        var trimmed = playerName?.Trim();
        var declared = string.IsNullOrEmpty(trimmed) ? null : trimmed;

        return new WebsocketAuthMessage
        {
            Type = "Authorization",
            Content = new WebsocketAuthMessageContent
            {
                AuthType = "web",
                Key = string.Empty,
                Id = "0",
                ApiVer = "0.0",
                ClientVersion = ClientVersion,
                Name = declared,
                PlayerName = declared
            }
        };
    }

    /// <summary>Serializes <see cref="Create"/> for sending on the wire.</summary>
    public static string Serialize(string? playerName) =>
        JsonConvert.SerializeObject(Create(playerName));
}
