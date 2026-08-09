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
/// </summary>
public sealed class UiLogSink
{
    private readonly ConcurrentQueue<LogEntry> _buffer = new();
    private readonly int _maxBuffered;

    public UiLogSink(int maxBuffered = 2000)
    {
        _maxBuffered = Math.Max(100, maxBuffered);
    }

    public event Action<LogEntry>? LineAppended;

    public void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.UtcNow, level, message ?? string.Empty);
        _buffer.Enqueue(entry);
        while (_buffer.Count > _maxBuffered && _buffer.TryDequeue(out _)) { }

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
    }
}
