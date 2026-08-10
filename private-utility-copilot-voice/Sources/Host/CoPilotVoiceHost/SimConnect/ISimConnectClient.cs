using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.SimConnect;

public interface ISimConnectClient : IDisposable
{
    bool IsConnected { get; }

    /// <summary>True when events actually go to MSFS (not offline recording).</summary>
    bool IsLive { get; }

    string StatusMessage { get; }

    /// <summary>Open SimConnect with unique app name. Returns false if sim not available.</summary>
    bool Connect(string appName, int configIndex = 0);

    void Disconnect();

    /// <summary>Current status snapshot (updated via event-based / low-rate requests, ≤ 5 Hz).</summary>
    SimVarSnapshot Snapshot { get; }

    /// <summary>TITLE SimVar when live (empty if unknown / offline).</summary>
    string AircraftTitle { get; }

    /// <summary>ATC MODEL SimVar when live (empty if unknown / offline).</summary>
    string AtcModel { get; }

    /// <summary>Best-effort airport ident (approach / GPS) when live; empty if unknown.</summary>
    string AirportIdent { get; }

    /// <summary>True after at least one DEF_STATUS payload was applied to <see cref="Snapshot"/>.</summary>
    bool HasReceivedStatusData { get; }

    void TransmitEvent(string eventName, uint data = 0);

    void SetSimVar(string name, double value, string units);

    /// <summary>
    /// Register extra named vars for Learn Mode (SECOND period).
    /// Must use a separate data definition from status so failed LVars cannot break the dashboard.
    /// No busy polling.
    /// </summary>
    void SetLearnWatchDefinitions(IReadOnlyList<(string Name, string Units)> vars);

    /// <summary>Clear Learn Mode watch definitions and stop the learn data request.</summary>
    void ClearLearnWatchDefinitions();

    /// <summary>Pump receive queue; call from UI/message loop or timer (not a busy poll of simvars).</summary>
    void ReceiveMessage();
}
