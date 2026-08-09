using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using Xunit;

namespace CoPilotVoiceHost.Tests;

/// <summary>
/// Drives HostSession command-source Apply/Save used by the Commands UI (no WPF).
/// </summary>
public class CommandEditorTests
{
    private static string FindConfigRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "PackageSources", "extras", "config");
            if (File.Exists(Path.Combine(candidate, "base_commands.json")))
                return candidate;
        }

        throw new DirectoryNotFoundException("config root not found");
    }

    private static string CopyConfigToTemp()
    {
        var root = FindConfigRoot();
        var tmp = Path.Combine(Path.GetTempPath(), "copilot-cmd-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "aircraft"));
        File.Copy(Path.Combine(root, "settings.json"), Path.Combine(tmp, "settings.json"));
        File.Copy(Path.Combine(root, "base_commands.json"), Path.Combine(tmp, "base_commands.json"));
        foreach (var f in Directory.GetFiles(Path.Combine(root, "aircraft"), "*.json"))
            File.Copy(f, Path.Combine(tmp, "aircraft", Path.GetFileName(f)));
        return tmp;
    }

    [Fact]
    public void SaveBaseCommands_RoundTrip_Preserves_ListCommands()
    {
        var root = FindConfigRoot();
        var src = ConfigLoader.LoadBaseCommands(Path.Combine(root, "base_commands.json"));
        Assert.Contains(src.Commands, c => c.Id == "list_commands");

        var tmp = Path.Combine(Path.GetTempPath(), "base-cmds-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ConfigLoader.SaveBaseCommands(tmp, src);
            var reloaded = ConfigLoader.LoadBaseCommands(tmp);
            Assert.Equal(src.Commands.Count, reloaded.Commands.Count);
            Assert.Contains(reloaded.Commands, c => c.Id == "list_commands" && c.Actions.Count == 0);
            Assert.Contains(reloaded.Commands, c => c.Id == "gear_up"
                && c.Actions.Any(a => a.Name == "GEAR_UP"));
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ApplyCommandSources_InMemory_Adds_Command_Visible_To_Inject()
    {
        var tmp = CopyConfigToTemp();
        try
        {
            using var session = new HostSession(new HostOptions
            {
                ConfigRoot = tmp,
                ForceOffline = true,
                NoTts = true,
                NoSpeech = true,
                AllowOfflineFallback = true
            });
            session.Start();

            var baseCat = session.LoadBaseCommandsFromDisk();
            var profile = session.LoadActiveProfileFromDisk();
            Assert.DoesNotContain(baseCat.Commands, c => c.Id == "ui_test_ping");

            baseCat.Commands.Add(new CommandDefinition
            {
                Id = "ui_test_ping",
                Phrases = new List<string> { "ui test ping" },
                Response = "Ping from editor.",
                Actions = new List<ActionDefinition>(),
                Conditions = new List<ConditionDefinition>()
            });

            session.ApplyCommandSources(baseCat, profile, saveToDisk: false);

            Assert.Contains(session.Catalog.Commands, c => c.Id == "ui_test_ping");
            var code = session.InjectPhrase("Co Pilot ui test ping");
            Assert.Equal(0, code);
            Assert.Equal("(no actions)", session.LastAction);

            // Disk unchanged when saveToDisk false
            var disk = ConfigLoader.LoadBaseCommands(Path.Combine(tmp, "base_commands.json"));
            Assert.DoesNotContain(disk.Commands, c => c.Id == "ui_test_ping");
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ApplyCommandSources_Save_Writes_Base_And_Profile()
    {
        var tmp = CopyConfigToTemp();
        try
        {
            using var session = new HostSession(new HostOptions
            {
                ConfigRoot = tmp,
                ForceOffline = true,
                NoTts = true,
                NoSpeech = true,
                AllowOfflineFallback = true,
                Profile = "a320"
            });
            session.Start();
            Assert.Equal("a320", session.AircraftProfile);

            var baseCat = session.LoadBaseCommandsFromDisk();
            var profile = session.LoadActiveProfileFromDisk();
            Assert.Contains(profile.Commands, c => c.Id == "a320_managed_speed");

            baseCat.Commands.Add(new CommandDefinition
            {
                Id = "ui_saved_base",
                Phrases = new List<string> { "ui saved base" },
                Response = "Saved base.",
                Actions = new List<ActionDefinition>()
            });
            profile.Commands.Add(new CommandDefinition
            {
                Id = "ui_saved_profile",
                Phrases = new List<string> { "ui saved profile" },
                Response = "Saved profile.",
                Actions = new List<ActionDefinition>()
            });

            session.ApplyCommandSources(baseCat, profile, saveToDisk: true);

            var diskBase = ConfigLoader.LoadBaseCommands(Path.Combine(tmp, "base_commands.json"));
            var diskProfile = ConfigLoader.LoadAircraftProfile(Path.Combine(tmp, "aircraft", "a320.json"));
            Assert.Contains(diskBase.Commands, c => c.Id == "ui_saved_base");
            Assert.Contains(diskProfile.Commands, c => c.Id == "ui_saved_profile");
            Assert.Contains(session.Catalog.Commands, c => c.Id == "ui_saved_base");
            Assert.Contains(session.Catalog.Commands, c => c.Id == "ui_saved_profile");
            Assert.Contains(session.Catalog.Commands, c => c.Id == "a320_managed_speed");
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }
}
