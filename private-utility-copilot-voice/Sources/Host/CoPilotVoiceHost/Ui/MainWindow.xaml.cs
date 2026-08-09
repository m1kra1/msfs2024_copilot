using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using CoPilotVoiceHost.Config;
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
    private bool _exitFromTray;
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

    public MainWindow(HostSession session, HostOptions _)
    {
        _session = session;
        InitializeComponent();

        CmdList.ItemsSource = _commandRows;

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

        LoadSettingsToUi();
        foreach (var line in _session.Log.Snapshot())
            AppendLogCore(line);

        RefreshStatus();
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
    }

    private void LoadSettingsToUi()
    {
        var s = _session.Settings;
        SetWakeWord.Text = s.Speech.WakeWord;
        SetPttKey.Text = s.Speech.PttKey;
        SetConfidence.Text = s.Speech.ConfidenceThreshold.ToString(CultureInfo.InvariantCulture);
        SetPttGrace.Text = s.Speech.PttGraceMs.ToString(CultureInfo.InvariantCulture);
        SetContinuous.IsChecked = s.Speech.ContinuousListen;
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
                Voice = SetTtsVoice.Text.Trim(),
                Rate = cur.Tts.Rate,
                Volume = cur.Tts.Volume
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
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _tray.ShowBalloonTip(800, "CoPilot Voice Host", "Running in system tray.", Forms.ToolTipIcon.Info);
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitFromTray)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }

        _session.Log.LineAppended -= OnLogLine;
        _tray.Visible = false;
        _tray.Dispose();
        _session.Dispose();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exitFromTray = true;
        Close();
        System.Windows.Application.Current?.Shutdown();
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
