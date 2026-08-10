using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>
/// Indexes catalog action signal names → commands for Learn Mode mapping badges.
/// Pure Core — no WPF / SimConnect I/O.
/// </summary>
public sealed class CommandMappingIndex
{
    private readonly Dictionary<string, List<CommandDefinition>> _bySignal =
        new(StringComparer.OrdinalIgnoreCase);

    public CommandMappingIndex(CommandCatalog catalog)
    {
        catalog ??= new CommandCatalog();
        foreach (var cmd in catalog.Commands)
        {
            if (cmd is null || string.IsNullOrWhiteSpace(cmd.Id))
                continue;

            foreach (var action in cmd.Actions)
            {
                if (action is null || string.IsNullOrWhiteSpace(action.Name))
                    continue;

                var type = (action.Type ?? "").Trim().ToLowerInvariant();
                if (type is not (HostConstants.ActionTypeEvent
                    or HostConstants.ActionTypeSetSimVar
                    or HostConstants.ActionTypeSimVar))
                    continue;

                foreach (var key in EnumerateIndexKeys(action.Name))
                {
                    if (!_bySignal.TryGetValue(key, out var list))
                    {
                        list = new List<CommandDefinition>();
                        _bySignal[key] = list;
                    }

                    if (!list.Any(c => c.Id.Equals(cmd.Id, StringComparison.OrdinalIgnoreCase)))
                        list.Add(cmd);
                }
            }
        }
    }

    /// <summary>Commands that write/send the given signal name (event or set_simvar).</summary>
    public IReadOnlyList<CommandDefinition> Lookup(string? signalName)
    {
        if (string.IsNullOrWhiteSpace(signalName))
            return Array.Empty<CommandDefinition>();

        foreach (var key in EnumerateIndexKeys(signalName))
        {
            if (_bySignal.TryGetValue(key, out var list) && list.Count > 0)
                return list;
        }

        return Array.Empty<CommandDefinition>();
    }

    public LearnMappingStatus GetStatus(string? signalName)
    {
        var n = Lookup(signalName).Count;
        if (n == 0) return LearnMappingStatus.Unmapped;
        if (n == 1) return LearnMappingStatus.Mapped;
        return LearnMappingStatus.Ambiguous;
    }

    public static string FormatLabel(CommandDefinition cmd)
    {
        var phrase = cmd.Phrases.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))?.Trim();
        return string.IsNullOrWhiteSpace(phrase) ? cmd.Id : $"{cmd.Id} ({phrase})";
    }

    /// <summary>
    /// Normalize action/signal names for lookup. LVars index under both <c>L:FOO</c> and <c>FOO</c>.
    /// </summary>
    public static IEnumerable<string> EnumerateIndexKeys(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            yield break;

        yield return trimmed;

        if (trimmed.StartsWith("L:", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 2)
        {
            var bare = trimmed[2..].Trim();
            if (bare.Length > 0 && !bare.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                yield return bare;
        }
        else if (!trimmed.StartsWith("A:", StringComparison.OrdinalIgnoreCase))
        {
            // Bare name may also match L: form used in profiles.
            yield return "L:" + trimmed;
        }
    }
}
