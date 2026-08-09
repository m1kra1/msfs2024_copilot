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
    public const string AircraftProfilesDirectoryName = "aircraft";
    public const string ManifestFileName = "manifest.json";
    public const string HeadlessLogFileName = "copilot-host-headless.log";

    public const string ActionTypeEvent = "event";
    public const string ActionTypeSimVar = "simvar";
    public const string ActionTypeSetSimVar = "set_simvar";

    public const string GearUpCommandId = "gear_up";
    public const string VerticalSpeedSimVar = "VERTICAL SPEED";
    public const double GearUpMinVerticalSpeedFpm = 100;

    public const double DefaultOfflineVerticalSpeedFpm = 500;

    /// <summary>Whitespace / null → <see cref="DefaultProfileId"/>; otherwise trimmed name.</summary>
    public static string NormalizeProfileId(string? profileName) =>
        string.IsNullOrWhiteSpace(profileName) ? DefaultProfileId : profileName.Trim();
}
