using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>
/// Pure rule matcher: aircraft title / ATC model → profile id.
/// Case-insensitive contains match; first matching rule wins; otherwise fallback.
/// </summary>
public static class AircraftProfileMatcher
{
    public const string UnknownTitle = "Unknown";

    /// <summary>
    /// Resolve profile name from identity strings and detection config.
    /// Empty/whitespace title and model both yield fallback.
    /// </summary>
    public static string ResolveProfile(
        AircraftDetectionConfig config,
        string? title,
        string? atcModel)
    {
        config ??= new AircraftDetectionConfig();
        var fallback = string.IsNullOrWhiteSpace(config.FallbackProfile)
            ? "generic"
            : config.FallbackProfile.Trim();

        var t = (title ?? string.Empty).Trim();
        var m = (atcModel ?? string.Empty).Trim();
        if (t.Length == 0 && m.Length == 0)
            return fallback;

        foreach (var rule in config.Rules ?? new List<AircraftDetectionRule>())
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Pattern) || string.IsNullOrWhiteSpace(rule.Profile))
                continue;

            if (RuleMatches(rule, t, m))
                return rule.Profile.Trim();
        }

        return fallback;
    }

    /// <summary>Stable identity key for change detection (title|model, case-insensitive normalize).</summary>
    public static string IdentityKey(string? title, string? atcModel)
    {
        var t = (title ?? string.Empty).Trim();
        var m = (atcModel ?? string.Empty).Trim();
        if (t.Length == 0 && m.Length == 0)
            return "";
        return t.ToUpperInvariant() + "\n" + m.ToUpperInvariant();
    }

    public static string DisplayTitle(string? title)
    {
        var t = (title ?? string.Empty).Trim();
        return t.Length == 0 ? UnknownTitle : t;
    }

    private static bool RuleMatches(AircraftDetectionRule rule, string title, string model)
    {
        var pattern = rule.Pattern.Trim();
        var field = (rule.Match ?? "any").Trim().ToLowerInvariant();

        return field switch
        {
            "title" => ContainsIgnoreCase(title, pattern),
            "atc_model" or "model" or "atc" => ContainsIgnoreCase(model, pattern),
            _ => ContainsIgnoreCase(title, pattern) || ContainsIgnoreCase(model, pattern)
        };
    }

    private static bool ContainsIgnoreCase(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
            return false;
        if (string.IsNullOrEmpty(haystack))
            return false;
        return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
