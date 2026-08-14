using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoPilotVoiceSetup.Core;

/// <summary>SHA-256 payload digest + strict manifest package_name check.</summary>
public static class PayloadIntegrity
{
    public const string HashFileName = "payload.sha256";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static bool TryReadPackageIdentity(string manifestPath, out string? packageName, out string? version)
    {
        packageName = null;
        version = null;
        try
        {
            if (!File.Exists(manifestPath))
                return false;
            var dto = JsonSerializer.Deserialize<ManifestDto>(File.ReadAllText(manifestPath), JsonOptions);
            if (dto is null)
                return false;
            packageName = string.IsNullOrWhiteSpace(dto.PackageName) ? null : dto.PackageName.Trim();
            version = string.IsNullOrWhiteSpace(dto.PackageVersion) ? null : dto.PackageVersion.Trim();
            return packageName is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool HasExactPackageName(string? packageName) =>
        string.Equals(packageName, InstallerConstants.PackageFolderName, StringComparison.OrdinalIgnoreCase);

    public static string WriteHashManifest(string payloadRoot, string hashFilePath)
    {
        payloadRoot = Path.GetFullPath(payloadRoot);
        var lines = new List<string>();
        foreach (var file in Directory.GetFiles(payloadRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(payloadRoot, file).Replace('\\', '/');
            if (rel.Contains("..", StringComparison.Ordinal))
                continue;
            var hash = HashFile(file);
            lines.Add($"{hash}  {rel}");
        }

        var dir = Path.GetDirectoryName(hashFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllLines(hashFilePath, lines, Encoding.ASCII);
        return hashFilePath;
    }

    public static string? VerifyHashManifest(string payloadRoot, string hashFilePath)
    {
        if (!File.Exists(hashFilePath))
            return $"Missing integrity file: {hashFilePath}";

        payloadRoot = Path.GetFullPath(payloadRoot);
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(hashFilePath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var sep = line.IndexOf("  ", StringComparison.Ordinal);
            if (sep < 0)
                return "Integrity file is malformed.";
            var hash = line[..sep].Trim();
            var rel = line[(sep + 2)..].Trim().Replace('\\', '/');
            if (rel.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
                return $"Unsafe path in integrity file: {rel}";
            expected[rel] = hash;
        }

        if (expected.Count == 0)
            return "Integrity file is empty.";

        foreach (var (rel, hash) in expected)
        {
            var full = Path.GetFullPath(Path.Combine(payloadRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(payloadRoot, StringComparison.OrdinalIgnoreCase))
                return $"Integrity path escaped payload: {rel}";
            if (!File.Exists(full))
                return $"Payload file missing: {rel}";
            var actual = HashFile(full);
            if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
                return $"Payload hash mismatch: {rel}";
        }

        return null;
    }

    public static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private sealed class ManifestDto
    {
        [JsonPropertyName("package_name")]
        public string? PackageName { get; set; }

        [JsonPropertyName("package_version")]
        public string? PackageVersion { get; set; }
    }
}
