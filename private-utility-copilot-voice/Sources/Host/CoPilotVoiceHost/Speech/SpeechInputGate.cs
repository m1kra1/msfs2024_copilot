using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Speech;

/// <summary>
/// When continuous_listen is false, accepts recognized text only if the wake word
/// is present or PTT is held. When continuous_listen is true, all phrases pass.
/// </summary>
public sealed class SpeechInputGate
{
    private readonly SpeechSettings _settings;
    private int _pttHeld; // 0/1 for thread-safe flag

    public SpeechInputGate(SpeechSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool ContinuousListen => _settings.ContinuousListen;
    public string WakeWord => _settings.WakeWord ?? string.Empty;
    public string PttKey => _settings.PttKey ?? string.Empty;
    public bool IsPttHeld => Interlocked.CompareExchange(ref _pttHeld, 0, 0) == 1;

    public void SetPtt(bool held) =>
        Interlocked.Exchange(ref _pttHeld, held ? 1 : 0);

    /// <summary>
    /// Returns true if the phrase may be processed. When accepted with a leading
    /// wake word, <paramref name="commandText"/> is the original text (matcher strips wake).
    /// </summary>
    public bool TryAccept(string recognizedText, out string commandText, out string? rejectReason)
    {
        commandText = recognizedText ?? string.Empty;
        rejectReason = null;

        if (string.IsNullOrWhiteSpace(commandText))
        {
            rejectReason = "empty";
            return false;
        }

        if (_settings.ContinuousListen)
            return true;

        if (IsPttHeld)
            return true;

        if (HasWakeWord(commandText, _settings.WakeWord))
            return true;

        rejectReason =
            $"ignored (continuous_listen=false; require wake word '{_settings.WakeWord}' or PTT {_settings.PttKey})";
        return false;
    }

    public static bool HasWakeWord(string recognizedText, string? wakeWord)
    {
        if (string.IsNullOrWhiteSpace(wakeWord))
            return false;

        var text = PhraseMatcher.Normalize(recognizedText);
        var wake = PhraseMatcher.Normalize(wakeWord);
        if (wake.Length == 0 || text.Length == 0)
            return false;

        return text.Equals(wake, StringComparison.Ordinal)
               || text.StartsWith(wake + " ", StringComparison.Ordinal);
    }
}
