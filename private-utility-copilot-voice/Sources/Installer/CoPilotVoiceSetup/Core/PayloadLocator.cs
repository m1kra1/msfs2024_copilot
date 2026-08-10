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
        try
        {
            var text = File.ReadAllText(manifest);
            return text.Contains(InstallerConstants.ManifestPackageHint, StringComparison.OrdinalIgnoreCase)
                   || text.Contains("Private Voice Co-Pilot", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string? ReadPackageVersion(string packageRoot)
    {
        try
        {
            var manifest = Path.Combine(packageRoot, InstallerConstants.ManifestFileName);
            if (!File.Exists(manifest))
                return null;
            var text = File.ReadAllText(manifest);
            const string marker = "\"package_version\"";
            var idx = text.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;
            var colon = text.IndexOf(':', idx);
            var q1 = text.IndexOf('"', colon + 1);
            var q2 = text.IndexOf('"', q1 + 1);
            if (q1 > 0 && q2 > q1)
                return text.Substring(q1 + 1, q2 - q1 - 1);
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
