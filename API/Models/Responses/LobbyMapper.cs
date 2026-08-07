using BZAPI.Maps;
using BZAPI.Steam;
using BZAPI.Storage;

namespace BZAPI.Models.Responses
{
    /// <summary>
    /// Projects internal wire models onto the public API contract.
    /// </summary>
    public static class LobbyMapper
    {
        public static LobbyResponse ToResponse(
            this BZ98Lobby lobby,
            IReadOnlyList<ChatMessageSnapshot>? recentChat = null,
            SteamWorkshopItem? workshop = null,
            BZ98MapMetadata? map = null) => new()
        {
            Id = lobby.Id,
            ClientVersion = lobby.ClientVersion,
            CreatedTime = lobby.CreatedTime,
            IsChat = lobby.IsChat,
            IsLocked = lobby.IsLocked,
            IsPrivate = lobby.IsPrivate,
            HasPassword = ReadPasswordFlag(lobby.MetaData),
            MemberLimit = lobby.MemberLimit,
            Owner = lobby.Owner,
            UserCount = lobby.UserCount,
            DirectJoinUrl = lobby.DirectJoinUrl,
            Host = lobby.Host?.ToResponse(),
            MetaData = lobby.MetaData?.ToResponse(),
            Stats = lobby.Stats?.ToResponse(),
            Workshop = workshop?.ToResponse(),
            Map = map?.ToResponse(),
            Users = lobby.Users?
                .Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToResponse()) ?? [],
            RecentChat = recentChat?
                .Select(message => new ChatMessageResponse
                {
                    Author = message.Author,
                    SpeakerId = message.SpeakerId,
                    Text = message.Text,
                    TimeUtc = message.TimeUtc
                })
                .ToArray() ?? []
        };

        public static UserResponse ToResponse(this BZ98User user) => new()
        {
            AuthType = user.AuthType,
            ClientVersion = user.ClientVersion,
            Id = user.Id,
            IsAdmin = user.IsAdmin,
            IsAuth = user.IsAuth,
            IsBB = user.IsBB,
            IsDangerous = user.IsDangerous,
            IsInLounge = user.IsInLounge,
            IsGOG = user.IsGOG,
            IsTest = user.IsTest,
            IsSteam = user.IsSteam,
            Lobby = user.Lobby,
            Name = user.Name,
            MetaData = user.MetaData?.ToResponse(),
            Stats = user.Stats?.ToResponse(),
            SteamImgUri = user.SteamImgUri,
            SteamCleanId = user.SteamCleanId
        };

        private static WorkshopItemResponse ToResponse(this SteamWorkshopItem workshop) => new()
        {
            PublishedFileId = workshop.PublishedFileId,
            Title = workshop.Title,
            PreviewUrl = workshop.PreviewUrl,
            CreatorSteamId = workshop.CreatorSteamId,
            CreatorProfileUrl = workshop.CreatorProfileUrl,
            WorkshopUrl = workshop.WorkshopUrl,
            UpdatedUtc = workshop.UpdatedUtc,
            Subscriptions = workshop.Subscriptions
        };

        private static MapMetadataResponse ToResponse(this BZ98MapMetadata map) => new()
        {
            MapFile = map.MapFile,
            ModId = map.ModId,
            IsStock = map.IsStock,
            Title = map.Title,
            ImageUrl = map.ImageUrl,
            Description = map.Description,
            MinPlayers = map.MinPlayers,
            MaxPlayers = map.MaxPlayers,
            TypeCode = map.TypeCode,
            TypeLabel = map.TypeLabel,
            ModeCode = map.ModeCode,
            ModeLabel = map.ModeLabel,
            CustomTypeCode = map.CustomTypeCode,
            CustomTypeName = map.CustomTypeName
        };

        private static LobbyMetaDataResponse ToResponse(this BZ98MetaData metaData) => new()
        {
            GameVersion = metaData.GameVersion,
            GameSettings = metaData.GameSettings,
            GameType = metaData.GameType,
            Launched = metaData.Launched,
            GameEnded = metaData.GameEnded,
            Name = metaData.Name,
            RawName = metaData.RawName,
            NextMid = metaData.NextMid,
            UserCount = metaData.UserCount,
            UserPack = metaData.UserPack
        };

        private static LobbyStatsResponse ToResponse(this BZ98LobbyData stats) => new()
        {
            MapFile = stats.MapFile,
            CRC32 = stats.CRC32,
            Mod = stats.Mod,
            MetaDataVersion = stats.MetaDataVersion,
            SyncJoin = stats.SyncJoin,
            TimeLimit = stats.TimeLimit,
            PlayerLimit = stats.PlayerLimit,
            KillLimit = stats.KillLimit,
            Attributes = stats.Attributes is null ? null : new LobbyStatsAttributesResponse
            {
                Lives = stats.Attributes.Lives,
                Satellite = stats.Attributes.Satellite,
                Barracks = stats.Attributes.Barracks,
                Sniper = stats.Attributes.Sniper,
                Splinter = stats.Attributes.Splinter
            }
        };

        private static UserMetaDataResponse ToResponse(this BZ98UserMetaData metaData) => new()
        {
            ClientsConnected = metaData.ClientsConnected,
            FriendId = metaData.FriendId,
            KnownPlayers = metaData.KnownPlayers,
            Launched = metaData.Launched,
            MiniId = metaData.MiniId,
            Ready = metaData.Ready,
            Team = metaData.Team,
            Vehicle = metaData.Vehicle,
            CommunityPatch = metaData.CommunityPatch,
            CommunityPatchShim = metaData.CommunityPatchShim
        };

        private static bool? ReadPasswordFlag(BZ98MetaData? metaData)
        {
            var rawName = metaData?.RawName;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return null;
            }

            var parts = rawName.Split('~', 5, StringSplitOptions.None);
            if (parts.Length != 5 || parts[0].Length != 0 ||
                (parts[1] != "game" && parts[1] != "chat"))
            {
                return null;
            }

            // The public lobby-name envelope uses an empty string for no password and "*" for a
            // passworded lobby. We intentionally expose only the boolean state, never the upstream
            // password property itself.
            return parts[3].Length > 0;
        }
    }
}
