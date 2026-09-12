using BZAPI.Configuration;

namespace BZAPI.Websocket;

/// <summary>
/// Process-wide sliding-window limiter for read-only chat observer socket creation.
/// State intentionally survives individual lobby session replacement.
/// </summary>
internal sealed class ChatObserverConnectionBudget(ChatObserverOptions options)
{
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _attempts = [];
    private readonly int _maxAttempts = Math.Clamp(options.MaxConnectionAttemptsPerWindow, 1, 16);
    private readonly TimeSpan _window = options.ConnectionAttemptWindow > TimeSpan.Zero
        ? options.ConnectionAttemptWindow
        : TimeSpan.FromMinutes(30);
    private readonly TimeSpan _openDuration = options.CircuitOpenDuration > TimeSpan.Zero
        ? options.CircuitOpenDuration
        : TimeSpan.FromHours(1);
    private DateTimeOffset? _openUntil;

    public bool TryReserve(DateTimeOffset now, out TimeSpan wait)
    {
        lock (_gate)
        {
            while (_attempts.Count > 0 && now - _attempts.Peek() >= _window)
            {
                _attempts.Dequeue();
            }

            if (_openUntil is { } openUntil && now < openUntil)
            {
                wait = openUntil - now;
                return false;
            }

            _openUntil = null;

            if (_attempts.Count >= _maxAttempts)
            {
                _openUntil = now + _openDuration;
                wait = _openDuration;
                return false;
            }

            _attempts.Enqueue(now);
            wait = TimeSpan.Zero;
            return true;
        }
    }

    public void OpenCircuit(DateTimeOffset now)
    {
        lock (_gate)
        {
            var proposed = now + _openDuration;
            if (_openUntil is null || proposed > _openUntil)
            {
                _openUntil = proposed;
            }
        }
    }
}
