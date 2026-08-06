using BZAPI.Configuration;
using BZAPI.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Websocket.Client;

namespace BZAPI.Bot;

public sealed record LobbyBotStatus(
    bool Enabled,
    bool Configured,
    bool Connected,
    string PlayerName,
    string LobbyName,
    string? OwnId,
    int? CurrentLobbyId,
    int? TargetLobbyId,
    string? LastAction,
    DateTimeOffset? LastActionUtc);

/// <summary>
/// Owns the optional interactive lobby-bot state while <see cref="Websocket.BZ98LobbyWatcher"/>
/// continues to own the underlying socket and public lobby snapshot.
/// </summary>
public sealed class LobbyBotCoordinator
{
    private const string ChatLobbyPrefix = "~chat~pub~~";
    private const string ClientVersion = "2.2.301";

    private readonly object _sync = new();
    private readonly LobbyBotOptions _options;
    private readonly ILogger<LobbyBotCoordinator> _logger;
    private readonly Dictionary<string, DateTimeOffset> _lastWelcomeByUser =
        new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string> _knownTargetUsers = new(StringComparer.OrdinalIgnoreCase);
    private string? _ownId;
    private int? _currentLobbyId;
    private int? _targetLobbyId;
    private bool _connected;
    private bool _hasSeenFullLobbyList;
    private bool _hasTargetUserSnapshot;
    private bool _joinPending;
    private bool _createPending;
    private DateTimeOffset _lastClaimAttemptUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAnnouncementUtc = DateTimeOffset.MinValue;
    private string? _lastAction;
    private DateTimeOffset? _lastActionUtc;

