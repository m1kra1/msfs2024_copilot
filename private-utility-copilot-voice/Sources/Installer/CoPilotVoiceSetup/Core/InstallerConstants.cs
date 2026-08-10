namespace CoPilotVoiceSetup.Core;

public static class InstallerConstants
{
    public const string PackageFolderName = "private-utility-copilot-voice";
    public const string HostExeName = "CoPilotVoiceHost.exe";
    public const string ManifestFileName = "manifest.json";
    public const string AppDataFolderName = "PrivateCoPilotVoice";
    public const string InstallStateFileName = "install.json";
    public const string DesktopShortcutName = "CoPilot Voice Host.lnk";
    public const string StartMenuFolderName = "Private Voice Co-Pilot";
    public const string StartMenuHostShortcut = "CoPilot Voice Host.lnk";
    public const string StartMenuUninstallShortcut = "Uninstall Co-Pilot.lnk";
    public const string PayloadRelative = "payload";
    public const string DotNetDesktopRuntimeDownload =
        "https://dotnet.microsoft.com/download/dotnet/8.0";

    /// <summary>Heuristic: package manifest should mention this app name fragment.</summary>
    public const string ManifestPackageHint = "private-utility-copilot-voice";
}
