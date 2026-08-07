using System.Threading.Channels;
using BZAPI.Bot;
using BZAPI.Configuration;
using BZAPI.Models;
using BZAPI.Protocol;
using BZAPI.Steam;
using BZAPI.Storage;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Websocket.Client;

namespace BZAPI.Websocket
{
    /// <summary>
    /// Maintains the connection to the Battlezone 98 Redux lobby server and keeps
    /// <see cref="ILobbyStore"/> in step with it.
    /// </summary>
    public sealed class BZ98LobbyWatcher : BackgroundService
    {
        private readonly ILobbyStore _store;
        private readonly IChatStore _chat;
        private readonly ISteamAvatarProvider _avatars;
        private readonly BattlezoneOptions _options;
        private readonly LobbyBotCoordinator _bot;
        private readonly LobbyConnectionState _connectionState;
        private readonly ILogger<BZ98LobbyWatcher> _logger;

        /// <summary>
        /// Incoming messages are queued and processed one at a time. Handling them inline in the
        /// subscription callback made the callback an <c>async void</c>, so any exception — an
        /// unparsable Steam ID, a Steam outage, malformed JSON — escaped onto a thread-pool thread
        /// and terminated the process. Queueing also keeps updates in order.
        /// </summary>
        private readonly Channel<string> _messages =
            Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

        public BZ98LobbyWatcher(
            ILobbyStore store,
            IChatStore chat,
            ISteamAvatarProvider avatars,
            IOptions<BattlezoneOptions> options,
            LobbyBotCoordinator bot,
            LobbyConnectionState connectionState,
            ILogger<BZ98LobbyWatcher> logger)
        {
            _store = store;
            _chat = chat;
            _avatars = avatars;
            _options = options.Value;
            _bot = bot;
            _connectionState = connectionState;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var url = new Uri(_options.LobbyServerUrl);

            _logger.LogInformation("Connecting to lobby server at {LobbyServerUrl}.", url);

            using var client = new WebsocketClient(url)
            {
                ReconnectTimeout = _options.StaleConnectionTimeout,
                ErrorReconnectTimeout = _options.ErrorReconnectTimeout
            };

            // Authorisation has to be re-sent on *every* connection, not just the first. Previously
            // it was sent once after Start(), so after any reconnect the socket was open but
            // unauthorised: no lobby updates ever arrived again and the API quietly served stale
            // data until it was restarted.
            using var reconnections = client.ReconnectionHappened.Subscribe(info =>
            {
                _connectionState.MarkConnected();
                _logger.LogInformation("Websocket connected ({ReconnectionType}); authorising.", info.Type);
                _bot.OnSocketConnected();
                SendAuthorization(client);
            });

            using var disconnections = client.DisconnectionHappened.Subscribe(info =>
            {
                _connectionState.MarkDisconnected();
                _bot.OnSocketDisconnected();
                _logger.LogWarning(
                    info.Exception,
                    "Websocket disconnected ({DisconnectionType}, close status {CloseStatus}).",
                    info.Type,
                    info.CloseStatus);
            });

            using var subscription = client.MessageReceived.Subscribe(message =>
            {
                if (message.Text is { Length: > 0 } text)
                {
                    _connectionState.MarkMessage();
                    _messages.Writer.TryWrite(text);
                }
            });

            await client.Start();

            await Task.WhenAll(
                ProcessMessagesAsync(client, stoppingToken),
                _bot.RunAsync(client, stoppingToken));
        }

        private async Task ProcessMessagesAsync(IWebsocketClient client, CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var text in _messages.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await HandleMessageAsync(client, text, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // A single bad message must never bring the watcher down.
                        _logger.LogError(ex, "Failed to process websocket message.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Lobby watcher shutting down.");
            }
        }

        private async Task HandleMessageAsync(IWebsocketClient client, string text, CancellationToken cancellationToken)
        {
            var envelope = JsonConvert.DeserializeObject<WebsocketGenericMessage>(text);

            if (envelope?.Type is null)
            {
                return;
            }

            _logger.LogDebug("Processing {MessageType} message.", envelope.Type);

            switch (envelope.Type)
            {
                case nameof(WebsocketMessageType.OnAuthorization):
                    EnterLounge(client);
                    _bot.OnAuthorized(client, text);
                    break;

                case nameof(WebsocketMessageType.OnLobbyListChanged):
                case "OnLobbyList":
                case "OnGetLobbyList":
                case nameof(WebsocketMessageType.OnLobbyChanged):
                case "OnLobbyUpdate":
                    await HandleLobbyUpdateAsync(client, envelope.Type, text, cancellationToken);
                    break;

                case nameof(WebsocketMessageType.OnLobbyRemoved):
                    var removal = JsonConvert.DeserializeObject<WebsocketIntMessage>(text);

                    if (removal?.Data is not null)
                    {
                        _store.Remove(removal.Data.Id);
                        _chat.RemoveLobby(removal.Data.Id);
                    }

                    _bot.OnLobbyRemoved(text);
                    break;

                case "OnLobbyJoined":
                    _bot.OnLobbyJoined(text);
                    break;

                case "OnLobbyCreated":
                    _bot.OnLobbyCreated(client, text);
                    break;

                case "OnLobbyMemberListChanged":
                    _bot.OnMemberListChanged(client, text);
                    break;
            }
        }

