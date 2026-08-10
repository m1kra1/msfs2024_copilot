using System.Runtime.InteropServices;

namespace CoPilotVoiceSetup.Core;

/// <summary>Creates/removes Windows .lnk shortcuts via WScript.Shell COM.</summary>
public static class ShortcutService
{
    public static string DesktopShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            InstallerConstants.DesktopShortcutName);

    public static string StartMenuFolderPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            InstallerConstants.StartMenuFolderName);

    public static string StartMenuHostShortcutPath =>
        Path.Combine(StartMenuFolderPath, InstallerConstants.StartMenuHostShortcut);

    public static string StartMenuUninstallShortcutPath =>
        Path.Combine(StartMenuFolderPath, InstallerConstants.StartMenuUninstallShortcut);

    public static void CreateHostShortcut(string packagePath, bool desktop, bool startMenu, string? uninstallTarget = null)
    {
        var hostExe = Path.Combine(packagePath, "extras", InstallerConstants.HostExeName);
        var workDir = Path.Combine(packagePath, "extras");
        if (!File.Exists(hostExe))
            throw new FileNotFoundException("Host executable not found for shortcut.", hostExe);

        if (desktop)
            CreateShortcut(DesktopShortcutPath, hostExe, workDir, "Private Voice Co-Pilot Host");

        if (startMenu)
        {
            Directory.CreateDirectory(StartMenuFolderPath);
            CreateShortcut(StartMenuHostShortcutPath, hostExe, workDir, "Private Voice Co-Pilot Host");
            if (!string.IsNullOrWhiteSpace(uninstallTarget) && File.Exists(uninstallTarget))
            {
                CreateShortcut(
                    StartMenuUninstallShortcutPath,
                    uninstallTarget,
                    Path.GetDirectoryName(uninstallTarget) ?? "",
                    "Uninstall Private Voice Co-Pilot",
                    arguments: "--uninstall");
            }
        }
    }

    public static void RemoveShortcuts()
    {
        TryDelete(DesktopShortcutPath);
        TryDelete(StartMenuHostShortcutPath);
        TryDelete(StartMenuUninstallShortcutPath);
        try
        {
            if (Directory.Exists(StartMenuFolderPath)
                && !Directory.EnumerateFileSystemEntries(StartMenuFolderPath).Any())
            {
                Directory.Delete(StartMenuFolderPath);
            }
        }
        catch
        {
            // ignore
        }
    }

    public static void CreateShortcut(
        string lnkPath,
        string targetPath,
        string workingDirectory,
        string description,
        string? arguments = null)
    {
        var dir = Path.GetDirectoryName(lnkPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            throw new InvalidOperationException("WScript.Shell COM is not available.");

        object? shell = Activator.CreateInstance(shellType);
        if (shell is null)
            throw new InvalidOperationException("Could not create WScript.Shell.");

        try
        {
            dynamic dshell = shell;
            var shortcut = dshell.CreateShortcut(lnkPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.Description = description;
            shortcut.IconLocation = targetPath + ",0";
            if (!string.IsNullOrEmpty(arguments))
                shortcut.Arguments = arguments;
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
