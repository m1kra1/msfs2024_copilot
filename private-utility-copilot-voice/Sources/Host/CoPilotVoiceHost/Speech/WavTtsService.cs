using System.Media;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Speech;

/// <summary>Plays mapped WAVs via <see cref="IWavPlayer"/>; silent when no mapping/file.</summary>
public sealed class WavTtsService : ITtsService
{
    private readonly string _extrasRoot;
    private readonly IWavPlayer _player;
    private readonly bool _ownsPlayer;
    private VoicePackManifest _manifest;
    private string _voicePack;
    private bool _disposed;

    public WavTtsService(string extrasRoot, string voicePack, IWavPlayer? player = null)
    {
        _extrasRoot = extrasRoot;
        _voicePack = string.IsNullOrWhiteSpace(voicePack) ? "austrian_airlines_en_us" : voicePack.Trim();
        if (player is null)
        {
            _player = new SoundPlayerWavPlayer();
            _ownsPlayer = true;
        }
        else
        {
            _player = player;
            _ownsPlayer = false;
        }

        _manifest = VoicePackManifest.Load(_extrasRoot, _voicePack);
    }

    public VoicePackManifest Manifest => _manifest;

    public void ApplySettings(TtsSettings settings)
    {
        var pack = string.IsNullOrWhiteSpace(settings.VoicePack)
            ? "austrian_airlines_en_us"
            : settings.VoicePack.Trim();
        if (!string.Equals(pack, _voicePack, StringComparison.OrdinalIgnoreCase))
        {
            _voicePack = pack;
            _manifest = VoicePackManifest.Load(_extrasRoot, _voicePack);
        }
    }

    public void Speak(string text, int delayMs = 0, string? commandId = null, string? responseKind = null)
    {
        TrySpeak(text, delayMs, commandId, responseKind);
    }

    /// <summary>Returns true if a WAV was started (or would play).</summary>
    public bool TrySpeak(string text, int delayMs, string? commandId, string? responseKind)
    {
        if (_disposed)
            return false;

        var path = _manifest.TryResolve(commandId, responseKind);
        if (path is null)
        {
            if (!string.IsNullOrWhiteSpace(commandId) || !string.IsNullOrWhiteSpace(responseKind))
                Console.WriteLine($"[TTS][Wav] no mapping for {commandId ?? "(none)"}/{responseKind ?? "(none)"}");
            return false;
        }

        void PlayNow()
        {
            if (_disposed)
                return;
            try
            {
                Console.WriteLine($"[TTS][Wav] {Path.GetFileName(path)} ({commandId}/{responseKind})");
                _player.Play(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TTS][Wav] play failed: {ex.Message}");
            }
        }

        if (delayMs > 0)
            TtsSpeakQueue.EnqueueDelayed(delayMs, PlayNow);
        else
            PlayNow();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsPlayer)
            _player.Dispose();
    }
}

/// <summary>Default WAV player using <see cref="SoundPlayer"/> (no extra NuGet).</summary>
public sealed class SoundPlayerWavPlayer : IWavPlayer
{
    private readonly object _gate = new();
    private SoundPlayer? _player;
    private bool _disposed;

    public void Play(string absoluteWavPath)
    {
        if (_disposed) return;
        lock (_gate)
        {
            _player?.Stop();
            _player?.Dispose();
            _player = new SoundPlayer(absoluteWavPath);
            _player.Play(); // async; does not block recognition long-term
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            try { _player?.Stop(); } catch { /* ignore */ }
            _player?.Dispose();
            _player = null;
        }
    }
}

/// <summary>Test double that records play paths without audio I/O.</summary>
public sealed class RecordingWavPlayer : IWavPlayer
{
    public List<string> PlayedPaths { get; } = new();

    public void Play(string absoluteWavPath) => PlayedPaths.Add(absoluteWavPath);

    public void Dispose() { }
}
