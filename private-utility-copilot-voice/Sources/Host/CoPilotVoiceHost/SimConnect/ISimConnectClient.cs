using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.SimConnect;

public interface ISimConnectClient : IDisposable
{
    bool IsConnected { get; }
    string StatusMessage { get; }

    /// <summary>Open SimConnect with unique app name. Returns false if sim not available.</summary>
    bool Connect(string appName, int configIndex = 0);

    void Disconnect();

    /// <summary>Current status snapshot (updated via event-based / low-rate requests, ≤ 5 Hz).</summary>
    SimVarSnapshot Snapshot { get; }

    void TransmitEvent(string eventName, uint data = 0);

    void SetSimVar(string name, double value, string units);

    /// <summary>Pump receive queue; call from UI/message loop or timer (not a busy poll of simvars).</summary>
    void ReceiveMessage();
}
