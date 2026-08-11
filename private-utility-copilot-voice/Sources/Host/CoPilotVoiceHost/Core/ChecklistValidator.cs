using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>Pure schema/semantic checks for checklist documents (no I/O, no WPF).</summary>
public static class ChecklistValidator
{
    public static ChecklistValidationResult Validate(ChecklistDefinition? def, IEnumerable<string>? knownCommandIds = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (def is null)
        {
            errors.Add("Checklist is null.");
            return new ChecklistValidationResult(errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(def.Id))
            errors.Add("id is required.");
        if (string.IsNullOrWhiteSpace(def.Name))
            errors.Add("name is required.");

        if (def.Phrases is null || def.Phrases.Count == 0)
            errors.Add("phrases must contain at least one entry.");
        else
        {
            for (var i = 0; i < def.Phrases.Count; i++)
            {
                var p = def.Phrases[i];
                if (string.IsNullOrWhiteSpace(p))
                {
                    errors.Add($"phrases[{i}] is empty.");
                    continue;
                }

                if (!PhraseContainsChecklistToken(p))
                    errors.Add($"phrases[{i}] must contain the whole word '{HostConstants.ChecklistToken}'.");
            }
        }

        if (def.GlobalDelayMs < 0)
            errors.Add("global_delay_ms must be >= 0.");
        else if (def.GlobalDelayMs > HostConstants.ChecklistMaxDelayMs)
            warnings.Add($"global_delay_ms capped at {HostConstants.ChecklistMaxDelayMs} at runtime.");

        if (def.Items is null || def.Items.Count == 0)
            errors.Add("items must contain at least one step.");
        else
        {
            var known = knownCommandIds is null
                ? null
                : new HashSet<string>(knownCommandIds, StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < def.Items.Count; i++)
            {
                var item = def.Items[i];
                var prefix = $"items[{i}]";
                if (item is null)
                {
                    errors.Add($"{prefix} is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Challenge))
                    errors.Add($"{prefix}.challenge is required.");

                var mode = (item.Mode ?? "").Trim().ToLowerInvariant();
                if (mode is not (ChecklistItemMode.Execute or ChecklistItemMode.Verify))
                    errors.Add($"{prefix}.mode must be '{ChecklistItemMode.Execute}' or '{ChecklistItemMode.Verify}'.");

                if (item.DelayAfterMs < 0)
                    errors.Add($"{prefix}.delay_after_ms must be >= 0.");
                else if (item.DelayAfterMs > HostConstants.ChecklistMaxDelayMs)
                    warnings.Add($"{prefix}.delay_after_ms will be capped at {HostConstants.ChecklistMaxDelayMs}.");

                if (!string.IsNullOrWhiteSpace(item.CommandId)
                    && known is not null
                    && !known.Contains(item.CommandId.Trim()))
                {
                    warnings.Add($"{prefix}.command_id '{item.CommandId}' not found in catalog.");
                }

                if (mode == ChecklistItemMode.Execute)
                {
                    if (!HasExecuteSource(item))
                        errors.Add($"{prefix} execute mode needs actions, action, or command_id.");
                }
                else if (mode == ChecklistItemMode.Verify)
                {
                    if (!HasVerifySource(item))
                        errors.Add($"{prefix} verify mode needs expected, expected_list, or command_id.");
                }
            }
        }

        return new ChecklistValidationResult(errors, warnings);
    }

    public static ChecklistValidationResult ValidateAll(
        IEnumerable<ChecklistDefinition> list,
        IEnumerable<string>? knownCommandIds = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in list ?? Enumerable.Empty<ChecklistDefinition>())
        {
            if (def is null) continue;
            var one = Validate(def, knownCommandIds);
            errors.AddRange(one.Errors.Select(e => $"[{def.Id}] {e}"));
            warnings.AddRange(one.Warnings.Select(w => $"[{def.Id}] {w}"));
            if (!string.IsNullOrWhiteSpace(def.Id) && !seen.Add(def.Id.Trim()))
                errors.Add($"Duplicate checklist id '{def.Id}'.");
        }

        return new ChecklistValidationResult(errors, warnings);
    }

    public static bool PhraseContainsChecklistToken(string phrase)
    {
        var tokens = PhraseMatcher.Tokenize(PhraseMatcher.Normalize(phrase));
        return tokens.Any(t => t.Equals(HostConstants.ChecklistToken, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasExecuteSource(ChecklistItem item)
    {
        if (item.Actions is { Count: > 0 }) return true;
        if (item.Action is not null && !string.IsNullOrWhiteSpace(item.Action.Name)) return true;
        return !string.IsNullOrWhiteSpace(item.CommandId);
    }

    public static bool HasVerifySource(ChecklistItem item)
    {
        if (item.ExpectedList is { Count: > 0 }) return true;
        if (item.Expected is not null && !string.IsNullOrWhiteSpace(item.Expected.SimVar)) return true;
        return !string.IsNullOrWhiteSpace(item.CommandId);
    }
}

public readonly record struct ChecklistValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}
