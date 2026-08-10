namespace CoPilotVoiceSetup.Core;

public sealed class InstallOptions
{
    public string CommunityPath { get; set; } = "";
    public bool DesktopShortcut { get; set; } = true;
    public bool StartMenuShortcut { get; set; } = true;
    public bool KeepConfig { get; set; } = true;
    public bool LaunchAfterInstall { get; set; }
    public string? PayloadRoot { get; set; }
    public string? SetupExePath { get; set; }
    public Action<string>? Progress { get; set; }
}

public sealed class InstallResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? PackagePath { get; init; }
    public string? HostExePath { get; init; }
    public string? ConfigBackupPath { get; init; }
}

/// <summary>Copies package tree, preserves config on upgrade, shortcuts, install state.</summary>
public static class InstallService
{
    public static InstallResult Install(InstallOptions options)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(options.CommunityPath) || !Directory.Exists(options.CommunityPath))
                return Fail("Community folder does not exist. Browse to your MSFS Community directory.");

            var payload = options.PayloadRoot ?? PayloadLocator.FindPayloadPackageRoot();
            if (payload is null || !PayloadLocator.IsValidPackageRoot(payload))
                return Fail("Install payload not found. Run pack-installer.ps1 or place package under payload/private-utility-copilot-voice.");

            var dest = CommunityDetector.GetPackageInstallPath(options.CommunityPath);
            Report(options, $"Target: {dest}");

            // Safety: dest must be under community and named correctly
            if (!IsSafePackageDestination(options.CommunityPath, dest))
                return Fail("Refusing to install: destination path is not a safe package folder under Community.");

            string? backupPath = null;
            var existingConfig = Path.Combine(dest, "extras", "config");
            Dictionary<string, string>? preserved = null;

            if (Directory.Exists(dest) && Directory.Exists(existingConfig))
            {
                backupPath = BackupConfig(existingConfig);
                Report(options, $"Config backup: {backupPath}");
                if (options.KeepConfig)
                    preserved = SnapshotPreservedConfigFiles(existingConfig);
            }

            Report(options, "Copying package files…");
            CopyDirectory(payload, dest, overwrite: true);

            if (options.KeepConfig && preserved is not null && preserved.Count > 0)
            {
                Report(options, "Restoring preserved config…");
                RestorePreservedConfig(Path.Combine(dest, "extras", "config"), preserved);
            }

            var hostExe = Path.Combine(dest, "extras", InstallerConstants.HostExeName);
            if (!File.Exists(hostExe))
                return Fail($"Install incomplete: {InstallerConstants.HostExeName} missing under extras/.");

            if (options.DesktopShortcut || options.StartMenuShortcut)
            {
                Report(options, "Creating shortcuts…");
                ShortcutService.CreateHostShortcut(
                    dest,
                    options.DesktopShortcut,
                    options.StartMenuShortcut,
                    uninstallTarget: options.SetupExePath);
            }

            var version = PayloadLocator.ReadPackageVersion(dest) ?? "unknown";
            InstallStateStore.Save(new InstallState
            {
                Version = version,
                CommunityPath = Path.GetFullPath(options.CommunityPath),
                PackagePath = dest,
                InstalledUtc = DateTime.UtcNow.ToString("o"),
                DesktopShortcut = options.DesktopShortcut,
                StartMenuShortcut = options.StartMenuShortcut
            });

            Report(options, "Done.");
            return new InstallResult
            {
                Success = true,
                Message = Directory.Exists(Path.Combine(options.CommunityPath, InstallerConstants.PackageFolderName))
                    ? $"Installed version {version} to {dest}"
                    : "Installed.",
                PackagePath = dest,
                HostExePath = hostExe,
                ConfigBackupPath = backupPath
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public static InstallResult Uninstall(string? packagePathOverride = null, Action<string>? progress = null)
    {
        try
        {
            var state = InstallStateStore.Load();
            var packagePath = packagePathOverride
                              ?? state?.PackagePath
                              ?? "";

            if (string.IsNullOrWhiteSpace(packagePath) || !Directory.Exists(packagePath))
            {
                // Try community from state
                if (state is not null && Directory.Exists(state.CommunityPath))
                    packagePath = CommunityDetector.GetPackageInstallPath(state.CommunityPath);
            }

            if (string.IsNullOrWhiteSpace(packagePath) || !Directory.Exists(packagePath))
            {
                ShortcutService.RemoveShortcuts();
                InstallStateStore.Delete();
                return new InstallResult
                {
                    Success = true,
                    Message = "No package folder found. Shortcuts and install state cleared."
                };
            }

            packagePath = Path.GetFullPath(packagePath);
            if (!CanSafelyDeletePackage(packagePath))
                return Fail($"Refusing to delete '{packagePath}' — not a recognized Co-Pilot package.");

            progress?.Invoke("Removing shortcuts…");
            ShortcutService.RemoveShortcuts();

            // Backup config before delete
            var config = Path.Combine(packagePath, "extras", "config");
            string? backup = null;
            if (Directory.Exists(config))
            {
                backup = BackupConfig(config);
                progress?.Invoke($"Config backup: {backup}");
            }

            progress?.Invoke($"Deleting {packagePath}…");
            Directory.Delete(packagePath, recursive: true);
            InstallStateStore.Delete();

            return new InstallResult
            {
                Success = true,
                Message = "Uninstalled package and shortcuts.",
                ConfigBackupPath = backup
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public static bool IsSafePackageDestination(string communityPath, string packagePath)
    {
        try
        {
            var community = Path.GetFullPath(communityPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dest = Path.GetFullPath(packagePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(Path.GetFileName(dest), InstallerConstants.PackageFolderName, StringComparison.OrdinalIgnoreCase))
                return false;
            var parent = Path.GetDirectoryName(dest);
            if (parent is null)
                return false;
            parent = Path.GetFullPath(parent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(parent, community, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool CanSafelyDeletePackage(string packagePath)
    {
        try
        {
            var full = Path.GetFullPath(packagePath);
            if (!string.Equals(Path.GetFileName(full), InstallerConstants.PackageFolderName, StringComparison.OrdinalIgnoreCase))
                return false;
            return PayloadLocator.IsValidPackageRoot(full);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// When KeepConfig: preserve settings.json and aircraft/*.json content; base_commands always from payload.
    /// </summary>
    public static Dictionary<string, string> SnapshotPreservedConfigFiles(string configDir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var settings = Path.Combine(configDir, "settings.json");
        if (File.Exists(settings))
            map["settings.json"] = File.ReadAllText(settings);

        var aircraft = Path.Combine(configDir, "aircraft");
        if (Directory.Exists(aircraft))
        {
            foreach (var file in Directory.GetFiles(aircraft, "*.json"))
            {
                var rel = "aircraft/" + Path.GetFileName(file);
                map[rel] = File.ReadAllText(file);
            }
        }

        return map;
    }

    public static void RestorePreservedConfig(string configDir, Dictionary<string, string> preserved)
    {
        Directory.CreateDirectory(configDir);
        foreach (var (rel, content) in preserved)
        {
            var path = Path.Combine(configDir, rel.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
        }
    }

    public static string BackupConfig(string configDir)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var dest = Path.Combine(InstallStateStore.GetBackupRoot(), stamp, "config");
        CopyDirectory(configDir, dest, overwrite: true);
        return dest;
    }

    public static void CopyDirectory(string sourceDir, string destDir, bool overwrite)
    {
        sourceDir = Path.GetFullPath(sourceDir);
        destDir = Path.GetFullPath(destDir);
        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            if (rel.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe relative path in source tree.");
            Directory.CreateDirectory(Path.Combine(destDir, rel));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            if (rel.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe relative path in source tree.");
            var target = Path.Combine(destDir, rel);
            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);
            File.Copy(file, target, overwrite);
        }
    }

    private static void Report(InstallOptions options, string msg) => options.Progress?.Invoke(msg);

    private static InstallResult Fail(string message) => new() { Success = false, Message = message };
}
