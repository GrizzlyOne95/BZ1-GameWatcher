using System.Globalization;
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
        ILogger<BZ98LobbyController> logger) : ControllerBase
    {
        private readonly ILobbyStore _lobbyStore = lobbyStore;
        private readonly IChatStore _chatStore = chatStore;
        private readonly ISteamWorkshopProvider _workshopProvider = workshopProvider;
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

            var responses = await Task.WhenAll(snapshot.Lobbies.Select(async lobby =>
            {
                var workshop = await ResolveWorkshopAsync(lobby, cancellationToken);
                return lobby.ToResponse(_chatStore.GetRecent(lobby.Id), workshop);
            }));

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

            var workshop = await ResolveWorkshopAsync(lobby, cancellationToken);
            return Ok(lobby.ToResponse(_chatStore.GetRecent(lobby.Id), workshop));
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
    }
}
