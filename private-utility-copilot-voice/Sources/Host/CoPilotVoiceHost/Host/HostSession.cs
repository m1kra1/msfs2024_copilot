using System.Reflection;
using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Diagnostics;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using CoPilotVoiceHost.Speech;

namespace CoPilotVoiceHost.Host;

/// <summary>
/// Application session shared by console/headless and WPF UI. No WPF dependencies.
/// </summary>
public sealed class HostSession : IDisposable
{
    private readonly HostOptions _options;
    private readonly object _gateLock = new();
    private ISimConnectClient? _sim;
    private ActionExecutor? _executor;
    private CommandProcessor? _processor;
    private PhraseMatcher? _matcher;
    private SpeechInputGate? _gate;
    private PttArmService? _pttArm;
    private ITtsService? _tts;
    private ISpeechRecognitionService? _speech;
    private System.Threading.Timer? _pollTimer;
    private System.Threading.Timer? _reconnectTimer;
    private bool _disposed;
    private bool _micActive;

    public UiLogSink Log { get; } = new();
    public string ConfigRoot { get; private set; } = "";
    public AppSettings Settings { get; private set; } = new();
    public CommandCatalog Catalog { get; private set; } = new();
    public string ApplicationVersion { get; }
    public string PackageVersion { get; private set; } = "1.2.0";

    public bool IsConnected => _sim?.IsConnected ?? false;
    public bool IsLive => _sim?.IsLive ?? false;
    public string SimStatusMessage => _sim?.StatusMessage ?? "Not started";
    public string? LastSimError { get; private set; }
    public string AircraftProfile => Settings.AircraftProfile;
    public string LastPhrase { get; private set; } = "";
    public float LastConfidence { get; private set; }
    public string LastAction { get; private set; } = "";
    public bool MicActive
    {
        get => _micActive;
        private set
        {
            if (_micActive == value) return;
            _micActive = value;
            StatusChanged?.Invoke();
        }
    }

    public event Action? StatusChanged;

    public HostSession(HostOptions options)
    {
        _options = options;
        ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.2.0";
    }

    /// <summary>Initialize config + SimConnect + speech pipeline (does not block on stdin).</summary>
    public void Start()
    {
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        }
        catch { /* ignore */ }

        ConfigRoot = ConfigLoader.ResolveConfigRoot(_options.ConfigRoot);
        LoadPackageVersionHint();
        ReloadConfigInternal(reconnectSim: true, restartSpeech: true);

