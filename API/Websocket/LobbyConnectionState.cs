namespace BZAPI.Websocket;

/// <summary>
/// Thread-safe runtime connection state for the primary public BZ98 lobby websocket.
/// This deliberately contains no credentials or player data; it exists so the public UI can
/// distinguish "no lobby changes recently" from an actually disconnected watcher.
/// </summary>
public sealed class LobbyConnectionState
{
    private readonly object _sync = new();
    private bool _isConnected;
    private DateTimeOffset? _lastConnectedUtc;
    private DateTimeOffset? _lastDisconnectedUtc;
    private DateTimeOffset? _lastMessageUtc;
    private string _state = "starting";

    public LobbyConnectionSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return new LobbyConnectionSnapshot(
                    _state,
                    _isConnected,
                    _lastConnectedUtc,
                    _lastDisconnectedUtc,
                    _lastMessageUtc);
            }
        }
    }

    public void MarkConnected(DateTimeOffset? nowUtc = null)
    {
        var now = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        lock (_sync)
        {
            _isConnected = true;
            _state = "connected";
            _lastConnectedUtc = now;
        }
    }

    public void MarkMessage(DateTimeOffset? nowUtc = null)
    {
        var now = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        lock (_sync)
        {
            _lastMessageUtc = now;
            if (_isConnected)
            {
                _state = "connected";
            }
        }
    }

    public void MarkDisconnected(DateTimeOffset? nowUtc = null)
    {
        var now = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        lock (_sync)
        {
            _isConnected = false;
            _state = "disconnected";
            _lastDisconnectedUtc = now;
        }
    }
}

public sealed record LobbyConnectionSnapshot(
    string State,
    bool IsConnected,
    DateTimeOffset? LastConnectedUtc,
    DateTimeOffset? LastDisconnectedUtc,
    DateTimeOffset? LastMessageUtc);
