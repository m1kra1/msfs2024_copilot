namespace CoPilotVoiceHost.Models;

/// <summary>
/// Stable host identifiers and config path fragments shared across Core, Config, Host, and UI.
/// Defaults match JSON schema property values — do not change without intentional product decision.
/// </summary>
public static class HostConstants
{
    public const string PackageName = "private-utility-copilot-voice";
    public const string DefaultProfileId = "generic";
    public const string SimConnectAppName = "PrivateCoPilotVoice";
    public const string SpeechGrammarName = "PrivateCoPilotVoiceGrammar";

    public const string SettingsFileName = "settings.json";
    public const string BaseCommandsFileName = "base_commands.json";
    public const string AircraftDetectionFileName = "aircraft_detection.json";
    public const string LearnWatchlistFileName = "learn_watchlist.json";
    public const string AircraftProfilesDirectoryName = "aircraft";
    public const string ChecklistsDirectoryName = "checklists";
    public const string ManifestFileName = "manifest.json";
    public const string HeadlessLogFileName = "copilot-host-headless.log";

    public const string ActionTypeEvent = "event";
    public const string ActionTypeSimVar = "simvar";
    public const string ActionTypeSetSimVar = "set_simvar";

    public const string GearUpCommandId = "gear_up";
    public const string VerticalSpeedSimVar = "VERTICAL SPEED";
    public const double GearUpMinVerticalSpeedFpm = 100;

    public const double DefaultOfflineVerticalSpeedFpm = 500;

    // ── Checklist system ──────────────────────────────────────────────────────
    public const int ChecklistDefaultGlobalDelayMs = 400;
    public const int ChecklistMaxDelayMs = 5000;
    public const string ChecklistDefaultResponseOk = "Checked";
    public const string ChecklistToken = "checklist";

    // ── Learn Mode ────────────────────────────────────────────────────────────
    /// <summary>Hard cap on SimConnect learn watch definitions (SECOND period).</summary>
    public const int LearnMaxWatches = 128;

    /// <summary>Ignore self-echo detections for this long after host-executed actions.</summary>
    public const int LearnSuppressMs = 750;

    /// <summary>Bounded ring buffer for recent Learn detections (newest first).</summary>
    public const int LearnRecentCapacity = 100;

    /// <summary>Float epsilon for LearnCaptureService value diffs.</summary>
    public const double LearnDiffEpsilon = 1e-4;

    /// <summary>
    /// Same-signal re-fires within this window update the latest detection instead of appending (debounce).
    /// </summary>
    public const int LearnDebounceMs = 400;

    /// <summary>Whitespace / null → <see cref="DefaultProfileId"/>; otherwise trimmed name.</summary>
    public static string NormalizeProfileId(string? profileName) =>
        string.IsNullOrWhiteSpace(profileName) ? DefaultProfileId : profileName.Trim();
}
