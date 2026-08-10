namespace CoPilotVoiceHost.SimConnect;

/// <summary>
/// Unique SimConnect definition / request / client-event IDs for PrivateCoPilotVoice.
/// Naming schema: PRIVATE_COPILOT_* — never reuse standard enum ranges blindly.
/// </summary>
public enum PrivateCopilotDefineId : uint
{
    PRIVATE_COPILOT_DEF_STATUS = 0xC0_01_00_01,
    PRIVATE_COPILOT_DEF_AIRCRAFT = 0xC0_01_00_02,
    /// <summary>Ad-hoc single FLOAT64 write definition for SetSimVar (A:/L:).</summary>
    PRIVATE_COPILOT_DEF_SET_VAR = 0xC0_01_02_01
}

/// <summary>Single double payload for managed SetDataOnSimObject writes.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct PrivateCopilotSetVarData
{
    public double Value;
}

public enum PrivateCopilotRequestId : uint
{
    PRIVATE_COPILOT_REQ_STATUS = 0xC0_02_00_01,
    PRIVATE_COPILOT_REQ_AIRCRAFT = 0xC0_02_00_02
}

public enum PrivateCopilotEventId : uint
{
    PRIVATE_COPILOT_EVT_GEAR_UP = 0xC0_03_00_01,
    PRIVATE_COPILOT_EVT_GEAR_DOWN = 0xC0_03_00_02,
    PRIVATE_COPILOT_EVT_LANDING_LIGHTS_ON = 0xC0_03_00_03,
    PRIVATE_COPILOT_EVT_LANDING_LIGHTS_OFF = 0xC0_03_00_04,
    PRIVATE_COPILOT_EVT_TOGGLE_TAXI_LIGHTS = 0xC0_03_00_05,
    PRIVATE_COPILOT_EVT_STROBES_ON = 0xC0_03_00_06,
    PRIVATE_COPILOT_EVT_STROBES_OFF = 0xC0_03_00_07,
    PRIVATE_COPILOT_EVT_BEACON_LIGHTS_ON = 0xC0_03_00_08,
    PRIVATE_COPILOT_EVT_BEACON_LIGHTS_OFF = 0xC0_03_00_09,
    PRIVATE_COPILOT_EVT_NAV_LIGHTS_ON = 0xC0_03_00_0A,
    PRIVATE_COPILOT_EVT_NAV_LIGHTS_OFF = 0xC0_03_00_0B,
    PRIVATE_COPILOT_EVT_FLAPS_UP = 0xC0_03_00_0C,
    PRIVATE_COPILOT_EVT_FLAPS_DOWN = 0xC0_03_00_0D,
    PRIVATE_COPILOT_EVT_FLAPS_1 = 0xC0_03_00_0E,
    PRIVATE_COPILOT_EVT_FLAPS_2 = 0xC0_03_00_0F,
    PRIVATE_COPILOT_EVT_FLAPS_3 = 0xC0_03_00_10,
    PRIVATE_COPILOT_EVT_FLAPS_INCR = 0xC0_03_00_11,
    PRIVATE_COPILOT_EVT_FLAPS_DECR = 0xC0_03_00_12,
    PRIVATE_COPILOT_EVT_AUTOPILOT_ON = 0xC0_03_00_13,
    PRIVATE_COPILOT_EVT_AUTOPILOT_OFF = 0xC0_03_00_14,
    PRIVATE_COPILOT_EVT_TOGGLE_FLIGHT_DIRECTOR = 0xC0_03_00_15,
    PRIVATE_COPILOT_EVT_AP_HDG_HOLD_ON = 0xC0_03_00_16,
    PRIVATE_COPILOT_EVT_AP_ALT_HOLD_ON = 0xC0_03_00_17,
    PRIVATE_COPILOT_EVT_AP_AIRSPEED_HOLD_ON = 0xC0_03_00_18,
    PRIVATE_COPILOT_EVT_AP_VS_HOLD = 0xC0_03_00_19,
    PRIVATE_COPILOT_EVT_AP_NAV1_HOLD_ON = 0xC0_03_00_1A,
    PRIVATE_COPILOT_EVT_AP_APR_HOLD_ON = 0xC0_03_00_1B,
    PRIVATE_COPILOT_EVT_AP_LOC_HOLD_ON = 0xC0_03_00_1C,
    PRIVATE_COPILOT_EVT_HEADING_BUG_INC = 0xC0_03_00_1D,
    PRIVATE_COPILOT_EVT_HEADING_BUG_DEC = 0xC0_03_00_1E,
    PRIVATE_COPILOT_EVT_AP_ALT_VAR_INC = 0xC0_03_00_1F,
    PRIVATE_COPILOT_EVT_AP_ALT_VAR_DEC = 0xC0_03_00_20,
    PRIVATE_COPILOT_EVT_AP_SPD_VAR_INC = 0xC0_03_00_21,
    PRIVATE_COPILOT_EVT_AP_SPD_VAR_DEC = 0xC0_03_00_22,
    PRIVATE_COPILOT_EVT_AP_VS_VAR_INC = 0xC0_03_00_23,
    PRIVATE_COPILOT_EVT_AP_VS_VAR_DEC = 0xC0_03_00_24,
    PRIVATE_COPILOT_EVT_ANTI_ICE_ON = 0xC0_03_00_25,
    PRIVATE_COPILOT_EVT_ANTI_ICE_OFF = 0xC0_03_00_26,
    PRIVATE_COPILOT_EVT_PITOT_HEAT_ON = 0xC0_03_00_27,
    PRIVATE_COPILOT_EVT_PITOT_HEAT_OFF = 0xC0_03_00_28,
    PRIVATE_COPILOT_EVT_PARKING_BRAKES = 0xC0_03_00_29,
    PRIVATE_COPILOT_EVT_SPOILERS_ARM_ON = 0xC0_03_00_2A,
    PRIVATE_COPILOT_EVT_SPOILERS_ARM_OFF = 0xC0_03_00_2B,
    PRIVATE_COPILOT_EVT_SPOILERS_ON = 0xC0_03_00_2C,
    PRIVATE_COPILOT_EVT_SPOILERS_OFF = 0xC0_03_00_2D,
    PRIVATE_COPILOT_EVT_APU_STARTER = 0xC0_03_00_2E,
    PRIVATE_COPILOT_EVT_APU_OFF_SWITCH = 0xC0_03_00_2F,
    PRIVATE_COPILOT_EVT_CABIN_LIGHTS_ON = 0xC0_03_00_30,
    PRIVATE_COPILOT_EVT_CABIN_LIGHTS_OFF = 0xC0_03_00_31,
    PRIVATE_COPILOT_EVT_TOGGLE_WING_LIGHTS = 0xC0_03_00_32,
    PRIVATE_COPILOT_EVT_TOGGLE_LOGO_LIGHTS = 0xC0_03_00_33,
    PRIVATE_COPILOT_EVT_AP_MANAGED_SPEED_IN_MACH_OFF = 0xC0_03_00_34,
    PRIVATE_COPILOT_EVT_AUTO_THROTTLE_TO_GA = 0xC0_03_00_35,
    PRIVATE_COPILOT_EVT_GENERIC = 0xC0_03_FF_FF
}

