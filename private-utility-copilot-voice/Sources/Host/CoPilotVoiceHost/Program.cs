using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using CoPilotVoiceHost.Speech;

namespace CoPilotVoiceHost;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("Private Voice Co-Pilot Host (CoPilotVoiceHost)");
        Console.WriteLine("==============================================");
        Console.WriteLine($"Package: private-utility-copilot-voice | Creator: Private");
        Console.WriteLine();

        var options = HostOptions.Parse(args);
        try
        {
            return Run(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FATAL] {ex}");
            return 2;
        }
    }

    public static int Run(HostOptions options)
    {
        string configRoot;
        try
        {
            configRoot = ConfigLoader.ResolveConfigRoot(options.ConfigRoot);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Config] {ex.Message}");
            return 1;
        }

        Console.WriteLine($"[Config] Root: {configRoot}");
        var (settings, catalog) = ConfigLoader.LoadAll(configRoot, options.Profile);
        Console.WriteLine($"[Config] Commands loaded: {catalog.Commands.Count}");
        Console.WriteLine($"[Config] Aircraft profile: {settings.AircraftProfile}");
        Console.WriteLine($"[Config] Wake word: '{settings.Speech.WakeWord}' | PTT: {settings.Speech.PttKey}");
        Console.WriteLine($"[Config] continuous_listen={settings.Speech.ContinuousListen}");
        Console.WriteLine($"[Config] require_positive_climb_for_gear_up={settings.Behavior.RequirePositiveClimbForGearUp}");

        using var sim = options.ForceOffline
            ? new RecordingSimConnectClient()
            : SimConnectClientFactory.Create(preferOffline: options.ForceOffline);

        var appName = settings.SimConnect.AppName;
        var connected = sim.Connect(appName, settings.SimConnect.ConfigIndex);
        Console.WriteLine($"[SimConnect] {sim.StatusMessage}");
        Console.WriteLine($"[SimConnect] Connected={connected} IsConnected={sim.IsConnected}");

        if (sim is RecordingSimConnectClient recording)
        {
            recording.Snapshot.Set("GEAR POSITION", 1);
            recording.Snapshot.Set("VERTICAL SPEED", options.FixtureVerticalSpeed ?? 500);
            recording.Snapshot.Set("FLAPS HANDLE INDEX", 0);
            recording.Snapshot.Set("AUTOPILOT MASTER", 0);
            recording.Snapshot.Set("LIGHT LANDING", 0);
            recording.Snapshot.Set("BRAKE PARKING POSITION", 0);
            recording.Snapshot.Set("ENG ANTI ICE", 0);
            Console.WriteLine($"[SimConnect] Offline snapshot seeded (VS={options.FixtureVerticalSpeed ?? 500} fpm, gear down=1).");
        }

        var matcher = new PhraseMatcher(catalog.Commands);
        var conditions = new ConditionEngine();
        var executor = new ActionExecutor(sim);
        var processor = new CommandProcessor(matcher, conditions, executor, settings.Behavior);
        var gate = new SpeechInputGate(settings.Speech);

        using ITtsService tts = options.NoTts
            ? new ConsoleTtsService()
            : CreateTts(settings.Tts);

        tts.ApplySettings(settings.Tts);

        if (!string.IsNullOrWhiteSpace(options.InjectPhrase))
        {
            return RunInjected(options.InjectPhrase!, processor, sim, tts, settings, gate, options);
        }

        if (options.Once)
        {
            Console.WriteLine("[Host] --once: config and SimConnect path exercised; exiting.");
            return 0;
        }

        return RunInteractive(settings, matcher, processor, sim, tts, gate, options);
    }

    private static int RunInjected(
        string phrase,
        CommandProcessor processor,
        ISimConnectClient sim,
        ITtsService tts,
        AppSettings settings,
        SpeechInputGate gate,
        HostOptions options)
    {
        Console.WriteLine($"[Inject] \"{phrase}\"");

        if (options.SimulatePtt)
            gate.SetPtt(true);

        if (!options.BypassSpeechGate
            && !gate.TryAccept(phrase, out _, out var rejectReason))
        {
            Console.WriteLine($"[Inject] Gate rejected: {rejectReason}");
            return 5;
        }

        // Speak-before-action is inside Process (Condition → TTS → Event)
        var result = processor.Process(
            phrase,
            sim.Snapshot,
            settings.Speech.WakeWord,
            speakWithDelay: tts.Speak);

        if (result is null)
        {
            Console.WriteLine("[Inject] No matching command.");
            return 3;
        }

        LogResult(result);
        if (sim is RecordingSimConnectClient rec)
        {
            foreach (var e in rec.TransmittedEvents)
                Console.WriteLine($"[Event] {e.Name} data={e.Data}");
        }

        return result.Allowed ? 0 : 4;
    }

    private static int RunInteractive(
        AppSettings settings,
        PhraseMatcher matcher,
        CommandProcessor processor,
        ISimConnectClient sim,
        ITtsService tts,
        SpeechInputGate gate,
        HostOptions options)
    {
        ISpeechRecognitionService speech;
        try
        {
            if (options.NoSpeech)
            {
                speech = new InjectSpeechRecognitionService();
                Console.WriteLine("[Speech] --no-speech: type phrases on stdin (or use --inject).");
                if (!settings.Speech.ContinuousListen)
                    Console.WriteLine($"[Speech] Gate active: wake '{settings.Speech.WakeWord}' or PTT {settings.Speech.PttKey} (prefix wake word in typed lines).");
            }
            else
            {
                var win = new WindowsSpeechRecognitionService(settings.Speech);
                win.IsPttActive = () => PttKeyboard.IsKeyDown(settings.Speech.PttKey);
                win.LoadGrammar(matcher.AllPhrases, settings.Speech.WakeWord);
                speech = win;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Speech] Engine unavailable ({ex.Message}). Falling back to stdin.");
            speech = new InjectSpeechRecognitionService();
        }

        using (speech)
        {
            speech.PhraseRecognized += (text, conf) =>
            {
                Console.WriteLine($"[Speech] Recognized ({conf:F2}): {text}");
                // WindowsSpeechRecognitionService already gated; inject path still gates here
                HandlePhrase(text, processor, sim, tts, settings, gate, alreadyGated: speech is WindowsSpeechRecognitionService);
            };

            try
            {
                speech.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Speech] Start failed: {ex.Message}");
            }

            Console.WriteLine("[Host] Running. Type a phrase and Enter, or quit/exit. Ctrl+C to stop.");
            using var timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    // Refresh PTT for inject/stdin path
                    if (speech is not WindowsSpeechRecognitionService)
                        gate.SetPtt(PttKeyboard.IsKeyDown(settings.Speech.PttKey));
                    sim.ReceiveMessage();
                }
                catch { /* ignore */ }
            }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(200));

            while (true)
            {
                var line = Console.ReadLine();
                if (line is null)
                    break;
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;
                if (trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase)
                    || trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                gate.SetPtt(PttKeyboard.IsKeyDown(settings.Speech.PttKey) || options.SimulatePtt);

                if (speech is InjectSpeechRecognitionService inject)
                    inject.Inject(trimmed);
                else
                    HandlePhrase(trimmed, processor, sim, tts, settings, gate, alreadyGated: false);
            }
        }

        sim.Disconnect();
        Console.WriteLine("[Host] Shutdown complete.");
        return 0;
    }

    private static void HandlePhrase(
        string text,
        CommandProcessor processor,
        ISimConnectClient sim,
        ITtsService tts,
        AppSettings settings,
        SpeechInputGate gate,
        bool alreadyGated)
    {
        if (!alreadyGated)
        {
            // Live PTT poll for typed input
            if (!gate.IsPttHeld)
                gate.SetPtt(PttKeyboard.IsKeyDown(settings.Speech.PttKey));

            if (!gate.TryAccept(text, out _, out var reason))
            {
                Console.WriteLine($"[Speech] {reason}");
                return;
            }
        }

        // Condition → TTS (delay) → Event — all inside Process
        var result = processor.Process(
            text,
            sim.Snapshot,
            settings.Speech.WakeWord,
            speakWithDelay: tts.Speak);

        if (result is null)
        {
            Console.WriteLine("[Host] Unrecognized command.");
            return;
        }

        LogResult(result);
        if (sim is RecordingSimConnectClient rec)
        {
            foreach (var e in rec.TransmittedEvents.TakeLast(result.ActionsExecuted.Count))
                Console.WriteLine($"[Event] {e.Name}");
        }
    }

    private static void LogResult(CommandResult result)
    {
        Console.WriteLine(
            $"[Command] id={result.CommandId} phrase='{result.MatchedPhrase}' allowed={result.Allowed}");
        if (!result.Allowed)
            Console.WriteLine($"[Command] deny={result.DenyReason}");
        foreach (var a in result.ActionsExecuted)
            Console.WriteLine($"[Action] {a.Type}:{a.Name}");
        Console.WriteLine($"[Response] {result.SpokenResponse}");
    }

    private static ITtsService CreateTts(TtsSettings settings)
    {
        try
        {
            var tts = new WindowsTtsService();
            tts.ApplySettings(settings);
            return tts;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS] Windows TTS unavailable ({ex.Message}); using console.");
            return new ConsoleTtsService();
        }
    }
}

