using BZAPI.Models;

namespace BZAPI.Protocol;

/// <summary>
/// Pure normalization for the reverse-engineered Battlezone 98 Redux lobby payload.
/// Keep protocol interpretation here so the live watcher and regression fixtures exercise the
/// same production code path.
/// </summary>
public static class BZ98ProtocolParser
{
    private const string ModNameSeparator = "~~";

    /// <summary>
    /// Normalizes the protocol fields that do not require external services. This must run before
    /// public-user filtering so the lobby owner snapshot is retained even when the owner is an
    /// intentionally hidden observer/service user.
    /// </summary>
    public static void NormalizeLobby(BZ98Lobby lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);

        if (lobby.MetaData is not null)
        {
            // Preserve the upstream envelope before deriving a friendlier display value. The raw
            // value contains the public password-marker bit, but never expose any actual password.
            lobby.MetaData.RawName = lobby.MetaData.Name;
            lobby.MetaData.Name = StripModPrefix(lobby.MetaData.Name);

            lobby.Stats ??= new BZ98LobbyData();
            ApplyGameSettings(lobby.MetaData.GameSettings, lobby.Stats);
        }

        if (lobby.Users is null)
        {
            return;
        }

        foreach (var pair in lobby.Users)
        {
            var user = pair.Value;
            if (user is null)
            {
                continue;
            }

            // Capture the owner before any optional hidden-user filtering. LobbyResponse maps Host
            // through UserResponse, so upstream IP/WAN/LAN fields still cannot escape publicly.
            if (user.Id is not null && user.Id == lobby.Owner)
            {
                lobby.Host = user;
            }

            NormalizeUser(user);
        }
    }

    /// <summary>
    /// Removes users that should not appear in Game Watcher's public roster and fixes the reported
    /// public count. Call <see cref="NormalizeLobby"/> first so Host has already been captured.
    /// </summary>
    public static int FilterPublicUsers(
        BZ98Lobby lobby,
        Func<string, BZ98User, bool> shouldHide)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        ArgumentNullException.ThrowIfNull(shouldHide);

        if (lobby.Users is null || lobby.Users.Count == 0)
        {
            return 0;
        }

        var removedFromPublicRoster = 0;

        foreach (var key in lobby.Users.Keys.ToList())
        {
            if (!lobby.Users.TryGetValue(key, out var user) || user is null)
            {
                lobby.Users.Remove(key);
                continue;
            }

            if (!shouldHide(key, user))
            {
                continue;
            }

            lobby.Users.Remove(key);
            removedFromPublicRoster++;
        }

        if (removedFromPublicRoster > 0)
        {
            lobby.UserCount = Math.Max(0, lobby.UserCount - removedFromPublicRoster);

            if (lobby.MetaData is not null)
            {
                lobby.MetaData.UserCount = lobby.UserCount.ToString();
            }
        }

        return removedFromPublicRoster;
    }

    /// <summary>
    /// Normalizes one upstream user. The protocol's authType field is authoritative; identifier
    /// prefixes are not platform classification.
    /// </summary>
    public static void NormalizeUser(BZ98User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        user.AuthType = NormalizeAuthType(user.AuthType);
        user.IsSteam = string.Equals(user.AuthType, "steam", StringComparison.OrdinalIgnoreCase);
        user.IsGOG = string.Equals(user.AuthType, "gog", StringComparison.OrdinalIgnoreCase);

        if (user.MetaData?.Ready is { Length: > 0 } ready)
        {
            user.Stats ??= new BZ98LobbyData();
            ApplyGameSettings(ready, user.Stats);
        }
    }

    /// <summary>
    /// Decodes the known 13-field Battlezone game-settings tuple. Field indexes are intentionally
    /// explicit here because this method doubles as executable protocol documentation:
    /// 0 metadata version, 1 map filename, 2 CRC32, 3 mod/Workshop ID, 4 sync join,
    /// 5 satellite, 6 barracks, 7 time limit, 8 lives, 9 player limit, 10 sniper,
    /// 11 kill limit, 12 splinter.
    /// Missing, empty, or malformed fields remain unknown/null.
    /// </summary>
    public static void ApplyGameSettings(string? settings, BZ98LobbyData target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (string.IsNullOrWhiteSpace(settings))
        {
            return;
        }

        var parts = settings.Split('*', StringSplitOptions.None);

        target.MetaDataVersion = ReadInt(parts, 0);
        target.MapFile = ReadString(parts, 1) ?? target.MapFile;
        target.CRC32 = ReadString(parts, 2) ?? target.CRC32;
        target.Mod = ReadString(parts, 3) ?? target.Mod;
        target.SyncJoin = ReadBool(parts, 4);
        target.TimeLimit = ReadInt(parts, 7);
        target.PlayerLimit = ReadInt(parts, 9);
        target.KillLimit = ReadInt(parts, 11);

        target.Attributes ??= new BZ98LobbyDataAttributes();
        target.Attributes.Satellite = ReadBool(parts, 5);
        target.Attributes.Barracks = ReadBool(parts, 6);
        target.Attributes.Lives = ReadString(parts, 8) ?? target.Attributes.Lives;
        target.Attributes.Sniper = ReadBool(parts, 10);
        target.Attributes.Splinter = ReadBool(parts, 12);
    }

    private static string? ReadString(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return null;
        }

        var value = parts[index].Trim();
        return value.Length == 0 ? null : value;
    }

    private static int? ReadInt(string[] parts, int index)
    {
        var value = ReadString(parts, index);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static bool? ReadBool(string[] parts, int index)
    {
        var value = ReadString(parts, index);
        return value switch
        {
            "0" => false,
            "1" => true,
            _ => null
        };
    }

    private static string? NormalizeAuthType(string? authType)
    {
        var normalized = authType?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized.ToLowerInvariant();
    }

    /// <summary>
    /// Lobby names can contain a "&lt;mod&gt;~~&lt;name&gt;" suffix. Preserve the historical parser
    /// behavior but keep it isolated from the websocket service so fixtures protect it.
    /// </summary>
    private static string? StripModPrefix(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var separator = name.IndexOf(ModNameSeparator, StringComparison.Ordinal);
        return separator < 0 ? name : name[(separator + ModNameSeparator.Length)..];
    }
}
