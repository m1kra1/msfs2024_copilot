namespace CoPilotVoiceHost.Config;

/// <summary>
/// Confines config IDs and file writes to a single directory (same pattern as voice-pack manifests).
/// </summary>
public static class SafeConfigPath
{
    public const int MaxIdLength = 64;

    public static bool IsSafeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        var t = id.Trim();
        if (t.Length == 0 || t.Length > MaxIdLength)
            return false;
        if (t.Contains("..", StringComparison.Ordinal)
            || t.Contains('/') || t.Contains('\\')
            || t.Contains(':') || Path.IsPathRooted(t))
            return false;
        foreach (var c in t)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
                continue;
            return false;
        }

        return true;
    }

    public static string SanitizeId(string? id, string fallback)
    {
        if (IsSafeId(id))
            return id!.Trim();
        return IsSafeId(fallback) ? fallback : "unnamed";
    }

    public static bool IsUnderDirectory(string directory, string path)
    {
        try
        {
            var root = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(
                       full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                       root.TrimEnd(Path.DirectorySeparatorChar),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string ConfineFile(string directory, string? requestedPath, string defaultFileName)
    {
        Directory.CreateDirectory(directory);
        var root = Path.GetFullPath(directory);
        if (string.IsNullOrWhiteSpace(requestedPath))
            return Path.GetFullPath(Path.Combine(root, defaultFileName));

        var name = Path.GetFileName(requestedPath.Trim());
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal))
            name = defaultFileName;

        var candidate = Path.GetFullPath(requestedPath.Trim());
        if (IsUnderDirectory(root, candidate) && !Directory.Exists(candidate))
            return candidate;

        return Path.GetFullPath(Path.Combine(root, name));
    }
}
