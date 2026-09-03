using BZAPI.Models;

namespace BZAPI.Storage
{
    /// <summary>
    /// Immutable point-in-time view of the lobby list.
    /// </summary>
    /// <param name="Lobbies">The lobbies, safe to enumerate without locking.</param>
    /// <param name="LastUpdatedUtc">When the lobby list was last changed, or null if never.</param>
    public sealed record LobbySnapshot(IReadOnlyList<BZ98Lobby> Lobbies, DateTimeOffset? LastUpdatedUtc)
    {
        public static readonly LobbySnapshot Empty = new([], null);
    }

    public interface ILobbyStore
    {
        /// <summary>
        /// The current snapshot. Never null; safe to read from any thread.
        /// </summary>
        LobbySnapshot Current { get; }

        /// <summary>
        /// Raised synchronously after a complete immutable snapshot has been published. Consumers
        /// must keep handlers lightweight and must never mutate either snapshot.
        /// </summary>
        event Action<LobbySnapshot, LobbySnapshot>? SnapshotChanged;

        void Replace(IEnumerable<BZ98Lobby> lobbies);

        void AddOrUpdate(BZ98Lobby lobby);

        void Remove(int lobbyId);
    }

    /// <summary>
    /// Holds the lobby list produced by the websocket watcher and read by HTTP requests.
    /// </summary>
    /// <remarks>
    /// Writes take a lock and publish a brand new list; readers take the current snapshot with a
    /// single atomic reference read. This means a reader can never observe a half-applied update,
    /// which previously caused intermittent "Collection was modified" failures during JSON
    /// serialisation.
    ///
    /// Lobby objects must be fully populated *before* being handed to this store — once published
    /// they are treated as immutable, because readers may be serialising them at any moment.
    /// </remarks>
    public sealed class LobbyStore : ILobbyStore
    {
        private readonly object _writeLock = new();
        private LobbySnapshot _current = LobbySnapshot.Empty;

        public LobbySnapshot Current => Volatile.Read(ref _current);

        public event Action<LobbySnapshot, LobbySnapshot>? SnapshotChanged;

        public void Replace(IEnumerable<BZ98Lobby> lobbies)
        {
            ArgumentNullException.ThrowIfNull(lobbies);

            LobbySnapshot previous;
            LobbySnapshot current;
            lock (_writeLock)
            {
                previous = _current;
                current = Publish(lobbies.ToList());
            }

            SnapshotChanged?.Invoke(previous, current);
        }

        public void AddOrUpdate(BZ98Lobby lobby)
        {
            ArgumentNullException.ThrowIfNull(lobby);

            LobbySnapshot previous;
            LobbySnapshot current;
            lock (_writeLock)
            {
                previous = _current;
                var updated = _current.Lobbies.ToList();
                var index = updated.FindIndex(l => l.Id == lobby.Id);

                if (index >= 0)
                {
                    updated[index] = lobby;
                }
                else
                {
                    updated.Add(lobby);
                }

                current = Publish(updated);
            }

            SnapshotChanged?.Invoke(previous, current);
        }

        public void Remove(int lobbyId)
        {
            LobbySnapshot previous;
            LobbySnapshot current;
            lock (_writeLock)
            {
                var updated = _current.Lobbies.Where(l => l.Id != lobbyId).ToList();

                if (updated.Count == _current.Lobbies.Count)
                {
                    return;
                }

                previous = _current;
                current = Publish(updated);
            }

            SnapshotChanged?.Invoke(previous, current);
        }

        private LobbySnapshot Publish(List<BZ98Lobby> lobbies)
        {
            var snapshot = new LobbySnapshot(lobbies, DateTimeOffset.UtcNow);
            Volatile.Write(ref _current, snapshot);
            return snapshot;
        }
    }
}
