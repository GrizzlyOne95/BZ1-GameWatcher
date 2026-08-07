using Newtonsoft.Json;

namespace BZAPI.Models
{
    /// <summary>
    /// Mirrors the lobby payload published by the Battlezone 98 Redux lobby server.
    /// </summary>
    /// <remarks>
    /// These are internal wire models and must not be returned from controllers — they carry
    /// player IP and LAN addresses. Project them onto the types in
    /// <see cref="Responses"/> before serving them.
    /// </remarks>
    public class BZ98Lobby
    {
        public BZ98Lobby()
        {
            MetaData = new();
            Stats = new();
            Users = [];
        }

        public int Id { get; set; }
        public string? ClientVersion { get; set; }
        public DateTimeOffset CreatedTime { get; set; }
        public bool IsChat { get; set; }
        public bool IsLocked { get; set; }
        public bool IsPrivate { get; set; }
        public int MemberLimit { get; set; }
        public string? Owner { get; set; }
        public int UserCount { get; set; }
        public string? DirectJoinUrl { get; set; }

        public BZ98User? Host { get; set; }
        public BZ98MetaData? MetaData { get; set; }
        public BZ98LobbyData? Stats { get; set; }
        public Dictionary<string, BZ98User>? Users { get; set; }
    }

    public class BZ98MetaData
    {
        public int LobbyId { get; set; }
        public string? GameVersion { get; set; }
        public string? GameSettings { get; set; }
        public string? GameType { get; set; }
        public string? Launched { get; set; }

        [JsonProperty("gameended")]
        public string? GameEnded { get; set; }

        public string? Name { get; set; }

        /// <summary>
        /// Original encoded lobby name before the watcher derives a display name. This retains the
        /// public/private/password marker tuple for diagnostics without exposing the actual
        /// password field from the upstream lobby object.
        /// </summary>
        [JsonIgnore]
        public string? RawName { get; set; }

        public string? NextMid { get; set; }
        public string? UserCount { get; set; }
        public string? UserPack { get; set; }
    }

    public class BZ98LobbyData
    {
        public BZ98LobbyData()
        {
            Attributes = new();
        }

        public int LobbyId { get; set; }
        public string? MapFile { get; set; }
        public string? CRC32 { get; set; }
        public string? Mod { get; set; }
        public int? MetaDataVersion { get; set; }
        public bool? SyncJoin { get; set; }
        public int? TimeLimit { get; set; }
        public int? PlayerLimit { get; set; }
        public int? KillLimit { get; set; }
        public BZ98LobbyDataAttributes? Attributes { get; set; }
    }

    public class BZ98LobbyDataAttributes
    {
        public string? Lives { get; set; }
        public bool? Satellite { get; set; }
        public bool? Barracks { get; set; }
        public bool? Sniper { get; set; }
        public bool? Splinter { get; set; }
    }

    public class BZ98User
    {
        public BZ98User()
        {
            MetaData = new();
            Stats = new();
        }

        public string? AuthType { get; set; }
        public string? ClientVersion { get; set; }
        public string? Id { get; set; }

        /// <summary>Personally identifying; never serialise to clients.</summary>
        public string? IPAddress { get; set; }

        public bool IsAdmin { get; set; }
        public bool IsAuth { get; set; }
        public bool IsBB { get; set; }
        public bool IsDangerous { get; set; }
        public bool IsInLounge { get; set; }
        public bool IsGOG { get; set; }
        public bool IsTest { get; set; }
        public bool IsSteam { get; set; }

        /// <summary>Personally identifying; never serialise to clients.</summary>
        public List<string>? LanAddresses { get; set; }

        public int Lobby { get; set; }
        public string? Name { get; set; }

        /// <summary>Personally identifying; never serialise to clients.</summary>
        public string? WANAddress { get; set; }

        public BZ98UserMetaData? MetaData { get; set; }
        public BZ98LobbyData? Stats { get; set; }
        public string? SteamImgUri { get; set; }
        public string? SteamCleanId { get; set; }
    }

    public class BZ98UserMetaData
    {
        public string? ClientsConnected { get; set; }
        public string? FriendId { get; set; }
        public string? KnownPlayers { get; set; }
        public string? Launched { get; set; }
        public string? MiniId { get; set; }
        public string? Ready { get; set; }
        public string? Team { get; set; }
        public string? Vehicle { get; set; }

        [JsonProperty("bzcp")]
        public string? CommunityPatch { get; set; }

        [JsonProperty("shim")]
        public string? CommunityPatchShim { get; set; }
    }
}
