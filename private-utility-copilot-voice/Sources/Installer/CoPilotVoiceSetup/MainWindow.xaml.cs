using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using CoPilotVoiceSetup.Core;
using WinForms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using Brush = System.Windows.Media.Brush;

namespace CoPilotVoiceSetup;

public partial class MainWindow : Window
{
    private enum WizardPage
    {
        Welcome,
        Runtime,
        Location,
        Options,
        Progress,
        Finish,
        UninstallConfirm
    }

    private WizardPage _page = WizardPage.Welcome;
    private bool _uninstallMode;
    private string? _payloadRoot;
    private string? _lastPackagePath;
    private string? _lastHostExe;
    private bool _runtimeOk;
    private bool _busy;

    public MainWindow(bool startInUninstallMode = false)
    {
        InitializeComponent();
        _uninstallMode = startInUninstallMode;
        Loaded += (_, _) => InitializeWizard();
    }

    private void InitializeWizard()
    {
        _payloadRoot = PayloadLocator.FindPayloadPackageRoot();
        var ver = _payloadRoot is not null
            ? PayloadLocator.ReadPackageVersion(_payloadRoot) ?? "?"
            : "?";
        var setupVer = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.5.0";
        WelcomeVersion.Text = $"Package version: {ver}  ·  Setup: {setupVer}";
        WelcomePayload.Text = _payloadRoot is not null
            ? $"Payload: {_payloadRoot}"
            : "WARNING: No payload found. For distribution, run scripts/pack-installer.ps1. Dev: repo Packages/ tree is used if present.";

        var state = InstallStateStore.Load();
        if (state is not null && Directory.Exists(state.PackagePath))
            BtnGoUninstall.Visibility = Visibility.Visible;

        RefreshRuntimeUi();
        LoadCommunityCandidates();

        if (_uninstallMode)
            ShowUninstallFlow();
        else
            ShowPage(WizardPage.Welcome);
    }

    private void RefreshRuntimeUi()
    {
        _runtimeOk = DotNetRuntimeChecker.IsDotNet8DesktopRuntimeInstalled();
        if (_runtimeOk)
        {
            RuntimeStatus.Text = ".NET 8 Desktop Runtime: found";
            RuntimeStatus.Foreground = (Brush)FindResource("OkBrush");
            RuntimeHint.Text = "CoPilotVoiceHost.exe can run on this machine.";
        }
        else
        {
            RuntimeStatus.Text = ".NET 8 Desktop Runtime: not found";
            RuntimeStatus.Foreground = (Brush)FindResource("ErrBrush");
            RuntimeHint.Text = "Install the Desktop Runtime (not just ASP.NET) from Microsoft, then re-check by reopening Setup.";
        }
    }

    private void LoadCommunityCandidates()
    {
        CommunityCombo.Items.Clear();
        var found = CommunityDetector.DetectCommunityFolders();
        foreach (var c in found)
            CommunityCombo.Items.Add(c);

        var state = InstallStateStore.Load();
        if (state is not null && !string.IsNullOrWhiteSpace(state.CommunityPath))
        {
            if (!CommunityCombo.Items.Contains(state.CommunityPath))
                CommunityCombo.Items.Add(state.CommunityPath);
            CommunityCombo.Text = state.CommunityPath;
        }
        else if (found.Count > 0)
        {
            CommunityCombo.SelectedIndex = 0;
        }

        UpdateLocationStatus();
    }

    private void UpdateLocationStatus()
    {
        var path = CommunityCombo.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(path))
        {
            LocationStatus.Text = "Select or browse to your Community folder.";
            LocationStatus.Foreground = (Brush)FindResource("MutedBrush");
            return;
        }

        if (!Directory.Exists(path))
        {
            LocationStatus.Text = "Folder does not exist.";
            LocationStatus.Foreground = (Brush)FindResource("ErrBrush");
            return;
        }

