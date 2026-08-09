using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using CoPilotVoiceHost.Diagnostics;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CoPilotVoiceHost.Ui;

public partial class MainWindow : Window
{
    private readonly HostSession _session;
    private readonly HostOptions _options;
    private readonly Forms.NotifyIcon _tray;
    private bool _exitFromTray;
    private bool _suppressContinuousEvent;

    public MainWindow(HostSession session, HostOptions options)
    {
        _session = session;
        _options = options;
        InitializeComponent();

        _tray = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "CoPilot Voice Host",
            Icon = Drawing.SystemIcons.Application
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowFromTray());
        menu.Items.Add("Hide", null, (_, _) => Hide());
        menu.Items.Add("Reconnect", null, (_, _) => Dispatcher.Invoke(() => _session.ForceReconnect()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();

        _session.Log.LineAppended += OnLogLine;
        _session.StatusChanged += () => Dispatcher.BeginInvoke(RefreshStatus);

        LoadSettingsToUi();
        foreach (var line in _session.Log.Snapshot())
            AppendLog(line);

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
        var local = entry.Utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var level = entry.Level switch
        {
            LogLevel.Warn => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Debug => "DBG",
            _ => "INF"
        };
        LogView.AppendText($"[{local} {level}] {entry.Message}{Environment.NewLine}");
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
        TxtProfile.Text = _session.AircraftProfile;
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
        _suppressContinuousEvent = false;
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
        SetProfile.Items.Clear();
        foreach (var p in _session.ListProfiles())
            SetProfile.Items.Add(p);
        SetProfile.Text = s.AircraftProfile;
        DbgContinuous.IsChecked = s.Speech.ContinuousListen;
    }

    private AppSettings ReadSettingsFromUi()
    {
        var s = _session.Settings;
        // mutate a shallow copy of current settings tree
        s.Speech.WakeWord = SetWakeWord.Text.Trim();
        s.Speech.PttKey = SetPttKey.Text.Trim();
        if (double.TryParse(SetConfidence.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var conf))
            s.Speech.ConfidenceThreshold = Math.Clamp(conf, 0, 1);
        if (int.TryParse(SetPttGrace.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var grace))
            s.Speech.PttGraceMs = Math.Clamp(grace, 0, 30_000);
        s.Speech.ContinuousListen = SetContinuous.IsChecked == true;
        s.Tts.Voice = SetTtsVoice.Text.Trim();
        s.Behavior.RequirePositiveClimbForGearUp = SetPositiveClimb.IsChecked == true;
        s.AircraftProfile = string.IsNullOrWhiteSpace(SetProfile.Text) ? "generic" : SetProfile.Text.Trim();
        return s;
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        _session.ApplySettingsFromUi(ReadSettingsFromUi(), saveToDisk: false, reloadProfiles: true);
        LoadSettingsToUi();
        RefreshStatus();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        _session.ApplySettingsFromUi(ReadSettingsFromUi(), saveToDisk: true, reloadProfiles: true);
        LoadSettingsToUi();
        RefreshStatus();
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        _session.ReloadFromDisk();
        LoadSettingsToUi();
        RefreshStatus();
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
    }

    private void BtnTestTts_Click(object sender, RoutedEventArgs e) => _session.TestTts();

    private void BtnReloadAll_Click(object sender, RoutedEventArgs e)
    {
        _session.ReloadFromDisk();
        _session.StartSpeechListening();
        LoadSettingsToUi();
        RefreshStatus();
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
}
