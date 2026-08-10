using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Diagnostics;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CoPilotVoiceHost.Ui;

public partial class MainWindow : Window
{
    /// <summary>Matches <see cref="UiLogSink.DefaultMaxBuffered"/> so the Debug TextBox cannot grow without bound.</summary>
    private const int MaxUiLogLines = UiLogSink.DefaultMaxBuffered;

    private readonly HostSession _session;
    private readonly Forms.NotifyIcon _tray;
    private bool _exitRequested;
    private bool _suppressContinuousEvent;
    private int _uiLogLineCount;
    private bool _logTrimScheduled;

    // Commands tab working copies (base + active profile); not WPF-bound to Core.
    private CommandCatalog _editBase = new();
    private AircraftProfile _editProfile = new();
    private readonly ObservableCollection<CommandRow> _commandRows = new();
    private CommandRow? _selectedRow;
    private bool _commandsLoaded;
    private bool _suppressCmdSelection;
    private bool _cmdDirty;
    private bool _manualBuilt;
    private int _manualCatalogFingerprint = -1;

    // Learn tab
    private readonly ObservableCollection<LearnRow> _learnRows = new();
    private bool _suppressLearnActiveEvent;
    private LearnDetection? _selectedLearnDetection;
    private List<ActionDefinition> _learnEditActions = new();
    private bool _learnEditIsEdit;

    public MainWindow(HostSession session, HostOptions _)
    {
        _session = session;
        InitializeComponent();

        CmdList.ItemsSource = _commandRows;
        LearnList.ItemsSource = _learnRows;

        _tray = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "CoPilot Voice Host",
            Icon = Drawing.SystemIcons.Application
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowFromTray());
        menu.Items.Add("Hide", null, (_, _) => Hide());
        // BeginInvoke: reconnect must not block the tray UI thread.
        menu.Items.Add("Reconnect", null, (_, _) => Dispatcher.BeginInvoke(() => _session.ForceReconnect()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();

        _session.Log.LineAppended += OnLogLine;
        _session.StatusChanged += () => Dispatcher.BeginInvoke(RefreshStatus);
        _session.LearnChanged += () => Dispatcher.BeginInvoke(RefreshLearnUi);

        LoadSettingsToUi();
        foreach (var line in _session.Log.Snapshot())
            AppendLogCore(line);

        RefreshStatus();
        RefreshLearnUi();
        StatusBarVersion.Text = $"App {_session.ApplicationVersion} · pkg {_session.PackageVersion}";
    }

    private void OnLogLine(LogEntry entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnLogLine(entry));
            return;
        }

