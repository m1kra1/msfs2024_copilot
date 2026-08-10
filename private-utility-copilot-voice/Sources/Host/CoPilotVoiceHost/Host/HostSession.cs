using System.Reflection;
using System.Text;
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
    /// <summary>Message-pump interval; keeps SimConnect ReceiveMessage responsive without busy SimVar polling.</summary>
    private const int PollIntervalMs = 50;

    /// <summary>Identity checks run every N poll ticks (~500 ms) — TITLE/ATC MODEL already update on SECOND period.</summary>
    private const int IdentityPollEveryNTicks = 10;

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
    private int _identityPollTick;
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

    // ── Learn Mode (Core-only services; no WPF) ───────────────────────────────
    private readonly LearnCaptureService _learnCapture = new();
    private CommandMappingIndex? _learnMappingIndex;
    private readonly List<LearnWatchEntry> _manualLearnWatches = new();
    private LearnWatchlistConfig _learnWatchlist = new();
    private bool _learnModeActive;

    /// <summary>Profile id last merged into <see cref="Catalog"/> (skips redundant disk re-merge on Apply).</summary>
    private string _mergedCatalogProfile = "";

    /// <summary>Last speech-grammar fingerprint that was loaded into the recognizer (or would be).</summary>
    private string _activeSpeechGrammarKey = "";

    /// <summary>Last pipeline services fingerprint (matcher/PTT/TTS/behavior).</summary>
    private string _activePipelineServicesKey = "";

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
    public string PackageVersion { get; private set; } = "1.4.0";

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

    /// <summary>Best-effort airport ident from SimConnect (may be empty).</summary>
    public string DetectedAirportIdent { get; private set; } = "";

    /// <summary>True after at least one live DEF_STATUS payload reached the snapshot.</summary>
    public bool HasReceivedSimData => _sim?.HasReceivedStatusData ?? false;

    public double IndicatedAirspeedKnots => SnapshotValue("AIRSPEED INDICATED");
    public double PlaneAltitudeFeet => SnapshotValue("PLANE ALTITUDE");
    public double VerticalSpeedFpm => SnapshotValue(HostConstants.VerticalSpeedSimVar);
    public double GroundVelocityKnots => SnapshotValue("GROUND VELOCITY");
    public bool SimOnGround => SnapshotValue("SIM ON GROUND") > 0.5;

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

    /// <summary>Raised when Learn Mode detections or watch state change (UI thin shell).</summary>
    public event Action? LearnChanged;

    public bool LearnModeActive => _learnModeActive;

    public int LearnWatchCount => _learnCapture.WatchCount;

    public IReadOnlyList<LearnDetection> LearnDetections => _learnCapture.Recent;

    public HostSession(HostOptions options)
    {
        _options = options;
        ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.4.0";
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
        Log.Info($"Package: {HostConstants.PackageName} | App {ApplicationVersion} | Package {PackageVersion}");
        Log.Info($"Config root: {ConfigRoot}");
    }

    public int RunHeadlessToCompletion()
    {
        Start();

        if (_options.LearnDump)
        {
            // Offline-friendly: dump resolved watch list for diagnostics / CI.
            StartLearnMode(allowOffline: true);
            var watches = PeekLearnWatches();
            Log.Info($"[LearnDump] profile={AircraftProfile} watches={watches.Count}");
            foreach (var w in watches)
                Log.Info($"[LearnDump] {w.Name} ({w.Units}){(w.IsManual ? " [manual]" : "")}");

            if (!string.IsNullOrWhiteSpace(_options.LearnExportPath))
            {
                try
                {
                    // Export watches snapshot as detections file shell (empty detections OK).
                    var path = ExportLearnDetections(_options.LearnExportPath);
                    Log.Info($"[LearnDump] export file: {path}");
                }
                catch (Exception ex)
                {
                    Log.Error($"[LearnDump] export failed: {ex.Message}");
                    return 2;
                }
            }

            StopLearnMode();
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(_options.InjectPhrase))
        {
            var code = InjectPhrase(_options.InjectPhrase!, forceGate: _options.BypassSpeechGate || _options.SimulatePtt);
            MaybeExportLearnAfterRun();
            return code;
        }

        if (_options.Once)
        {
            Log.Info("[Host] --once: config and SimConnect path exercised; exiting.");
            MaybeExportLearnAfterRun();
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
        MaybeExportLearnAfterRun();
        return 0;
    }

    private void MaybeExportLearnAfterRun()
    {
        if (string.IsNullOrWhiteSpace(_options.LearnExportPath))
            return;
        try
        {
            ExportLearnDetections(_options.LearnExportPath);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Learn] Export after run failed: {ex.Message}");
        }
    }

    public int InjectPhrase(string phrase, bool forceGate = false)
    {
        Log.Info($"[Inject] \"{phrase}\"");
        if (_options.SimulatePtt)
            _pttArm?.ForceArm();

        var code = HandlePhrase(phrase, alreadyGated: false, forceGate: forceGate);
        return code;
    }

    /// <summary>
    /// Manually run a catalog command by id (GUI Manual tab). Uses the first phrase and
    /// force-gates the speech gate so wake word / PTT is not required.
    /// </summary>
    public int RunCatalogCommand(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            return 3;

        var cmd = Catalog.Commands.FirstOrDefault(c =>
            c.Id.Equals(commandId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (cmd is null)
        {
            Log.Warn($"[Manual] Unknown command id '{commandId}'");
            return 3;
        }

        var phrase = cmd.Phrases.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))?.Trim();
        if (string.IsNullOrWhiteSpace(phrase))
            phrase = cmd.Id.Replace('_', ' ');

        // Include wake word for matcher residual strip; forceGate skips speech gate.
        var wake = Settings.Speech.WakeWord?.Trim() ?? "";
        var text = string.IsNullOrWhiteSpace(wake) ? phrase : $"{wake} {phrase}";
        Log.Info($"[Manual] id={cmd.Id} → \"{text}\"");
        return InjectPhrase(text, forceGate: true);
    }

    public void ForceReconnect()
    {
        if (_sim is null || _options.ForceOffline)
        {
            Log.Warn("[SimConnect] Reconnect skipped (offline mode or no client).");
            return;
        }

        // Avoid half-state learn watches across reconnect.
        if (_learnModeActive)
        {
            StopLearnMode();
            Log.Info("[Learn] Stopped (reconnect)");
        }

        try
        {
            _sim.Disconnect();
            if (TryOpenSimConnection())
                Log.Info($"[SimConnect] Reconnected: {_sim.StatusMessage}");
            else
                Log.Warn($"[SimConnect] Reconnect failed: {_sim.StatusMessage}");
        }
        catch (Exception ex)
        {
            LastSimError = ex.Message;
            Log.Error($"[SimConnect] Reconnect error: {ex.Message}");
        }

        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Start Learn Mode: build watches, register isolated SimConnect learn DEF, begin capture.
    /// Requires Live SimConnect unless <paramref name="allowOffline"/> is true (tests).
    /// </summary>
    public void StartLearnMode(bool allowOffline = false)
    {
        if (_sim is null)
        {
            Log.Warn("[Learn] Cannot start — SimConnect client not ready.");
            return;
        }

        if (!IsLive && !allowOffline)
        {
            Log.Warn("[Learn] Cannot start — SimConnect is not Live. Connect Free Flight first.");
            return;
        }

        _learnMappingIndex = new CommandMappingIndex(Catalog);
        var watches = BuildCurrentLearnWatches();
        try
        {
            _sim.SetLearnWatchDefinitions(
                watches.Select(w => (w.Name, w.Units)).ToList());
        }
        catch (Exception ex)
        {
            Log.Error($"[Learn] SetLearnWatchDefinitions failed: {ex.Message}");
        }

        _learnCapture.Start(watches);
        _learnModeActive = true;
        Log.Info(
            $"[Learn] Mode ON — {watches.Count} watches (manual={_manualLearnWatches.Count}, profile={AircraftProfile}, global_defaults={_learnWatchlist.DefaultWatches.Count})");
        LearnChanged?.Invoke();
        StatusChanged?.Invoke();
    }

    public void StopLearnMode()
    {
        if (!_learnModeActive && !_learnCapture.IsActive)
        {
            try { _sim?.ClearLearnWatchDefinitions(); } catch { /* ignore */ }
            return;
        }

        _learnModeActive = false;
        _learnCapture.Stop();
        try { _sim?.ClearLearnWatchDefinitions(); } catch { /* ignore */ }
        Log.Info("[Learn] Mode OFF");
        LearnChanged?.Invoke();
        StatusChanged?.Invoke();
    }

    /// <summary>Add a session-only manual watch (e.g. study LVar). Re-registers if Learn is active.</summary>
    public void AddManualLearnWatch(string name, string units = "number")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Log.Warn("[Learn] Manual watch ignored — empty name.");
            return;
        }

        var entry = new LearnWatchEntry
        {
            Name = name.Trim(),
            Units = string.IsNullOrWhiteSpace(units) ? "number" : units.Trim(),
            IsManual = true
        };

        if (_manualLearnWatches.Any(w =>
                w.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Log.Info($"[Learn] Manual watch already present: {entry.Name}");
            return;
        }

        _manualLearnWatches.Add(entry);
        Log.Info($"[Learn] Manual watch added: {entry.Name} ({entry.Units})");

        if (_learnModeActive)
            RefreshLearnRegistration("manual watch");
        else
            LearnChanged?.Invoke();
    }

    public void ClearLearnDetections()
    {
        _learnCapture.ClearRecent();
        LearnChanged?.Invoke();
    }

    /// <summary>
    /// Write recent Learn detections as indented JSON. Returns absolute path written.
    /// </summary>
    public string ExportLearnDetections(string? path = null)
    {
        var target = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(
                AppContext.BaseDirectory,
                $"learn-detections-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json")
            : path.Trim();

        var dir = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = _learnCapture.ExportRecentJson();
        File.WriteAllText(target, json);
        Log.Info($"[Learn] Exported {_learnCapture.Recent.Count} detections → {target}");
        return Path.GetFullPath(target);
    }

    /// <summary>Current learn watch names (for diagnostics / headless dump).</summary>
    public IReadOnlyList<LearnWatchEntry> PeekLearnWatches() => BuildCurrentLearnWatches();

    /// <summary>Re-build mapping index after catalog changes; re-register watches if Learn is on.</summary>
    private void OnCatalogChangedForLearn(string reason)
    {
        _learnMappingIndex = new CommandMappingIndex(Catalog);
        if (_learnModeActive)
            RefreshLearnRegistration(reason);
    }

    private IReadOnlyList<LearnWatchEntry> BuildCurrentLearnWatches()
    {
        IReadOnlyList<LearnWatchEntry>? profileWatches = null;
        try
        {
            var profile = LoadActiveProfileFromDisk();
            profileWatches = profile.LearnWatch;
        }
        catch
        {
            profileWatches = null;
        }

        return LearnWatchBuilder.Build(
            Catalog,
            _manualLearnWatches,
            profileWatches,
            _learnWatchlist);
    }

    private void RefreshLearnRegistration(string reason)
    {
        if (_sim is null)
            return;

        var watches = BuildCurrentLearnWatches();
        try
        {
            _sim.SetLearnWatchDefinitions(
                watches.Select(w => (w.Name, w.Units)).ToList());
        }
        catch (Exception ex)
        {
            Log.Warn($"[Learn] Re-register watches failed ({reason}): {ex.Message}");
        }

        _learnCapture.Start(watches);
        _learnModeActive = true;
        Log.Info($"[Learn] Watches re-registered ({reason}): {watches.Count} — baseline reset");
        LearnChanged?.Invoke();
    }

    private void SuppressLearnAfterActions(IReadOnlyList<ActionDefinition>? actions)
    {
        if (!_learnModeActive || actions is null || actions.Count == 0)
            return;

        _learnCapture.SuppressSignals(
            actions.Where(a => !string.IsNullOrWhiteSpace(a.Name)).Select(a => a.Name),
            HostConstants.LearnSuppressMs);
    }

    public void TestTts(string? text = null)
    {
        // Prefer a known pack sample so Wav/Hybrid exercise the selected voice pack when present.
        _tts?.Speak(
            text ?? "Co Pilot voice check.",
            Settings.Behavior.CalloutDelayMs,
            commandId: "gear_up",
            responseKind: TtsResponseKind.Success);
    }

    /// <summary>Voice pack folder names under extras/voices that contain manifest.json.</summary>
    public IReadOnlyList<string> ListAvailableVoicePacks()
    {
        try
        {
            var voicesRoot = Path.Combine(ConfigLoader.ResolveExtrasRoot(ConfigRoot), "voices");
            if (!Directory.Exists(voicesRoot))
                return Array.Empty<string>();

            return Directory.GetDirectories(voicesRoot)
                .Where(d => File.Exists(Path.Combine(d, "manifest.json")))
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }
        catch
        {
            return Array.Empty<string>();
        }
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
    /// if the profile differs from the last merge; speech grammar restarts only when grammar inputs change.
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
            var path = ConfigLoader.SettingsPath(ConfigRoot);
            ConfigLoader.SaveSettings(path, Settings);
            Log.Info($"[Settings] Saved to {path}");
        }
        else
        {
            Log.Info("[Settings] Applied in memory (not saved to disk).");
        }

        // Never call LoadAll() here — that would wipe in-memory Apply edits.
        // Re-merge catalog only when profile changed (or never merged yet).
        if (rebuildCatalog)
        {
            var profile = ActiveProfileName();
            if (!string.Equals(profile, _mergedCatalogProfile, StringComparison.OrdinalIgnoreCase))
                RebuildCatalogFromCurrentSettings();
            else
                Log.Info($"[Settings] Catalog unchanged (profile '{profile}').");
        }

        RefreshPipelineAndSpeechIfNeeded(reason: "Settings");

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
            RebuildPipelineServices(force: true);
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
        var path = ConfigLoader.BaseCommandsPath(ConfigRoot);
        return ConfigLoader.CloneCatalog(ConfigLoader.LoadBaseCommands(path));
    }

    /// <summary>Loads the active aircraft profile from disk (working copy for the Commands UI).</summary>
    public AircraftProfile LoadActiveProfileFromDisk()
    {
        var profileName = ActiveProfileName();
        var path = ConfigLoader.AircraftProfilePath(ConfigRoot, profileName);
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

        var profileName = ActiveProfileName();
        if (string.IsNullOrWhiteSpace(profile.ProfileId))
            profile.ProfileId = profileName;

        if (saveToDisk)
        {
            var basePath = ConfigLoader.BaseCommandsPath(ConfigRoot);
            ConfigLoader.SaveBaseCommands(basePath, baseCatalog);
            Log.Info($"[Commands] Saved {basePath} ({baseCatalog.Commands.Count} commands)");

            var profilePath = ConfigLoader.AircraftProfilePath(ConfigRoot, profileName);
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
        _mergedCatalogProfile = profileName;
        Log.Info($"[Config] Catalog rebuilt from UI sources: {Catalog.Commands.Count} commands (profile '{profileName}')");
        OnCatalogChangedForLearn("ApplyCommandSources");

        // Command edits always change the phrase set — force pipeline + speech refresh.
        RebuildPipelineServices(force: true);
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
        _identityPollTick = 0;

        // Ensure matcher/gate reflect current Settings before loading grammar
        if (_matcher is null || _gate is null)
            RebuildPipelineServices(force: true);

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
                win.Matcher = _matcher;
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

        StampActiveFingerprints();

        _pollTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                _pttArm?.Poll();
                _sim?.ReceiveMessage();
                var armed = _pttArm?.IsArmed == true || _pttArm?.IsKeyCurrentlyDown == true;
                MicActive = armed;

                // Learn capture: Observe is cheap (dict diffs); snapshot updates ~1 Hz (SECOND).
                if (_learnModeActive && _sim is not null)
                {
                    var index = _learnMappingIndex ??= new CommandMappingIndex(Catalog);
                    var added = _learnCapture.Observe(_sim.Snapshot, index);
                    if (added > 0)
                        LearnChanged?.Invoke();
                }

                // TITLE/ATC MODEL + flight snapshot arrive on SECOND period — evaluate ~2 Hz.
                if (_sim is not null && _sim.IsLive)
                {
                    if (++_identityPollTick >= IdentityPollEveryNTicks)
                    {
                        _identityPollTick = 0;
                        DetectedAirportIdent = (_sim.AirportIdent ?? "").Trim();
                        ProcessAircraftIdentity(_sim.AircraftTitle, _sim.AtcModel);
                        // Always refresh Status UI for altitude/speed/VS/airport while live.
                        StatusChanged?.Invoke();
                    }
                }
            }
            catch { /* ignore */ }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(PollIntervalMs));

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
        try
        {
            StopLearnMode();
        }
        catch
        {
            // best-effort
        }

        try
        {
            StopSpeechListeningForShutdown();
        }
        catch
        {
            // best-effort shutdown
        }

        try { _pttArm?.Dispose(); } catch { /* ignore */ }
        _pttArm = null;
        try { _tts?.Dispose(); } catch { /* ignore */ }
        _tts = null;
        try { _sim?.Dispose(); } catch { /* ignore */ }
        _sim = null;
        _executor = null;
        _processor = null;
        _matcher = null;
        _gate = null;
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
            speakWithDelay: (t, d, id, kind) => _tts.Speak(t, d, id, kind));

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

        SuppressLearnAfterActions(result.ActionsExecuted);

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
            if (TryOpenSimConnection())
                Log.Info($"[SimConnect] {_sim.StatusMessage}");
            else
                Log.Warn($"[SimConnect] Still not connected: {_sim.StatusMessage}");
        }
        catch (Exception ex)
        {
            LastSimError = ex.Message;
            Log.Error($"[SimConnect] Connect attempt failed: {ex.Message}");
        }

        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Shared SimConnect open used by reconnect and pre-action connect.
    /// Updates <see cref="LastSimError"/>; caller logs and raises StatusChanged as needed.
    /// </summary>
    private bool TryOpenSimConnection()
    {
        if (_sim is null)
            return false;

        if (_sim.Connect(Settings.SimConnect.AppName, Settings.SimConnect.ConfigIndex))
        {
            LastSimError = null;
            return true;
        }

        LastSimError = _sim.StatusMessage;
        return false;
    }

    private string ActiveProfileName() => HostConstants.NormalizeProfileId(Settings.AircraftProfile);

    private double SnapshotValue(string simVar)
    {
        if (_sim is null) return 0;
        return _sim.Snapshot.TryGet(simVar, out var v) ? v : 0;
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
        OnCatalogChangedForLearn("ReloadConfig");

        _detectionConfig = ConfigLoader.LoadAircraftDetection(ConfigRoot);
        _learnWatchlist = ConfigLoader.LoadLearnWatchlist(ConfigRoot);
        _lastDetectedIdentityKey = "";
        _lastSeenDisplayIdentityKey = null;

        Log.Info($"[Config] Commands loaded: {Catalog.Commands.Count}");
        Log.Info($"[Config] Aircraft profile: {Settings.AircraftProfile}");
        Log.Info(
            $"[Config] auto_detect_aircraft={Settings.AutoDetectAircraft} announce_profile_switch={Settings.AnnounceProfileSwitch}" +
            (CliProfileLocked ? " (CLI --profile lock)" : ""));
        Log.Info(
            $"[Config] Detection rules: {_detectionConfig.Rules.Count} fallback='{_detectionConfig.FallbackProfile}'");
        Log.Info(
            $"[Config] Learn watchlist: exclude={_learnWatchlist.ExcludeNames.Count} defaults={_learnWatchlist.DefaultWatches.Count}");
        Log.Info($"[Config] Wake word: '{Settings.Speech.WakeWord}' | PTT: {Settings.Speech.PttKey}");
        Log.Info($"[Config] continuous_listen={Settings.Speech.ContinuousListen} ptt_grace_ms={Settings.Speech.PttGraceMs}");

        if (reconnectSim || _sim is null)
            ConnectSimClient();

        _mergedCatalogProfile = ActiveProfileName();
        RebuildPipelineServices(force: true);

        if (restartSpeech && _speechListening)
        {
            StartSpeechListening();
        }
        else if (restartSpeech)
        {
            Log.Info("[Config] Speech restart requested but listening was not active.");
            StampActiveFingerprints();
        }
        else
        {
            StampActiveFingerprints();
        }

        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Re-merge base_commands + aircraft profile using <see cref="Settings.AircraftProfile"/>
    /// without reloading settings.json (preserves in-memory Apply edits).
    /// </summary>
    public void RebuildCatalogFromCurrentSettings()
    {
        var basePath = ConfigLoader.BaseCommandsPath(ConfigRoot);
        var baseCatalog = ConfigLoader.LoadBaseCommands(basePath);
        var profileName = ActiveProfileName();
        var profilePath = ConfigLoader.AircraftProfilePath(ConfigRoot, profileName);
        AircraftProfile profile;
        if (File.Exists(profilePath))
            profile = ConfigLoader.LoadAircraftProfile(profilePath);
        else
            profile = new AircraftProfile { ProfileId = profileName, Title = "missing profile" };

        Catalog = ConfigLoader.Merge(baseCatalog, profile);
        CatalogRebuildCount++;
        _mergedCatalogProfile = profileName;
        Log.Info($"[Config] Catalog rebuilt for profile '{profileName}': {Catalog.Commands.Count} commands");
        OnCatalogChangedForLearn("RebuildCatalog");
    }

    private string EnsureProfileExists(string profileName)
    {
        var name = HostConstants.NormalizeProfileId(profileName);
        var path = ConfigLoader.AircraftProfilePath(ConfigRoot, name);
        if (File.Exists(path))
            return name;

        var fallback = HostConstants.NormalizeProfileId(_detectionConfig.FallbackProfile);
        Log.Warn($"[AircraftDetect] Profile '{name}' not found — using fallback '{fallback}'");
        return fallback;
    }

    /// <summary>
    /// Rebuild pipeline and/or restart speech only when fingerprints change.
    /// Grammar restart is required for wake word, culture/engine, profile/phrase-set changes —
    /// not for pure TTS/behavior/PTT-grace tweaks (pipeline-only).
    /// </summary>
    private void RefreshPipelineAndSpeechIfNeeded(string reason)
    {
        var speechKey = ComputeSpeechGrammarKey();
        var pipelineKey = ComputePipelineServicesKey(speechKey);
        var pipelineChanged = !string.Equals(pipelineKey, _activePipelineServicesKey, StringComparison.Ordinal);
        var speechChanged = !string.Equals(speechKey, _activeSpeechGrammarKey, StringComparison.Ordinal);

        if (pipelineChanged)
            RebuildPipelineServices(force: true);
        else
            Log.Info($"[{reason}] Pipeline services unchanged — skip rebuild.");

        if (_speechListening && speechChanged)
        {
            StartSpeechListening();
        }
        else if (_speechListening)
        {
            Log.Info($"[{reason}] Speech grammar unchanged — skip restart (#{SpeechStartCount}).");
            if (pipelineChanged)
                StampActiveFingerprints();
        }
        else
        {
            if (pipelineChanged)
                Log.Info($"[{reason}] Pipeline rebuilt (speech not listening yet).");
            StampActiveFingerprints();
        }
    }

    private void RebuildPipelineServices(bool force = false)
    {
        if (_sim is null)
            throw new InvalidOperationException("SimConnect client not initialized");

        if (!force)
        {
            var speechKey = ComputeSpeechGrammarKey();
            var pipelineKey = ComputePipelineServicesKey(speechKey);
            if (string.Equals(pipelineKey, _activePipelineServicesKey, StringComparison.Ordinal)
                && _matcher is not null && _gate is not null && _pttArm is not null && _tts is not null)
            {
                return;
            }
        }

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
        // No private PttArm timer: HostSession poll (when listening) and HandlePhrase call Poll().
        // Avoids a second 50 ms timer on inject/--once/offline paths.
        _pttArm = new PttArmService(Settings.Speech.PttKey, Settings.Speech.PttGraceMs);
        if (_options.SimulatePtt)
            _pttArm.ForceArm(60_000);

        _tts?.Dispose();
        _tts = _options.NoTts
            ? new ConsoleTtsService()
            : CreateTts(Settings.Tts);
        _tts.ApplySettings(Settings.Tts);

        StampActiveFingerprints();
    }

    private void StampActiveFingerprints()
    {
        _mergedCatalogProfile = ActiveProfileName();
        var speechKey = ComputeSpeechGrammarKey();
        _activeSpeechGrammarKey = speechKey;
        _activePipelineServicesKey = ComputePipelineServicesKey(speechKey);
    }

    /// <summary>
    /// Inputs that require reloading System.Speech grammar (wake prefixes + phrase set + culture).
    /// </summary>
    private string ComputeSpeechGrammarKey()
    {
        var sb = new StringBuilder(256);
        sb.Append(Settings.Speech.WakeWord ?? "").Append('\u001f');
        sb.Append(Settings.Speech.Culture ?? "").Append('\u001f');
        sb.Append(Settings.Speech.Engine ?? "").Append('\u001f');
        sb.Append(ActiveProfileName()).Append('\u001f');
        sb.Append(CatalogFingerprint());
        return sb.ToString();
    }

    /// <summary>Pipeline services fingerprint (includes speech key + PTT/TTS/behavior).</summary>
    private string ComputePipelineServicesKey(string? speechKey = null)
    {
        speechKey ??= ComputeSpeechGrammarKey();
        var sb = new StringBuilder(speechKey.Length + 128);
        sb.Append(speechKey).Append('\u001f');
        sb.Append(Settings.Speech.PttKey ?? "").Append('\u001f');
        sb.Append(Settings.Speech.PttGraceMs).Append('\u001f');
        sb.Append(Settings.Speech.ContinuousListen).Append('\u001f');
        sb.Append(Settings.Speech.ConfidenceThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append('\u001f');
        sb.Append(Settings.Tts.Engine ?? "").Append('\u001f');
        sb.Append(Settings.Tts.Voice ?? "").Append('\u001f');
        sb.Append(Settings.Tts.Rate).Append('\u001f');
        sb.Append(Settings.Tts.Volume).Append('\u001f');
        sb.Append(Settings.Tts.VoicePack ?? "").Append('\u001f');
        sb.Append(Settings.Behavior.RequirePositiveClimbForGearUp).Append('\u001f');
        sb.Append(Settings.Behavior.ConfirmBeforeAction).Append('\u001f');
        sb.Append(Settings.Behavior.CalloutDelayMs);
        return sb.ToString();
    }

    private string CatalogFingerprint()
    {
        var cmds = Catalog.Commands;
        var sb = new StringBuilder(cmds.Count * 24);
        sb.Append(cmds.Count);
        foreach (var c in cmds.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('|').Append(c.Id).Append(':').Append(c.Phrases.Count);
            if (c.Phrases.Count > 0)
                sb.Append(':').Append(c.Phrases[0]);
        }

        return sb.ToString();
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
            SeedOfflineSnapshot(recording);
    }

    private void SeedOfflineSnapshot(RecordingSimConnectClient recording)
    {
        var vs = _options.FixtureVerticalSpeed ?? HostConstants.DefaultOfflineVerticalSpeedFpm;
        recording.Snapshot.Set("GEAR POSITION", 1);
        recording.Snapshot.Set(HostConstants.VerticalSpeedSimVar, vs);
        // Airborne default so fenix gear_up (SIM ON GROUND == 0) works offline with inject/--vs.
        recording.Snapshot.Set("SIM ON GROUND", 0);
        recording.Snapshot.Set("FLAPS HANDLE INDEX", 0);
        recording.Snapshot.Set("AUTOPILOT MASTER", 0);
        recording.Snapshot.Set("LIGHT LANDING", 0);
        recording.Snapshot.Set("BRAKE PARKING POSITION", 0);
        recording.Snapshot.Set("ENG ANTI ICE", 0);
        Log.Info($"[SimConnect] Offline snapshot seeded (VS={vs} fpm, SIM ON GROUND=0).");
    }

    private static void CopyEditableSettings(AppSettings from, AppSettings to)
    {
        to.Speech.WakeWord = from.Speech.WakeWord;
        to.Speech.PttKey = from.Speech.PttKey;
        to.Speech.ConfidenceThreshold = from.Speech.ConfidenceThreshold;
        to.Speech.ContinuousListen = from.Speech.ContinuousListen;
        to.Speech.PttGraceMs = from.Speech.PttGraceMs;
        to.Tts.Engine = from.Tts.Engine;
        to.Tts.Voice = from.Tts.Voice;
        to.Tts.Rate = from.Tts.Rate;
        to.Tts.Volume = from.Tts.Volume;
        to.Tts.VoicePack = from.Tts.VoicePack;
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
                var manifest = Path.Combine(dir.FullName, HostConstants.ManifestFileName);
                if (!File.Exists(manifest))
                    manifest = Path.Combine(
                        dir.FullName, "Packages", HostConstants.PackageName, HostConstants.ManifestFileName);
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

    private ITtsService CreateTts(TtsSettings settings)
    {
        var engine = NormalizeTtsEngine(settings.Engine);
        var extrasRoot = ConfigLoader.ResolveExtrasRoot(ConfigRoot);
        var pack = string.IsNullOrWhiteSpace(settings.VoicePack)
            ? "austrian_airlines_en_us"
            : settings.VoicePack.Trim();

        try
        {
            if (string.Equals(engine, TtsEngineKind.Wav, StringComparison.OrdinalIgnoreCase))
            {
                var wav = new WavTtsService(extrasRoot, pack);
                wav.ApplySettings(settings);
                Log.Info($"[TTS] engine=Wav voice_pack={pack}");
                return wav;
            }

            if (string.Equals(engine, TtsEngineKind.Hybrid, StringComparison.OrdinalIgnoreCase))
            {
                var wav = new WavTtsService(extrasRoot, pack);
                ITtsService windows;
                try
                {
                    windows = new WindowsTtsService();
                    windows.ApplySettings(settings);
                }
                catch
                {
                    windows = new ConsoleTtsService();
                }

                var hybrid = new HybridTtsService(wav, windows, ownsFallback: true);
                hybrid.ApplySettings(settings);
                Log.Info($"[TTS] engine=Hybrid voice_pack={pack}");
                return hybrid;
            }

            // Windows (default / unknown)
            if (!string.Equals(engine, TtsEngineKind.Windows, StringComparison.OrdinalIgnoreCase))
                Log.Warn($"[TTS] Unknown engine '{settings.Engine}' — using Windows.");

            var tts = new WindowsTtsService();
            tts.ApplySettings(settings);
            Log.Info("[TTS] engine=Windows");
            return tts;
        }
        catch (Exception ex)
        {
            Log.Warn($"[TTS] CreateTts failed ({ex.Message}) — console fallback.");
            return new ConsoleTtsService();
        }
    }

    private static string NormalizeTtsEngine(string? engine)
    {
        if (string.IsNullOrWhiteSpace(engine))
            return TtsEngineKind.Hybrid;
        return engine.Trim();
    }
}
