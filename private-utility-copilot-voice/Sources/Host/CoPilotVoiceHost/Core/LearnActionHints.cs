using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>
/// Optional dual-write suggestions for common status SimVars (event + set_simvar).
/// Pure Core — used by Learn capture when building SuggestedActions.
/// </summary>
public static class LearnActionHints
{
    /// <summary>
    /// Build suggested actions for a detection: always <c>set_simvar</c> for the signal,
    /// plus a standard event when a known mapping exists for the new value.
    /// </summary>
    public static IReadOnlyList<ActionDefinition> Suggest(
        string signalName,
        double newValue,
        string units)
    {
        var list = new List<ActionDefinition>();
        var name = (signalName ?? "").Trim();
        units = string.IsNullOrWhiteSpace(units) ? "number" : units.Trim();

        if (TryGetEventHint(name, newValue, out var eventName) && !string.IsNullOrWhiteSpace(eventName))
        {
            list.Add(new ActionDefinition
            {
                Type = HostConstants.ActionTypeEvent,
                Name = eventName!
            });
        }

        list.Add(new ActionDefinition
        {
            Type = HostConstants.ActionTypeSetSimVar,
            Name = name,
            Value = newValue,
            Units = units
        });

        return list;
    }

    /// <summary>Maps discrete light / gear style status vars to standard SimConnect events.</summary>
    public static bool TryGetEventHint(string signalName, double newValue, out string? eventName)
    {
        eventName = null;
        if (string.IsNullOrWhiteSpace(signalName))
            return false;

        var key = signalName.Trim();
        var on = newValue > 0.5;

        // Standard LIGHT * bools
        if (key.Equals("LIGHT LANDING", StringComparison.OrdinalIgnoreCase))
        {
            eventName = on ? "LANDING_LIGHTS_ON" : "LANDING_LIGHTS_OFF";
            return true;
        }

        if (key.Equals("LIGHT STROBE", StringComparison.OrdinalIgnoreCase))
        {
            eventName = on ? "STROBES_ON" : "STROBES_OFF";
            return true;
        }

        if (key.Equals("LIGHT BEACON", StringComparison.OrdinalIgnoreCase))
        {
            eventName = on ? "BEACON_LIGHTS_ON" : "BEACON_LIGHTS_OFF";
            return true;
        }

        if (key.Equals("LIGHT NAV", StringComparison.OrdinalIgnoreCase))
        {
            eventName = on ? "NAV_LIGHTS_ON" : "NAV_LIGHTS_OFF";
            return true;
        }

        if (key.Equals("LIGHT TAXI", StringComparison.OrdinalIgnoreCase))
        {
            // No dedicated ON/OFF pair in StandardEventMap — toggle is the mapped event.
            if (on)
            {
                eventName = "TOGGLE_TAXI_LIGHTS";
                return true;
            }

            return false;
        }

        if (key.Equals("BRAKE PARKING POSITION", StringComparison.OrdinalIgnoreCase))
        {
            eventName = "PARKING_BRAKES";
            return true;
        }

        if (key.Equals("ENG ANTI ICE", StringComparison.OrdinalIgnoreCase))
        {
            eventName = on ? "ANTI_ICE_ON" : "ANTI_ICE_OFF";
            return true;
        }

        // GEAR POSITION: percent — treat near 0 as up, near 1 as down
        if (key.Equals("GEAR POSITION", StringComparison.OrdinalIgnoreCase))
        {
            if (newValue < 0.25)
            {
                eventName = "GEAR_UP";
                return true;
            }

            if (newValue > 0.75)
            {
                eventName = "GEAR_DOWN";
                return true;
            }
        }

        return false;
    }
}
