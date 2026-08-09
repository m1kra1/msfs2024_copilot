using System.Globalization;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Speech;

public interface ITtsService : IDisposable
{
    void Speak(string text, int delayMs = 0);
    void ApplySettings(TtsSettings settings);
}

public interface ISpeechRecognitionService : IDisposable
{
    event Action<string, float>? PhraseRecognized;
    void LoadGrammar(IEnumerable<string> phrases, string? wakeWord);
    void Start();
    void Stop();
    bool IsRunning { get; }
}

/// <summary>Windows System.Speech synthesizer driven by settings.json tts section.</summary>
public sealed class WindowsTtsService : ITtsService
{
    private readonly SpeechSynthesizer _synth = new();
    private bool _disposed;

    public void ApplySettings(TtsSettings settings)
    {
        _synth.Rate = Math.Clamp(settings.Rate, -10, 10);
        _synth.Volume = Math.Clamp(settings.Volume, 0, 100);
        try
        {
            var voice = _synth.GetInstalledVoices()
                .Select(v => v.VoiceInfo)
                .FirstOrDefault(v =>
                    v.Name.Contains(settings.Voice, StringComparison.OrdinalIgnoreCase));
            if (voice is not null)
                _synth.SelectVoice(voice.Name);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS] Voice select failed ({settings.Voice}): {ex.Message}. Using default.");
        }
    }

    public void Speak(string text, int delayMs = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (delayMs > 0)
            Thread.Sleep(delayMs);
        Console.WriteLine($"[TTS] {text}");
        try
        {
            _synth.SpeakAsyncCancelAll();
            _synth.SpeakAsync(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS] Speak failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _synth.Dispose();
    }
}

/// <summary>Console-only TTS for headless / test environments. Logs [TTS] before returning.</summary>
public sealed class ConsoleTtsService : ITtsService
{
    public List<(string Text, int DelayMs, long Ticks)> SpeakLog { get; } = new();

    public void ApplySettings(TtsSettings settings) { }

    public void Speak(string text, int delayMs = 0)
    {
        if (delayMs > 0)
            Thread.Sleep(delayMs);
        SpeakLog.Add((text, delayMs, Environment.TickCount64));
        Console.WriteLine($"[TTS] {text}");
    }

    public void Dispose() { }
}

/// <summary>
/// Builds a System.Speech grammar from JSON phrases (+ wake word prefixes).
/// When continuous_listen=false, OnRecognized enforces wake word or live PTT before raising PhraseRecognized.
/// </summary>
public sealed class WindowsSpeechRecognitionService : ISpeechRecognitionService
{
    private SpeechRecognitionEngine? _engine;
    private readonly SpeechSettings _settings;
    private readonly SpeechInputGate _gate;
    private bool _disposed;

    public event Action<string, float>? PhraseRecognized;
    public bool IsRunning { get; private set; }

    /// <summary>Optional override for tests; default polls <see cref="PttKeyboard"/> for settings.PttKey.</summary>
    public Func<bool>? IsPttActive { get; set; }

    public WindowsSpeechRecognitionService(SpeechSettings settings)
    {
        _settings = settings;
        _gate = new SpeechInputGate(settings);
    }

    public SpeechInputGate Gate => _gate;

    public void LoadGrammar(IEnumerable<string> phrases, string? wakeWord)
    {
        Stop();
        var culture = SafeCulture(_settings.Culture);
        _engine = new SpeechRecognitionEngine(culture);

        var choices = new Choices();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in phrases)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var phrase = p.Trim();
            // Always include bare phrases so PTT mode can match without wake word
            set.Add(phrase);
            if (!string.IsNullOrWhiteSpace(wakeWord))
                set.Add($"{wakeWord.Trim()} {phrase}");
        }

        if (set.Count == 0)
            set.Add("gear up");

        foreach (var p in set)
            choices.Add(p);

        var gb = new GrammarBuilder { Culture = culture };
        gb.Append(choices);
        var grammar = new Grammar(gb) { Name = HostConstants.SpeechGrammarName };
        _engine.LoadGrammar(grammar);
        _engine.SpeechRecognized += OnRecognized;
        try
        {
            _engine.SetInputToDefaultAudioDevice();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Speech] Default audio device unavailable: {ex.Message}");
        }
    }

    public void Start()
    {
        if (_engine is null)
            throw new InvalidOperationException("LoadGrammar first");
        _engine.RecognizeAsync(RecognizeMode.Multiple);
        IsRunning = true;
        Console.WriteLine(
            $"[Speech] Listening (wake_word='{_settings.WakeWord}', ptt='{_settings.PttKey}', continuous={_settings.ContinuousListen})");
        if (!_settings.ContinuousListen)
            Console.WriteLine("[Speech] continuous_listen=false → require wake word or hold PTT.");
    }

    public void Stop()
    {
        if (_engine is null) return;
        try
        {
            _engine.RecognizeAsyncCancel();
        }
        catch
        {
            // ignore
        }

        IsRunning = false;
    }

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        var conf = e.Result.Confidence;
        if (conf < _settings.ConfidenceThreshold)
        {
            Console.WriteLine($"[Speech] Low confidence {conf:F2}: {e.Result.Text}");
            return;
        }

        // Refresh PTT state on every recognition when not continuous
        var ptt = IsPttActive?.Invoke()
                  ?? PttKeyboard.IsKeyDown(_settings.PttKey);
        _gate.SetPtt(ptt);

        if (!_gate.TryAccept(e.Result.Text, out var commandText, out var reason))
        {
            Console.WriteLine($"[Speech] {reason}: {e.Result.Text}");
            return;
        }

        PhraseRecognized?.Invoke(commandText, conf);
    }

    private static CultureInfo SafeCulture(string name)
    {
        try
        {
            return new CultureInfo(name);
        }
        catch
        {
            return new CultureInfo("en-US");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        if (_engine is not null)
        {
            _engine.SpeechRecognized -= OnRecognized;
            _engine.Dispose();
            _engine = null;
        }
    }
}

/// <summary>Text injection recognizer for tests and --inject CLI path (gate applied by host).</summary>
public sealed class InjectSpeechRecognitionService : ISpeechRecognitionService
{
    public event Action<string, float>? PhraseRecognized;
    public bool IsRunning { get; private set; }

    public void LoadGrammar(IEnumerable<string> phrases, string? wakeWord) { }

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;

    public void Inject(string text, float confidence = 1.0f) =>
        PhraseRecognized?.Invoke(text, confidence);

    public void Dispose() => Stop();
}
