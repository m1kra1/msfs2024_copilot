using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

/// <summary>
/// Whole-word phrase matcher for checklist start phrases and in-run control commands.
/// Reuses <see cref="PhraseMatcher"/> normalize/tokenize/score rules.
/// </summary>
public sealed class ChecklistPhraseIndex
{
    private readonly List<(string NormalizedPhrase, string[] Tokens, ChecklistDefinition Checklist)> _index = new();

    public static readonly string[] ContinuePhrases =
    {
        "continue",
        "checklist continue",
        "go on",
        "next"
    };

    public static readonly string[] StopPhrases =
    {
        "stop checklist",
        "cancel checklist",
        "abort checklist"
    };

    public ChecklistPhraseIndex(IEnumerable<ChecklistDefinition> checklists)
    {
        foreach (var cl in checklists ?? Enumerable.Empty<ChecklistDefinition>())
        {
            if (cl is null || string.IsNullOrWhiteSpace(cl.Id)) continue;
            foreach (var phrase in cl.Phrases ?? Enumerable.Empty<string>())
            {
                var n = PhraseMatcher.Normalize(phrase);
                if (n.Length == 0) continue;
                var tokens = PhraseMatcher.Tokenize(n);
                if (tokens.Length == 0) continue;
                _index.Add((n, tokens, cl));
            }
        }

        _index.Sort((a, b) => b.NormalizedPhrase.Length.CompareTo(a.NormalizedPhrase.Length));
    }

    public IReadOnlyList<string> AllStartPhrases =>
        _index.Select(x => x.NormalizedPhrase).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static IEnumerable<string> ControlPhrases =>
        ContinuePhrases.Concat(StopPhrases);

    /// <summary>Match residual text (wake already stripped) to a checklist start phrase.</summary>
    public (ChecklistDefinition? Checklist, string MatchedPhrase) MatchStart(string residualText)
    {
        var residual = PhraseMatcher.Normalize(residualText);
        if (residual.Length == 0) return (null, "");

        // Require the word "checklist" in residual for start (done-condition / gate).
        if (!ChecklistValidator.PhraseContainsChecklistToken(residual))
            return (null, "");

        var residualTokens = PhraseMatcher.Tokenize(residual);
        ChecklistDefinition? best = null;
        string bestPhrase = "";
        double bestScore = 0;

        foreach (var (phrase, tokens, cl) in _index)
        {
            var score = PhraseMatcher.ScoreMatch(residual, residualTokens, phrase, tokens);
            if (score <= 0) continue;
            if (score > bestScore
                || (Math.Abs(score - bestScore) < 1e-9 && phrase.Length > bestPhrase.Length))
            {
                bestScore = score;
                best = cl;
                bestPhrase = phrase;
            }
        }

        if (best is null || bestScore < 0.75)
            return (null, "");

        return (best, bestPhrase);
    }

    public static bool IsContinuePhrase(string residualText)
    {
        var residual = PhraseMatcher.Normalize(residualText);
        return MatchesAny(residual, ContinuePhrases);
    }

    public static bool IsStopPhrase(string residualText)
    {
        var residual = PhraseMatcher.Normalize(residualText);
        return MatchesAny(residual, StopPhrases);
    }

    private static bool MatchesAny(string residual, IEnumerable<string> phrases)
    {
        if (residual.Length == 0) return false;
        var residualTokens = PhraseMatcher.Tokenize(residual);
        foreach (var p in phrases)
        {
            var n = PhraseMatcher.Normalize(p);
            var tokens = PhraseMatcher.Tokenize(n);
            if (PhraseMatcher.ScoreMatch(residual, residualTokens, n, tokens) >= 0.75)
                return true;
        }

        return false;
    }

    /// <summary>Strip leading wake word using the same rules as <see cref="PhraseMatcher"/>.</summary>
    public static string StripWake(string recognizedText, string? wakeWord)
    {
        var residual = PhraseMatcher.Normalize(recognizedText);
        if (string.IsNullOrWhiteSpace(wakeWord))
            return residual;
        var wake = PhraseMatcher.Normalize(wakeWord);
        if (residual.StartsWith(wake + " ", StringComparison.Ordinal)
            || residual.Equals(wake, StringComparison.Ordinal))
        {
            return residual.Length == wake.Length
                ? string.Empty
                : residual[(wake.Length + 1)..].Trim();
        }

        return residual;
    }
}
