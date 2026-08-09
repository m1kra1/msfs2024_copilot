using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>
/// Builds a short TTS summary and full command list from the currently loaded catalog.
/// Pure logic — no speech/SimConnect/UI dependencies.
/// </summary>
public static class CommandListBuilder
{
    public const string CommandId = "list_commands";

    public static bool IsListCommand(string? commandId) =>
        string.Equals(commandId, CommandId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Short spoken summary: command count plus a few example phrases.
    /// </summary>
    public static string BuildSpokenSummary(IEnumerable<CommandDefinition> commands)
    {
        var list = commands?.ToList() ?? new List<CommandDefinition>();
        var count = list.Count;

        // Prefer actionable commands (non-checklist, non-list) for examples
        var samples = list
            .Where(c => !IsListCommand(c.Id) && !c.Checklist && c.Phrases.Count > 0)
            .Take(5)
            .Select(c => c.Phrases[0])
            .ToList();

        if (count == 0)
            return "No voice commands are currently loaded.";

        var sampleText = samples.Count > 0
            ? " Examples include " + string.Join(", ", samples) + "."
            : string.Empty;

        return $"I have {count} available voice commands.{sampleText} Full list is in the log.";
    }

    /// <summary>
    /// Full list lines for console / debug log: each command id + at least one phrase.
    /// </summary>
    public static IReadOnlyList<string> BuildFullLogLines(IEnumerable<CommandDefinition> commands)
    {
        var ordered = (commands ?? Enumerable.Empty<CommandDefinition>())
            .OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string>(ordered.Count + 1)
        {
            $"[CommandList] Available commands ({ordered.Count}):"
        };

        foreach (var cmd in ordered)
        {
            var phrase = cmd.Phrases.Count > 0 ? cmd.Phrases[0] : "(no phrase)";
            lines.Add($"[CommandList] {cmd.Id} — \"{phrase}\"");
        }

        return lines;
    }
}
