namespace CoPilotVoiceHost.Speech;

/// <summary>
/// Arms PTT on key-down and keeps it armed for a grace period after key-up so
/// speech recognition can finish after the user releases the PTT key.
/// Without grace, OnRecognized often fires with the key already up → bare phrases rejected.
/// </summary>
public sealed class PttArmService : IDisposable
{
    private readonly string _keyName;
    private readonly int _graceMs;
    private readonly object _lock = new();
    private long _armedUntilTick;
    private bool _wasDown;
    private bool _disposed;
    private Timer? _timer;

    public PttArmService(string keyName, int graceMs = 3000)
    {
        _keyName = keyName ?? "F12";
        _graceMs = Math.Clamp(graceMs, 0, 30_000);
    }

    /// <summary>True while key is held or within grace after release.</summary>
    public bool IsArmed
    {
        get
        {
            lock (_lock)
            {
                return Environment.TickCount64 <= _armedUntilTick;
            }
        }
    }

    public bool IsKeyCurrentlyDown => PttKeyboard.IsKeyDown(_keyName);

    public int GraceMs => _graceMs;
    public string KeyName => _keyName;

    /// <summary>Start background edge polling (default 50 ms).</summary>
    public void StartPolling(int intervalMs = 50)
    {
        _timer?.Dispose();
        _timer = new Timer(_ => Poll(), null, 0, Math.Max(20, intervalMs));
    }

    /// <summary>Poll key state once (also called from recognition path).</summary>
    public void Poll()
    {
        if (_disposed) return;
        var down = PttKeyboard.IsKeyDown(_keyName);
        lock (_lock)
        {
            var now = Environment.TickCount64;
            if (down)
            {
                // Hold: keep extending arm window
                _armedUntilTick = now + _graceMs;
            }
            else if (_wasDown)
            {
                // Release edge: start/refresh grace so late recognition still counts
                _armedUntilTick = now + _graceMs;
            }

            _wasDown = down;
        }
    }

    /// <summary>Force-arm (tests / --ptt).</summary>
    public void ForceArm(int? graceMs = null)
    {
        lock (_lock)
        {
            _armedUntilTick = Environment.TickCount64 + (graceMs ?? _graceMs);
            _wasDown = false;
        }
    }

    public void Disarm()
    {
        lock (_lock)
        {
            _armedUntilTick = 0;
            _wasDown = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
