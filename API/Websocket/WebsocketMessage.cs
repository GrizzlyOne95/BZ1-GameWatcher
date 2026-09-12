using BZAPI.Models;
using Newtonsoft.Json;

namespace BZAPI.Websocket
{
    public enum WebsocketMessageType
    {
        OnAuthorization = 0,
        OnLobbyListChanged = 1,
        OnLobbyChanged  = 2,
        OnLobbyRemoved = 3
    }

    public class WebsocketGenericMessage
    {
        [JsonProperty("type")]
        public string? Type { get; set; }
    }

    public class WebsocketBoolMessage : WebsocketGenericMessage
    {
        [JsonProperty("content")]
        public bool Content { get; set; }
    }

    public class WebsocketIntMessage : WebsocketGenericMessage
    {
        [JsonProperty("data")]
        public WebSocketIntData? Data { get; set; }
    }

    public class WebSocketIntData
    {
        [JsonProperty("id")]
        public int Id { get; set; }
    }

    public class WebsocketAuthMessage : WebsocketGenericMessage
    {
        [JsonProperty("content")]
        public WebsocketAuthMessageContent? Content { get; set; }
    }

    public class WebsocketLobbyMessage : WebsocketGenericMessage
    {
        [JsonProperty("data")]
        public WebsocketLobbyData? Data { get; set; }
    }

    public class WebsocketLobbyData
    {
        [JsonProperty("lobbies")]
        public Dictionary<string, BZ98Lobby>? BZ98Lobbies { get; set; }
    }

    public class WebsocketAuthMessageContent
    {
        [JsonProperty("authtype")]
        public string? AuthType { get; set; }

        [JsonProperty("key")]
        public string? Key { get; set; }

        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("apiVer")]
        public string? ApiVer { get; set; }

        [JsonProperty("clientVersion", NullValueHandling = NullValueHandling.Ignore)]
        public string? ClientVersion { get; set; }

        /// <summary>
        /// Public display name. The lobby server takes the user's top-level name from this field;
        /// a later <c>SetPlayerData</c> update only reaches the metadata bag. Omitted when null so
        /// an unnamed session stays byte-identical to the historical anonymous payload.
        /// </summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string? Name { get; set; }

        /// <summary>Sent alongside <see cref="Name"/>, matching the stock Web client.</summary>
        [JsonProperty("playerName", NullValueHandling = NullValueHandling.Ignore)]
        public string? PlayerName { get; set; }
    }
}
