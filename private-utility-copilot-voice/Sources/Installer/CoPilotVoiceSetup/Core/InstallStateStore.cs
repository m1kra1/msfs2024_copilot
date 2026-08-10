using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoPilotVoiceSetup.Core;

public sealed class InstallState
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("community_path")]
    public string CommunityPath { get; set; } = "";

    [JsonPropertyName("package_path")]
    public string PackagePath { get; set; } = "";

    [JsonPropertyName("installed_utc")]
    public string InstalledUtc { get; set; } = "";

    [JsonPropertyName("desktop_shortcut")]
    public bool DesktopShortcut { get; set; }

    [JsonPropertyName("start_menu_shortcut")]
    public bool StartMenuShortcut { get; set; }
}

public static class InstallStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetDefaultStatePath(string? localAppData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, InstallerConstants.AppDataFolderName, InstallerConstants.InstallStateFileName);
    }

    public static string GetBackupRoot(string? localAppData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, InstallerConstants.AppDataFolderName, "backup");
    }

    public static InstallState? Load(string? statePath = null)
    {
        statePath ??= GetDefaultStatePath();
        if (!File.Exists(statePath))
            return null;
        try
        {
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<InstallState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(InstallState state, string? statePath = null)
    {
        statePath ??= GetDefaultStatePath();
        var dir = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(statePath, JsonSerializer.Serialize(state, JsonOptions));
    }

    public static void Delete(string? statePath = null)
    {
        statePath ??= GetDefaultStatePath();
        if (File.Exists(statePath))
            File.Delete(statePath);
    }
}
