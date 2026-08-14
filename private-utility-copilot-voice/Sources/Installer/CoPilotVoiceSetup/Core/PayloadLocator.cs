namespace CoPilotVoiceSetup.Core;

/// <summary>Resolves the package tree to install (payload next to Setup or repo Packages/).</summary>
public static class PayloadLocator
{
    public static string? FindPayloadPackageRoot(string? baseDirectory = null)
    {
        baseDirectory ??= AppContext.BaseDirectory;

        // 1) Side-by-side payload (distribution layout)
        var sideBySide = Path.Combine(baseDirectory, InstallerConstants.PayloadRelative, InstallerConstants.PackageFolderName);
        if (IsValidPackageRoot(sideBySide))
            return Path.GetFullPath(sideBySide);

        // 2) Walk up for repo Packages/private-utility-copilot-voice (dev)
        var dir = new DirectoryInfo(baseDirectory);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Packages", InstallerConstants.PackageFolderName);
            if (IsValidPackageRoot(candidate))
                return Path.GetFullPath(candidate);

            candidate = Path.Combine(dir.FullName, "private-utility-copilot-voice", "Packages", InstallerConstants.PackageFolderName);
            if (IsValidPackageRoot(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    public static bool IsValidPackageRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        var manifest = Path.Combine(path, InstallerConstants.ManifestFileName);
        if (!File.Exists(manifest))
            return false;
        if (!PayloadIntegrity.TryReadPackageIdentity(manifest, out var packageName, out _))
            return false;
        return PayloadIntegrity.HasExactPackageName(packageName);
    }

    public static string? ReadPackageVersion(string packageRoot)
    {
        var manifest = Path.Combine(packageRoot, InstallerConstants.ManifestFileName);
        return PayloadIntegrity.TryReadPackageIdentity(manifest, out _, out var version)
            ? version
            : null;
    }
}
