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

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    private static readonly JsonSerializerOptions WriteCommandsOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string SettingsPath(string configRoot) =>
        Path.Combine(configRoot, HostConstants.SettingsFileName);

    /// <summary>
    /// Parent of config root is the extras package root (config + voices sit side-by-side).
    /// </summary>
    public static string ResolveExtrasRoot(string configRoot) =>
        Path.GetFullPath(Path.Combine(configRoot, ".."));

    public static string BaseCommandsPath(string configRoot) =>
        Path.Combine(configRoot, HostConstants.BaseCommandsFileName);

    public static string AircraftDetectionPath(string configRoot) =>
        Path.Combine(configRoot, HostConstants.AircraftDetectionFileName);

    public static string LearnWatchlistPath(string configRoot) =>
        Path.Combine(configRoot, HostConstants.LearnWatchlistFileName);

    public static string ChecklistsDirectory(string configRoot) =>
        Path.Combine(configRoot, HostConstants.ChecklistsDirectoryName);

    public static string ChecklistPath(string configRoot, string checklistId) =>
        Path.Combine(
            ChecklistsDirectory(configRoot),
            $"{(string.IsNullOrWhiteSpace(checklistId) ? "unnamed" : checklistId.Trim())}.json");

    public static string AircraftProfilePath(string configRoot, string? profileName) =>
        Path.Combine(
            configRoot,
            HostConstants.AircraftProfilesDirectoryName,
            $"{HostConstants.NormalizeProfileId(profileName)}.json");
    public static AppSettings LoadSettings(string settingsPath)
    {
        if (!File.Exists(settingsPath))
            throw new FileNotFoundException($"{HostConstants.SettingsFileName} not found", settingsPath);

        var json = File.ReadAllText(settingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize {HostConstants.SettingsFileName}");
    }

    /// <summary>Persists settings.json using the same property names as the schema (JsonPropertyName).</summary>
    public static void SaveSettings(string settingsPath, AppSettings settings)
    {
        var dir = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, WriteOptions);
        File.WriteAllText(settingsPath, json);
    }

    /// <summary>Persists base_commands.json (same schema as load).</summary>
    public static void SaveBaseCommands(string baseCommandsPath, CommandCatalog catalog)
    {
        var dir = Path.GetDirectoryName(baseCommandsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(catalog ?? new CommandCatalog(), WriteCommandsOptions);
        File.WriteAllText(baseCommandsPath, json);
    }

    /// <summary>Persists an aircraft profile JSON file.</summary>
    public static void SaveAircraftProfile(string profilePath, AircraftProfile profile)
    {
        var dir = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(profile ?? new AircraftProfile(), WriteCommandsOptions);
        File.WriteAllText(profilePath, json);
    }

    /// <summary>Deep-clones a command definition (safe for UI working copies).</summary>
    public static CommandDefinition CloneCommandPublic(CommandDefinition src) => CloneCommand(src);

    /// <summary>Deep-clones a catalog.</summary>
    public static CommandCatalog CloneCatalog(CommandCatalog src) =>
        new()
        {
            Commands = (src?.Commands ?? new List<CommandDefinition>())
                .Select(CloneCommand)
                .ToList()
        };

    /// <summary>Deep-clones an aircraft profile (commands + aliases).</summary>
    public static AircraftProfile CloneProfile(AircraftProfile src)
    {
        src ??= new AircraftProfile();
        return new AircraftProfile
        {
            ProfileId = src.ProfileId,
            Title = src.Title,
            Extends = src.Extends,
            Commands = src.Commands.Select(CloneCommand).ToList(),
            SimVarAliases = new Dictionary<string, string>(src.SimVarAliases, StringComparer.OrdinalIgnoreCase),
            EventAliases = new Dictionary<string, string>(src.EventAliases, StringComparer.OrdinalIgnoreCase),
            LearnWatch = (src.LearnWatch ?? new List<LearnWatchEntry>())
                .Select(CloneLearnWatch)
                .ToList()
        };
    }

    private static LearnWatchEntry CloneLearnWatch(LearnWatchEntry src) => new()
    {
        Name = src?.Name ?? "",
        Units = string.IsNullOrWhiteSpace(src?.Units) ? "number" : src!.Units,
        IsManual = src?.IsManual ?? false
    };

    public static IReadOnlyList<string> ListAircraftProfiles(string configRoot)
    {
        var aircraftDir = Path.Combine(configRoot, HostConstants.AircraftProfilesDirectoryName);
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
            throw new FileNotFoundException($"{HostConstants.BaseCommandsFileName} not found", baseCommandsPath);

        var json = File.ReadAllText(baseCommandsPath);
        return JsonSerializer.Deserialize<CommandCatalog>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize {HostConstants.BaseCommandsFileName}");
    }

    /// <summary>
    /// Loads aircraft_detection.json. Missing file yields empty rules + default fallback profile.
    /// </summary>
    public static AircraftDetectionConfig LoadAircraftDetection(string configRoot)
    {
        var path = AircraftDetectionPath(configRoot);
        if (!File.Exists(path))
            return new AircraftDetectionConfig { FallbackProfile = HostConstants.DefaultProfileId };

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AircraftDetectionConfig>(json, JsonOptions)
               ?? new AircraftDetectionConfig { FallbackProfile = HostConstants.DefaultProfileId };
    }

    /// <summary>
    /// Loads config/learn_watchlist.json. Missing file → empty excludes/defaults (built-in continuous excludes still apply).
    /// </summary>
    public static LearnWatchlistConfig LoadLearnWatchlist(string configRoot)
    {
        var path = LearnWatchlistPath(configRoot);
        if (!File.Exists(path))
            return new LearnWatchlistConfig();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LearnWatchlistConfig>(json, JsonOptions)
                   ?? new LearnWatchlistConfig();
        }
        catch
        {
            return new LearnWatchlistConfig();
        }
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
    /// Loads all <c>config/checklists/*.json</c>. Missing directory → empty list.
    /// Invalid files are skipped (caller may log via returned skip messages).
    /// </summary>
    public static IReadOnlyList<ChecklistDefinition> LoadAllChecklists(string configRoot)
    {
        var dir = ChecklistsDirectory(configRoot);
        if (!Directory.Exists(dir))
            return Array.Empty<ChecklistDefinition>();

        var list = new List<ChecklistDefinition>();
        foreach (var path in Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var json = File.ReadAllText(path);
                var def = JsonSerializer.Deserialize<ChecklistDefinition>(json, JsonOptions);
                if (def is null) continue;
                if (string.IsNullOrWhiteSpace(def.Id))
                    def.Id = Path.GetFileNameWithoutExtension(path) ?? "";
                list.Add(def);
            }
            catch
            {
                // Skip unreadable files; HostSession may re-load after fix.
            }
        }

        return list;
    }

    public static void SaveChecklist(string configRoot, ChecklistDefinition checklist)
    {
        if (checklist is null) throw new ArgumentNullException(nameof(checklist));
        if (string.IsNullOrWhiteSpace(checklist.Id))
            throw new ArgumentException("Checklist id required", nameof(checklist));

        var path = ChecklistPath(configRoot, checklist.Id);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(checklist, WriteCommandsOptions);
        File.WriteAllText(path, json);
    }

    public static bool DeleteChecklistFile(string configRoot, string checklistId)
    {
        var path = ChecklistPath(configRoot, checklistId);
        if (!File.Exists(path))
            return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Deep-clones a checklist definition (safe for UI working copies).</summary>
    public static ChecklistDefinition CloneChecklist(ChecklistDefinition src)
    {
        src ??= new ChecklistDefinition();
        return new ChecklistDefinition
        {
            Id = src.Id,
            Name = src.Name,
            Phrases = (src.Phrases ?? new List<string>()).ToList(),
            GlobalDelayMs = src.GlobalDelayMs,
            AssignedProfiles = (src.AssignedProfiles ?? new List<string>()).ToList(),
            Items = (src.Items ?? new List<ChecklistItem>()).Select(CloneChecklistItem).ToList()
        };
    }

    public static IReadOnlyList<ChecklistDefinition> CloneChecklists(IEnumerable<ChecklistDefinition>? list) =>
        (list ?? Enumerable.Empty<ChecklistDefinition>()).Select(CloneChecklist).ToList();

    private static ChecklistItem CloneChecklistItem(ChecklistItem src) => new()
    {
        Mode = src?.Mode ?? ChecklistItemMode.Verify,
        Challenge = src?.Challenge ?? "",
        CommandId = src?.CommandId,
        Action = src?.Action is null
            ? null
            : new ActionDefinition
            {
                Type = src.Action.Type,
                Name = src.Action.Name,
                Value = src.Action.Value,
                Units = src.Action.Units
            },
        Actions = (src?.Actions ?? new List<ActionDefinition>())
            .Select(a => new ActionDefinition
            {
                Type = a.Type,
                Name = a.Name,
                Value = a.Value,
                Units = a.Units
            }).ToList(),
        Expected = src?.Expected is null
            ? null
            : new ConditionDefinition
            {
                SimVar = src.Expected.SimVar,
                Op = src.Expected.Op,
                Value = src.Expected.Value,
                Units = src.Expected.Units
            },
        ExpectedList = (src?.ExpectedList ?? new List<ConditionDefinition>())
            .Select(c => new ConditionDefinition
            {
                SimVar = c.SimVar,
                Op = c.Op,
                Value = c.Value,
                Units = c.Units
            }).ToList(),
        ResponseOk = src?.ResponseOk,
        DelayAfterMs = src?.DelayAfterMs ?? 0
    };

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
            if (File.Exists(Path.Combine(candidate, HostConstants.SettingsFileName)))
                return candidate;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate config directory (expected {HostConstants.SettingsFileName} + {HostConstants.BaseCommandsFileName}).");
    }

    public static (AppSettings Settings, CommandCatalog Commands) LoadAll(
        string? configRoot = null,
        string? profileOverride = null)
    {
        var root = ResolveConfigRoot(configRoot);
        var settings = LoadSettings(SettingsPath(root));
        var baseCommands = LoadBaseCommands(BaseCommandsPath(root));

        var profileName = HostConstants.NormalizeProfileId(
            string.IsNullOrWhiteSpace(profileOverride) ? settings.AircraftProfile : profileOverride);

        var profilePath = AircraftProfilePath(root, profileName);
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
            if (action.Type.Equals(HostConstants.ActionTypeEvent, StringComparison.OrdinalIgnoreCase)
                && aliases.TryGetValue(action.Name, out var mapped))
            {
                action.Name = mapped;
            }
        }

        return cmd;
    }
}
