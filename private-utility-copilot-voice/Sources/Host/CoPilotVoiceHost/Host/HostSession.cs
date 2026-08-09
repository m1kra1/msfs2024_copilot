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
    private bool _speechListening;
    /// <summary>Identity key last handled for auto profile switch (stamped only when auto can switch).</summary>
    private string _lastDetectedIdentityKey = "";

    /// <summary>
    /// Identity key last used for display/log/StatusChanged (always; dedupes 50ms poll spam).
    /// Separate from switch key so auto-off observations do not block false→true re-eval.
    /// Null = never seen.
    /// </summary>
    private string? _lastSeenDisplayIdentityKey;

    private AircraftDetectionConfig _detectionConfig = new();
    private readonly object _detectLock = new();

    /// <summary>
    /// When CLI --profile is set, auto profile switching is locked for the session.
    /// Detected title is still updated when available.
    /// </summary>
    private bool CliProfileLocked => !string.IsNullOrWhiteSpace(_options.Profile);

    public UiLogSink Log { get; } = new();
    public string ConfigRoot { get; private set; } = "";
    public AppSettings Settings { get; private set; } = new();
    public CommandCatalog Catalog { get; private set; } = new();
    public AircraftDetectionConfig DetectionConfig => _detectionConfig;
    public string ApplicationVersion { get; }
    public string PackageVersion { get; private set; } = "1.2.0";

    /// <summary>How many times speech listening was (re)started — for tests and diagnostics.</summary>
    public int SpeechStartCount { get; private set; }

    /// <summary>How many times auto-detect applied a profile change (catalog rebuild path).</summary>
    public int AutoDetectProfileSwitchCount { get; private set; }

    /// <summary>How many times the command catalog was rebuilt from settings/profile.</summary>
    public int CatalogRebuildCount { get; private set; }

    /// <summary>Current phrase matcher built from the active catalog (for tests).</summary>
    public PhraseMatcher? Matcher => _matcher;

    public bool IsConnected => _sim?.IsConnected ?? false;
    public bool IsLive => _sim?.IsLive ?? false;
    public string SimStatusMessage => _sim?.StatusMessage ?? "Not started";
    public string? LastSimError { get; private set; }
    public string AircraftProfile => Settings.AircraftProfile;

    /// <summary>Last detected TITLE (or "Unknown").</summary>
    public string DetectedAircraftTitle { get; private set; } = AircraftProfileMatcher.UnknownTitle;

    /// <summary>Last detected ATC MODEL (may be empty).</summary>
    public string DetectedAtcModel { get; private set; } = "";

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
        // Initial load from disk; speech starts separately via StartSpeechListening when needed.
        ReloadConfigInternal(reconnectSim: true, restartSpeech: false);

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

    /// <summary>
    /// Apply UI edits into the live <see cref="Settings"/> object.
    /// When <paramref name="saveToDisk"/> is false, values stay in memory only (disk is not re-read).
    /// When <paramref name="rebuildCatalog"/> is true, base_commands + selected aircraft profile are re-merged
    /// and the speech grammar is rebuilt if listening is active.
    /// </summary>
    public void ApplySettingsFromUi(AppSettings edited, bool saveToDisk, bool rebuildCatalog = true)
    {
        // Snapshot BEFORE copy — edited must not be the live Settings object already mutated by UI.
        var autoWas = Settings.AutoDetectAircraft;
        CopyEditableSettings(edited, Settings);

        // Manual Apply always wins for the selected profile unless false→true auto-detect
        // re-evaluates a previously observed identity below. CLI --profile only suppresses
        // auto-detect switching (ProcessAircraftIdentity), not GUI/settings Apply.

        if (saveToDisk)
        {
            var path = Path.Combine(ConfigRoot, "settings.json");
            ConfigLoader.SaveSettings(path, Settings);
            Log.Info($"[Settings] Saved to {path}");
        }
        else
        {
            Log.Info("[Settings] Applied in memory (not saved to disk).");
        }

        // Never call LoadAll() here — that would wipe in-memory Apply edits.
        if (rebuildCatalog)
            RebuildCatalogFromCurrentSettings();

        RebuildPipelineServices();

        if (_speechListening)
            StartSpeechListening();
        else
            Log.Info("[Settings] Pipeline rebuilt (speech not listening yet).");

        // false→true: identity may already be known while auto was off (key was not stamped).
        // Re-run detection so the matching profile applies without waiting for a new aircraft.
        if (!autoWas && Settings.AutoDetectAircraft && !CliProfileLocked)
        {
            _lastDetectedIdentityKey = "";
            var titleForReeval = string.Equals(
                    DetectedAircraftTitle, AircraftProfileMatcher.UnknownTitle, StringComparison.OrdinalIgnoreCase)
                ? null
                : DetectedAircraftTitle;
            ProcessAircraftIdentity(titleForReeval, DetectedAtcModel);
        }

        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Evaluate aircraft identity and optionally switch profile (auto-detect).
    /// Safe offline: updates display title, never throws. Used by the poll path and unit tests.
    /// Display/log/StatusChanged are deduped by identity so the live SECOND/50ms path does not thrash.
    /// Switch-stamp key is separate and only set when auto can switch.
    /// </summary>
    public void ProcessAircraftIdentity(string? title, string? atcModel)
    {
        lock (_detectLock)
        {
            var key = AircraftProfileMatcher.IdentityKey(title, atcModel);
            var displayTitle = AircraftProfileMatcher.DisplayTitle(title);
            var displayModel = (atcModel ?? string.Empty).Trim();

            // Empty identity: Unknown display — notify once, not every poll.
            if (key.Length == 0)
            {
                if (_lastSeenDisplayIdentityKey is not null
                    && _lastSeenDisplayIdentityKey.Length == 0)
                    return;

                _lastSeenDisplayIdentityKey = "";
                DetectedAircraftTitle = AircraftProfileMatcher.UnknownTitle;
                DetectedAtcModel = "";
                StatusChanged?.Invoke();
                return;
            }

            var canAutoSwitch = Settings.AutoDetectAircraft && !CliProfileLocked;

            // When auto-switch is off/locked: update display + log only if identity changed.
            // Do NOT stamp _lastDetectedIdentityKey (false→true re-eval must still work).
            if (!canAutoSwitch)
            {
                if (string.Equals(key, _lastSeenDisplayIdentityKey, StringComparison.Ordinal))
                    return;

                _lastSeenDisplayIdentityKey = key;
                DetectedAircraftTitle = displayTitle;
                DetectedAtcModel = displayModel;

                var matchedPreview = EnsureProfileExists(
                    AircraftProfileMatcher.ResolveProfile(_detectionConfig, title, atcModel));
                Log.Info(
                    $"[AircraftDetect] title='{DetectedAircraftTitle}' model='{DetectedAtcModel}' → profile '{matchedPreview}'" +
                    (CliProfileLocked ? " (CLI --profile lock: no switch)" : " (auto-detect off: no switch)"));
                StatusChanged?.Invoke();
                return;
            }

            // Auto can switch: silent if this identity was already handled for switching.
            if (string.Equals(key, _lastDetectedIdentityKey, StringComparison.Ordinal))
                return;

            _lastDetectedIdentityKey = key;
            _lastSeenDisplayIdentityKey = key;
            DetectedAircraftTitle = displayTitle;
            DetectedAtcModel = displayModel;

            var matched = EnsureProfileExists(
                AircraftProfileMatcher.ResolveProfile(_detectionConfig, title, atcModel));

            Log.Info(
                $"[AircraftDetect] title='{DetectedAircraftTitle}' model='{DetectedAtcModel}' → profile '{matched}'");

            if (string.Equals(Settings.AircraftProfile, matched, StringComparison.OrdinalIgnoreCase))
            {
                StatusChanged?.Invoke();
                return;
            }

            var previous = Settings.AircraftProfile;
            Settings.AircraftProfile = matched;
            RebuildCatalogFromCurrentSettings();
            RebuildPipelineServices();
            if (_speechListening)
                StartSpeechListening();

            AutoDetectProfileSwitchCount++;
            Log.Info($"[AircraftDetect] Profile switched: '{previous}' → '{matched}' (catalog rebuilt)");

            if (Settings.AnnounceProfileSwitch && _tts is not null)
            {
                try
                {
                    _tts.Speak($"Aircraft profile {matched}.", 0);
                }
                catch
                {
                    // TTS optional
                }
            }

            StatusChanged?.Invoke();
        }
    }

    public void ReloadFromDisk()
    {
        // Full disk reload replaces Settings, then rebuilds pipeline + speech if active.
        ReloadConfigInternal(reconnectSim: false, restartSpeech: true);
        Log.Info("[Settings] Reloaded from disk.");
        StatusChanged?.Invoke();
    }

    public IReadOnlyList<string> ListProfiles() => ConfigLoader.ListAircraftProfiles(ConfigRoot);

    /// <summary>Loads base_commands.json from the active config root (working copy for the Commands UI).</summary>
    public CommandCatalog LoadBaseCommandsFromDisk()
    {
        var path = Path.Combine(ConfigRoot, "base_commands.json");
        return ConfigLoader.CloneCatalog(ConfigLoader.LoadBaseCommands(path));
    }

    /// <summary>Loads the active aircraft profile from disk (working copy for the Commands UI).</summary>
    public AircraftProfile LoadActiveProfileFromDisk()
    {
        var profileName = string.IsNullOrWhiteSpace(Settings.AircraftProfile)
            ? "generic"
            : Settings.AircraftProfile.Trim();
        var path = Path.Combine(ConfigRoot, "aircraft", $"{profileName}.json");
        if (File.Exists(path))
            return ConfigLoader.CloneProfile(ConfigLoader.LoadAircraftProfile(path));

        return new AircraftProfile { ProfileId = profileName, Title = profileName };
    }

    /// <summary>
    /// Merges base + profile into the live catalog, rebuilds the pipeline, and optionally persists both files.
    /// Does not re-read settings.json (preserves in-memory settings Apply edits).
    /// </summary>
    public void ApplyCommandSources(CommandCatalog baseCatalog, AircraftProfile profile, bool saveToDisk)
    {
        if (baseCatalog is null)
            throw new ArgumentNullException(nameof(baseCatalog));
        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        var profileName = string.IsNullOrWhiteSpace(Settings.AircraftProfile)
            ? "generic"
            : Settings.AircraftProfile.Trim();
        if (string.IsNullOrWhiteSpace(profile.ProfileId))
            profile.ProfileId = profileName;

        if (saveToDisk)
        {
            var basePath = Path.Combine(ConfigRoot, "base_commands.json");
            ConfigLoader.SaveBaseCommands(basePath, baseCatalog);
            Log.Info($"[Commands] Saved {basePath} ({baseCatalog.Commands.Count} commands)");

            var profilePath = Path.Combine(ConfigRoot, "aircraft", $"{profileName}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            // Keep profile id aligned with active settings profile name for file mapping.
            profile.ProfileId = profileName;
            ConfigLoader.SaveAircraftProfile(profilePath, profile);
            Log.Info($"[Commands] Saved {profilePath} ({profile.Commands.Count} profile commands)");
        }
        else
        {
            Log.Info(
                $"[Commands] Applied in memory (base={baseCatalog.Commands.Count}, profile={profile.Commands.Count}; not saved to disk).");
        }

        Catalog = ConfigLoader.Merge(baseCatalog, profile);
        CatalogRebuildCount++;
        Log.Info($"[Config] Catalog rebuilt from UI sources: {Catalog.Commands.Count} commands (profile '{profileName}')");

        RebuildPipelineServices();
        if (_speechListening)
            StartSpeechListening();
        else
            Log.Info("[Commands] Pipeline rebuilt (speech not listening yet).");

        StatusChanged?.Invoke();
    }

    public void StartSpeechListening()
    {
        StopSpeechListening();
        _speechListening = true;
        SpeechStartCount++;

        // Ensure matcher/gate reflect current Settings before loading grammar
        if (_matcher is null || _gate is null)
            RebuildPipelineServices();

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
            Log.Info(
                $"[Speech] Listening (#{SpeechStartCount}) wake='{Settings.Speech.WakeWord}' ptt={Settings.Speech.PttKey} continuous={Settings.Speech.ContinuousListen} phrases={_matcher?.AllPhrases.Count ?? 0}");
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

                // Low-rate aircraft identity: SimConnect already requests TITLE/ATC MODEL on SECOND period.
                if (_sim is not null && _sim.IsLive)
                    ProcessAircraftIdentity(_sim.AircraftTitle, _sim.AtcModel);
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
        StopSpeechListeningForShutdown();
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

        // Keep _speechListening true across restarts so Apply/Reload know to start again;
        // only Dispose/full stop clears it via StopSpeechListeningForShutdown.
    }

    private void StopSpeechListeningForShutdown()
    {
        _speechListening = false;
        StopSpeechListening();
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

        // Full dynamic list (and any other detail lines) before the short spoken summary
        if (result.DetailLogLines is { Count: > 0 })
        {
            foreach (var line in result.DetailLogLines)
                Log.Info(line);
        }

        Log.Info($"[Response] {result.SpokenResponse}");
        if (result.Allowed && result.ActionsExecuted.Count > 0 && !IsLive)
            Log.Warn("[SimConnect] NOTE: command accepted but SimConnect is NOT live — aircraft will not change.");
    }

    private void ReloadConfigInternal(bool reconnectSim, bool restartSpeech)
    {
        // Disk → Settings (this intentionally overwrites in-memory Apply edits)
        var profileOverride = string.IsNullOrWhiteSpace(_options.Profile) ? null : _options.Profile;
        var (settings, catalog) = ConfigLoader.LoadAll(ConfigRoot, profileOverride);
        Settings = settings;
        if (!string.IsNullOrWhiteSpace(profileOverride))
            Settings.AircraftProfile = profileOverride!;
        Catalog = catalog;
        CatalogRebuildCount++;

        _detectionConfig = ConfigLoader.LoadAircraftDetection(ConfigRoot);
        _lastDetectedIdentityKey = "";
        _lastSeenDisplayIdentityKey = null;

        Log.Info($"[Config] Commands loaded: {Catalog.Commands.Count}");
        Log.Info($"[Config] Aircraft profile: {Settings.AircraftProfile}");
        Log.Info(
            $"[Config] auto_detect_aircraft={Settings.AutoDetectAircraft} announce_profile_switch={Settings.AnnounceProfileSwitch}" +
            (CliProfileLocked ? " (CLI --profile lock)" : ""));
        Log.Info(
            $"[Config] Detection rules: {_detectionConfig.Rules.Count} fallback='{_detectionConfig.FallbackProfile}'");
        Log.Info($"[Config] Wake word: '{Settings.Speech.WakeWord}' | PTT: {Settings.Speech.PttKey}");
        Log.Info($"[Config] continuous_listen={Settings.Speech.ContinuousListen} ptt_grace_ms={Settings.Speech.PttGraceMs}");

        if (reconnectSim || _sim is null)
            ConnectSimClient();

        RebuildPipelineServices();

        if (restartSpeech && _speechListening)
        {
            StartSpeechListening();
        }
        else if (restartSpeech)
        {
            Log.Info("[Config] Speech restart requested but listening was not active.");
        }

        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Re-merge base_commands + aircraft profile using <see cref="Settings.AircraftProfile"/>
    /// without reloading settings.json (preserves in-memory Apply edits).
    /// </summary>
    public void RebuildCatalogFromCurrentSettings()
    {
        var basePath = Path.Combine(ConfigRoot, "base_commands.json");
        var baseCatalog = ConfigLoader.LoadBaseCommands(basePath);
        var profileName = string.IsNullOrWhiteSpace(Settings.AircraftProfile)
            ? "generic"
            : Settings.AircraftProfile;
        var profilePath = Path.Combine(ConfigRoot, "aircraft", $"{profileName}.json");
        AircraftProfile profile;
        if (File.Exists(profilePath))
            profile = ConfigLoader.LoadAircraftProfile(profilePath);
        else
            profile = new AircraftProfile { ProfileId = profileName, Title = "missing profile" };

        Catalog = ConfigLoader.Merge(baseCatalog, profile);
        CatalogRebuildCount++;
        Log.Info($"[Config] Catalog rebuilt for profile '{profileName}': {Catalog.Commands.Count} commands");
    }

    private string EnsureProfileExists(string profileName)
    {
        var name = string.IsNullOrWhiteSpace(profileName) ? "generic" : profileName.Trim();
        var path = Path.Combine(ConfigRoot, "aircraft", $"{name}.json");
        if (File.Exists(path))
            return name;

        var fallback = string.IsNullOrWhiteSpace(_detectionConfig.FallbackProfile)
            ? "generic"
            : _detectionConfig.FallbackProfile.Trim();
        Log.Warn($"[AircraftDetect] Profile '{name}' not found — using fallback '{fallback}'");
        return fallback;
    }

    private void RebuildPipelineServices()
    {
        if (_sim is null)
            throw new InvalidOperationException("SimConnect client not initialized");

        _matcher = new PhraseMatcher(Catalog.Commands);
        _executor = new ActionExecutor(_sim);
        _processor = new CommandProcessor(
            _matcher,
            new ConditionEngine(),
            _executor,
            Settings.Behavior,
            Catalog.Commands);

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
    }

    private void ConnectSimClient()
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

    private static void CopyEditableSettings(AppSettings from, AppSettings to)
    {
        to.Speech.WakeWord = from.Speech.WakeWord;
        to.Speech.PttKey = from.Speech.PttKey;
        to.Speech.ConfidenceThreshold = from.Speech.ConfidenceThreshold;
        to.Speech.ContinuousListen = from.Speech.ContinuousListen;
        to.Speech.PttGraceMs = from.Speech.PttGraceMs;
        to.Tts.Voice = from.Tts.Voice;
        to.Tts.Rate = from.Tts.Rate;
        to.Tts.Volume = from.Tts.Volume;
        to.Behavior.RequirePositiveClimbForGearUp = from.Behavior.RequirePositiveClimbForGearUp;
        to.Behavior.ConfirmBeforeAction = from.Behavior.ConfirmBeforeAction;
        to.Behavior.CalloutDelayMs = from.Behavior.CalloutDelayMs;
        to.AircraftProfile = from.AircraftProfile;
        to.AutoDetectAircraft = from.AutoDetectAircraft;
        to.AnnounceProfileSwitch = from.AnnounceProfileSwitch;
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
