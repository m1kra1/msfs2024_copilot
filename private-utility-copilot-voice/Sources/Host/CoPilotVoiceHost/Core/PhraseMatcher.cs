using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

public sealed class PhraseMatcher
{
    private readonly List<(string NormalizedPhrase, CommandDefinition Command)> _index = new();

    public PhraseMatcher(IEnumerable<CommandDefinition> commands)
    {
        foreach (var cmd in commands)
        {
            foreach (var phrase in cmd.Phrases)
            {
                var n = Normalize(phrase);
                if (n.Length == 0)
                    continue;
                _index.Add((n, cmd));
            }
        }

        // Longer phrases first so "positive climb gear up" beats "gear up"
        _index.Sort((a, b) => b.NormalizedPhrase.Length.CompareTo(a.NormalizedPhrase.Length));
    }

    public IReadOnlyList<string> AllPhrases =>
        _index.Select(x => x.NormalizedPhrase).Distinct().ToList();

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var chars = text.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();
        var collapsed = new string(chars);
        while (collapsed.Contains("  ", StringComparison.Ordinal))
            collapsed = collapsed.Replace("  ", " ", StringComparison.Ordinal);
        return collapsed.Trim();
    }

    /// <summary>
    /// Strips a leading wake word if present, then matches the remaining command text.
    /// </summary>
    public (CommandDefinition? Command, string MatchedPhrase, string Residual) Match(
        string recognizedText,
        string? wakeWord = null)
    {
        var residual = Normalize(recognizedText);
        if (residual.Length == 0)
            return (null, string.Empty, residual);

        if (!string.IsNullOrWhiteSpace(wakeWord))
        {
            var wake = Normalize(wakeWord);
            if (residual.StartsWith(wake + " ", StringComparison.Ordinal)
                || residual.Equals(wake, StringComparison.Ordinal))
            {
                residual = residual.Length == wake.Length
                    ? string.Empty
                    : residual[(wake.Length + 1)..].Trim();
            }
        }

        if (residual.Length == 0)
            return (null, string.Empty, residual);

        foreach (var (phrase, cmd) in _index)
        {
            if (residual.Equals(phrase, StringComparison.Ordinal)
                || residual.EndsWith(" " + phrase, StringComparison.Ordinal)
                || residual.Contains(phrase, StringComparison.Ordinal))
            {
                // Prefer exact or end-match; first hit is longest phrase due to sort
                if (residual.Equals(phrase, StringComparison.Ordinal)
                    || residual.EndsWith(phrase, StringComparison.Ordinal))
                {
                    return (cmd, phrase, residual);
                }
            }
        }

        // Second pass: contains longest phrase
        foreach (var (phrase, cmd) in _index)
        {
            if (residual.Contains(phrase, StringComparison.Ordinal))
                return (cmd, phrase, residual);
        }

        return (null, string.Empty, residual);
    }
}
