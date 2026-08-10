using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>
/// Groups merged catalog commands into pilot-friendly categories for Manual UI and docs.
/// Pure logic — no WPF.
/// </summary>
public static class CommandCatalogGroups
{
    public static readonly string[] CategoryOrder =
    {
        "Gear",
        "Lights",
        "Flaps",
        "Autopilot / FCU",
        "Anti-ice",
        "Brakes / Spoilers",
        "APU / Systems",
        "Overhead",
        "Checklists",
        "Info",
        "Other"
    };

    public static string Categorize(CommandDefinition command)
    {
        var id = (command?.Id ?? string.Empty).Trim().ToLowerInvariant();
        if (id.Length == 0)
            return "Other";

        if (id.StartsWith("gear_", StringComparison.Ordinal) || id.Contains("gear", StringComparison.Ordinal))
            return "Gear";
        if (id.Contains("light", StringComparison.Ordinal) || id.Contains("strobe", StringComparison.Ordinal)
            || id.Contains("beacon", StringComparison.Ordinal) || id.Contains("nav_", StringComparison.Ordinal)
            || id.Contains("logo", StringComparison.Ordinal) || id.Contains("wing_light", StringComparison.Ordinal)
            || id.Contains("cabin", StringComparison.Ordinal) || id.Contains("dome", StringComparison.Ordinal))
            return "Lights";
        if (id.StartsWith("flaps", StringComparison.Ordinal) || id.Contains("flap", StringComparison.Ordinal))
            return "Flaps";
        if (id.StartsWith("ap_", StringComparison.Ordinal) || id.StartsWith("autopilot", StringComparison.Ordinal)
            || id.Contains("flight_director", StringComparison.Ordinal) || id.Contains("heading", StringComparison.Ordinal)
            || id.Contains("altitude", StringComparison.Ordinal) || id.Contains("managed", StringComparison.Ordinal))
            return "Autopilot / FCU";
        if (id.Contains("anti_ice", StringComparison.Ordinal) || id.Contains("pitot", StringComparison.Ordinal)
            || id.Contains("probe_heat", StringComparison.Ordinal)
            || (id.Contains("ice", StringComparison.Ordinal) && !id.Contains("service", StringComparison.Ordinal)))
            return "Anti-ice";
        if (id.Contains("brake", StringComparison.Ordinal) || id.Contains("spoiler", StringComparison.Ordinal)
            || id.Contains("speedbrake", StringComparison.Ordinal))
            return "Brakes / Spoilers";
        if (id.Contains("apu", StringComparison.Ordinal))
            return "APU / Systems";
        if (id.Contains("fuel", StringComparison.Ordinal) || id.Contains("battery", StringComparison.Ordinal)
            || id.Contains("pack", StringComparison.Ordinal) || id.Contains("adirs", StringComparison.Ordinal)
            || id.Contains("seatbelt", StringComparison.Ordinal) || id.Contains("signs", StringComparison.Ordinal)
            || id.Contains("ext_power", StringComparison.Ordinal) || id.Contains("bleed", StringComparison.Ordinal)
            || id.Contains("overhead", StringComparison.Ordinal))
            return "Overhead";
        if (id.Contains("checklist", StringComparison.Ordinal) || command!.Checklist)
            return "Checklists";
        if (id.Contains("list_command", StringComparison.Ordinal) || id.Contains("what_can", StringComparison.Ordinal))
            return "Info";

        return "Other";
    }

    /// <summary>
    /// Groups commands by category, ordered by <see cref="CategoryOrder"/> then command id.
    /// </summary>
    public static IReadOnlyList<(string Category, IReadOnlyList<CommandDefinition> Commands)> Group(
        IEnumerable<CommandDefinition> commands)
    {
        var buckets = new Dictionary<string, List<CommandDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in CategoryOrder)
            buckets[cat] = new List<CommandDefinition>();

        foreach (var cmd in commands ?? Enumerable.Empty<CommandDefinition>())
        {
            if (cmd is null || string.IsNullOrWhiteSpace(cmd.Id))
                continue;
            var cat = Categorize(cmd);
            if (!buckets.TryGetValue(cat, out var list))
            {
                list = new List<CommandDefinition>();
                buckets[cat] = list;
            }

            list.Add(cmd);
        }

        foreach (var list in buckets.Values)
            list.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));

        var result = new List<(string, IReadOnlyList<CommandDefinition>)>();
        foreach (var cat in CategoryOrder)
        {
            if (buckets.TryGetValue(cat, out var list) && list.Count > 0)
                result.Add((cat, list));
        }

        // Any unexpected category keys
        foreach (var kv in buckets.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (CategoryOrder.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                continue;
            if (kv.Value.Count > 0)
                result.Add((kv.Key, kv.Value));
        }

        return result;
    }

    public static string DisplayLabel(CommandDefinition command)
    {
        if (command is null)
            return string.Empty;
        if (command.Phrases.Count > 0 && !string.IsNullOrWhiteSpace(command.Phrases[0]))
            return command.Phrases[0].Trim();
        return command.Id.Replace('_', ' ');
    }
}
