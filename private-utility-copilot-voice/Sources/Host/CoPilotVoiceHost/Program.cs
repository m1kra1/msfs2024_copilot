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

        // SimConnect.cfg is resolved from the process directory — always run from the EXE folder.
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        }
        catch
        {
            // ignore
        }

        Console.WriteLine($"[Config] Root: {configRoot}");
        var (settings, catalog) = ConfigLoader.LoadAll(configRoot, options.Profile);
        Console.WriteLine($"[Config] Commands loaded: {catalog.Commands.Count}");
        Console.WriteLine($"[Config] Aircraft profile: {settings.AircraftProfile}");
        Console.WriteLine($"[Config] Wake word: '{settings.Speech.WakeWord}' | PTT: {settings.Speech.PttKey}");
        Console.WriteLine($"[Config] continuous_listen={settings.Speech.ContinuousListen} ptt_grace_ms={settings.Speech.PttGraceMs}");
        Console.WriteLine($"[Config] require_positive_climb_for_gear_up={settings.Behavior.RequirePositiveClimbForGearUp}");

        // Prefer live SimConnect (managed → native). Offline recording only if --offline or connect fails with fallback.
        var sim = SimConnectClientFactory.CreateAndConnect(
            settings.SimConnect.AppName,
            settings.SimConnect.ConfigIndex,
            preferOffline: options.ForceOffline,
            allowOfflineFallback: options.ForceOffline || options.AllowOfflineFallback,
            out var live);

        Console.WriteLine($"[SimConnect] {sim.StatusMessage}");
        Console.WriteLine($"[SimConnect] Connected={sim.IsConnected} IsLive={sim.IsLive}");
        if (!sim.IsLive)
        {
            Console.WriteLine("[SimConnect] WARNING: Not live. Recognized commands will speak but will NOT move switches in MSFS.");
            if (NativeSimConnectClient.TryLoadNativeDll(out var dllPath))
                Console.WriteLine($"[SimConnect] Found SimConnect.dll at: {dllPath} (start Free Flight then restart host)");
            else
                Console.WriteLine("[SimConnect] Tip: copy SimConnect.dll next to CoPilotVoiceHost.exe (MSFS SDK or Addon Manager/couatl).");
        }

        if (!live && sim is RecordingSimConnectClient recording)
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
        using var pttArm = new PttArmService(settings.Speech.PttKey, settings.Speech.PttGraceMs);
        pttArm.StartPolling(50);
        if (options.SimulatePtt)
            pttArm.ForceArm(60_000);

        using ITtsService tts = options.NoTts
            ? new ConsoleTtsService()
            : CreateTts(settings.Tts);

        tts.ApplySettings(settings.Tts);

        try
        {
            if (!string.IsNullOrWhiteSpace(options.InjectPhrase))
            {
                return RunInjected(options.InjectPhrase!, processor, sim, tts, settings, gate, pttArm, options, executor);
            }

            if (options.Once)
            {
                Console.WriteLine("[Host] --once: config and SimConnect path exercised; exiting.");
                return 0;
            }

            return RunInteractive(settings, matcher, processor, sim, tts, gate, pttArm, options, executor);
        }
        finally
        {
            sim.Dispose();
        }
    }

    private static int RunInjected(
        string phrase,
        CommandProcessor processor,
        ISimConnectClient sim,
        ITtsService tts,
        AppSettings settings,
        SpeechInputGate gate,
        PttArmService pttArm,
        HostOptions options,
        ActionExecutor executor)
    {
        Console.WriteLine($"[Inject] \"{phrase}\"");

        if (options.SimulatePtt)
            pttArm.ForceArm();

        pttArm.Poll();
        gate.SetPtt(pttArm.IsArmed);

        if (!options.BypassSpeechGate
            && !gate.TryAccept(phrase, out _, out var rejectReason))
        {
            Console.WriteLine($"[Inject] Gate rejected: {rejectReason}");
            return 5;
        }

        if (!options.ForceOffline)
            EnsureLiveConnection(sim, settings);

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

        LogResult(result, sim, executor);
        return result.Allowed ? 0 : 4;
    }

    private static int RunInteractive(
        AppSettings settings,
        PhraseMatcher matcher,
        CommandProcessor processor,
        ISimConnectClient sim,
        ITtsService tts,
        SpeechInputGate gate,
        PttArmService pttArm,
        HostOptions options,
        ActionExecutor executor)
    {
        ISpeechRecognitionService speech;
        try
        {
            if (options.NoSpeech)
            {
                speech = new InjectSpeechRecognitionService();
                Console.WriteLine("[Speech] --no-speech: type phrases on stdin (or use --inject).");
                if (!settings.Speech.ContinuousListen)
                    Console.WriteLine($"[Speech] Gate: wake '{settings.Speech.WakeWord}' OR hold PTT {settings.Speech.PttKey} (grace {settings.Speech.PttGraceMs} ms after release).");
            }
            else
            {
                var win = new WindowsSpeechRecognitionService(settings.Speech);
                // Use arm service (key-down + grace), not raw key-at-recognition-time only
                win.IsPttActive = () =>
                {
                    pttArm.Poll();
                    return pttArm.IsArmed;
                };
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
                HandlePhrase(text, processor, sim, tts, settings, gate, pttArm, executor,
                    alreadyGated: speech is WindowsSpeechRecognitionService);
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
            Console.WriteLine($"[Host] PTT={settings.Speech.PttKey} grace={settings.Speech.PttGraceMs}ms | Live={sim.IsLive}");

            using var timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    pttArm.Poll();
                    // Periodic reconnect attempt if not live
                    if (!sim.IsConnected && sim is NativeSimConnectClient native)
                    {
                        // light retry every few seconds handled by counter below
                    }
                    sim.ReceiveMessage();
                }
                catch { /* ignore */ }
            }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

            // Reconnect timer (every 5s if not connected)
            var lastReconnect = Environment.TickCount64;
            using var reconnectTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (sim.IsConnected || options.ForceOffline) return;
                    if (Environment.TickCount64 - lastReconnect < 5000) return;
                    lastReconnect = Environment.TickCount64;
                    Console.WriteLine("[SimConnect] Retry connect...");
                    if (sim.Connect(settings.SimConnect.AppName, settings.SimConnect.ConfigIndex))
                        Console.WriteLine($"[SimConnect] {sim.StatusMessage}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SimConnect] Retry failed: {ex.Message}");
                }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

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

                pttArm.Poll();
                if (options.SimulatePtt)
                    pttArm.ForceArm();

                if (speech is InjectSpeechRecognitionService inject)
                    inject.Inject(trimmed);
                else
                    HandlePhrase(trimmed, processor, sim, tts, settings, gate, pttArm, executor, alreadyGated: false);
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
        PttArmService pttArm,
        ActionExecutor executor,
        bool alreadyGated)
    {
        pttArm.Poll();
        gate.SetPtt(pttArm.IsArmed);

        if (!alreadyGated)
        {
            if (!gate.TryAccept(text, out _, out var reason))
            {
                Console.WriteLine($"[Speech] {reason} (PTT armed={pttArm.IsArmed}, keyDown={pttArm.IsKeyCurrentlyDown})");
                return;
            }
        }

        EnsureLiveConnection(sim, settings);

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

        LogResult(result, sim, executor);
    }

    private static void EnsureLiveConnection(ISimConnectClient sim, AppSettings settings)
    {
        if (sim.IsConnected)
            return;
        Console.WriteLine("[SimConnect] Not connected — attempting open before action...");
        try
        {
            if (sim.Connect(settings.SimConnect.AppName, settings.SimConnect.ConfigIndex))
                Console.WriteLine($"[SimConnect] {sim.StatusMessage}");
            else
                Console.WriteLine($"[SimConnect] Still not connected: {sim.StatusMessage}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimConnect] Connect attempt failed: {ex.Message}");
        }
    }

    private static void LogResult(CommandResult result, ISimConnectClient sim, ActionExecutor executor)
    {
        Console.WriteLine(
            $"[Command] id={result.CommandId} phrase='{result.MatchedPhrase}' allowed={result.Allowed} live={sim.IsLive}");
        if (!result.Allowed)
            Console.WriteLine($"[Command] deny={result.DenyReason}");
        foreach (var a in result.ActionsExecuted)
            Console.WriteLine($"[Action] {a.Type}:{a.Name}");
        if (executor.LastExecuteHadErrors)
        {
            foreach (var e in executor.Errors)
                Console.WriteLine($"[ActionError] {e}");
        }
        Console.WriteLine($"[Response] {result.SpokenResponse}");
        if (result.Allowed && result.ActionsExecuted.Count > 0 && !sim.IsLive)
            Console.WriteLine("[SimConnect] NOTE: command accepted but SimConnect is NOT live — aircraft will not change.");
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
    public bool SimulatePtt { get; init; }
    public bool BypassSpeechGate { get; init; }
    /// <summary>When true, fall back to offline recording if live connect fails (default false for interactive).</summary>
    public bool AllowOfflineFallback { get; init; }

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
        var allowOffline = false;

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
                case "--allow-offline-fallback":
                    allowOffline = true;
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
            BypassSpeechGate = bypassGate,
            // Offline inject/tests: --offline implies recording; unit tests use ForceOffline
            AllowOfflineFallback = allowOffline || offline
        };
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            CoPilotVoiceHost — Private Voice Co-Pilot
            Usage:
              CoPilotVoiceHost [--config <dir>] [--profile generic|a320|b737]
                               [--inject "Co Pilot gear up"] [--offline] [--no-tts] [--no-speech]
                               [--once] [--vs 500] [--ptt] [--bypass-gate] [--allow-offline-fallback]
            PTT: hold key (default F12); bare phrases accepted while held and for ptt_grace_ms after release.
            Live sim control requires SimConnect.dll next to the EXE (or discoverable path).
            """);
    }
}
