using System.Net.WebSockets;
using System.Threading.Channels;
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
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<int, ObserverSession> _observers = [];
    private readonly object _attemptGate = new();
    private readonly Queue<DateTimeOffset> _connectionAttempts = [];
    private DateTimeOffset? _circuitOpenUntil;

    // LobbyStore already publishes a synchronous change event. Coalescing it through a bounded
    // channel keeps the event handler non-blocking and lets the background service reconcile only
    // when authoritative lobby state changes instead of polling every few seconds.
    private readonly Channel<bool> _reconcileSignals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    public BZ98ChatObserver(
        ILobbyStore lobbies,
        IChatStore chat,
        IOptions<BattlezoneOptions> battlezone,
        IOptions<ChatObserverOptions> options,
        ILogger<BZ98ChatObserver> logger,
        TimeProvider timeProvider)
    {
        _lobbies = lobbies;
        _chat = chat;
        _battlezone = battlezone.Value;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Read-only lobby chat observation is disabled.");
            return;
        }

        void OnSnapshotChanged(LobbySnapshot _, LobbySnapshot __) =>
            _reconcileSignals.Writer.TryWrite(true);

        _lobbies.SnapshotChanged += OnSnapshotChanged;

        try
        {
            await ReconcileObserversAsync(stoppingToken);

            while (await _reconcileSignals.Reader.WaitToReadAsync(stoppingToken))
            {
                // Multiple lobby deltas can arrive in a burst. Only the latest immutable snapshot
                // matters for deciding which observer sessions should exist.
                while (_reconcileSignals.Reader.TryRead(out _))
                {
                }

                await ReconcileObserversAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        finally
        {
            _lobbies.SnapshotChanged -= OnSnapshotChanged;

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

    private async Task ReconcileObserversAsync(CancellationToken stoppingToken)
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

            try
            {
                await session.Task;
            }
            catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
            {
                // Expected while replacing an observer whose lobby disappeared.
            }

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
            _ = task.ContinueWith(
                _ => _reconcileSignals.Writer.TryWrite(true),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _logger.LogInformation(
                "Started read-only chat observer for {LobbyName} ({LobbyId}).",
                lobby.MetaData?.Name,
                lobby.Id);
        }
    }

    private async Task ObserveLobbyAsync(int lobbyId, string lobbyName, CancellationToken cancellationToken)
    {
        try
        {
            var reconnectDelay = _options.ReconnectDelay <= TimeSpan.Zero
                ? TimeSpan.FromMinutes(1)
                : _options.ReconnectDelay;

            while (!cancellationToken.IsCancellationRequested)
            {
                // Every socket creation, initial or replacement, consumes one slot from the
                // process-wide budget. When the budget is exhausted the circuit opens and this
                // session waits it out instead of creating sockets.
                if (!TryReserveConnectionSlot())
                {
                    var circuitWait = GetCircuitWait();
                    _logger.LogError(
                        "Read-only chat observer connection budget exhausted; {LobbyName} ({LobbyId}) waits {Wait} before creating its next socket.",
                        lobbyName,
                        lobbyId,
                        circuitWait);
                    await Task.Delay(circuitWait, cancellationToken);
                    continue;
                }

                var sessionEnded =
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                // Websocket.Client's automatic recovery is disabled: the library would otherwise
                // recreate sockets on its own schedule, which cannot respect the process-wide
                // connection budget. This loop creates each replacement socket itself after
                // ReconnectDelay, and each creation reserves another budget slot.
                using (var client = new WebsocketClient(new Uri(_battlezone.LobbyServerUrl))
                       {
                           ReconnectTimeout = null,
                           ErrorReconnectTimeout = null,
                           LostReconnectTimeout = null
                       })
                {
                    bool SendOrEnd(IWebsocketClient target, object payload, string action)
                    {
                        try
                        {
                            if (target.Send(JsonConvert.SerializeObject(payload)))
                            {
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Read-only chat observer {Action} send threw for {LobbyName} ({LobbyId}).",
                                action,
                                lobbyName,
                                lobbyId);
                        }

                        // Send failures open the circuit: retrying against a rejecting or
                        // broken peer is what the budget exists to prevent.
                        OpenCircuit();
                        sessionEnded.TrySetResult();
                        return false;
                    }

                    using var reconnections = client.ReconnectionHappened.Subscribe(info =>
                    {
                        _logger.LogInformation(
                            "Read-only chat observer connection established for {LobbyName} ({LobbyId}) ({ReconnectionType}); authorizing.",
                            lobbyName,
                            lobbyId,
                            info.Type);

                        SendOrEnd(client, new
                        {
                            type = "Authorization",
                            content = new
                            {
                                authtype = "web",
                                key = string.Empty,
                                id = "0",
                                apiVer = "0.0"
                            }
                        }, "authorization");
                    });

                    using var disconnections = client.DisconnectionHappened.Subscribe(info =>
                    {
                        // A replacement socket creates a new upstream Web session. Stop filtering
                        // the old ID until the replacement Authorization response identifies it.
                        _chat.SetObserverUserId(lobbyId, null);
                        _logger.LogWarning(
                            info.Exception,
                            "Read-only chat observer disconnected from {LobbyName} ({LobbyId}) ({DisconnectionType}); retry is delayed by {ReconnectDelay}.",
                            lobbyName,
                            lobbyId,
                            info.Type,
                            reconnectDelay);
                        sessionEnded.TrySetResult();
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
                                            "Read-only chat observer authorization was rejected for lobby {LobbyId}; pausing this room for {Cooldown}.",
                                            lobbyId,
                                            _options.CircuitOpenDuration);
                                        // Authorization rejections open the circuit: retrying with
                                        // the same credentials would only consume budget.
                                        OpenCircuit();
                                        sessionEnded.TrySetResult();
                                        return;
                                    }

                                    // The observer is a real Web user while joined. Retain its server-issued
                                    // ID internally so the public Game Watcher roster/count can exclude only
                                    // our own observer without hiding third-party Web accounts such as !BRIDGE.
                                    _chat.SetObserverUserId(lobbyId, payload?["id"]?.ToString());

                                    // Rebellion's own Web client authenticates once, enters the lounge, and
                                    // then receives chat through push OnChatMessage events. A dedicated
                                    // joined session is retained here only so messages can be attributed to
                                    // one configured lobby without depending on undocumented payload fields.
                                    if (!SendOrEnd(client, new { type = "DoEnterLounge", content = true }, "lounge-entry"))
                                    {
                                        return;
                                    }

                                    SetIdentity(client);

                                    if (!SendOrEnd(client, new
                                        {
                                            type = "DoJoinLobby",
                                            content = new { id = lobbyId, password = string.Empty }
                                        }, "lobby-join"))
                                    {
                                        return;
                                    }

                                    break;

                                case "OnLobbyJoined":
                                    if (ReadBoolean(payload?["success"]) is false)
                                    {
                                        _logger.LogWarning(
                                            "Read-only chat observer could not join {LobbyName} ({LobbyId}): {Reason}; pausing this room for {Cooldown}.",
                                            lobbyName,
                                            lobbyId,
                                            payload?["reason"]?.ToString(),
                                            _options.CircuitOpenDuration);
                                        OpenCircuit();
                                        sessionEnded.TrySetResult();
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

                    if (!client.IsStarted)
                    {
                        _logger.LogWarning(
                            "Read-only chat observer could not connect to {LobbyName} ({LobbyId}); retry is delayed by {ReconnectDelay}.",
                            lobbyName,
                            lobbyId,
                            reconnectDelay);
                        sessionEnded.TrySetResult();
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Read-only chat observer socket for {LobbyName} ({LobbyId}) is live.",
                            lobbyName,
                            lobbyId);
                    }

                    // Stay on this socket until it disconnects, the protocol rejects the session,
                    // or the application shuts down. The wait is cancellable so shutdown never
                    // hangs on a session whose socket died without raising DisconnectionHappened.
                    try
                    {
                        await sessionEnded.Task.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Normal observer shutdown; client disposal below closes the socket.
                    }
                }

                _chat.SetObserverUserId(lobbyId, null);

                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(reconnectDelay, cancellationToken);
                }
            }
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
        finally
        {
            _chat.SetObserverUserId(lobbyId, null);
        }
    }

    /// <summary>
    /// Reserves one socket-creation slot from the process-wide budget. Opens the circuit
    /// breaker when the sliding window is exhausted; the circuit then blocks every room.
    /// </summary>
    private bool TryReserveConnectionSlot()
    {
        lock (_attemptGate)
        {
            var now = _timeProvider.GetUtcNow();
            var window = _options.ConnectionAttemptWindow;

            while (_connectionAttempts.Count > 0 && now - _connectionAttempts.Peek() > window)
            {
                _connectionAttempts.Dequeue();
            }

            if (_circuitOpenUntil is { } openUntil && now < openUntil)
            {
                return false;
            }

            _circuitOpenUntil = null;

            if (window > TimeSpan.Zero &&
                _connectionAttempts.Count >= _options.MaxConnectionAttemptsPerWindow)
            {
                _circuitOpenUntil = now + _options.CircuitOpenDuration;
                _logger.LogWarning(
                    "Chat observer made {Attempts} socket attempts in {Window}; circuit open until {OpenUntil}.",
                    _connectionAttempts.Count,
                    window,
                    _circuitOpenUntil);
                return false;
            }

            _connectionAttempts.Enqueue(now);
            return true;
        }
    }

    /// <summary>How long a budget-blocked session should wait before asking again.</summary>
    private TimeSpan GetCircuitWait()
    {
        lock (_attemptGate)
        {
            var now = _timeProvider.GetUtcNow();
            if (_circuitOpenUntil is { } openUntil && openUntil > now)
            {
                return openUntil - now;
            }

            return TimeSpan.FromMinutes(1);
        }
    }

    /// <summary>Opens the circuit for protocol-level rejections (auth, join, send).</summary>
    private void OpenCircuit()
    {
        lock (_attemptGate)
        {
            _circuitOpenUntil = _timeProvider.GetUtcNow() + _options.CircuitOpenDuration;
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
