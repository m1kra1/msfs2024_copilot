using System.Runtime.InteropServices;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.SimConnect;

/// <summary>
/// TITLE (STRING256) + ATC MODEL (STRING32) for aircraft auto-detection.
/// Field order matches AddToDataDefinition registration.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
public struct PrivateCopilotAircraftData
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Title;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string AtcModel;
}

/// <summary>
/// Sequential layout matching <see cref="StatusSimVars.Definitions"/> order for
/// RegisterDataDefineStruct / OnRecvSimObjectData.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PrivateCopilotStatusData
{
    public double GearPosition;
    public double VerticalSpeed;
    public double FlapsHandleIndex;
    public double AutopilotMaster;
    public double LightLanding;
    public double LightTaxi;
    public double LightStrobe;
    public double LightBeacon;
    public double LightNav;
    public double BrakeParkingPosition;
    public double AntiSkidBrakesActive;
    public double EngAntiIce;
    public double AirspeedIndicated;
    public double PlaneAltitude;
    public double SimOnGround;
}

/// <summary>
/// Writes DEF_STATUS payloads into a <see cref="SimVarSnapshot"/>.
/// Used by live ManagedSimConnect recv and unit tests.
/// </summary>
public static class StatusSnapshotMapper
{
    public static void Apply(SimVarSnapshot snapshot, PrivateCopilotStatusData data)
    {
        snapshot.Set("GEAR POSITION", data.GearPosition);
        snapshot.Set("VERTICAL SPEED", data.VerticalSpeed);
        snapshot.Set("FLAPS HANDLE INDEX", data.FlapsHandleIndex);
        snapshot.Set("AUTOPILOT MASTER", data.AutopilotMaster);
        snapshot.Set("LIGHT LANDING", data.LightLanding);
        snapshot.Set("LIGHT TAXI", data.LightTaxi);
        snapshot.Set("LIGHT STROBE", data.LightStrobe);
        snapshot.Set("LIGHT BEACON", data.LightBeacon);
        snapshot.Set("LIGHT NAV", data.LightNav);
        snapshot.Set("BRAKE PARKING POSITION", data.BrakeParkingPosition);
        snapshot.Set("ANTITOCK BRAKES ACTIVE", data.AntiSkidBrakesActive);
        snapshot.Set("ENG ANTI ICE", data.EngAntiIce);
        snapshot.Set("AIRSPEED INDICATED", data.AirspeedIndicated);
        snapshot.Set("PLANE ALTITUDE", data.PlaneAltitude);
        snapshot.Set("SIM ON GROUND", data.SimOnGround);
    }

    /// <summary>Apply ordered doubles in the same order as <see cref="StatusSimVars.Definitions"/>.</summary>
    public static void Apply(SimVarSnapshot snapshot, ReadOnlySpan<double> values)
    {
        var defs = StatusSimVars.Definitions;
        var n = Math.Min(defs.Length, values.Length);
        for (var i = 0; i < n; i++)
            snapshot.Set(defs[i].Name, values[i]);
    }

    public static int ExpectedFieldCount => StatusSimVars.Definitions.Length;
}
