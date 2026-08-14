using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Core;

public sealed class PhraseMatcher
{
    private readonly List<(string NormalizedPhrase, string[] Tokens, CommandDefinition Command)> _index = new();
    private readonly IReadOnlyList<string> _allPhrases;

    public PhraseMatcher(IEnumerable<CommandDefinition> commands)
    {
        foreach (var cmd in commands)
        {
            foreach (var phrase in cmd.Phrases)
            {
                var n = Normalize(phrase);
                if (n.Length == 0)
                    continue;
                var tokens = Tokenize(n);
                if (tokens.Length == 0)
                    continue;
                _index.Add((n, tokens, cmd));
            }
        }

        // Longer phrases first so "positive climb gear up" beats "gear up" on equal score ties
        _index.Sort((a, b) => b.NormalizedPhrase.Length.CompareTo(a.NormalizedPhrase.Length));
        _allPhrases = _index.Select(x => x.NormalizedPhrase).Distinct().ToList();
    }

    public IReadOnlyList<string> AllPhrases => _allPhrases;

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var raw in text)
        {
            var c = char.ToLowerInvariant(raw);
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0)
                    sb.Append(' ');
                sb.Append(c);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = sb.Length > 0;
            }
        }

        return sb.ToString();
    }

    public static string[] Tokenize(string normalized) =>
        string.IsNullOrWhiteSpace(normalized)
            ? Array.Empty<string>()
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Strips a leading wake word if present, then matches the remaining command text.
    /// Matching uses whole-word sequences (not loose substring Contains) to avoid
    /// confusions like partial overlaps between similar aviation phrases.
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

        var best = FindBestMatch(residual);
        if (best is null)
            return (null, string.Empty, residual);

        return (best.Value.Command, best.Value.Phrase, residual);
    }

    /// <summary>
    /// Among several recognition hypotheses (primary + alternates), pick the best phrase match.
    /// Prefers higher match quality, then higher STT confidence.
    /// </summary>
    public (CommandDefinition? Command, string MatchedPhrase, string ChosenText, float ChosenConfidence) MatchBestHypothesis(
        IEnumerable<(string Text, float Confidence)> hypotheses,
        string? wakeWord = null)
    {
        CommandDefinition? bestCmd = null;
        string bestPhrase = string.Empty;
        string bestText = string.Empty;
        float bestConf = 0;
        double bestScore = 0;

        foreach (var (text, conf) in hypotheses)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var residual = StripWake(Normalize(text), wakeWord);
            if (residual.Length == 0)
                continue;

            var hit = FindBestMatch(residual);
            if (hit is null)
                continue;

            // Combined score: match quality dominates; confidence breaks ties.
            var combined = hit.Value.Score * 10.0 + conf;
            if (combined > bestScore
                || (Math.Abs(combined - bestScore) < 1e-9
                    && hit.Value.Phrase.Length > bestPhrase.Length))
            {
                bestScore = combined;
                bestCmd = hit.Value.Command;
                bestPhrase = hit.Value.Phrase;
                bestText = text;
                bestConf = conf;
            }
        }

        return (bestCmd, bestPhrase, bestText, bestConf);
    }

    private static string StripWake(string residual, string? wakeWord)
    {
        if (string.IsNullOrWhiteSpace(wakeWord))
            return residual;
        var wake = Normalize(wakeWord);
        if (residual.StartsWith(wake + " ", StringComparison.Ordinal)
            || residual.Equals(wake, StringComparison.Ordinal))
        {
            return residual.Length == wake.Length
                ? string.Empty
                : residual[(wake.Length + 1)..].Trim();
        }

        return residual;
    }

    private (CommandDefinition Command, string Phrase, double Score)? FindBestMatch(string residual)
    {
        var residualTokens = Tokenize(residual);
        if (residualTokens.Length == 0)
            return null;

        CommandDefinition? bestCmd = null;
        string bestPhrase = string.Empty;
        double bestScore = 0;

        foreach (var (phrase, tokens, cmd) in _index)
        {
            var score = ScoreMatch(residual, residualTokens, phrase, tokens);
            if (score <= 0)
                continue;

            if (score > bestScore
                || (Math.Abs(score - bestScore) < 1e-9 && phrase.Length > bestPhrase.Length))
            {
                bestScore = score;
                bestCmd = cmd;
                bestPhrase = phrase;
            }
        }

        // Require a solid whole-word hit (exact / end / contiguous token sequence).
        if (bestCmd is null || bestScore < 0.75)
            return null;

        return (bestCmd, bestPhrase, bestScore);
    }

    /// <summary>
    /// Score 1.0 exact, 0.95 residual ends with phrase words, 0.85 contiguous token window,
    /// 0 otherwise (no loose substring matching).
    /// </summary>
    public static double ScoreMatch(
        string residual,
        string[] residualTokens,
        string phrase,
        string[] phraseTokens)
    {
        if (phraseTokens.Length == 0 || residualTokens.Length == 0)
            return 0;

        if (residual.Equals(phrase, StringComparison.Ordinal))
            return 1.0;

        // Exact whole-phrase at end: "... positive rate gear up"
        if (residual.EndsWith(" " + phrase, StringComparison.Ordinal))
            return 0.95;

        // Contiguous token sequence (word-boundary safe)
        if (ContainsTokenSequence(residualTokens, phraseTokens))
        {
            // Prefer phrases that cover more of the residual (less leftover noise)
            var coverage = (double)phraseTokens.Length / residualTokens.Length;
            return 0.85 + 0.1 * Math.Min(1.0, coverage);
        }

        return 0;
    }

    public static bool ContainsTokenSequence(string[] haystack, string[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
            return false;

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (!haystack[i + j].Equals(needle[j], StringComparison.Ordinal))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
                return true;
        }

        return false;
    }
}
