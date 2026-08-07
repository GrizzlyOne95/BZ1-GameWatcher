using BZAPI.Configuration;
using Microsoft.Extensions.Options;

namespace BZAPI.Storage;

/// <summary>A single public chat message observed from a Battlezone chat lobby.</summary>
public sealed record ChatMessageSnapshot(
    int LobbyId,
    string? Author,
    string? SpeakerId,
    string Text,
    DateTimeOffset TimeUtc);

public interface IChatStore
{
    IReadOnlyList<ChatMessageSnapshot> GetRecent(int lobbyId);

    void Add(ChatMessageSnapshot message);

    void SetObserverUserId(int lobbyId, string? userId);

    bool IsObserverUser(int lobbyId, string? userId);

    void RemoveLobby(int lobbyId);
}

/// <summary>
/// Keeps only a small, bounded, process-local window of recent public chat. Nothing is persisted,
/// and upstream styling/network fields are deliberately not retained.
/// </summary>
public sealed class ChatStore : IChatStore
{
    private readonly object _sync = new();
    private readonly Dictionary<int, List<ChatMessageSnapshot>> _messages = [];
    private readonly Dictionary<int, string> _observerUserIds = [];
    private readonly int _maxMessagesPerLobby;
    private readonly int _maxMessageLength;

    public ChatStore(IOptions<ChatObserverOptions> options)
    {
        var configured = options.Value;
        _maxMessagesPerLobby = Math.Clamp(configured.MaxMessagesPerLobby, 1, 200);
        _maxMessageLength = Math.Clamp(configured.MaxMessageLength, 32, 2000);
    }

    public IReadOnlyList<ChatMessageSnapshot> GetRecent(int lobbyId)
    {
        lock (_sync)
        {
            return _messages.TryGetValue(lobbyId, out var messages)
                ? messages.ToArray()
                : [];
        }
    }

    public void Add(ChatMessageSnapshot message)
    {
        var text = Clean(message.Text, _maxMessageLength);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var author = Clean(message.Author, 96);
        var speakerId = Clean(message.SpeakerId, 96);
        var normalized = message with
        {
            Author = string.IsNullOrWhiteSpace(author) ? null : author,
            SpeakerId = string.IsNullOrWhiteSpace(speakerId) ? null : speakerId,
            Text = text,
            TimeUtc = message.TimeUtc.ToUniversalTime()
        };

        lock (_sync)
        {
            if (!_messages.TryGetValue(message.LobbyId, out var messages))
            {
                messages = [];
                _messages[message.LobbyId] = messages;
            }

            // Reconnects can occasionally replay the last event. Avoid a duplicate line in the UI
            // without trying to infer broader chat semantics.
            var duplicate = messages.LastOrDefault() is { } last &&
                last.SpeakerId == normalized.SpeakerId &&
                last.Author == normalized.Author &&
                last.Text == normalized.Text &&
                last.TimeUtc == normalized.TimeUtc;

            if (!duplicate)
            {
                messages.Add(normalized);
            }

            while (messages.Count > _maxMessagesPerLobby)
            {
                messages.RemoveAt(0);
            }
        }
    }

    public void SetObserverUserId(int lobbyId, string? userId)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                _observerUserIds.Remove(lobbyId);
                return;
            }

            _observerUserIds[lobbyId] = userId.Trim();
        }
    }

    public bool IsObserverUser(int lobbyId, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        lock (_sync)
        {
            return _observerUserIds.TryGetValue(lobbyId, out var observerId) &&
                string.Equals(observerId, userId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void RemoveLobby(int lobbyId)
    {
        lock (_sync)
        {
            _messages.Remove(lobbyId);
            _observerUserIds.Remove(lobbyId);
        }
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var cleaned = new string(value
            .Where(character => character is '\t' or '\r' or '\n' || !char.IsControl(character))
            .ToArray())
            .Trim();

        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}
