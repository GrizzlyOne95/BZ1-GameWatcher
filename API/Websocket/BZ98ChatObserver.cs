using BZAPI.Configuration;
using BZAPI.Models;
using BZAPI.Storage;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Websocket.Client;

namespace BZAPI.Websocket;

/// <summary>
/// Opens narrowly scoped server-side WebSocket sessions for configured public chat lobbies and
/// records a bounded recent-message window. It deliberately implements no chat-send operation, so
/// browser visitors never receive credentials or a protocol path that can write into Battlezone.
/// </summary>
public sealed class BZ98ChatObserver : BackgroundService
{
    private const string ClientVersion = "2.2.301";

    private sealed record ObserverSession(CancellationTokenSource Cancellation, Task Task);

    private readonly ILobbyStore _lobbies;
    private readonly IChatStore _chat;
    private readonly BattlezoneOptions _battlezone;
    private readonly ChatObserverOptions _options;
    private readonly ILogger<BZ98ChatObserver> _logger;
    private readonly Dictionary<int, ObserverSession> _observers = [];

    public BZ98ChatObserver(
        ILobbyStore lobbies,
        IChatStore chat,
        IOptions<BattlezoneOptions> battlezone,
        IOptions<ChatObserverOptions> options,
        ILogger<BZ98ChatObserver> logger)
    {
        _lobbies = lobbies;
        _chat = chat;
        _battlezone = battlezone.Value;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Read-only lobby chat observation is disabled.");
            return;
        }

        var interval = _options.ScanInterval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(10)
            : _options.ScanInterval;

        using var timer = new PeriodicTimer(interval);

        try
        {
            await ReconcileObserversAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ReconcileObserversAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        finally
        {
            foreach (var session in _observers.Values)
            {
                session.Cancellation.Cancel();
            }

            try
            {
                await Task.WhenAll(_observers.Values.Select(session => session.Task));
            }
            catch (OperationCanceledException)
            {
                // Expected while stopping observers.
            }

            foreach (var pair in _observers)
            {
                _chat.SetObserverUserId(pair.Key, null);
                pair.Value.Cancellation.Dispose();
            }

            _observers.Clear();
        }
    }

