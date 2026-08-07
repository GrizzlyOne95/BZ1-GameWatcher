using BZAPI.Models.Responses;
using BZAPI.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BZAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BZ98LobbyController(
        ILobbyStore lobbyStore,
        IChatStore chatStore,
        ILogger<BZ98LobbyController> logger) : ControllerBase
    {
        private readonly ILobbyStore _lobbyStore = lobbyStore;
        private readonly IChatStore _chatStore = chatStore;
        private readonly ILogger<BZ98LobbyController> _logger = logger;

        /// <summary>
        /// Returns the lobbies currently known to the watcher. Always an array, never null —
        /// returning null previously made the client throw before the first websocket message
        /// arrived.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<LobbyResponse>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<LobbyResponse>> GetLobbies()
        {
            var snapshot = _lobbyStore.Current;

            _logger.LogDebug(
                "Serving {LobbyCount} lobbies, last updated {LastUpdatedUtc}.",
                snapshot.Lobbies.Count,
                snapshot.LastUpdatedUtc);

            return Ok(snapshot.Lobbies
                .Select(lobby => lobby.ToResponse(_chatStore.GetRecent(lobby.Id)))
                .ToList());
        }

        /// <summary>
        /// Returns one currently listed lobby for stable detail/share pages. A lobby disappearing
        /// from the upstream list is represented as 404 rather than returning a stale cached copy.
        /// </summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(LobbyResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult<LobbyResponse> GetLobby(int id)
        {
            var snapshot = _lobbyStore.Current;
            var lobby = snapshot.Lobbies.FirstOrDefault(candidate => candidate.Id == id);

            if (lobby is null)
            {
                return NotFound();
            }

            return Ok(lobby.ToResponse(_chatStore.GetRecent(lobby.Id)));
        }
    }
}
