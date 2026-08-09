using System.Collections.Concurrent;

namespace CoPilotVoiceHost.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public readonly record struct LogEntry(DateTime Utc, LogLevel Level, string Message);

/// <summary>
/// Thread-safe log fan-out for console + UI. No WPF types.
/// Bounded ring-style buffer: oldest entries drop when over capacity.
/// </summary>
public sealed class UiLogSink
{
    public const int DefaultMaxBuffered = 2000;

    private readonly ConcurrentQueue<LogEntry> _buffer = new();
    private readonly int _maxBuffered;
    private int _approxCount;

    public UiLogSink(int maxBuffered = DefaultMaxBuffered)
    {
        _maxBuffered = Math.Max(100, maxBuffered);
    }

    /// <summary>Maximum entries retained in the in-memory buffer (and recommended UI cap).</summary>
    public int MaxBuffered => _maxBuffered;

    /// <summary>Approximate buffered count (may briefly exceed MaxBuffered under concurrent writers).</summary>
    public int Count => _approxCount;

    public event Action<LogEntry>? LineAppended;

    public void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.UtcNow, level, message ?? string.Empty);
        _buffer.Enqueue(entry);
        var count = Interlocked.Increment(ref _approxCount);
        while (count > _maxBuffered && _buffer.TryDequeue(out _))
            count = Interlocked.Decrement(ref _approxCount);

        try
        {
            var prefix = level switch
            {
                LogLevel.Warn => "[WARN] ",
                LogLevel.Error => "[ERROR] ",
                LogLevel.Debug => "[DBG] ",
                _ => ""
            };
            Console.WriteLine(prefix + entry.Message);
        }
        catch
        {
            // no console attached
        }

        LineAppended?.Invoke(entry);
    }

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warn(string message) => Write(LogLevel.Warn, message);
    public void Error(string message) => Write(LogLevel.Error, message);
    public void Debug(string message) => Write(LogLevel.Debug, message);

    public IReadOnlyList<LogEntry> Snapshot() => _buffer.ToArray();

    public void Clear()
    {
        while (_buffer.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _approxCount, 0);
    }
}