        private async Task HandleLobbyUpdateAsync(
            IWebsocketClient client,
            string messageType,
            string text,
            CancellationToken cancellationToken)
        {
            var isFullList = messageType is
                nameof(WebsocketMessageType.OnLobbyListChanged) or
                "OnLobbyList" or
                "OnGetLobbyList";

            var message = JsonConvert.DeserializeObject<WebsocketLobbyMessage>(text);
            var lobbies = message?.Data?.BZ98Lobbies?.Values.Where(lobby => lobby is not null).ToList();

            if (lobbies is null || lobbies.Count == 0)
            {
                // An empty list is a legitimate state — it means nobody is online.
                if (isFullList)
                {
                    _store.Replace([]);
                    _bot.OnLobbySnapshot(client, [], true);
                }

                return;
            }

            // Populate every lobby *before* publishing it. Once a lobby is in the store an HTTP
            // request may be serialising it at any moment, so it must not be mutated afterwards.
            foreach (var lobby in lobbies)
            {
                await PopulateLobbyAsync(lobby, cancellationToken);
            }

            _bot.OnLobbySnapshot(client, lobbies, isFullList);

            if (isFullList)
            {
                _store.Replace(lobbies);
                return;
            }

            foreach (var lobby in lobbies)
            {
                _store.AddOrUpdate(lobby);
            }
        }

        private async Task PopulateLobbyAsync(BZ98Lobby lobby, CancellationToken cancellationToken)
        {
            // Keep reverse-engineered protocol semantics in the pure parser so the live watcher and
            // sanitized regression fixtures exercise the same production normalization path.
            BZ98ProtocolParser.NormalizeLobby(lobby);

            var users = lobby.Users;
            if (users is null || users.Count == 0)
            {
                return;
            }

            // The owner snapshot above must happen before this filter. The observer is a real Web
            // user upstream, but Game Watcher's own identity should not inflate public activity.
            BZ98ProtocolParser.FilterPublicUsers(
                lobby,
                (key, user) =>
                    _chat.IsObserverUser(lobby.Id, user.Id ?? key) ||
                    (user.IPAddress is not null && _options.HiddenUserIpAddresses.Contains(user.IPAddress)));

            if (users.Count == 0)
            {
                return;
            }

            foreach (var pair in users)
            {
                var key = pair.Key;
                var user = pair.Value;
                if (user is null || !user.IsSteam)
                {
                    continue;
                }

                // authType has already been normalized by BZ98ProtocolParser. Steam-ID-shaped
                // identifiers are used only for Steam enrichment when authType actually is steam.
                var steamKey = user.Id ?? key;
                if (steamKey.Length > 1 && steamKey[0] == 'S' && ulong.TryParse(steamKey[1..], out var steamId))
                {
                    user.SteamCleanId = steamKey[1..];
                    user.IsDangerous = _options.FlaggedSteamIds.Contains(steamId);
                    user.SteamImgUri = await _avatars.GetAvatarUrlAsync(steamId, cancellationToken);
                }
                else
                {
                    _logger.LogDebug(
                        "Steam-authenticated user {UserKey} did not contain a parsable Steam ID.",
                        steamKey);
                }
            }
        }

        private static void SendAuthorization(IWebsocketClient client)
        {
            var message = new WebsocketAuthMessage
            {
                Type = "Authorization",
                Content = new WebsocketAuthMessageContent
                {
                    AuthType = "web",
                    Key = string.Empty,
                    Id = "0",
                    ApiVer = "0.0"
                }
            };

            client.Send(JsonConvert.SerializeObject(message));
        }

        private static void EnterLounge(IWebsocketClient client)
        {
            var message = new WebsocketBoolMessage
            {
                Type = "DoEnterLounge",
                Content = true
            };

            client.Send(JsonConvert.SerializeObject(message));
        }
    }
}
