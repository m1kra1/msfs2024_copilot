using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Speech;

/// <summary>Tries WAV callout first; falls back to Windows (or provided) TTS on miss/error.</summary>
public sealed class HybridTtsService : ITtsService
{
    private readonly WavTtsService _wav;
    private readonly ITtsService _fallback;
    private readonly bool _ownsFallback;
    private bool _disposed;

    public HybridTtsService(WavTtsService wav, ITtsService fallback, bool ownsFallback = true)
    {
        _wav = wav;
        _fallback = fallback;
        _ownsFallback = ownsFallback;
    }

    public WavTtsService Wav => _wav;

    public void ApplySettings(TtsSettings settings)
    {
        _wav.ApplySettings(settings);
        _fallback.ApplySettings(settings);
    }

    public void Speak(string text, int delayMs = 0, string? commandId = null, string? responseKind = null)
    {
        if (_disposed) return;

        // Wav path already applies delay when it plays; only apply delay once.
        if (_wav.TrySpeak(text, delayMs, commandId, responseKind))
            return;

        _fallback.Speak(text, delayMs, commandId, responseKind);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wav.Dispose();
        if (_ownsFallback)
            _fallback.Dispose();
    }
}
