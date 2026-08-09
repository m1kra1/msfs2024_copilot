using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.SimConnect;

/// <summary>
/// Offline / test client that records transmitted events and holds an injectable snapshot.
/// Used when Managed SimConnect assembly is unavailable and for unit tests.
/// </summary>
public sealed class RecordingSimConnectClient : ISimConnectClient
{
    private readonly List<(string Name, uint Data)> _events = new();
    private readonly List<(string Name, double Value, string Units)> _sets = new();

    public bool IsConnected { get; private set; }
    /// <summary>Always false — events are not sent to MSFS.</summary>
    public bool IsLive => false;
    public string StatusMessage { get; private set; } = "Not connected";
    public SimVarSnapshot Snapshot { get; } = new();
    public string AircraftTitle { get; set; } = "";
    public string AtcModel { get; set; } = "";
    public IReadOnlyList<(string Name, uint Data)> TransmittedEvents => _events;
    public IReadOnlyList<(string Name, double Value, string Units)> SetSimVars => _sets;

    public bool Connect(string appName, int configIndex = 0)
    {
        // Offline mode: we "connect" to the recorder so the host pipeline can run.
        IsConnected = true;
        StatusMessage =
            $"OFFLINE/recording mode (app_name={appName}, config_index={configIndex}). " +
            "Events are NOT sent to MSFS — only logged.";
        Console.WriteLine("[SimConnect] *** OFFLINE — sim will NOT receive gear/lights/etc. ***");
        return true;
    }

    public void Disconnect()
    {
        IsConnected = false;
        StatusMessage = "Disconnected (recording client)";
    }

    public void TransmitEvent(string eventName, uint data = 0)
    {
        _events.Add((eventName, data));
        Console.WriteLine($"[Event] {eventName} data={data} (OFFLINE — not sent to sim)");
    }

    public void SetSimVar(string name, double value, string units)
    {
        _sets.Add((name, value, units));
        Snapshot.Set(name, value);
    }

    public void ReceiveMessage()
    {
        // No-op: no message queue.
    }

    public void Dispose()
    {
        Disconnect();
    }

    public void ClearLog()
    {
        _events.Clear();
        _sets.Clear();
    }
}