        AppendLog(entry);
    }

    private void AppendLog(LogEntry entry)
    {
        AppendLogCore(entry);
        if (_uiLogLineCount > MaxUiLogLines && !_logTrimScheduled)
        {
            _logTrimScheduled = true;
            // Coalesce trims on the dispatcher so a log flood does not rebuild the TextBox every line.
            Dispatcher.BeginInvoke(TrimLogViewToSink, System.Windows.Threading.DispatcherPriority.Background);
        }
        else
        {
            LogView.ScrollToEnd();
        }
    }

    private void AppendLogCore(LogEntry entry)
    {
        var local = entry.Utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var level = entry.Level switch
        {
            LogLevel.Warn => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Debug => "DBG",
            _ => "INF"
        };
        LogView.AppendText($"[{local} {level}] {entry.Message}{Environment.NewLine}");
        _uiLogLineCount++;
    }

    /// <summary>Rebuild Debug log from the bounded sink snapshot (drops UI-only excess).</summary>
    private void TrimLogViewToSink()
    {
        _logTrimScheduled = false;
        var snap = _session.Log.Snapshot();
        LogView.Clear();
        _uiLogLineCount = 0;
        foreach (var e in snap)
            AppendLogCore(e);
        LogView.ScrollToEnd();
    }

    private void RefreshStatus()
    {
        var live = _session.IsLive;
        var connected = _session.IsConnected;
        Title = live ? "CoPilot Voice Host – Live" : "CoPilot Voice Host – Offline";
        HeaderMode.Text = live ? "Mode: Live" : "Mode: Offline";

        ConnDot.Fill = new SolidColorBrush(connected
            ? (live ? MediaColor.FromRgb(0x2E, 0xCC, 0x71) : MediaColor.FromRgb(0xF3, 0x9C, 0x12))
            : MediaColor.FromRgb(0xE7, 0x4C, 0x3C));
        StatusBarText.Text = connected
            ? (live ? "Connected · Live" : "Connected · Offline recording")
            : "Disconnected · Offline";

        TxtConnected.Text = connected ? "Connected" : "Disconnected";
        TxtLive.Text = live ? "True" : "False";
        TxtSimMessage.Text = _session.SimStatusMessage;
        TxtSimError.Text = string.IsNullOrWhiteSpace(_session.LastSimError) ? "—" : _session.LastSimError;
        TxtDetectedAircraft.Text = string.IsNullOrWhiteSpace(_session.DetectedAircraftTitle)
            ? "Unknown"
            : _session.DetectedAircraftTitle;
        if (!string.IsNullOrWhiteSpace(_session.DetectedAtcModel))
            TxtDetectedAircraft.Text += $" · ATC {_session.DetectedAtcModel}";

        // Live flight telemetry (updated ~2 Hz while SimConnect is live).
        TxtAirport.Text = string.IsNullOrWhiteSpace(_session.DetectedAirportIdent)
            ? "—"
            : _session.DetectedAirportIdent;
        if (live && _session.HasReceivedSimData)
        {
            TxtAltitude.Text = $"{_session.PlaneAltitudeFeet:F0} ft";
            TxtAirspeed.Text = $"{_session.IndicatedAirspeedKnots:F0} kt";
            var vs = _session.VerticalSpeedFpm;
            TxtVerticalSpeed.Text = $"{vs:+0;-0;0} fpm";
            TxtOnGround.Text = _session.SimOnGround ? "Yes" : "No (airborne)";
            TxtSimData.Text = "Receiving";
        }
        else if (live)
        {
            TxtAltitude.Text = "…";
            TxtAirspeed.Text = "…";
            TxtVerticalSpeed.Text = "…";
            TxtOnGround.Text = "…";
            TxtSimData.Text = "Waiting for SimConnect data…";
        }
        else
        {
            TxtAltitude.Text = "—";
            TxtAirspeed.Text = "—";
            TxtVerticalSpeed.Text = "—";
            TxtOnGround.Text = "—";
            TxtSimData.Text = "Offline";
        }

        var activeProfile = _session.AircraftProfile;
        TxtProfile.Text = activeProfile;
        // Keep Settings combo in sync with auto-detect / session profile so Apply cannot
        // clobber a newly auto-selected profile with a stale SetProfile.Text.
        if (!string.Equals(SetProfile.Text, activeProfile, StringComparison.OrdinalIgnoreCase))
        {
            if (!SetProfile.Items.Contains(activeProfile) && !string.IsNullOrWhiteSpace(activeProfile))
                SetProfile.Items.Add(activeProfile);
            SetProfile.Text = activeProfile;
            if (_commandsLoaded)
                ReloadCommandsEditorFromDisk(keepSelectionId: _selectedRow?.Id);
        }

        TxtLastPhrase.Text = string.IsNullOrWhiteSpace(_session.LastPhrase) ? "—" : _session.LastPhrase;
        TxtConfidence.Text = _session.LastConfidence > 0 ? _session.LastConfidence.ToString("F2", CultureInfo.InvariantCulture) : "—";
        TxtLastAction.Text = string.IsNullOrWhiteSpace(_session.LastAction) ? "—" : _session.LastAction;
        MicDot.Fill = new SolidColorBrush(_session.MicActive
            ? MediaColor.FromRgb(0x2E, 0xCC, 0x71)
            : MediaColor.FromRgb(0x3A, 0x42, 0x50));
        TxtMic.Text = _session.MicActive ? "Active (PTT/arm)" : "Idle";
        TxtVersions.Text = $"Application {_session.ApplicationVersion} · Package {_session.PackageVersion}";

        _suppressContinuousEvent = true;
        DbgContinuous.IsChecked = _session.Settings.Speech.ContinuousListen;
        SetContinuous.IsChecked = _session.Settings.Speech.ContinuousListen;
        SetAutoDetect.IsChecked = _session.Settings.AutoDetectAircraft;
        SetAnnounceProfile.IsChecked = _session.Settings.AnnounceProfileSwitch;
        _suppressContinuousEvent = false;

        if (_commandsLoaded)
            UpdateCmdStatusBar();

        // Keep Manual tab buttons aligned with live catalog after profile auto-switch / Apply.
        if (_manualBuilt && ManualCatalogFingerprint() != _manualCatalogFingerprint)
            RebuildManualPanel();

        // Learn toggle enabled only when Live; keep checkbox in sync without re-entrancy.
        LearnActiveCheck.IsEnabled = live || _session.LearnModeActive;
        _suppressLearnActiveEvent = true;
        LearnActiveCheck.IsChecked = _session.LearnModeActive;
        _suppressLearnActiveEvent = false;
        UpdateLearnMeta();
    }

    // ── Learn tab ────────────────────────────────────────────────────────────

    private void LearnTab_GotFocus(object sender, RoutedEventArgs e) => RefreshLearnUi();

    private void RefreshLearnUi()
    {
        UpdateLearnMeta();

        var selectedSignal = (_selectedLearnDetection?.SignalName ?? "");
        var selectedUtc = _selectedLearnDetection?.Utc;
        _learnRows.Clear();

        IEnumerable<LearnDetection> source = _session.LearnDetections;
        if (LearnOnlyUnmapped.IsChecked == true)
            source = source.Where(d => d.MappingStatus == LearnMappingStatus.Unmapped);

        LearnRow? reselect = null;
        foreach (var d in source)
        {
            var row = new LearnRow(d);
            _learnRows.Add(row);
            if (selectedUtc.HasValue
                && d.Utc == selectedUtc
                && d.SignalName.Equals(selectedSignal, StringComparison.OrdinalIgnoreCase))
                reselect = row;
        }

        if (reselect is not null)
            LearnList.SelectedItem = reselect;
        else if (_learnRows.Count == 0)
        {
            _selectedLearnDetection = null;
            LearnDetail.Text = "Select a detection.";
            BtnLearnEdit.IsEnabled = false;
        }

        LearnStatus.Text = _session.LearnModeActive
            ? $"Listening for changes · {_session.LearnWatchCount} watches · {_session.LearnDetections.Count} detections"
            : "Learn Mode off — enable when SimConnect is Live.";
    }

    private void UpdateLearnMeta()
    {
        var live = _session.IsLive ? "Yes" : "No";
        LearnMeta.Text =
            $"Watches: {_session.LearnWatchCount} · Profile: {_session.AircraftProfile} · Live: {live}";
    }

    private void LearnActive_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressLearnActiveEvent)
            return;

        try
        {
            if (LearnActiveCheck.IsChecked == true)
            {
                if (!_session.IsLive)
                {
                    System.Windows.MessageBox.Show(
                        this,
                        "Learn Mode requires a Live SimConnect connection (Free Flight + MSFS SimConnect).",
                        "Learn Mode",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    _suppressLearnActiveEvent = true;
                    LearnActiveCheck.IsChecked = false;
                    _suppressLearnActiveEvent = false;
                    return;
                }

                _session.StartLearnMode();
            }
            else
            {
                _session.StopLearnMode();
            }
        }
        catch (Exception ex)
        {
            LearnEditStatus.Text = ex.Message;
        }

        RefreshLearnUi();
    }

    private void BtnLearnAddWatch_Click(object sender, RoutedEventArgs e)
    {
        var name = LearnWatchName.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            LearnStatus.Text = "Enter a SimVar / LVar name to watch.";
            return;
        }

        var units = LearnWatchUnits.Text?.Trim();
        if (string.IsNullOrWhiteSpace(units))
            units = "number";
        _session.AddManualLearnWatch(name, units);
        LearnWatchName.Text = "";
        RefreshLearnUi();
    }

    private void BtnLearnClear_Click(object sender, RoutedEventArgs e)
    {
        _session.ClearLearnDetections();
        _selectedLearnDetection = null;
        LearnDetail.Text = "Select a detection.";
        BtnLearnEdit.IsEnabled = false;
        RefreshLearnUi();
    }

    private void BtnLearnExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _session.ExportLearnDetections();
            LearnEditStatus.Text = $"Exported: {path}";
            LearnStatus.Text = $"Exported {_session.LearnDetections.Count} detections.";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Learn Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LearnFilter_Changed(object sender, RoutedEventArgs e) => RefreshLearnUi();

    private void LearnList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LearnList.SelectedItem is not LearnRow row)
        {
            _selectedLearnDetection = null;
            LearnDetail.Text = "Select a detection.";
            BtnLearnEdit.IsEnabled = false;
            return;
        }

        _selectedLearnDetection = row.Detection;
        var d = row.Detection;
        var matched = d.MatchedCommandLabels.Count > 0
            ? string.Join(", ", d.MatchedCommandLabels)
            : "—";
        var actions = d.SuggestedActions.Count > 0
            ? string.Join("; ", d.SuggestedActions.Select(a =>
                $"{a.Type}:{a.Name}={a.Value?.ToString(CultureInfo.InvariantCulture)} {a.Units}"))
            : "—";

        LearnDetail.Text =
            $"Signal: {d.SignalName} ({d.Kind})\n" +
            $"Change: {FormatLearnValue(d.OldValue)} → {FormatLearnValue(d.NewValue)} ({d.Units})\n" +
            $"Status: {d.MappingStatus}\n" +
            $"Matched: {matched}\n" +
            $"Suggested id: {d.SuggestedCommandId}\n" +
            $"Suggested actions: {actions}\n" +
            (string.IsNullOrEmpty(d.GroupId) ? "" : $"Group: {d.GroupId}\n") +
            $"UTC: {d.Utc:HH:mm:ss}";

        BtnLearnEdit.IsEnabled = d.MappingStatus is LearnMappingStatus.Mapped or LearnMappingStatus.Ambiguous
            && d.MatchedCommandIds.Count > 0;
    }

    private void BtnLearnCreate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLearnDetection is null)
        {
            LearnEditStatus.Text = "Select a detection first.";
            return;
        }

        var d = _selectedLearnDetection;
        _learnEditIsEdit = false;
        LearnEditId.Text = d.SuggestedCommandId ?? LearnCaptureService.SuggestCommandId(d.SignalName, d.NewValue ?? 0);
        LearnEditPhrases.Text = "";
        LearnEditResponse.Text = "Checked.";
        LearnEditReject.Text = "";
        _learnEditActions = d.SuggestedActions.Select(CloneAction).ToList();
        LearnEditActions.Text = FormatLearnActionsSummary(_learnEditActions);
        LearnEditStatus.Text = "Create mode — enter phrases, then Save to active profile.";
    }

    private void BtnLearnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLearnDetection is null || _selectedLearnDetection.MatchedCommandIds.Count == 0)
        {
            LearnEditStatus.Text = "No mapped command to edit.";
            return;
        }

        var id = _selectedLearnDetection.MatchedCommandIds[0];
        var cmd = _session.Catalog.Commands.FirstOrDefault(c =>
            c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (cmd is null)
        {
            LearnEditStatus.Text = $"Command '{id}' not in live catalog.";
            return;
        }

        _learnEditIsEdit = true;
        LearnEditId.Text = cmd.Id;
        LearnEditPhrases.Text = string.Join(Environment.NewLine, cmd.Phrases);
        LearnEditResponse.Text = cmd.Response ?? "";
        LearnEditReject.Text = cmd.RejectResponse ?? "";
        _learnEditActions = cmd.Actions.Select(CloneAction).ToList();
        LearnEditActions.Text = FormatLearnActionsSummary(_learnEditActions);
        LearnEditStatus.Text = $"Edit mode — will write profile override for '{cmd.Id}'.";
    }

    private void BtnLearnCopyName_Click(object sender, RoutedEventArgs e)
    {
        var name = _selectedLearnDetection?.SignalName;
        if (string.IsNullOrWhiteSpace(name))
        {
            LearnEditStatus.Text = "Nothing to copy.";
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(name);
            LearnEditStatus.Text = $"Copied: {name}";
        }
        catch (Exception ex)
        {
            LearnEditStatus.Text = $"Clipboard failed: {ex.Message}";
        }
    }

    private void BtnLearnCancelEdit_Click(object sender, RoutedEventArgs e)
    {
        LearnEditId.Text = "";
        LearnEditPhrases.Text = "";
        LearnEditResponse.Text = "";
        LearnEditReject.Text = "";
        LearnEditActions.Text = "";
        _learnEditActions = new();
        _learnEditIsEdit = false;
        LearnEditStatus.Text = "Cancelled.";
    }

    private void BtnLearnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var id = LearnEditId.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(id))
            {
                LearnEditStatus.Text = "Command id is required.";
                return;
            }

            var phrases = ParseLines(LearnEditPhrases.Text);
            if (phrases.Count == 0)
            {
                LearnEditStatus.Text = "At least one phrase is required.";
                return;
            }

            if (_learnEditActions.Count == 0)
            {
                LearnEditStatus.Text = "No actions — create from a detection first.";
                return;
            }

            // Base unchanged; vendor LVars go into active profile only.
            var baseCatalog = _session.LoadBaseCommandsFromDisk();
            var profile = _session.LoadActiveProfileFromDisk();

            var cmd = new CommandDefinition
            {
                Id = id,
                Phrases = phrases,
                Response = LearnEditResponse.Text?.Trim() ?? "",
                RejectResponse = string.IsNullOrWhiteSpace(LearnEditReject.Text)
                    ? null
                    : LearnEditReject.Text.Trim(),
                Conditions = new List<ConditionDefinition>(),
                Actions = _learnEditActions.Select(CloneAction).ToList()
            };

            var existing = profile.Commands.FindIndex(c =>
                c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                profile.Commands[existing] = cmd;
            else
                profile.Commands.Add(cmd);

            _session.ApplyCommandSources(baseCatalog, profile, saveToDisk: true);

            if (_commandsLoaded)
                ReloadCommandsEditorFromDisk(keepSelectionId: id);

            LearnEditStatus.Text = _learnEditIsEdit
                ? $"Saved profile override '{id}'."
                : $"Created profile command '{id}'.";
            _learnEditIsEdit = false;
            RefreshLearnUi();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Learn Save", MessageBoxButton.OK, MessageBoxImage.Error);
            LearnEditStatus.Text = ex.Message;
        }
    }

    private static string FormatLearnValue(double? v) =>
        v.HasValue ? v.Value.ToString("0.####", CultureInfo.InvariantCulture) : "?";

    private static string FormatLearnActionsSummary(IEnumerable<ActionDefinition> actions) =>
        string.Join("; ", actions.Select(a =>
        {
            var val = a.Value.HasValue
                ? a.Value.Value.ToString(CultureInfo.InvariantCulture)
                : "";
            return string.IsNullOrEmpty(val)
                ? $"{a.Type}:{a.Name}"
                : $"{a.Type}:{a.Name}={val} {a.Units}";
        }));

    private static ActionDefinition CloneAction(ActionDefinition a) => new()
    {
        Type = a.Type,
        Name = a.Name,
        Value = a.Value,
        Units = a.Units
    };

    private sealed class LearnRow
    {
        public LearnRow(LearnDetection detection) => Detection = detection;

        public LearnDetection Detection { get; }
        public string SignalName => Detection.SignalName;
        public string TimeText => Detection.Utc.ToLocalTime().ToString("HH:mm:ss");
        public string ChangeText =>
            $"{FormatLearnValue(Detection.OldValue)} → {FormatLearnValue(Detection.NewValue)}";
        public string StatusText => Detection.MappingStatus.ToString();
        public string MappedText =>
            Detection.MatchedCommandIds.Count > 0
                ? string.Join(", ", Detection.MatchedCommandIds)
                : "—";
    }

    private void ManualTab_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_manualBuilt || ManualCatalogFingerprint() != _manualCatalogFingerprint)
            RebuildManualPanel();
    }

    private void BtnManualRefresh_Click(object sender, RoutedEventArgs e) => RebuildManualPanel();

    private int ManualCatalogFingerprint() =>
        HashCode.Combine(
            _session.AircraftProfile?.ToLowerInvariant() ?? "",
            _session.Catalog.Commands.Count,
            _session.CatalogRebuildCount);

    private void RebuildManualPanel()
    {
        ManualPanel.Children.Clear();
        var groups = CommandCatalogGroups.Group(_session.Catalog.Commands);
        var total = 0;

        foreach (var (category, commands) in groups)
        {
            var header = new TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                Margin = new Thickness(0, 4, 0, 8)
            };
            ManualPanel.Children.Add(header);

            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var cmd in commands)
            {
                total++;
                var label = CommandCatalogGroups.DisplayLabel(cmd);
                var tip = new StringBuilder();
                tip.AppendLine($"id: {cmd.Id}");
                if (cmd.Phrases.Count > 0)
                    tip.AppendLine("phrases: " + string.Join(", ", cmd.Phrases.Take(4)));
                if (cmd.Actions.Count == 0)
                    tip.Append("info / no sim events");
                else
                    tip.Append("actions: " + string.Join(", ", cmd.Actions.Select(a => a.Name).Take(4)));

                var btn = new System.Windows.Controls.Button
                {
                    Content = label,
                    ToolTip = tip.ToString().TrimEnd(),
                    Tag = cmd.Id,
                    Style = (Style)FindResource("ChipButton")
                };
                btn.Click += ManualCommand_Click;
                wrap.Children.Add(btn);
            }

            ManualPanel.Children.Add(wrap);
        }

        if (total == 0)
        {
            ManualPanel.Children.Add(new TextBlock
            {
                Text = "No commands loaded.",
                Style = (Style)FindResource("LabelMuted")
            });
        }

        ManualHint.Text =
            $"Active profile: {_session.AircraftProfile} · {total} commands (wake/PTT bypassed; conditions still apply).";
        ManualStatus.Text = $"Ready · {total} buttons";
        _manualBuilt = true;
        _manualCatalogFingerprint = ManualCatalogFingerprint();
    }

    private void ManualCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string id } || string.IsNullOrWhiteSpace(id))
            return;

        try
        {
            ManualStatus.Text = $"Running {id}…";
            var code = _session.RunCatalogCommand(id);
            ManualStatus.Text = code switch
            {
                0 => $"OK · {id} · {_session.LastAction}",
                4 => $"Denied · {id} · {_session.LastAction}",
                3 => $"Unrecognized · {id}",
                5 => $"Gate rejected · {id}",
                _ => $"Exit {code} · {id} · {_session.LastAction}"
            };
            RefreshStatus();
        }
        catch (Exception ex)
        {
            ManualStatus.Text = $"Error · {id}: {ex.Message}";
        }
    }

    private void LoadSettingsToUi()
    {
        var s = _session.Settings;
        SetWakeWord.Text = s.Speech.WakeWord;
        SetPttKey.Text = s.Speech.PttKey;
        SetConfidence.Text = s.Speech.ConfidenceThreshold.ToString(CultureInfo.InvariantCulture);
        SetPttGrace.Text = s.Speech.PttGraceMs.ToString(CultureInfo.InvariantCulture);
        SetContinuous.IsChecked = s.Speech.ContinuousListen;
        SetTtsEngine.Items.Clear();
        foreach (var eng in new[] { TtsEngineKind.Hybrid, TtsEngineKind.Wav, TtsEngineKind.Windows })
            SetTtsEngine.Items.Add(eng);
        var engine = string.IsNullOrWhiteSpace(s.Tts.Engine) ? TtsEngineKind.Hybrid : s.Tts.Engine.Trim();
        if (!SetTtsEngine.Items.Contains(engine))
            SetTtsEngine.Items.Add(engine);
        SetTtsEngine.SelectedItem = engine;
        SetTtsVoicePack.Items.Clear();
        foreach (var pack in _session.ListAvailableVoicePacks())
            SetTtsVoicePack.Items.Add(pack);
        // Always offer known sample packs even if scan is empty (e.g. wrong cwd)
        foreach (var known in new[] { "austrian_airlines_en_us", "lufthansa_en_us", "copilot_en_us" })
        {
            if (!SetTtsVoicePack.Items.Contains(known))
                SetTtsVoicePack.Items.Add(known);
        }
        SetTtsVoicePack.Text = string.IsNullOrWhiteSpace(s.Tts.VoicePack)
            ? "austrian_airlines_en_us"
            : s.Tts.VoicePack;
        SetTtsVoice.Text = s.Tts.Voice;
        SetPositiveClimb.IsChecked = s.Behavior.RequirePositiveClimbForGearUp;
        SetAutoDetect.IsChecked = s.AutoDetectAircraft;
        SetAnnounceProfile.IsChecked = s.AnnounceProfileSwitch;
        SetProfile.Items.Clear();
        foreach (var p in _session.ListProfiles())
            SetProfile.Items.Add(p);
        SetProfile.Text = s.AircraftProfile;
        DbgContinuous.IsChecked = s.Speech.ContinuousListen;
    }

    /// <summary>
    /// Build a detached settings snapshot from UI controls — does NOT mutate live session.Settings
    /// so ApplySettingsFromUi can detect false→true auto-detect transitions.
    /// </summary>
    private AppSettings ReadSettingsFromUi()
    {
        var cur = _session.Settings;
        var conf = cur.Speech.ConfidenceThreshold;
        if (double.TryParse(SetConfidence.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var confParsed))
            conf = Math.Clamp(confParsed, 0, 1);
        var grace = cur.Speech.PttGraceMs;
        if (int.TryParse(SetPttGrace.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var graceParsed))
            grace = Math.Clamp(graceParsed, 0, 30_000);

        return new AppSettings
        {
            SimConnect = cur.SimConnect,
            Speech = new SpeechSettings
            {
                Engine = cur.Speech.Engine,
                Culture = cur.Speech.Culture,
                WakeWord = SetWakeWord.Text.Trim(),
                PttKey = SetPttKey.Text.Trim(),
                ContinuousListen = SetContinuous.IsChecked == true,
                ConfidenceThreshold = conf,
                PttGraceMs = grace
            },
            Tts = new TtsSettings
            {
                Engine = (SetTtsEngine.SelectedItem as string)
                         ?? SetTtsEngine.Text?.Trim()
                         ?? cur.Tts.Engine
                         ?? TtsEngineKind.Hybrid,
                Voice = SetTtsVoice.Text.Trim(),
                Rate = cur.Tts.Rate,
                Volume = cur.Tts.Volume,
                VoicePack = string.IsNullOrWhiteSpace(SetTtsVoicePack.Text)
                    ? cur.Tts.VoicePack
                    : SetTtsVoicePack.Text.Trim()
            },
            Behavior = new BehaviorSettings
            {
                ConfirmBeforeAction = cur.Behavior.ConfirmBeforeAction,
                RequirePositiveClimbForGearUp = SetPositiveClimb.IsChecked == true,
                CalloutDelayMs = cur.Behavior.CalloutDelayMs
            },
            AircraftProfile = HostConstants.NormalizeProfileId(SetProfile.Text),
            AutoDetectAircraft = SetAutoDetect.IsChecked == true,
            AnnounceProfileSwitch = SetAnnounceProfile.IsChecked == true
        };
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        // In-memory only: must not re-read settings.json (would wipe edits).
        _session.ApplySettingsFromUi(ReadSettingsFromUi(), saveToDisk: false, rebuildCatalog: true);
        LoadSettingsToUi();
        RefreshStatus();
        // Profile may have changed — refresh command editor from disk sources for that profile.
        ReloadCommandsEditorFromDisk(keepSelectionId: _selectedRow?.Id);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        _session.ApplySettingsFromUi(ReadSettingsFromUi(), saveToDisk: true, rebuildCatalog: true);
        LoadSettingsToUi();
        RefreshStatus();
        ReloadCommandsEditorFromDisk(keepSelectionId: _selectedRow?.Id);
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        _session.ReloadFromDisk();
        LoadSettingsToUi();
        RefreshStatus();
        ReloadCommandsEditorFromDisk();
    }

    private void BtnOpenConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _session.ConfigRoot,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Open Config Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnReconnect_Click(object sender, RoutedEventArgs e) => _session.ForceReconnect();

    private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _session.Log.Clear();
        LogView.Clear();
        _uiLogLineCount = 0;
    }

    private void BtnTestTts_Click(object sender, RoutedEventArgs e) => _session.TestTts();

    private void BtnReloadAll_Click(object sender, RoutedEventArgs e)
    {
        // ReloadFromDisk restarts speech when listening was already active.
        _session.ReloadFromDisk();
        if (_session.SpeechStartCount == 0)
            _session.StartSpeechListening();
        LoadSettingsToUi();
        RefreshStatus();
        ReloadCommandsEditorFromDisk();
    }

    private void BtnInject_Click(object sender, RoutedEventArgs e)
    {
        var phrase = InjectBox.Text.Trim();
        if (phrase.Length == 0) return;
        _session.InjectPhrase(phrase, forceGate: InjectForce.IsChecked == true);
        RefreshStatus();
    }

    private void DbgContinuous_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressContinuousEvent) return;
        _session.SetContinuousListen(DbgContinuous.IsChecked == true);
        SetContinuous.IsChecked = DbgContinuous.IsChecked;
    }

    private void AlwaysOnTop_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = AlwaysOnTopCheck.IsChecked == true;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // Minimize still hides to tray for convenience; full close (X) exits completely.
        if (!_exitRequested && WindowState == WindowState.Minimized)
        {
            Hide();
            _tray.ShowBalloonTip(800, "CoPilot Voice Host", "Minimized to tray. Close the window or use Exit to quit.", Forms.ToolTipIcon.Info);
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Always fully shut down: speech, SimConnect dispatch thread, tray, session.
        _exitRequested = true;
        try { _session.Log.LineAppended -= OnLogLine; } catch { /* ignore */ }
        try
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        catch { /* ignore */ }

        try { _session.Dispose(); } catch { /* ignore */ }

        try { System.Windows.Application.Current?.Shutdown(); } catch { /* ignore */ }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exitRequested = true;
        Close();
    }

    // ── Commands tab ─────────────────────────────────────────────────────────

    private void CommandsTab_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_commandsLoaded)
            ReloadCommandsEditorFromDisk();
    }

    private void ReloadCommandsEditorFromDisk(string? keepSelectionId = null)
    {
        try
        {
            _editBase = _session.LoadBaseCommandsFromDisk();
            _editProfile = _session.LoadActiveProfileFromDisk();
            _commandsLoaded = true;
            _cmdDirty = false;
            RebuildCommandRows(keepSelectionId ?? _selectedRow?.Id);
            UpdateCmdStatusBar();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Load commands", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RebuildCommandRows(string? selectId)
    {
        _suppressCmdSelection = true;
        CommitSelectedEditorToModel();

        var baseIds = _editBase.Commands
            .Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Merged view: profile wins on id collision (same as ConfigLoader.Merge).
        var byId = new Dictionary<string, (CommandDefinition Cmd, string Source)>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _editBase.Commands)
            byId[c.Id] = (c, "base");
        foreach (var c in _editProfile.Commands)
            byId[c.Id] = (c, "profile");

        var filter = (CmdFilter?.Text ?? string.Empty).Trim();
        _commandRows.Clear();
        foreach (var kv in byId.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var (cmd, source) = kv.Value;
            if (filter.Length > 0)
            {
                var hay = $"{cmd.Id} {string.Join(' ', cmd.Phrases)} {cmd.Response}";
                if (hay.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }

            var label = source == "profile" && baseIds.Contains(cmd.Id)
                ? "profile*" // overrides base
                : source;
            _commandRows.Add(new CommandRow(cmd, label));
        }

        _suppressCmdSelection = false;

        CommandRow? pick = null;
        if (!string.IsNullOrWhiteSpace(selectId))
            pick = _commandRows.FirstOrDefault(r => r.Id.Equals(selectId, StringComparison.OrdinalIgnoreCase));
        pick ??= _commandRows.FirstOrDefault();
        CmdList.SelectedItem = pick;
        if (pick is null)
            ClearCommandEditor();
        else
            LoadCommandIntoEditor(pick);
    }

    private void UpdateCmdStatusBar()
    {
        var dirty = _cmdDirty ? " · unsaved edits" : "";
        CmdStatus.Text =
            $"{_commandRows.Count} shown · base {_editBase.Commands.Count} · profile '{_session.AircraftProfile}' {_editProfile.Commands.Count} · live {_session.Catalog.Commands.Count}{dirty}";
    }

    private void CmdFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_commandsLoaded) return;
        var keep = _selectedRow?.Id;
        RebuildCommandRows(keep);
    }

    private void CmdList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCmdSelection) return;
        CommitSelectedEditorToModel();
        if (CmdList.SelectedItem is CommandRow row)
            LoadCommandIntoEditor(row);
        else
            ClearCommandEditor();
    }

    private void LoadCommandIntoEditor(CommandRow row)
    {
        _selectedRow = row;
        var c = row.Command;
        CmdEditId.Text = c.Id;
        CmdEditSource.Text = row.Source;
        CmdEditResponse.Text = c.Response ?? "";
        CmdEditReject.Text = c.RejectResponse ?? "";
        CmdEditClimb.IsChecked = c.RequirePositiveClimbFlag;
        CmdEditChecklist.IsChecked = c.Checklist;
        CmdEditPhrases.Text = string.Join(Environment.NewLine, c.Phrases ?? new List<string>());
        CmdEditActions.Text = FormatActions(c.Actions);
        CmdEditConditions.Text = FormatConditions(c.Conditions);
    }

    private void ClearCommandEditor()
    {
        _selectedRow = null;
        CmdEditId.Text = "";
        CmdEditSource.Text = "—";
        CmdEditResponse.Text = "";
        CmdEditReject.Text = "";
        CmdEditClimb.IsChecked = false;
        CmdEditChecklist.IsChecked = false;
        CmdEditPhrases.Text = "";
        CmdEditActions.Text = "";
        CmdEditConditions.Text = "";
    }

    private void CommitSelectedEditorToModel()
    {
        if (_selectedRow is null) return;

        var oldId = _selectedRow.Command.Id;
        var newId = (CmdEditId.Text ?? "").Trim();
        if (newId.Length == 0)
            newId = oldId;

        var updated = new CommandDefinition
        {
            Id = newId,
            Response = CmdEditResponse.Text?.Trim() ?? "",
            RejectResponse = string.IsNullOrWhiteSpace(CmdEditReject.Text) ? null : CmdEditReject.Text.Trim(),
            RequirePositiveClimbFlag = CmdEditClimb.IsChecked == true,
            Checklist = CmdEditChecklist.IsChecked == true,
            Phrases = ParseLines(CmdEditPhrases.Text),
            Actions = ParseActions(CmdEditActions.Text),
            Conditions = ParseConditions(CmdEditConditions.Text)
        };

        // Write back into the correct source list (base vs profile).
        var sourceIsProfile = _selectedRow.Source.StartsWith("profile", StringComparison.OrdinalIgnoreCase);
        var list = sourceIsProfile ? _editProfile.Commands : _editBase.Commands;
        var idx = list.FindIndex(c => ReferenceEquals(c, _selectedRow.Command)
                                      || c.Id.Equals(oldId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            list[idx] = updated;
        else
            list.Add(updated);

        _selectedRow.ReplaceCommand(updated);
        _cmdDirty = true;
    }

    private void BtnCmdAdd_Click(object sender, RoutedEventArgs e)
    {
        if (!_commandsLoaded)
            ReloadCommandsEditorFromDisk();

        CommitSelectedEditorToModel();

        var toProfile = CmdAddTarget.SelectedIndex == 1;
        var list = toProfile ? _editProfile.Commands : _editBase.Commands;
        var id = MakeUniqueId(list, toProfile ? "profile_command" : "new_command");
        var cmd = new CommandDefinition
        {
            Id = id,
            Phrases = new List<string> { id.Replace('_', ' ') },
            Response = "Acknowledged.",
            Actions = new List<ActionDefinition>(),
            Conditions = new List<ConditionDefinition>()
        };
        list.Add(cmd);
        _cmdDirty = true;
        RebuildCommandRows(id);
        UpdateCmdStatusBar();
    }

    private void BtnCmdDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow is null) return;

        var id = _selectedRow.Command.Id;
        var sourceIsProfile = _selectedRow.Source.StartsWith("profile", StringComparison.OrdinalIgnoreCase);
        var list = sourceIsProfile ? _editProfile.Commands : _editBase.Commands;
        list.RemoveAll(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
                            || ReferenceEquals(c, _selectedRow.Command));

        // If this was a profile override, deleting profile entry reveals base again — OK.
        _selectedRow = null;
        _cmdDirty = true;
        RebuildCommandRows(null);
        UpdateCmdStatusBar();
    }

    private void BtnCmdReload_Click(object sender, RoutedEventArgs e)
    {
        if (_cmdDirty)
        {
            var r = System.Windows.MessageBox.Show(
                this,
                "Discard unsaved command edits and reload from disk?",
                "Reload commands",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }

        ReloadCommandsEditorFromDisk();
    }

    private void BtnCmdApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitSelectedEditorToModel();
            _session.ApplyCommandSources(
                ConfigLoader.CloneCatalog(_editBase),
                ConfigLoader.CloneProfile(_editProfile),
                saveToDisk: false);
            _cmdDirty = false;
            RebuildCommandRows(_selectedRow?.Id);
            UpdateCmdStatusBar();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Apply commands", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCmdSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitSelectedEditorToModel();
            _session.ApplyCommandSources(
                ConfigLoader.CloneCatalog(_editBase),
                ConfigLoader.CloneProfile(_editProfile),
                saveToDisk: true);
            _cmdDirty = false;
            // Re-read disk to confirm round-trip
            ReloadCommandsEditorFromDisk(keepSelectionId: _selectedRow?.Id);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Save commands", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string MakeUniqueId(List<CommandDefinition> list, string prefix)
    {
        var set = list.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(prefix)) return prefix;
        for (var i = 2; i < 1000; i++)
        {
            var id = $"{prefix}_{i}";
            if (!set.Contains(id)) return id;
        }

        return prefix + "_" + Guid.NewGuid().ToString("N")[..6];
    }

    private static List<string> ParseLines(string? text) =>
        (text ?? "")
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(s => s.Length > 0)
        .ToList();

    private static string FormatActions(IEnumerable<ActionDefinition>? actions)
    {
        if (actions is null) return "";
        var sb = new StringBuilder();
        foreach (var a in actions)
        {
            if (string.IsNullOrWhiteSpace(a.Name)) continue;
            if (string.IsNullOrWhiteSpace(a.Type)
                || a.Type.Equals(HostConstants.ActionTypeEvent, StringComparison.OrdinalIgnoreCase))
                sb.AppendLine(a.Name);
            else
                sb.AppendLine($"{a.Type}|{a.Name}");
        }

        return sb.ToString().TrimEnd();
    }

    private static List<ActionDefinition> ParseActions(string? text)
    {
        var list = new List<ActionDefinition>();
        foreach (var line in ParseLines(text))
        {
            var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                list.Add(new ActionDefinition { Type = parts[0], Name = parts[1] });
            }
            else
            {
                list.Add(new ActionDefinition { Type = HostConstants.ActionTypeEvent, Name = parts[0] });
            }
        }

        return list;
    }

    private static string FormatConditions(IEnumerable<ConditionDefinition>? conditions)
    {
        if (conditions is null) return "";
        var sb = new StringBuilder();
        foreach (var c in conditions)
        {
            sb.AppendLine(
                $"{c.SimVar} | {c.Op} | {c.Value.ToString(CultureInfo.InvariantCulture)} | {c.Units}");
        }

        return sb.ToString().TrimEnd();
    }

    private static List<ConditionDefinition> ParseConditions(string? text)
    {
        var list = new List<ConditionDefinition>();
        foreach (var line in ParseLines(text))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 3) continue;
            var simvar = parts[0];
            var op = parts[1];
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                continue;
            var units = parts.Length >= 4 ? parts[3] : "number";
            list.Add(new ConditionDefinition
            {
                SimVar = simvar,
                Op = op,
                Value = value,
                Units = units
            });
        }

        return list;
    }

    /// <summary>List row for the Commands tab (UI-only; holds reference into working copy).</summary>
    private sealed class CommandRow
    {
        public CommandRow(CommandDefinition command, string source)
        {
            Command = command;
            Source = source;
        }

        public CommandDefinition Command { get; private set; }
        public string Source { get; }
        public string Id => Command.Id;
        public string PhrasesPreview =>
            Command.Phrases is { Count: > 0 }
                ? string.Join(", ", Command.Phrases.Take(3)) + (Command.Phrases.Count > 3 ? "…" : "")
                : "—";

        public void ReplaceCommand(CommandDefinition cmd) => Command = cmd;
    }
}
