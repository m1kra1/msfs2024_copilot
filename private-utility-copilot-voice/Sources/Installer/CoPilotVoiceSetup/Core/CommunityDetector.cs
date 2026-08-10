using System.Text.RegularExpressions;

namespace CoPilotVoiceSetup.Core;

/// <summary>
/// Finds MSFS Community folders via UserCfg.opt InstalledPackagesPath and common fallbacks.
/// Pure path logic — no WPF.
/// </summary>
public static class CommunityDetector
{
    private static readonly Regex InstalledPackagesPathRegex = new(
        @"InstalledPackagesPath\s+""?([^""\r\n]+)""?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Parse InstalledPackagesPath from UserCfg.opt text; returns null if missing.</summary>
    public static string? ParseInstalledPackagesPath(string userCfgOptText)
    {
        if (string.IsNullOrWhiteSpace(userCfgOptText))
            return null;

        var m = InstalledPackagesPathRegex.Match(userCfgOptText);
        if (!m.Success)
            return null;

        var path = m.Groups[1].Value.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static string? ToCommunityPath(string? installedPackagesPath)
    {
        if (string.IsNullOrWhiteSpace(installedPackagesPath))
            return null;
        try
        {
            var community = Path.GetFullPath(Path.Combine(installedPackagesPath.Trim(), "Community"));
            return community;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Discover existing Community directories on this machine.
    /// </summary>
    public static IReadOnlyList<string> DetectCommunityFolders(
        string? appData = null,
        string? localAppData = null,
        IEnumerable<string>? extraUserCfgPaths = null)
    {
        appData ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var cfgCandidates = new List<string>
        {
            Path.Combine(appData, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
            Path.Combine(appData, "Microsoft Flight Simulator", "UserCfg.opt"),
        };

        try
        {
            var packagesRoot = Path.Combine(localAppData, "Packages");
            if (Directory.Exists(packagesRoot))
            {
                foreach (var dir in Directory.GetDirectories(packagesRoot, "Microsoft.Limitless_*"))
                {
                    cfgCandidates.Add(Path.Combine(dir, "LocalCache", "UserCfg.opt"));
                }
            }
        }
        catch
        {
            // ignore enumeration errors
        }

        if (extraUserCfgPaths is not null)
            cfgCandidates.AddRange(extraUserCfgPaths);

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cfg in cfgCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(cfg))
                continue;
            try
            {
                var text = File.ReadAllText(cfg);
                var packages = ParseInstalledPackagesPath(text);
                var community = ToCommunityPath(packages);
                if (community is null)
                    continue;
                if (Directory.Exists(community) && seen.Add(community))
                    results.Add(community);
            }
            catch
            {
                // skip unreadable cfg
            }
        }

        // Best-effort fallbacks (only if exist)
        foreach (var fallback in GetFallbackCommunityCandidates())
        {
            try
            {
                var full = Path.GetFullPath(fallback);
                if (Directory.Exists(full) && seen.Add(full))
                    results.Add(full);
            }
            catch
            {
                // ignore
            }
        }

        return results;
    }

    private static IEnumerable<string> GetFallbackCommunityCandidates()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(userProfile, "AppData", "Roaming", "Microsoft Flight Simulator 2024", "Community");
        // Steam-style default is highly variable; user browse covers the rest.
    }

    public static string GetPackageInstallPath(string communityFolder) =>
        Path.GetFullPath(Path.Combine(communityFolder, InstallerConstants.PackageFolderName));

    public static bool LooksLikeCommunityFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            var name = new DirectoryInfo(Path.GetFullPath(path)).Name;
            return string.Equals(name, "Community", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
