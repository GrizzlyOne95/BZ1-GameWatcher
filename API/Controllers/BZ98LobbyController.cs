using System.Globalization;
using BZAPI.Maps;
using BZAPI.Models;
using BZAPI.Models.Responses;
using BZAPI.Steam;
using BZAPI.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BZAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BZ98LobbyController(
        ILobbyStore lobbyStore,
        IChatStore chatStore,
        ISteamWorkshopProvider workshopProvider,
        IMapMetadataProvider mapMetadataProvider,
        ILogger<BZ98LobbyController> logger) : ControllerBase
    {
        private readonly ILobbyStore _lobbyStore = lobbyStore;
        private readonly IChatStore _chatStore = chatStore;
        private readonly ISteamWorkshopProvider _workshopProvider = workshopProvider;
        private readonly IMapMetadataProvider _mapMetadataProvider = mapMetadataProvider;
        private readonly ILogger<BZ98LobbyController> _logger = logger;

        /// <summary>
        /// Returns the lobbies currently known to the watcher. Always an array, never null —
        /// returning null previously made the client throw before the first websocket message
        /// arrived.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<LobbyResponse>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<LobbyResponse>>> GetLobbies(CancellationToken cancellationToken)
        {
            var snapshot = _lobbyStore.Current;

            _logger.LogDebug(
                "Serving {LobbyCount} lobbies, last updated {LastUpdatedUtc}.",
                snapshot.Lobbies.Count,
                snapshot.LastUpdatedUtc);

            var responses = await Task.WhenAll(snapshot.Lobbies.Select(lobby => BuildResponseAsync(lobby, cancellationToken)));
            return Ok(responses);
        }

        /// <summary>
        /// Returns one currently listed lobby for stable detail/share pages. A lobby disappearing
        /// from the upstream list is represented as 404 rather than returning a stale cached copy.
        /// </summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(LobbyResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<LobbyResponse>> GetLobby(int id, CancellationToken cancellationToken)
        {
            var snapshot = _lobbyStore.Current;
            var lobby = snapshot.Lobbies.FirstOrDefault(candidate => candidate.Id == id);

            if (lobby is null)
            {
                return NotFound();
            }

            return Ok(await BuildResponseAsync(lobby, cancellationToken));
        }

        private async Task<LobbyResponse> BuildResponseAsync(BZ98Lobby lobby, CancellationToken cancellationToken)
        {
            var workshopTask = ResolveWorkshopAsync(lobby, cancellationToken);
            var mapTask = ResolveMapAsync(lobby, cancellationToken);

            await Task.WhenAll(workshopTask, mapTask);

            return lobby.ToResponse(
                _chatStore.GetRecent(lobby.Id),
                await workshopTask,
                await mapTask);
        }

        private Task<SteamWorkshopItem?> ResolveWorkshopAsync(BZ98Lobby lobby, CancellationToken cancellationToken)
        {
            var rawMod = lobby.Stats?.Mod?.Trim();
            if (lobby.IsChat || string.IsNullOrWhiteSpace(rawMod) ||
                !ulong.TryParse(rawMod, NumberStyles.None, CultureInfo.InvariantCulture, out var publishedFileId) ||
                publishedFileId == 0)
            {
                return Task.FromResult<SteamWorkshopItem?>(null);
            }

            return _workshopProvider.GetItemAsync(publishedFileId, cancellationToken);
        }

        private Task<BZ98MapMetadata?> ResolveMapAsync(BZ98Lobby lobby, CancellationToken cancellationToken)
        {
            var mapFile = lobby.Stats?.MapFile?.Trim();
            if (lobby.IsChat || string.IsNullOrWhiteSpace(mapFile))
            {
                return Task.FromResult<BZ98MapMetadata?>(null);
            }

            // MultiplayerSessionList treats a missing map mod as stock mod 0. Preserve any
            // reported non-zero/non-numeric value because the map metadata service keys on both.
            var modId = lobby.Stats?.Mod?.Trim();
            return _mapMetadataProvider.GetMapAsync(mapFile, string.IsNullOrWhiteSpace(modId) ? "0" : modId, cancellationToken);
        }
    }
}