        Log.Info("Private Voice Co-Pilot Host session started");
        Log.Info($"Package: private-utility-copilot-voice | App {ApplicationVersion} | Package {PackageVersion}");
        Log.Info($"Config root: {ConfigRoot}");
    }

    public int RunHeadlessToCompletion()
    {
        Start();

        if (!string.IsNullOrWhiteSpace(_options.InjectPhrase))
            return InjectPhrase(_options.InjectPhrase!, forceGate: _options.BypassSpeechGate || _options.SimulatePtt);

        if (_options.Once)
        {
            Log.Info("[Host] --once: config and SimConnect path exercised; exiting.");
            return 0;
        }

        // Interactive console loop
        StartSpeechListening();
        Log.Info("[Host] Running headless. Type a phrase and Enter, or quit/exit.");
        while (true)
        {
            string? line;
            try { line = Console.ReadLine(); }
            catch { break; }
            if (line is null) break;
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (_options.SimulatePtt)
                _pttArm?.ForceArm();

            if (_speech is InjectSpeechRecognitionService inject)
                inject.Inject(trimmed);
            else
                HandlePhrase(trimmed, alreadyGated: false, forceGate: false);
        }

        Log.Info("[Host] Shutdown complete.");
        return 0;
    }

    public int InjectPhrase(string phrase, bool forceGate = false)
    {
        Log.Info($"[Inject] \"{phrase}\"");
        if (_options.SimulatePtt)
            _pttArm?.ForceArm();

        var code = HandlePhrase(phrase, alreadyGated: false, forceGate: forceGate);
        return code;
    }

    public void ForceReconnect()
    {
        if (_sim is null || _options.ForceOffline)
        {
            Log.Warn("[SimConnect] Reconnect skipped (offline mode or no client).");
            return;
        }

        try
        {
            _sim.Disconnect();
            if (_sim.Connect(Settings.SimConnect.AppName, Settings.SimConnect.ConfigIndex))
            {
                LastSimError = null;
                Log.Info($"[SimConnect] Reconnected: {_sim.StatusMessage}");
            }
            else
            {
                LastSimError = _sim.StatusMessage;
                Log.Warn($"[SimConnect] Reconnect failed: {_sim.StatusMessage}");
            }
        }
        catch (Exception ex)
        {
            LastSimError = ex.Message;
            Log.Error($"[SimConnect] Reconnect error: {ex.Message}");
        }

        StatusChanged?.Invoke();
    }

    public void TestTts(string? text = null)
    {
        _tts?.Speak(text ?? "Co Pilot voice check.", Settings.Behavior.CalloutDelayMs);
    }

    public void SetContinuousListen(bool value)
    {
        Settings.Speech.ContinuousListen = value;
        // Gate re-created from settings on next phrase; update in place
        _gate = new SpeechInputGate(Settings.Speech);
        Log.Info($"[Settings] continuous_listen={value}");
        StatusChanged?.Invoke();
    }

    public void ApplySettingsFromUi(AppSettings edited, bool saveToDisk, bool reloadProfiles)
    {
        Settings.Speech.WakeWord = edited.Speech.WakeWord;
        Settings.Speech.PttKey = edited.Speech.PttKey;
        Settings.Speech.ConfidenceThreshold = edited.Speech.ConfidenceThreshold;
        Settings.Speech.ContinuousListen = edited.Speech.ContinuousListen;
        Settings.Speech.PttGraceMs = edited.Speech.PttGraceMs;
        Settings.Tts.Voice = edited.Tts.Voice;
        Settings.Tts.Rate = edited.Tts.Rate;
        Settings.Tts.Volume = edited.Tts.Volume;
        Settings.Behavior.RequirePositiveClimbForGearUp = edited.Behavior.RequirePositiveClimbForGearUp;
        Settings.Behavior.ConfirmBeforeAction = edited.Behavior.ConfirmBeforeAction;
        Settings.Behavior.CalloutDelayMs = edited.Behavior.CalloutDelayMs;
        Settings.AircraftProfile = edited.AircraftProfile;

        if (saveToDisk)
        {
            var path = Path.Combine(ConfigRoot, "settings.json");
            ConfigLoader.SaveSettings(path, Settings);
            Log.Info($"[Settings] Saved to {path}");
        }
        else
        {
            Log.Info("[Settings] Applied in memory (not saved).");
        }

        _tts?.ApplySettings(Settings.Tts);
        _pttArm?.Dispose();
        _pttArm = new PttArmService(Settings.Speech.PttKey, Settings.Speech.PttGraceMs);
        _pttArm.StartPolling(50);
        _gate = new SpeechInputGate(Settings.Speech);

        if (reloadProfiles)
            ReloadConfigInternal(reconnectSim: false, restartSpeech: true);

        StatusChanged?.Invoke();
    }

    public void ReloadFromDisk()
    {
        ReloadConfigInternal(reconnectSim: false, restartSpeech: true);
        Log.Info("[Settings] Reloaded from disk.");
        StatusChanged?.Invoke();
    }

    public IReadOnlyList<string> ListProfiles() => ConfigLoader.ListAircraftProfiles(ConfigRoot);

    public void StartSpeechListening()
    {
        StopSpeechListening();
        try
        {
            if (_options.NoSpeech)
            {
                _speech = new InjectSpeechRecognitionService();
                Log.Info("[Speech] --no-speech / inject-only mode.");
            }
            else
            {
                var win = new WindowsSpeechRecognitionService(Settings.Speech);
                win.IsPttActive = () =>
                {
                    _pttArm?.Poll();
                    return _pttArm?.IsArmed ?? false;
                };
                win.LoadGrammar(_matcher!.AllPhrases, Settings.Speech.WakeWord);
                _speech = win;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[Speech] Engine unavailable ({ex.Message}); inject-only.");
            _speech = new InjectSpeechRecognitionService();
        }

        _speech.PhraseRecognized += OnSpeechRecognized;
        try
        {
            _speech.Start();
            Log.Info($"[Speech] Listening wake='{Settings.Speech.WakeWord}' ptt={Settings.Speech.PttKey} continuous={Settings.Speech.ContinuousListen}");
        }
        catch (Exception ex)
        {
            Log.Error($"[Speech] Start failed: {ex.Message}");
        }

        _pollTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                _pttArm?.Poll();
                _sim?.ReceiveMessage();
                var armed = _pttArm?.IsArmed == true || _pttArm?.IsKeyCurrentlyDown == true;
                MicActive = armed;
            }
            catch { /* ignore */ }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

        if (!_options.ForceOffline)
        {
            _reconnectTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (_sim is null || _sim.IsConnected || _options.ForceOffline) return;
                    Log.Info("[SimConnect] Retry connect...");
                    ForceReconnect();
                }
                catch (Exception ex)
                {
                    Log.Warn($"[SimConnect] Retry failed: {ex.Message}");
                }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopSpeechListening();
        _pttArm?.Dispose();
        _tts?.Dispose();
        _sim?.Dispose();
    }

    private void StopSpeechListening()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;
        if (_speech is not null)
        {
            try
            {
                _speech.PhraseRecognized -= OnSpeechRecognized;
                _speech.Stop();
                _speech.Dispose();
            }
            catch { /* ignore */ }
            _speech = null;
        }
    }

    private void OnSpeechRecognized(string text, float conf)
    {
        LastPhrase = text;
        LastConfidence = conf;
        MicActive = true;
        Log.Info($"[Speech] Recognized ({conf:F2}): {text}");
        HandlePhrase(text, alreadyGated: _speech is WindowsSpeechRecognitionService, forceGate: false);
        StatusChanged?.Invoke();
    }

    private int HandlePhrase(string text, bool alreadyGated, bool forceGate)
    {
        if (_processor is null || _sim is null || _executor is null || _gate is null || _pttArm is null || _tts is null)
            return 2;

        _pttArm.Poll();
        _gate.SetPtt(_pttArm.IsArmed || forceGate || _options.SimulatePtt);

        if (!alreadyGated && !forceGate)
        {
            if (!_gate.TryAccept(text, out _, out var reason))
            {
                Log.Info($"[Speech] {reason} (PTT armed={_pttArm.IsArmed})");
                return 5;
            }
        }

        if (!_options.ForceOffline)
            EnsureLiveConnection();

        var result = _processor.Process(
            text,
            _sim.Snapshot,
            Settings.Speech.WakeWord,
            speakWithDelay: _tts.Speak);

        if (result is null)
        {
            Log.Info("[Host] Unrecognized command.");
            return 3;
        }

        LastPhrase = result.MatchedPhrase;
        if (result.ActionsExecuted.Count > 0)
            LastAction = string.Join(", ", result.ActionsExecuted.Select(a => $"{a.Type}:{a.Name}"));
        else if (!result.Allowed)
            LastAction = $"denied:{result.DenyReason}";
        else
            LastAction = "(no actions)";

        LogResult(result);
        StatusChanged?.Invoke();
        return result.Allowed ? 0 : 4;
    }

    private void EnsureLiveConnection()
    {
        if (_sim is null || _sim.IsConnected) return;
        Log.Info("[SimConnect] Not connected — attempting open before action...");
        try
        {
            if (_sim.Connect(Settings.SimConnect.AppName, Settings.SimConnect.ConfigIndex))
            {
                LastSimError = null;
                Log.Info($"[SimConnect] {_sim.StatusMessage}");
            }
            else
            {
                LastSimError = _sim.StatusMessage;
                Log.Warn($"[SimConnect] Still not connected: {_sim.StatusMessage}");
            }
        }
        catch (Exception ex)
        {
            LastSimError = ex.Message;
            Log.Error($"[SimConnect] Connect attempt failed: {ex.Message}");
        }

        StatusChanged?.Invoke();
    }

    private void LogResult(CommandResult result)
    {
        Log.Info(
            $"[Command] id={result.CommandId} phrase='{result.MatchedPhrase}' allowed={result.Allowed} live={IsLive}");
        if (!result.Allowed)
            Log.Info($"[Command] deny={result.DenyReason}");
        foreach (var a in result.ActionsExecuted)
            Log.Info($"[Action] {a.Type}:{a.Name}");
        if (_executor?.LastExecuteHadErrors == true)
        {
            foreach (var e in _executor.Errors)
                Log.Error($"[ActionError] {e}");
        }

        Log.Info($"[Response] {result.SpokenResponse}");
        if (result.Allowed && result.ActionsExecuted.Count > 0 && !IsLive)
            Log.Warn("[SimConnect] NOTE: command accepted but SimConnect is NOT live — aircraft will not change.");
    }

    private void ReloadConfigInternal(bool reconnectSim, bool restartSpeech)
    {
        var (settings, catalog) = ConfigLoader.LoadAll(ConfigRoot, _options.Profile);
        Settings = settings;
        if (!string.IsNullOrWhiteSpace(_options.Profile))
            Settings.AircraftProfile = _options.Profile!;
        Catalog = catalog;

        Log.Info($"[Config] Commands loaded: {Catalog.Commands.Count}");
        Log.Info($"[Config] Aircraft profile: {Settings.AircraftProfile}");
        Log.Info($"[Config] Wake word: '{Settings.Speech.WakeWord}' | PTT: {Settings.Speech.PttKey}");
        Log.Info($"[Config] continuous_listen={Settings.Speech.ContinuousListen} ptt_grace_ms={Settings.Speech.PttGraceMs}");

        if (reconnectSim || _sim is null)
        {
            _sim?.Dispose();
            _sim = SimConnectClientFactory.CreateAndConnect(
                Settings.SimConnect.AppName,
                Settings.SimConnect.ConfigIndex,
                preferOffline: _options.ForceOffline,
                allowOfflineFallback: _options.ForceOffline || _options.AllowOfflineFallback,
                out var live);

            Log.Info($"[SimConnect] {_sim.StatusMessage}");
            Log.Info($"[SimConnect] Connected={_sim.IsConnected} IsLive={_sim.IsLive}");
            if (!_sim.IsLive)
            {
                LastSimError = _sim.StatusMessage;
                Log.Warn("[SimConnect] WARNING: Not live. Recognized commands will speak but will NOT move switches in MSFS.");
            }
            else
            {
                LastSimError = null;
            }

            if (!live && _sim is RecordingSimConnectClient recording)
            {
                recording.Snapshot.Set("GEAR POSITION", 1);
                recording.Snapshot.Set("VERTICAL SPEED", _options.FixtureVerticalSpeed ?? 500);
                recording.Snapshot.Set("FLAPS HANDLE INDEX", 0);
                recording.Snapshot.Set("AUTOPILOT MASTER", 0);
                recording.Snapshot.Set("LIGHT LANDING", 0);
                recording.Snapshot.Set("BRAKE PARKING POSITION", 0);
                recording.Snapshot.Set("ENG ANTI ICE", 0);
                Log.Info($"[SimConnect] Offline snapshot seeded (VS={_options.FixtureVerticalSpeed ?? 500} fpm).");
            }
        }

        _matcher = new PhraseMatcher(Catalog.Commands);
        _executor = new ActionExecutor(_sim!);
        _processor = new CommandProcessor(_matcher, new ConditionEngine(), _executor, Settings.Behavior);
        _gate = new SpeechInputGate(Settings.Speech);
        _pttArm?.Dispose();
        _pttArm = new PttArmService(Settings.Speech.PttKey, Settings.Speech.PttGraceMs);
        _pttArm.StartPolling(50);
        if (_options.SimulatePtt)
            _pttArm.ForceArm(60_000);

        _tts?.Dispose();
        _tts = _options.NoTts
            ? new ConsoleTtsService()
            : CreateTts(Settings.Tts);
        _tts.ApplySettings(Settings.Tts);

        if (restartSpeech && !_options.Once && string.IsNullOrWhiteSpace(_options.InjectPhrase))
        {
            // For GUI/session continuous mode; headless inject doesn't need speech yet
        }

        StatusChanged?.Invoke();
    }

    private void LoadPackageVersionHint()
    {
        try
        {
            var dir = new DirectoryInfo(ConfigRoot);
            for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var manifest = Path.Combine(dir.FullName, "manifest.json");
                if (!File.Exists(manifest))
                    manifest = Path.Combine(dir.FullName, "Packages", "private-utility-copilot-voice", "manifest.json");
                if (!File.Exists(manifest)) continue;
                var text = File.ReadAllText(manifest);
                var marker = "\"package_version\"";
                var idx = text.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0) continue;
                var colon = text.IndexOf(':', idx);
                var q1 = text.IndexOf('"', colon + 1);
                var q2 = text.IndexOf('"', q1 + 1);
                if (q1 > 0 && q2 > q1)
                    PackageVersion = text.Substring(q1 + 1, q2 - q1 - 1);
                break;
            }
        }
        catch
        {
            PackageVersion = ApplicationVersion;
        }
    }

    private static ITtsService CreateTts(TtsSettings settings)
    {
        try
        {
            var tts = new WindowsTtsService();
            tts.ApplySettings(settings);
            return tts;
        }
        catch
        {
            return new ConsoleTtsService();
        }
    }
}