        var warn = CommunityDetector.LooksLikeCommunityFolder(path)
            ? ""
            : " Warning: folder name is not 'Community'.";
        var pkg = CommunityDetector.GetPackageInstallPath(path);
        if (Directory.Exists(pkg) && PayloadLocator.IsValidPackageRoot(pkg))
        {
            var v = PayloadLocator.ReadPackageVersion(pkg) ?? "?";
            LocationStatus.Text = $"Existing install detected (v{v}). Next step will upgrade.{warn}";
            LocationStatus.Foreground = (Brush)FindResource("WarnBrush");
        }
        else
        {
            LocationStatus.Text = $"Ready to install into:{Environment.NewLine}{pkg}{warn}";
            LocationStatus.Foreground = (Brush)FindResource("OkBrush");
        }
    }

    private void ShowPage(WizardPage page)
    {
        _page = page;
        PageWelcome.Visibility = page == WizardPage.Welcome ? Visibility.Visible : Visibility.Collapsed;
        PageRuntime.Visibility = page == WizardPage.Runtime ? Visibility.Visible : Visibility.Collapsed;
        PageLocation.Visibility = page == WizardPage.Location ? Visibility.Visible : Visibility.Collapsed;
        PageOptions.Visibility = page == WizardPage.Options ? Visibility.Visible : Visibility.Collapsed;
        PageProgress.Visibility = page == WizardPage.Progress ? Visibility.Visible : Visibility.Collapsed;
        PageFinish.Visibility = page == WizardPage.Finish ? Visibility.Visible : Visibility.Collapsed;
        PageUninstall.Visibility = page == WizardPage.UninstallConfirm ? Visibility.Visible : Visibility.Collapsed;

        BtnBack.IsEnabled = page is WizardPage.Runtime or WizardPage.Location or WizardPage.Options or WizardPage.UninstallConfirm;
        BtnNext.IsEnabled = !_busy;
        BtnCancel.Content = page is WizardPage.Finish ? "Close" : "Cancel";

        switch (page)
        {
            case WizardPage.Welcome:
                TitleText.Text = "Private Voice Co-Pilot Setup";
                SubtitleText.Text = "Install or upgrade the Community package and host shortcuts.";
                BtnNext.Content = "Next";
                break;
            case WizardPage.Runtime:
                TitleText.Text = ".NET Runtime";
                SubtitleText.Text = "Host requires .NET 8 Desktop Runtime.";
                RefreshRuntimeUi();
                BtnNext.Content = "Next";
                break;
            case WizardPage.Location:
                TitleText.Text = "Community folder";
                SubtitleText.Text = "Where MSFS loads Community packages from.";
                UpdateLocationStatus();
                BtnNext.Content = "Next";
                break;
            case WizardPage.Options:
                TitleText.Text = "Options";
                SubtitleText.Text = "Shortcuts and upgrade behavior.";
                BtnNext.Content = "Install";
                break;
            case WizardPage.Progress:
                TitleText.Text = _uninstallMode ? "Uninstalling…" : "Installing…";
                SubtitleText.Text = "Please wait.";
                BtnBack.IsEnabled = false;
                BtnNext.IsEnabled = false;
                BtnCancel.IsEnabled = false;
                break;
            case WizardPage.Finish:
                TitleText.Text = "Finished";
                SubtitleText.Text = "";
                BtnBack.IsEnabled = false;
                BtnNext.Visibility = Visibility.Collapsed;
                BtnCancel.IsEnabled = true;
                BtnCancel.Content = "Close";
                BtnLaunchHost.IsEnabled = _runtimeOk && !_uninstallMode && File.Exists(_lastHostExe ?? "");
                break;
            case WizardPage.UninstallConfirm:
                TitleText.Text = "Uninstall";
                SubtitleText.Text = "Remove package and shortcuts.";
                BtnNext.Content = "Uninstall";
                var st = InstallStateStore.Load();
                UninstallTarget.Text = st is not null
                    ? $"Package: {st.PackagePath}\nCommunity: {st.CommunityPath}\nVersion: {st.Version}"
                    : "No install.json — will try to remove package if path is known.";
                break;
        }
    }

    private void ShowUninstallFlow()
    {
        _uninstallMode = true;
        BtnNext.Visibility = Visibility.Visible;
        ShowPage(WizardPage.UninstallConfirm);
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_uninstallMode && _page == WizardPage.UninstallConfirm)
        {
            _uninstallMode = false;
            ShowPage(WizardPage.Welcome);
            return;
        }

        ShowPage(_page switch
        {
            WizardPage.Runtime => WizardPage.Welcome,
            WizardPage.Location => WizardPage.Runtime,
            WizardPage.Options => WizardPage.Location,
            _ => WizardPage.Welcome
        });
    }

    private async void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        if (_page == WizardPage.UninstallConfirm)
        {
            await RunUninstallAsync();
            return;
        }

        switch (_page)
        {
            case WizardPage.Welcome:
                ShowPage(WizardPage.Runtime);
                break;
            case WizardPage.Runtime:
                ShowPage(WizardPage.Location);
                break;
            case WizardPage.Location:
                if (!ValidateLocation())
                    return;
                ShowPage(WizardPage.Options);
                break;
            case WizardPage.Options:
                await RunInstallAsync();
                break;
        }
    }

    private bool ValidateLocation()
    {
        UpdateLocationStatus();
        var path = CommunityCombo.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            MessageBox.Show(this, "Please select an existing Community folder.", "Location",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (_payloadRoot is null)
        {
            MessageBox.Show(this,
                "Install payload not found.\n\nRun scripts/pack-installer.ps1 to build a distribution folder with payload/, or run Setup from a tree that can reach Packages/private-utility-copilot-voice.",
                "Payload missing", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        return true;
    }

    private async Task RunInstallAsync()
    {
        _busy = true;
        ShowPage(WizardPage.Progress);
        ProgressLog.Clear();
        ProgressTitle.Text = "Installing…";

        var community = CommunityCombo.Text!.Trim();
        var options = new InstallOptions
        {
            CommunityPath = community,
            DesktopShortcut = OptDesktop.IsChecked == true,
            StartMenuShortcut = OptStartMenu.IsChecked == true,
            KeepConfig = OptKeepConfig.IsChecked == true,
            LaunchAfterInstall = OptLaunch.IsChecked == true,
            PayloadRoot = _payloadRoot,
            SetupExePath = Environment.ProcessPath,
            Progress = msg => Dispatcher.Invoke(() =>
            {
                ProgressLog.AppendText(msg + Environment.NewLine);
                ProgressLog.ScrollToEnd();
            })
        };

        InstallResult result;
        try
        {
            result = await Task.Run(() => InstallService.Install(options));
        }
        catch (Exception ex)
        {
            result = new InstallResult { Success = false, Message = ex.Message };
        }

        _busy = false;
        BtnCancel.IsEnabled = true;
        BtnNext.Visibility = Visibility.Collapsed;

        _lastPackagePath = result.PackagePath;
        _lastHostExe = result.HostExePath;
        FinishMessage.Text = result.Success
            ? result.Message + (result.ConfigBackupPath is not null
                ? $"\n\nConfig backup:\n{result.ConfigBackupPath}"
                : "")
            : "Install failed:\n" + result.Message;
        FinishMessage.Foreground = result.Success
            ? (Brush)FindResource("OkBrush")
            : (Brush)FindResource("ErrBrush");

        ShowPage(WizardPage.Finish);

        if (result.Success && options.LaunchAfterInstall && _runtimeOk && File.Exists(_lastHostExe))
            TryLaunchHost();
    }

    private async Task RunUninstallAsync()
    {
        _busy = true;
        ShowPage(WizardPage.Progress);
        ProgressLog.Clear();
        ProgressTitle.Text = "Uninstalling…";

        InstallResult result;
        try
        {
            result = await Task.Run(() => InstallService.Uninstall(progress: msg =>
                Dispatcher.Invoke(() =>
                {
                    ProgressLog.AppendText(msg + Environment.NewLine);
                    ProgressLog.ScrollToEnd();
                })));
        }
        catch (Exception ex)
        {
            result = new InstallResult { Success = false, Message = ex.Message };
        }

        _busy = false;
        BtnCancel.IsEnabled = true;
        BtnNext.Visibility = Visibility.Collapsed;
        _lastHostExe = null;
        FinishMessage.Text = result.Success
            ? result.Message + (result.ConfigBackupPath is not null
                ? $"\n\nConfig backup kept at:\n{result.ConfigBackupPath}"
                : "")
            : "Uninstall failed:\n" + result.Message;
        FinishMessage.Foreground = result.Success
            ? (Brush)FindResource("OkBrush")
            : (Brush)FindResource("ErrBrush");
        ShowPage(WizardPage.Finish);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Close();
    }

    private void BtnGoUninstall_Click(object sender, RoutedEventArgs e) => ShowUninstallFlow();

    private void BtnDetect_Click(object sender, RoutedEventArgs e)
    {
        LoadCommunityCandidates();
        if (CommunityCombo.Items.Count == 0)
            MessageBox.Show(this,
                "No Community folder detected automatically. Use Browse and select your MSFS Community directory.",
                "Detect", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "Select MSFS Community folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        var current = CommunityCombo.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            dlg.SelectedPath = current;

        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
        {
            if (!CommunityCombo.Items.Contains(dlg.SelectedPath))
                CommunityCombo.Items.Add(dlg.SelectedPath);
            CommunityCombo.Text = dlg.SelectedPath;
            UpdateLocationStatus();
        }
    }

    private void BtnDownloadDotNet_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(InstallerConstants.DotNetDesktopRuntimeDownload)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open browser", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnLaunchHost_Click(object sender, RoutedEventArgs e) => TryLaunchHost();

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = _lastPackagePath ?? InstallStateStore.Load()?.PackagePath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            MessageBox.Show(this, "Package folder not available.", "Open folder",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TryLaunchHost()
    {
        if (!_runtimeOk)
        {
            MessageBox.Show(this, "Install .NET 8 Desktop Runtime before launching the host.",
                "Runtime", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var exe = _lastHostExe;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            MessageBox.Show(this, "Host executable not found.", "Launch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Launch", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
