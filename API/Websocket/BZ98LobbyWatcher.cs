using System.Threading.Channels;
using BZAPI.Bot;
using BZAPI.Configuration;
using BZAPI.Models;
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
        private const string ModNameSeparator = "~~";

        private readonly ILobbyStore _store;
        private readonly ISteamAvatarProvider _avatars;
        private readonly BattlezoneOptions _options;
        private readonly LobbyBotCoordinator _bot;
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
            ISteamAvatarProvider avatars,
            IOptions<BattlezoneOptions> options,
            LobbyBotCoordinator bot,
            ILogger<BZ98LobbyWatcher> logger)
        {
            _store = store;
            _avatars = avatars;
            _options = options.Value;
            _bot = bot;
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
                _logger.LogInformation("Websocket connected ({ReconnectionType}); authorising.", info.Type);
                _bot.OnSocketConnected();
                SendAuthorization(client);
            });

            using var disconnections = client.DisconnectionHappened.Subscribe(info =>
            {
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
            if (lobby.MetaData is not null)
            {
                // Preserve the protocol envelope before deriving a friendly name. The envelope is
                // useful for diagnostics and carries the public password-marker bit, but never the
                // actual password value.
                lobby.MetaData.RawName = lobby.MetaData.Name;
                lobby.MetaData.Name = StripModPrefix(lobby.MetaData.Name);

                lobby.Stats ??= new BZ98LobbyData();
                ApplyGameSettings(lobby.MetaData.GameSettings, lobby.Stats);
            }

            if (lobby.Users is null || lobby.Users.Count == 0)
            {
                return;
            }

            foreach (var key in lobby.Users.Keys.ToList())
            {
                if (!lobby.Users.TryGetValue(key, out var user) || user is null)
                {
                    lobby.Users.Remove(key);
                    continue;
                }

                // Capture the owner identity before any optional hidden-user filter is applied.
                // LobbyResponse maps Host through UserResponse, which deliberately excludes
                // IP/WAN/LAN fields.
                if (user.Id is not null && user.Id == lobby.Owner)
                {
                    lobby.Host = user;
                }

                if (user.IPAddress is not null && _options.HiddenUserIpAddresses.Contains(user.IPAddress))
                {
                    lobby.Users.Remove(key);
                    continue;
                }

                // authType is the protocol's source of truth for platform. ID prefixes are used
                // only for platform-specific enrichment (for example extracting a Steam64 ID),
                // never to relabel a Web account as GOG.
                user.AuthType = NormalizeAuthType(user.AuthType);
                user.IsSteam = string.Equals(user.AuthType, "steam", StringComparison.OrdinalIgnoreCase);
                user.IsGOG = string.Equals(user.AuthType, "gog", StringComparison.OrdinalIgnoreCase);

                if (user.MetaData?.Ready is { Length: > 0 } ready)
                {
                    user.Stats ??= new BZ98LobbyData();
                    ApplyGameSettings(ready, user.Stats);
                }

                if (!user.IsSteam)
                {
                    continue;
                }

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

        /// <summary>
        /// Decode the 13-field Battlezone game-settings tuple documented by community tooling.
        /// Missing or malformed fields remain null instead of being silently converted to false/0.
        /// </summary>
        private static void ApplyGameSettings(string? settings, BZ98LobbyData target)
        {
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
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            return normalized.ToLowerInvariant();
        }

        /// <summary>
        /// Lobby names arrive as "&lt;mod&gt;~~&lt;name&gt;"; this returns the part after the separator.
        /// </summary>
        private static string? StripModPrefix(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            var separator = name.IndexOf(ModNameSeparator, StringComparison.Ordinal);

            // This was previously an unguarded IndexOf(...) + 2, so a name with no separator had
            // its first character sliced off (-1 + 2 == 1).
            return separator < 0 ? name : name[(separator + ModNameSeparator.Length)..];
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
