using System.Text.Json;
using System.Text.Json.Serialization;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Host;

/// <summary>Reads <c>package_version</c> from an MSFS-style manifest.json via System.Text.Json.</summary>
public static class PackageVersionReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string? TryRead(string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return null;
        try
        {
            var json = File.ReadAllText(manifestPath);
            var dto = JsonSerializer.Deserialize<ManifestVersionDto>(json, JsonOptions);
            var v = dto?.PackageVersion?.Trim();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch
        {
            return null;
        }
    }

    public static string? TryFindNearConfig(string configRoot, string? fallbackVersion = null)
    {
        try
        {
            var dir = new DirectoryInfo(configRoot);
            for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var manifest = Path.Combine(dir.FullName, HostConstants.ManifestFileName);
                var v = TryRead(manifest);
                if (v is not null)
                    return v;

                manifest = Path.Combine(
                    dir.FullName, "Packages", HostConstants.PackageName, HostConstants.ManifestFileName);
                v = TryRead(manifest);
                if (v is not null)
                    return v;
            }
        }
        catch
        {
            // ignore
        }

        return fallbackVersion;
    }

    private sealed class ManifestVersionDto
    {
        [JsonPropertyName("package_version")]
        public string? PackageVersion { get; set; }
    }
}
