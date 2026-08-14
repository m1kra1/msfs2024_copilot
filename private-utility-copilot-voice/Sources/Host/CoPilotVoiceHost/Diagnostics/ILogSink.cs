namespace CoPilotVoiceHost.Diagnostics;

/// <summary>WPF-free log sink used by Core/SimConnect (default: <see cref="UiLogSink"/>).</summary>
public interface ILogSink
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Debug(string message);
}