    public LobbyBotCoordinator(
        IOptions<LobbyBotOptions> options,
        ILogger<LobbyBotCoordinator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    public LobbyBotStatus Status
    {
        get
        {
            lock (_sync)
            {
                return BuildStatusLocked();
            }
        }
    }

    public void OnSocketConnected()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_sync)
        {
            _connected = true;
            ResetSessionLocked();
            RecordActionLocked("Lobby WebSocket connected; waiting for authorization.");
        }
    }

    public void OnSocketDisconnected()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_sync)
        {
            _connected = false;
            ResetSessionLocked();
            RecordActionLocked("Lobby WebSocket disconnected.");
        }
    }

    public void OnAuthorized(IWebsocketClient client, string messageText)
    {
        if (!CanRun())
        {
            return;
        }

        try
        {
            var data = JObject.Parse(messageText)["data"] as JObject;
            if (ReadBoolean(data?["success"]) is false)
            {
                _logger.LogWarning("Lobby bot authorization was rejected.");
                return;
            }

            var ownId = data?["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(ownId))
            {
                _logger.LogWarning("Lobby bot authorization succeeded without returning a user ID.");
                return;
            }

            lock (_sync)
            {
                _ownId = ownId;
                RecordActionLocked("Authorized; setting bot identity and requesting the lobby list.");
            }

            SetPlayerIdentity(client);
            Send(client, new { type = "GetLobbyList", content = true }, "Requested lobby list.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse lobby-bot authorization response.");
        }
    }

    public void OnLobbySnapshot(
        IWebsocketClient client,
        IReadOnlyCollection<BZ98Lobby> lobbies,
        bool isFullList)
    {
        if (!CanRun())
        {
            return;
        }

        var target = lobbies.FirstOrDefault(IsTargetLobby);
        BZ98Lobby? current = null;
        string? ownId;

        lock (_sync)
        {
            ownId = _ownId;
        }

        if (!string.IsNullOrWhiteSpace(ownId))
        {
            current = lobbies.FirstOrDefault(lobby => LobbyContainsUser(lobby, ownId));
        }

        List<(string Id, string Name)> welcomeCandidates = [];
        int? joinLobbyId = null;
        var shouldCreate = false;

        lock (_sync)
        {
            if (isFullList)
            {
                _hasSeenFullLobbyList = true;
            }

            _targetLobbyId = target?.Id;
            _currentLobbyId = current?.Id;

            if (target is not null)
            {
                _createPending = false;
            }

            if (current is not null)
            {
                _joinPending = false;
            }

            if (target is not null && current?.Id == target.Id)
            {
                var currentUsers = GetUserIdentities(target)
                    .Where(user => !IsOwnUser(user.Id))
                    .ToDictionary(user => user.Id, user => user.Name, StringComparer.OrdinalIgnoreCase);

                if (_hasTargetUserSnapshot)
                {
                    welcomeCandidates.AddRange(
                        currentUsers
                            .Where(user => !_knownTargetUsers.Contains(user.Key))
                            .Select(user => (user.Key, user.Value)));
                }

                _knownTargetUsers = currentUsers.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                _hasTargetUserSnapshot = true;
            }
            else
            {
                _knownTargetUsers.Clear();
                _hasTargetUserSnapshot = false;
            }

            if (current is null && target is not null && !_joinPending)
            {
                _joinPending = true;
                joinLobbyId = target.Id;
            }
            else if (
                current is null &&
                target is null &&
                _options.AutoClaim &&
                _hasSeenFullLobbyList &&
                !_createPending &&
                DateTimeOffset.UtcNow - _lastClaimAttemptUtc >= TimeSpan.FromSeconds(10))
            {
                _createPending = true;
                _lastClaimAttemptUtc = DateTimeOffset.UtcNow;
                shouldCreate = true;
            }
            else if (current is not null && target is not null && current.Id != target.Id)
            {
                RecordActionLocked(
                    $"Bot is already in lobby {current.Id}; target lobby {target.Id} was not joined.");
            }
        }

        if (joinLobbyId is not null)
        {
            JoinLobby(client, joinLobbyId.Value);
        }
        else if (shouldCreate)
        {
            CreateLobby(client);
        }

        foreach (var user in welcomeCandidates)
        {
            TrySendWelcome(client, target?.Id, user.Id, user.Name);
        }
    }

    public void OnLobbyJoined(string messageText)
    {
        if (!CanRun())
        {
            return;
        }

        UpdateCurrentLobbyFromResult(messageText, "Joined lobby");
    }

    public void OnLobbyCreated(IWebsocketClient client, string messageText)
    {
        if (!CanRun())
        {
            return;
        }

        var lobbyId = UpdateCurrentLobbyFromResult(messageText, "Created lobby");
        if (lobbyId is not null)
        {
            SetLobbyMetadata(client);
        }
    }

    public void OnLobbyRemoved(string messageText)
    {
        if (!CanRun())
        {
            return;
        }

        try
        {
            var removedId = ReadInt(JObject.Parse(messageText)["data"]?["id"]);
            if (removedId is null)
            {
                return;
            }

            lock (_sync)
            {
                if (_targetLobbyId == removedId)
                {
                    _targetLobbyId = null;
                    _knownTargetUsers.Clear();
                    _hasTargetUserSnapshot = false;
                    _createPending = false;
                }

                if (_currentLobbyId == removedId)
                {
                    _currentLobbyId = null;
                    _joinPending = false;
                }

                RecordActionLocked($"Lobby {removedId} was removed.");
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Could not parse lobby removal for bot state.");
        }
    }

    public void OnMemberListChanged(IWebsocketClient client, string messageText)
    {
        if (!CanRun())
        {
            return;
        }

        try
        {
            var data = JObject.Parse(messageText)["data"] as JObject;
            if (data is null || ReadBoolean(data["removed"]) is true)
            {
                return;
            }

            var lobbyId = ReadInt(data["lobbyId"]);
            var userId = data["id"]?.ToString() ?? data["member"]?.ToString();
            var playerName = data["member"]?.ToString() ?? userId;

            if (lobbyId is null || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(playerName))
            {
                return;
            }

            TrySendWelcome(client, lobbyId, userId, playerName);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Could not parse lobby membership change for bot greeting.");
        }
    }

    public async Task RunAsync(IWebsocketClient client, CancellationToken cancellationToken)
    {
        if (!CanRun())
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var sendAnnouncement = false;
                var createLobby = false;
                var now = DateTimeOffset.UtcNow;

                lock (_sync)
                {
                    if (
                        _currentLobbyId is not null &&
                        _targetLobbyId == _currentLobbyId &&
                        !string.IsNullOrWhiteSpace(_options.AnnouncementMessage) &&
                        _options.AnnouncementInterval > TimeSpan.Zero &&
                        now - _lastAnnouncementUtc >= _options.AnnouncementInterval)
                    {
                        _lastAnnouncementUtc = now;
                        sendAnnouncement = true;
                    }

                    if (
                        _currentLobbyId is null &&
                        _targetLobbyId is null &&
                        _options.AutoClaim &&
                        _hasSeenFullLobbyList &&
                        !_createPending &&
                        now - _lastClaimAttemptUtc >= TimeSpan.FromSeconds(10))
                    {
                        _createPending = true;
                        _lastClaimAttemptUtc = now;
                        createLobby = true;
                    }
                }

                if (sendAnnouncement)
                {
                    SendChat(client, _options.AnnouncementMessage, "Sent timed lobby announcement.");
                }

                if (createLobby)
                {
                    CreateLobby(client);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private bool CanRun()
    {
        if (!Enabled)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_options.PlayerName) &&
            !string.IsNullOrWhiteSpace(_options.LobbyName))
        {
            return true;
        }

        _logger.LogWarning(
            "LobbyBot is enabled but PlayerName or LobbyName is empty; bot automation is disabled.");
        return false;
    }

    private int? UpdateCurrentLobbyFromResult(string messageText, string action)
    {
        try
        {
            var data = JObject.Parse(messageText)["data"] as JObject;
            if (ReadBoolean(data?["success"]) is false)
            {
                lock (_sync)
                {
                    _joinPending = false;
                    _createPending = false;
                    RecordActionLocked($"{action} failed: {data?["reason"]}");
                }

                return null;
            }

            var lobbyId = ReadInt(data?["id"]);
            if (lobbyId is null)
            {
                return null;
            }

            lock (_sync)
            {
                _currentLobbyId = lobbyId;
                _targetLobbyId ??= lobbyId;
                _joinPending = false;
                _createPending = false;
                _knownTargetUsers.Clear();
                _hasTargetUserSnapshot = false;
                RecordActionLocked($"{action} {lobbyId}.");
            }

            return lobbyId;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Could not parse lobby join/create result for bot state.");
            return null;
        }
    }

    private void SetPlayerIdentity(IWebsocketClient client)
    {
        var updates = new[]
        {
            new { key = "name", value = _options.PlayerName },
            new { key = "playerName", value = _options.PlayerName },
            new { key = "clientVersion", value = ClientVersion },
            new { key = "authType", value = "web" }
        };

        foreach (var update in updates)
        {
            Send(client, new { type = "SetPlayerData", content = update }, null);
        }
    }

    private void JoinLobby(IWebsocketClient client, int lobbyId)
    {
        Send(
            client,
            new { type = "DoJoinLobby", content = new { id = lobbyId, password = string.Empty } },
            $"Requested join to lobby {lobbyId}.");
    }

    private void CreateLobby(IWebsocketClient client)
    {
        Send(
            client,
            new
            {
                type = "CreateLobby",
                content = new
                {
                    name = ChatLobbyPrefix + _options.LobbyName,
                    isPrivate = false,
                    memberLimit = Math.Max(2, _options.MemberLimit),
                    password = string.Empty
                }
            },
            $"Requested creation of chat lobby '{_options.LobbyName}'.");
    }

    private void SetLobbyMetadata(IWebsocketClient client)
    {
        var updates = new[]
        {
            new { key = "clientVersion", value = ClientVersion },
            new { key = "GameVersion", value = ClientVersion },
            new { key = "gameType", value = "1" },
            new { key = "gameSettings", value = "*" },
            new { key = "name", value = ChatLobbyPrefix + _options.LobbyName }
        };

        foreach (var update in updates)
        {
            Send(client, new { type = "SetLobbyData", content = update }, null);
        }

        RecordAction($"Configured chat lobby '{_options.LobbyName}'.");
    }

    private void TrySendWelcome(
        IWebsocketClient client,
        int? lobbyId,
        string userId,
        string playerName)
    {
        if (string.IsNullOrWhiteSpace(_options.WelcomeMessage) || IsOwnUser(userId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (lobbyId is null || lobbyId != _currentLobbyId || lobbyId != _targetLobbyId)
            {
                return;
            }

            if (_lastWelcomeByUser.TryGetValue(userId, out var previous) &&
                now - previous < _options.WelcomeCooldown)
            {
                return;
            }

            _lastWelcomeByUser[userId] = now;
        }

        var message = _options.WelcomeMessage.Replace(
            "{player}",
            playerName,
            StringComparison.OrdinalIgnoreCase);
        SendChat(client, message, $"Welcomed {playerName}.");
    }

    private void SendChat(IWebsocketClient client, string message, string action)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Send(client, new { type = "DoSendChat", content = message.Trim() }, action);
    }

    private void Send(IWebsocketClient client, object payload, string? action)
    {
        try
        {
            client.Send(JsonConvert.SerializeObject(payload));
            if (!string.IsNullOrWhiteSpace(action))
            {
                RecordAction(action);
                _logger.LogInformation("Lobby bot: {Action}", action);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lobby bot could not send a WebSocket command.");
            RecordAction("WebSocket command failed; see server logs.");
        }
    }

    private bool IsTargetLobby(BZ98Lobby lobby) =>
        string.Equals(
            CleanLobbyName(lobby.MetaData?.Name),
            _options.LobbyName,
            StringComparison.OrdinalIgnoreCase);

    private static string? CleanLobbyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var separator = name.IndexOf("~~", StringComparison.Ordinal);
        return separator < 0 ? name : name[(separator + 2)..];
    }

    private static bool LobbyContainsUser(BZ98Lobby lobby, string ownId)
    {
        if (lobby.Users is null)
        {
            return false;
        }

        return lobby.Users.ContainsKey(ownId) ||
               lobby.Users.Values.Any(user =>
                   string.Equals(user?.Id, ownId, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<(string Id, string Name)> GetUserIdentities(BZ98Lobby lobby)
    {
        if (lobby.Users is null)
        {
            yield break;
        }

        foreach (var pair in lobby.Users)
        {
            if (pair.Value is null)
            {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(pair.Value.Id) ? pair.Key : pair.Value.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(pair.Value.Name) ? id : pair.Value.Name;
            yield return (id, name);
        }
    }

    private bool IsOwnUser(string userId)
    {
        lock (_sync)
        {
            return !string.IsNullOrWhiteSpace(_ownId) &&
                   string.Equals(_ownId, userId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ResetSessionLocked()
    {
        _ownId = null;
        _currentLobbyId = null;
        _targetLobbyId = null;
        _hasSeenFullLobbyList = false;
        _hasTargetUserSnapshot = false;
        _joinPending = false;
        _createPending = false;
        _knownTargetUsers.Clear();
    }

    private void RecordAction(string action)
    {
        lock (_sync)
        {
            RecordActionLocked(action);
        }
    }

    private void RecordActionLocked(string action)
    {
        _lastAction = action;
        _lastActionUtc = DateTimeOffset.UtcNow;
    }

    private LobbyBotStatus BuildStatusLocked() =>
        new(
            Enabled,
            !string.IsNullOrWhiteSpace(_options.PlayerName) &&
            !string.IsNullOrWhiteSpace(_options.LobbyName),
            _connected,
            _options.PlayerName,
            _options.LobbyName,
            _ownId,
            _currentLobbyId,
            _targetLobbyId,
            _lastAction,
            _lastActionUtc);

    private static bool? ReadBoolean(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        if (token.Type == JTokenType.Boolean)
        {
            return token.Value<bool>();
        }

        return bool.TryParse(token.ToString(), out var value) ? value : null;
    }

    private static int? ReadInt(JToken? token) =>
        token is not null && int.TryParse(token.ToString(), out var value) ? value : null;
}