public sealed class HostOptions
{
    public string? ConfigRoot { get; init; }
    public string? Profile { get; init; }
    public string? InjectPhrase { get; init; }
    public bool ForceOffline { get; init; }
    public bool NoTts { get; init; }
    public bool NoSpeech { get; init; }
    public bool Once { get; init; }
    public double? FixtureVerticalSpeed { get; init; }
    /// <summary>Treat inject as PTT-held for gate testing / bare phrases.</summary>
    public bool SimulatePtt { get; init; }
    /// <summary>Skip speech gate (diagnostic only).</summary>
    public bool BypassSpeechGate { get; init; }

    public static HostOptions Parse(string[] args)
    {
        string? config = null;
        string? profile = null;
        string? inject = null;
        var offline = false;
        var noTts = false;
        var noSpeech = false;
        var once = false;
        double? vs = null;
        var simPtt = false;
        var bypassGate = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--config":
                    config = args[++i];
                    break;
                case "--profile":
                    profile = args[++i];
                    break;
                case "--inject":
                    inject = args[++i];
                    break;
                case "--offline":
                    offline = true;
                    break;
                case "--no-tts":
                    noTts = true;
                    break;
                case "--no-speech":
                    noSpeech = true;
                    break;
                case "--once":
                    once = true;
                    break;
                case "--vs":
                    vs = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--ptt":
                    simPtt = true;
                    break;
                case "--bypass-gate":
                    bypassGate = true;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        return new HostOptions
        {
            ConfigRoot = config,
            Profile = profile,
            InjectPhrase = inject,
            ForceOffline = offline,
            NoTts = noTts,
            NoSpeech = noSpeech,
            Once = once,
            FixtureVerticalSpeed = vs,
            SimulatePtt = simPtt,
            BypassSpeechGate = bypassGate
        };
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            CoPilotVoiceHost — Private Voice Co-Pilot
            Usage:
              CoPilotVoiceHost [--config <dir>] [--profile generic|a320|b737]
                               [--inject "Co Pilot gear up"] [--offline] [--no-tts] [--no-speech]
                               [--once] [--vs 500] [--ptt] [--bypass-gate]
            When continuous_listen=false, use wake word "Co Pilot …" or hold PTT / --ptt.
            """);
    }
}
