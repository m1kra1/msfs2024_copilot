using System.Text.Json;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppSettings LoadSettings(string settingsPath)
    {
        if (!File.Exists(settingsPath))
            throw new FileNotFoundException("settings.json not found", settingsPath);

        var json = File.ReadAllText(settingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize settings.json");
    }

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    /// <summary>Persists settings.json using the same property names as the schema (JsonPropertyName).</summary>
    public static void SaveSettings(string settingsPath, AppSettings settings)
    {
        var dir = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, WriteOptions);
        File.WriteAllText(settingsPath, json);
    }

    public static IReadOnlyList<string> ListAircraftProfiles(string configRoot)
    {
        var aircraftDir = Path.Combine(configRoot, "aircraft");
        if (!Directory.Exists(aircraftDir))
            return Array.Empty<string>();

        return Directory.GetFiles(aircraftDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static CommandCatalog LoadBaseCommands(string baseCommandsPath)
    {
        if (!File.Exists(baseCommandsPath))
            throw new FileNotFoundException("base_commands.json not found", baseCommandsPath);

        var json = File.ReadAllText(baseCommandsPath);
        return JsonSerializer.Deserialize<CommandCatalog>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize base_commands.json");
    }

    public static AircraftProfile LoadAircraftProfile(string profilePath)
    {
        if (!File.Exists(profilePath))
            throw new FileNotFoundException("Aircraft profile not found", profilePath);

        var json = File.ReadAllText(profilePath);
        return JsonSerializer.Deserialize<AircraftProfile>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize {profilePath}");
    }

    /// <summary>
    /// Merges base commands with an aircraft profile. Profile commands with the same id replace base;
    /// new ids are appended. Event aliases rewrite action event names.
    /// </summary>
    public static CommandCatalog Merge(CommandCatalog baseCatalog, AircraftProfile profile)
    {
        var byId = new Dictionary<string, CommandDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in baseCatalog.Commands)
            byId[cmd.Id] = CloneCommand(cmd);

        foreach (var cmd in profile.Commands)
            byId[cmd.Id] = CloneCommand(cmd);

        var merged = byId.Values.Select(c => ApplyEventAliases(c, profile.EventAliases)).ToList();
        return new CommandCatalog { Commands = merged };
    }

    public static string ResolveConfigRoot(string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot) && Directory.Exists(explicitRoot))
            return Path.GetFullPath(explicitRoot);

        // 1) next to EXE (published extras layout)
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "config");
        if (Directory.Exists(candidate))
            return candidate;

        // 2) extras folder when running from repo publish path
        candidate = Path.Combine(baseDir, "..", "config");
        if (Directory.Exists(Path.GetFullPath(candidate)))
            return Path.GetFullPath(candidate);

        // 3) PackageSources/extras/config relative to common host source locations
        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            candidate = Path.Combine(dir.FullName, "PackageSources", "extras", "config");
            if (Directory.Exists(candidate))
                return candidate;

            candidate = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(candidate, "settings.json")))
                return candidate;
        }

        throw new DirectoryNotFoundException(
            "Could not locate config directory (expected settings.json + base_commands.json).");
    }

    public static (AppSettings Settings, CommandCatalog Commands) LoadAll(
        string? configRoot = null,
        string? profileOverride = null)
    {
        var root = ResolveConfigRoot(configRoot);
        var settings = LoadSettings(Path.Combine(root, "settings.json"));
        var baseCommands = LoadBaseCommands(Path.Combine(root, "base_commands.json"));

        var profileName = string.IsNullOrWhiteSpace(profileOverride)
            ? settings.AircraftProfile
            : profileOverride!;

        var profilePath = Path.Combine(root, "aircraft", $"{profileName}.json");
        AircraftProfile profile;
        if (File.Exists(profilePath))
            profile = LoadAircraftProfile(profilePath);
        else
            profile = new AircraftProfile { ProfileId = profileName, Title = "missing profile" };

        var merged = Merge(baseCommands, profile);
        return (settings, merged);
    }

    private static CommandDefinition CloneCommand(CommandDefinition src) => new()
    {
        Id = src.Id,
        Phrases = src.Phrases.ToList(),
        Response = src.Response,
        RejectResponse = src.RejectResponse,
        Conditions = src.Conditions.Select(c => new ConditionDefinition
        {
            SimVar = c.SimVar,
            Op = c.Op,
            Value = c.Value,
            Units = c.Units
        }).ToList(),
        Actions = src.Actions.Select(a => new ActionDefinition
        {
            Type = a.Type,
            Name = a.Name,
            Value = a.Value,
            Units = a.Units
        }).ToList(),
        RequirePositiveClimbFlag = src.RequirePositiveClimbFlag,
        Checklist = src.Checklist
    };

    private static CommandDefinition ApplyEventAliases(
        CommandDefinition cmd,
        Dictionary<string, string> aliases)
    {
        if (aliases.Count == 0)
            return cmd;

        foreach (var action in cmd.Actions)
        {
            if (action.Type.Equals("event", StringComparison.OrdinalIgnoreCase)
                && aliases.TryGetValue(action.Name, out var mapped))
            {
                action.Name = mapped;
            }
        }

        return cmd;
    }
}