    private Task ReconcileObserversAsync(CancellationToken stoppingToken)
    {
        foreach (var completed in _observers
                     .Where(pair => pair.Value.Task.IsCompleted)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _chat.SetObserverUserId(completed, null);
            _observers[completed].Cancellation.Dispose();
            _observers.Remove(completed);
        }

        var configuredNames = _options.LobbyNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var maxObservers = Math.Clamp(_options.MaxObservedLobbies, 1, 8);
        var targets = configuredNames.Count == 0
            ? []
            : _lobbies.Current.Lobbies
                .Where(lobby => lobby.IsChat && !lobby.IsPrivate)
                .Where(lobby =>
                    lobby.MetaData?.Name is { Length: > 0 } name && configuredNames.Contains(name))
                .OrderBy(lobby => lobby.Id)
                .Take(maxObservers)
                .ToList();

        var targetIds = targets.Select(lobby => lobby.Id).ToHashSet();

        foreach (var obsoleteId in _observers.Keys.Where(id => !targetIds.Contains(id)).ToList())
        {
            var session = _observers[obsoleteId];
            session.Cancellation.Cancel();
            session.Cancellation.Dispose();
            _observers.Remove(obsoleteId);
            _chat.RemoveLobby(obsoleteId);
            _logger.LogInformation("Stopped read-only chat observer for lobby {LobbyId}.", obsoleteId);
        }

        foreach (var lobby in targets)
        {
            if (_observers.ContainsKey(lobby.Id))
            {
                continue;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var task = ObserveLobbyAsync(lobby.Id, lobby.MetaData?.Name ?? $"Lobby {lobby.Id}", cancellation.Token);
            _observers[lobby.Id] = new ObserverSession(cancellation, task);
            _logger.LogInformation(
                "Started read-only chat observer for {LobbyName} ({LobbyId}).",
                lobby.MetaData?.Name,
                lobby.Id);
        }

        return Task.CompletedTask;
    }

    private async Task ObserveLobbyAsync(int lobbyId, string lobbyName, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new WebsocketClient(new Uri(_battlezone.LobbyServerUrl))
            {
                ReconnectTimeout = _battlezone.StaleConnectionTimeout,
                ErrorReconnectTimeout = _battlezone.ErrorReconnectTimeout
            };

            using var reconnections = client.ReconnectionHappened.Subscribe(_ =>
            {
                Send(client, new
                {
                    type = "Authorization",
                    content = new
                    {
                        authtype = "web",
                        key = string.Empty,
                        id = "0",
                        apiVer = "0.0"
                    }
                });
            });

            using var messages = client.MessageReceived.Subscribe(message =>
            {
                if (message.Text is not { Length: > 0 } text)
                {
                    return;
                }

                try
                {
                    var envelope = JObject.Parse(text);
                    var type = envelope["type"]?.ToString();
                    // The public service has emitted both `data` and `content` envelopes over its
                    // lifetime. Accept both, matching the older LobbyMonitor compatibility logic.
                    var payload = envelope["data"] as JObject ?? envelope["content"] as JObject;

                    switch (type)
                    {
                        case "OnAuthorization":
                            if (ReadBoolean(payload?["success"]) is false)
                            {
                                _logger.LogWarning(
                                    "Read-only chat observer authorization was rejected for lobby {LobbyId}.",
                                    lobbyId);
                                return;
                            }

                            // The observer is a real Web user while joined. Retain its server-issued
                            // ID internally so the public Game Watcher roster/count can exclude only
                            // our own observer without hiding third-party Web accounts such as !BRIDGE.
                            _chat.SetObserverUserId(lobbyId, payload?["id"]?.ToString());

                            Send(client, new { type = "DoEnterLounge", content = true });
                            SetIdentity(client);
                            Send(client, new
                            {
                                type = "DoJoinLobby",
                                content = new { id = lobbyId, password = string.Empty }
                            });
                            break;

                        case "OnLobbyJoined":
                            if (ReadBoolean(payload?["success"]) is false)
                            {
                                _logger.LogWarning(
                                    "Read-only chat observer could not join {LobbyName} ({LobbyId}): {Reason}",
                                    lobbyName,
                                    lobbyId,
                                    payload?["reason"]?.ToString());
                            }
                            break;

                        case "OnChatMessage":
                            if (payload is not null)
                            {
                                StoreMessage(lobbyId, payload);
                            }
                            break;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(
                        ex,
                        "Ignoring malformed chat-observer message for lobby {LobbyId}.",
                        lobbyId);
                }
            });

            await client.Start();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal observer shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Read-only chat observer for {LobbyName} ({LobbyId}) stopped unexpectedly.",
                lobbyName,
                lobbyId);
        }
    }

    private void StoreMessage(int lobbyId, JObject data)
    {
        var speakerId = data["speakerId"]?.ToString();
        var author = data["author"]?.ToString();

        if (string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(speakerId))
        {
            author = _lobbies.Current.Lobbies
                .FirstOrDefault(lobby => lobby.Id == lobbyId)?
                .Users?
                .Values
                .FirstOrDefault(user => string.Equals(user.Id, speakerId, StringComparison.OrdinalIgnoreCase))?
                .Name;
        }

        _chat.Add(new ChatMessageSnapshot(
            lobbyId,
            author,
            speakerId,
            data["text"]?.ToString() ?? string.Empty,
            ReadTime(data["time"])));
    }

    private void SetIdentity(IWebsocketClient client)
    {
        var playerName = string.IsNullOrWhiteSpace(_options.PlayerName)
            ? "BZ1 Game Watcher (read-only)"
            : _options.PlayerName.Trim();

        foreach (var update in new[]
                 {
                     new { key = "name", value = playerName },
                     new { key = "playerName", value = playerName },
                     new { key = "clientVersion", value = ClientVersion },
                     new { key = "authType", value = "web" }
                 })
        {
            Send(client, new { type = "SetPlayerData", content = update });
        }
    }

    private static void Send(IWebsocketClient client, object payload) =>
        client.Send(JsonConvert.SerializeObject(payload));

    private static bool? ReadBoolean(JToken? token)
    {
        if (token is null)
        {
            return null;
        }

        if (token.Type == JTokenType.Boolean)
        {
            return token.Value<bool>();
        }

        return bool.TryParse(token.ToString(), out var parsed) ? parsed : null;
    }

    private static DateTimeOffset ReadTime(JToken? token)
    {
        if (token is null)
        {
            return DateTimeOffset.UtcNow;
        }

        if (DateTimeOffset.TryParse(token.ToString(), out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        if (long.TryParse(token.ToString(), out var numeric))
        {
            try
            {
                return numeric > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
                    : DateTimeOffset.FromUnixTimeSeconds(numeric);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Fall back to receipt time below.
            }
        }

        return DateTimeOffset.UtcNow;
    }
}
