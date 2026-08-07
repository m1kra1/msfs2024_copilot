using System.Text.Json.Serialization;

namespace CoPilotVoiceHost.Models;

public sealed class AppSettings
{
    [JsonPropertyName("simconnect")]
    public SimConnectSettings SimConnect { get; set; } = new();

    [JsonPropertyName("speech")]
    public SpeechSettings Speech { get; set; } = new();

    [JsonPropertyName("tts")]
    public TtsSettings Tts { get; set; } = new();

    [JsonPropertyName("behavior")]
    public BehaviorSettings Behavior { get; set; } = new();

    [JsonPropertyName("aircraft_profile")]
    public string AircraftProfile { get; set; } = "generic";
}

public sealed class SimConnectSettings
{
    [JsonPropertyName("app_name")]
    public string AppName { get; set; } = "PrivateCoPilotVoice";

    [JsonPropertyName("config_index")]
    public int ConfigIndex { get; set; }
}

public sealed class SpeechSettings
{
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "Windows";

    [JsonPropertyName("culture")]
    public string Culture { get; set; } = "en-US";

    [JsonPropertyName("wake_word")]
    public string WakeWord { get; set; } = "Co Pilot";

    [JsonPropertyName("ptt_key")]
    public string PttKey { get; set; } = "F12";

    [JsonPropertyName("continuous_listen")]
    public bool ContinuousListen { get; set; }

    [JsonPropertyName("confidence_threshold")]
    public double ConfidenceThreshold { get; set; } = 0.75;
}

public sealed class TtsSettings
{
    [JsonPropertyName("voice")]
    public string Voice { get; set; } = "Microsoft David";

    [JsonPropertyName("rate")]
    public int Rate { get; set; }

    [JsonPropertyName("volume")]
    public int Volume { get; set; } = 100;
}

public sealed class BehaviorSettings
{
    [JsonPropertyName("confirm_before_action")]
    public bool ConfirmBeforeAction { get; set; } = true;

    [JsonPropertyName("require_positive_climb_for_gear_up")]
    public bool RequirePositiveClimbForGearUp { get; set; } = true;

    [JsonPropertyName("callout_delay_ms")]
    public int CalloutDelayMs { get; set; } = 400;
}

public sealed class CommandCatalog
{
    [JsonPropertyName("commands")]
    public List<CommandDefinition> Commands { get; set; } = new();
}

public sealed class CommandDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("phrases")]
    public List<string> Phrases { get; set; } = new();

    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("reject_response")]
    public string? RejectResponse { get; set; }

    [JsonPropertyName("conditions")]
    public List<ConditionDefinition> Conditions { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<ActionDefinition> Actions { get; set; } = new();

    [JsonPropertyName("require_positive_climb_flag")]
    public bool RequirePositiveClimbFlag { get; set; }

    [JsonPropertyName("checklist")]
    public bool Checklist { get; set; }
}

public sealed class ConditionDefinition
{
    [JsonPropertyName("simvar")]
    public string SimVar { get; set; } = string.Empty;

    [JsonPropertyName("op")]
    public string Op { get; set; } = "==";

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("units")]
    public string Units { get; set; } = "number";
}

public sealed class ActionDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "event";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("units")]
    public string? Units { get; set; }
}

public sealed class AircraftProfile
{
    [JsonPropertyName("profile_id")]
    public string ProfileId { get; set; } = "generic";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("extends")]
    public string? Extends { get; set; }

    [JsonPropertyName("commands")]
    public List<CommandDefinition> Commands { get; set; } = new();

    [JsonPropertyName("simvar_aliases")]
    public Dictionary<string, string> SimVarAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("event_aliases")]
    public Dictionary<string, string> EventAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Live or fixture snapshot of SimVars used by the condition engine.</summary>
public sealed class SimVarSnapshot
{
    private readonly Dictionary<string, double> _values =
        new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, double value) => _values[Normalize(name)] = value;

    public bool TryGet(string name, out double value) =>
        _values.TryGetValue(Normalize(name), out value);

    public IReadOnlyDictionary<string, double> Values => _values;

    public static string Normalize(string name) =>
        name.Trim().ToUpperInvariant();
}

public sealed class CommandResult
{
    public required string CommandId { get; init; }
    public required string MatchedPhrase { get; init; }
    public bool Allowed { get; init; }
    public string SpokenResponse { get; init; } = string.Empty;
    public IReadOnlyList<ActionDefinition> ActionsExecuted { get; init; } = Array.Empty<ActionDefinition>();
    public string? DenyReason { get; init; }
}
