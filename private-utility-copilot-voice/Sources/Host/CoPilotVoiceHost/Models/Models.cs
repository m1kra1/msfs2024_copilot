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
    public string AircraftProfile { get; set; } = HostConstants.DefaultProfileId;

    /// <summary>
    /// When true and SimConnect is live (and CLI --profile is not set), switch
    /// aircraft_profile from config/aircraft_detection.json rules.
    /// </summary>
    [JsonPropertyName("auto_detect_aircraft")]
    public bool AutoDetectAircraft { get; set; }

    /// <summary>Optional short TTS when auto-detect changes the active profile.</summary>
    [JsonPropertyName("announce_profile_switch")]
    public bool AnnounceProfileSwitch { get; set; }
}

/// <summary>Loaded from config/aircraft_detection.json (editable without recompile).</summary>
public sealed class AircraftDetectionConfig
{
    [JsonPropertyName("fallback_profile")]
    public string FallbackProfile { get; set; } = HostConstants.DefaultProfileId;

    [JsonPropertyName("rules")]
    public List<AircraftDetectionRule> Rules { get; set; } = new();
}

public sealed class AircraftDetectionRule
{
    /// <summary>Case-insensitive substring to match.</summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>title | atc_model | any (default any).</summary>
    [JsonPropertyName("match")]
    public string Match { get; set; } = "any";

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = HostConstants.DefaultProfileId;

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

public sealed class SimConnectSettings
{
    [JsonPropertyName("app_name")]
    public string AppName { get; set; } = HostConstants.SimConnectAppName;

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

    /// <summary>
    /// After PTT key release, keep accepting bare phrases for this many ms
    /// so recognition can complete after the key is up.
    /// </summary>
    [JsonPropertyName("ptt_grace_ms")]
    public int PttGraceMs { get; set; } = 3000;
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
    public string Type { get; set; } = HostConstants.ActionTypeEvent;

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
    public string ProfileId { get; set; } = HostConstants.DefaultProfileId;

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

    /// <summary>
    /// Optional Learn Mode watches for this aircraft (Phase G). Merged into the watch set when Learn is on.
    /// </summary>
    [JsonPropertyName("learn_watch")]
    public List<LearnWatchEntry> LearnWatch { get; set; } = new();
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

    /// <summary>
    /// Optional extra log lines (e.g. full command list for list_commands).
    /// Written by the host to console / debug log after the main response.
    /// </summary>
    public IReadOnlyList<string> DetailLogLines { get; init; } = Array.Empty<string>();
}
