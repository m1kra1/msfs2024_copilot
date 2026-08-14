using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Host;

/// <summary>
/// Pure auto-detect decision (no disk / speech / TTS). HostSession applies side effects outside the detect lock.
/// </summary>
public static class AircraftIdentityService
{
    public readonly record struct Decision(
        bool Silent,
        bool UpdateDisplayOnly,
        bool SwitchProfile,
        string DisplayTitle,
        string DisplayModel,
        string IdentityKey,
        string MatchedProfile,
        string PreviousProfile);

    public static Decision Evaluate(
        string? title,
        string? atcModel,
        string? lastSeenDisplayKey,
        string lastSwitchKey,
        bool canAutoSwitch,
        string currentProfile,
        AircraftDetectionConfig detection,
        Func<string, string> ensureProfileExists)
    {
        var key = AircraftProfileMatcher.IdentityKey(title, atcModel);
        var displayTitle = AircraftProfileMatcher.DisplayTitle(title);
        var displayModel = (atcModel ?? string.Empty).Trim();

        if (key.Length == 0)
        {
            if (lastSeenDisplayKey is not null && lastSeenDisplayKey.Length == 0)
                return new Decision(Silent: true, false, false, AircraftProfileMatcher.UnknownTitle, "", "", currentProfile, currentProfile);

            return new Decision(
                Silent: false,
                UpdateDisplayOnly: true,
                SwitchProfile: false,
                DisplayTitle: AircraftProfileMatcher.UnknownTitle,
                DisplayModel: "",
                IdentityKey: "",
                MatchedProfile: currentProfile,
                PreviousProfile: currentProfile);
        }

        if (!canAutoSwitch)
        {
            if (string.Equals(key, lastSeenDisplayKey, StringComparison.Ordinal))
                return new Decision(true, false, false, displayTitle, displayModel, key, currentProfile, currentProfile);

            var preview = ensureProfileExists(
                AircraftProfileMatcher.ResolveProfile(detection, title, atcModel));
            return new Decision(false, true, false, displayTitle, displayModel, key, preview, currentProfile);
        }

        if (string.Equals(key, lastSwitchKey, StringComparison.Ordinal))
            return new Decision(true, false, false, displayTitle, displayModel, key, currentProfile, currentProfile);

        var matched = ensureProfileExists(
            AircraftProfileMatcher.ResolveProfile(detection, title, atcModel));
        var switchProfile = !string.Equals(currentProfile, matched, StringComparison.OrdinalIgnoreCase);
        return new Decision(
            Silent: false,
            UpdateDisplayOnly: !switchProfile,
            SwitchProfile: switchProfile,
            DisplayTitle: displayTitle,
            DisplayModel: displayModel,
            IdentityKey: key,
            MatchedProfile: matched,
            PreviousProfile: currentProfile);
    }
}
