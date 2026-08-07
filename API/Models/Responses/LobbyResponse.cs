using System.Text.Json.Serialization;

namespace BZAPI.Models.Responses
{
    /// <summary>
    /// The public shape of a lobby.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="BZ98Lobby"/>, which mirrors the upstream websocket
    /// payload and therefore carries data that must never be published — most importantly each
    /// player's IP and LAN addresses. Mapping through an explicit response type means a new field
    /// on the wire model cannot silently become public.
    /// </remarks>
    public sealed class LobbyResponse
    {
        public int Id { get; init; }
        public string? ClientVersion { get; init; }
        public DateTimeOffset CreatedTime { get; init; }
        public bool IsChat { get; init; }
        public bool IsLocked { get; init; }
        public bool IsPrivate { get; init; }
        public bool? HasPassword { get; init; }
        public int MemberLimit { get; init; }
        public string? Owner { get; init; }
        public int UserCount { get; init; }
        public string? DirectJoinUrl { get; init; }
        public UserResponse? Host { get; init; }
        public LobbyMetaDataResponse? MetaData { get; init; }
        public LobbyStatsResponse? Stats { get; init; }
        public WorkshopItemResponse? Workshop { get; init; }
        public MapMetadataResponse? Map { get; init; }
        public Dictionary<string, UserResponse> Users { get; init; } = [];
        public IReadOnlyList<ChatMessageResponse> RecentChat { get; init; } = [];
    }

    /// <summary>
    /// Public metadata for a Steam Workshop item referenced by a lobby. This is enrichment only;
    /// a failed Steam lookup leaves this null and never prevents the lobby itself from rendering.
    /// </summary>
    public sealed class WorkshopItemResponse
    {
        public string PublishedFileId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? PreviewUrl { get; init; }
        public string? CreatorSteamId { get; init; }
        public string? CreatorProfileUrl { get; init; }
        public string WorkshopUrl { get; init; } = string.Empty;
        public DateTimeOffset? UpdatedUtc { get; init; }
        public long? Subscriptions { get; init; }
    }

    /// <summary>
    /// Public display metadata for the current map. A null value only means the optional map
    /// metadata source could not recognize/respond for the map; the raw map filename remains in
    /// <see cref="LobbyStatsResponse.MapFile"/>.
    /// </summary>
    public sealed class MapMetadataResponse
    {
        public string MapFile { get; init; } = string.Empty;
        public string ModId { get; init; } = string.Empty;
        public bool IsStock { get; init; }
        public string? Title { get; init; }
        public string? ImageUrl { get; init; }
        public string? Description { get; init; }
        public int? MinPlayers { get; init; }
        public int? MaxPlayers { get; init; }
        public string? TypeCode { get; init; }
        public string? TypeLabel { get; init; }
        public string? ModeCode { get; init; }
        public string? ModeLabel { get; init; }
        public string? CustomTypeCode { get; init; }
        public string? CustomTypeName { get; init; }
    }

    public sealed class ChatMessageResponse
    {
        public string? Author { get; init; }
        public string? SpeakerId { get; init; }
        public string Text { get; init; } = string.Empty;
        public DateTimeOffset TimeUtc { get; init; }
    }

    public sealed class LobbyMetaDataResponse
    {
        public string? GameVersion { get; init; }
        public string? GameSettings { get; init; }
        public string? GameType { get; init; }
        public string? Launched { get; init; }
        public string? GameEnded { get; init; }
        public string? Name { get; init; }
        public string? RawName { get; init; }
        public string? NextMid { get; init; }
        public string? UserCount { get; init; }
        public string? UserPack { get; init; }
    }

    public sealed class LobbyStatsResponse
    {
        public string? MapFile { get; init; }

        // Without this the default camelCase policy emits "crC32", which no client expects.
        [JsonPropertyName("crc32")]
        public string? CRC32 { get; init; }
        public string? Mod { get; init; }
        public int? MetaDataVersion { get; init; }
        public bool? SyncJoin { get; init; }
        public int? TimeLimit { get; init; }
        public int? PlayerLimit { get; init; }
        public int? KillLimit { get; init; }
        public LobbyStatsAttributesResponse? Attributes { get; init; }
    }

    public sealed class LobbyStatsAttributesResponse
    {
        public string? Lives { get; init; }
        public bool? Satellite { get; init; }
        public bool? Barracks { get; init; }
        public bool? Sniper { get; init; }
        public bool? Splinter { get; init; }
    }

    /// <summary>
    /// The public shape of a player. Note the absence of IPAddress, WANAddress and LanAddresses.
    /// </summary>
    public sealed class UserResponse
    {
        public string? AuthType { get; init; }
        public string? ClientVersion { get; init; }
        public string? Id { get; init; }
        public bool IsAdmin { get; init; }
        public bool IsAuth { get; init; }
        public bool IsBB { get; init; }
        public bool IsDangerous { get; init; }
        public bool IsInLounge { get; init; }
        public bool IsGOG { get; init; }
        public bool IsTest { get; init; }
        public bool IsSteam { get; init; }
        public int Lobby { get; init; }
        public string? Name { get; init; }
        public UserMetaDataResponse? MetaData { get; init; }
        public LobbyStatsResponse? Stats { get; init; }
        public string? SteamImgUri { get; init; }
        public string? SteamCleanId { get; init; }
    }

    public sealed class UserMetaDataResponse
    {
        public string? ClientsConnected { get; init; }
        public string? FriendId { get; init; }
        public string? KnownPlayers { get; init; }
        public string? Launched { get; init; }
        public string? MiniId { get; init; }
        public string? Ready { get; init; }
        public string? Team { get; init; }
        public string? Vehicle { get; init; }
        public string? CommunityPatch { get; init; }
        public string? CommunityPatchShim { get; init; }
    }
}