public static class StandardEventMap
{
    private static readonly Dictionary<string, PrivateCopilotEventId> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["GEAR_UP"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_GEAR_UP,
            ["GEAR_DOWN"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_GEAR_DOWN,
            ["LANDING_LIGHTS_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_LANDING_LIGHTS_ON,
            ["LANDING_LIGHTS_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_LANDING_LIGHTS_OFF,
            ["TOGGLE_TAXI_LIGHTS"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_TOGGLE_TAXI_LIGHTS,
            ["STROBES_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_STROBES_ON,
            ["STROBES_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_STROBES_OFF,
            ["BEACON_LIGHTS_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_BEACON_LIGHTS_ON,
            ["BEACON_LIGHTS_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_BEACON_LIGHTS_OFF,
            ["NAV_LIGHTS_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_NAV_LIGHTS_ON,
            ["NAV_LIGHTS_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_NAV_LIGHTS_OFF,
            ["FLAPS_UP"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_FLAPS_UP,
            ["FLAPS_DOWN"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_FLAPS_DOWN,
            ["FLAPS_1"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_FLAPS_1,
            ["FLAPS_2"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_FLAPS_2,
            ["FLAPS_3"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_FLAPS_3,
            ["FLAPS_INCR"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_FLAPS_INCR,
            ["FLAPS_DECR"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_FLAPS_DECR,
            ["AUTOPILOT_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AUTOPILOT_ON,
            ["AUTOPILOT_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AUTOPILOT_OFF,
            ["TOGGLE_FLIGHT_DIRECTOR"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_TOGGLE_FLIGHT_DIRECTOR,
            ["AP_HDG_HOLD_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_HDG_HOLD_ON,
            ["AP_ALT_HOLD_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_ALT_HOLD_ON,
            ["AP_AIRSPEED_HOLD_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_AIRSPEED_HOLD_ON,
            ["AP_VS_HOLD"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_VS_HOLD,
            ["AP_NAV1_HOLD_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_NAV1_HOLD_ON,
            ["AP_APR_HOLD_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_APR_HOLD_ON,
            ["AP_LOC_HOLD_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_LOC_HOLD_ON,
            ["HEADING_BUG_INC"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_HEADING_BUG_INC,
            ["HEADING_BUG_DEC"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_HEADING_BUG_DEC,
            ["AP_ALT_VAR_INC"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_ALT_VAR_INC,
            ["AP_ALT_VAR_DEC"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_ALT_VAR_DEC,
            ["AP_SPD_VAR_INC"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_SPD_VAR_INC,
            ["AP_SPD_VAR_DEC"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_SPD_VAR_DEC,
            ["AP_VS_VAR_INC"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_VS_VAR_INC,
            ["AP_VS_VAR_DEC"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_VS_VAR_DEC,
            ["ANTI_ICE_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_ANTI_ICE_ON,
            ["ANTI_ICE_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_ANTI_ICE_OFF,
            ["PITOT_HEAT_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_PITOT_HEAT_ON,
            ["PITOT_HEAT_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_PITOT_HEAT_OFF,
            ["PARKING_BRAKES"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_PARKING_BRAKES,
            ["SPOILERS_ARM_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_SPOILERS_ARM_ON,
            ["SPOILERS_ARM_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_SPOILERS_ARM_OFF,
            ["SPOILERS_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_SPOILERS_ON,
            ["SPOILERS_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_SPOILERS_OFF,
            ["APU_STARTER"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_APU_STARTER,
            ["APU_OFF_SWITCH"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_APU_OFF_SWITCH,
            ["CABIN_LIGHTS_ON"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_CABIN_LIGHTS_ON,
            ["CABIN_LIGHTS_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_CABIN_LIGHTS_OFF,
            ["TOGGLE_WING_LIGHTS"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_TOGGLE_WING_LIGHTS,
            ["TOGGLE_LOGO_LIGHTS"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_TOGGLE_LOGO_LIGHTS,
            ["AP_MANAGED_SPEED_IN_MACH_OFF"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AP_MANAGED_SPEED_IN_MACH_OFF,
            ["AUTO_THROTTLE_TO_GA"] = PrivateCopilotEventId.PRIVATE_COPILOT_EVT_AUTO_THROTTLE_TO_GA,
        };

    public static bool TryGet(string simEventName, out PrivateCopilotEventId id) =>
        Map.TryGetValue(simEventName, out id);

    public static IReadOnlyDictionary<string, PrivateCopilotEventId> All => Map;
}

/// <summary>Status SimVars requested at ≤ 5 Hz (SIMCONNECT_PERIOD_SIM_FRAME with skip, or SECOND).</summary>
public static class StatusSimVars
{
    public static readonly (string Name, string Units)[] Definitions =
    {
        ("GEAR POSITION", "percent over 100"),
        ("VERTICAL SPEED", "feet per minute"),
        ("FLAPS HANDLE INDEX", "number"),
        ("AUTOPILOT MASTER", "bool"),
        ("LIGHT LANDING", "bool"),
        ("LIGHT TAXI", "bool"),
        ("LIGHT STROBE", "bool"),
        ("LIGHT BEACON", "bool"),
        ("LIGHT NAV", "bool"),
        ("BRAKE PARKING POSITION", "bool"),
        ("ANTITOCK BRAKES ACTIVE", "bool"),
        ("ENG ANTI ICE", "bool"),
        ("AIRSPEED INDICATED", "knots"),
        ("PLANE ALTITUDE", "feet"),
        ("SIM ON GROUND", "bool")
    };
}
