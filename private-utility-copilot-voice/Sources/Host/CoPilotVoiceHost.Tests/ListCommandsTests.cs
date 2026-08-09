using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using Xunit;

namespace CoPilotVoiceHost.Tests;

/// <summary>
/// Drives the shipped inject/process path for the dynamic list_commands feature.
/// Uses real base_commands.json + aircraft profiles from PackageSources.
/// </summary>
public class ListCommandsTests
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

        var linked = Path.Combine(AppContext.BaseDirectory, "config");
        if (File.Exists(Path.Combine(linked, "base_commands.json")))
            return linked;

        throw new DirectoryNotFoundException("Could not find PackageSources/extras/config for tests.");
    }

    [Fact]
    public void BaseCommands_Json_Defines_ListCommands_With_Required_Phrases()
    {
        var root = FindConfigRoot();
        var catalog = ConfigLoader.LoadBaseCommands(Path.Combine(root, "base_commands.json"));
        var cmd = catalog.Commands.FirstOrDefault(c => c.Id == CommandListBuilder.CommandId);
        Assert.NotNull(cmd);
        Assert.Empty(cmd!.Actions);
        Assert.Empty(cmd.Conditions);

        var phrases = cmd.Phrases.Select(p => p.ToLowerInvariant()).ToHashSet();
        foreach (var required in new[]
                 {
                     "list commands", "what can you do", "command list", "available commands"
                 })
        {
            Assert.Contains(required, phrases);
        }
    }

    [Fact]
    public void Processor_ListCommands_Speaks_Summary_And_DetailLines_Include_All_Ids()
    {
        var root = FindConfigRoot();
        var (settings, catalog) = ConfigLoader.LoadAll(root, "a320");
        Assert.Contains(catalog.Commands, c => c.Id == "a320_managed_speed");

        var sim = new RecordingSimConnectClient();
        sim.Connect(settings.SimConnect.AppName, settings.SimConnect.ConfigIndex);

        var matcher = new PhraseMatcher(catalog.Commands);
        var processor = new CommandProcessor(
            matcher,
            new ConditionEngine(),
            new ActionExecutor(sim),
            settings.Behavior,
            catalog.Commands);

        string? spoken = null;
        var result = processor.Process(
            "what can you do",
            sim.Snapshot,
            "Co Pilot",
            speakWithDelay: (text, _) => spoken = text);

        Assert.NotNull(result);
        Assert.True(result!.Allowed);
        Assert.Equal(CommandListBuilder.CommandId, result.CommandId);
        Assert.Empty(result.ActionsExecuted);
        Assert.Empty(sim.TransmittedEvents);

        Assert.False(string.IsNullOrWhiteSpace(spoken));
        Assert.Equal(spoken, result.SpokenResponse);
        Assert.Contains("available", result.SpokenResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(catalog.Commands.Count.ToString(), result.SpokenResponse);

        Assert.NotEmpty(result.DetailLogLines);
        var joined = string.Join("\n", result.DetailLogLines);
        foreach (var cmd in catalog.Commands)
        {
            Assert.Contains(cmd.Id, joined, StringComparison.OrdinalIgnoreCase);
            if (cmd.Phrases.Count > 0)
                Assert.Contains(cmd.Phrases[0], joined, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("a320_managed_speed", joined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostSession_Inject_ListCommands_Logs_Full_List_Offline()
    {
        var root = FindConfigRoot();
        using var session = new HostSession(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true,
            AllowOfflineFallback = true,
            Profile = "generic"
        });
        session.Start();

        var code = session.InjectPhrase("Co Pilot list commands", forceGate: false);
        Assert.Equal(0, code);
        Assert.Equal("(no actions)", session.LastAction);

        var log = string.Join("\n", session.Log.Snapshot().Select(e => e.Message));
        Assert.Contains("[CommandList] Available commands", log);
        Assert.Contains("list_commands", log);
        Assert.Contains("gear_up", log);
        Assert.Contains("[Response]", log);
        Assert.Contains("available", log, StringComparison.OrdinalIgnoreCase);
        // generic profile must NOT include a320-only command
        Assert.DoesNotContain("a320_managed_speed", log);
    }

    [Fact]
    public void HostSession_ProfileSwitch_Changes_List_Output()
    {
        var root = FindConfigRoot();
        using var session = new HostSession(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true,
            AllowOfflineFallback = true
        });
        session.Start();

        // generic first
        session.Settings.AircraftProfile = "generic";
        session.ApplySettingsFromUi(session.Settings, saveToDisk: false, rebuildCatalog: true);
        session.Log.Clear();
        Assert.Equal(0, session.InjectPhrase("Co Pilot available commands"));
        var logGeneric = string.Join("\n", session.Log.Snapshot().Select(e => e.Message));
        Assert.Contains("gear_up", logGeneric);
        Assert.DoesNotContain("a320_managed_speed", logGeneric);

        // switch to a320 — aircraft-only command must appear
        session.Settings.AircraftProfile = "a320";
        session.ApplySettingsFromUi(session.Settings, saveToDisk: false, rebuildCatalog: true);
        Assert.Contains(session.Catalog.Commands, c => c.Id == "a320_managed_speed");
        session.Log.Clear();
        Assert.Equal(0, session.InjectPhrase("Co Pilot command list"));
        var logA320 = string.Join("\n", session.Log.Snapshot().Select(e => e.Message));
        Assert.Contains("a320_managed_speed", logA320);
        Assert.Contains("managed speed", logA320, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gear_up", logA320);

        // back to generic — aircraft-only gone again
        session.Settings.AircraftProfile = "generic";
        session.ApplySettingsFromUi(session.Settings, saveToDisk: false, rebuildCatalog: true);
        session.Log.Clear();
        Assert.Equal(0, session.InjectPhrase("Co Pilot what can you do"));
        var logBack = string.Join("\n", session.Log.Snapshot().Select(e => e.Message));
        Assert.DoesNotContain("a320_managed_speed", logBack);
        Assert.Contains("gear_up", logBack);
    }

    [Fact]
    public void LandingLights_Still_Works_After_ListCommands_Present()
    {
        var root = FindConfigRoot();
        using var session = new HostSession(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true,
            AllowOfflineFallback = true
        });
        session.Start();
        var code = session.InjectPhrase("Co Pilot landing lights on");
        Assert.Equal(0, code);
        Assert.Contains("LANDING_LIGHTS_ON", session.LastAction, StringComparison.OrdinalIgnoreCase);
    }
}
