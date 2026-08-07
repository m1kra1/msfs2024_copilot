using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using Xunit;

namespace CoPilotVoiceHost.Tests;

/// <summary>
/// Drives the real shipped loaders + phrase matcher + condition engine + action executor.
/// Uses the package base_commands.json on disk (not a reimplemented catalog).
/// </summary>
public class CommandPipelineTests
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

        // Fallback: relative from test assembly when copied
        var linked = Path.Combine(AppContext.BaseDirectory, "config");
        if (File.Exists(Path.Combine(linked, "base_commands.json")))
            return linked;

        throw new DirectoryNotFoundException("Could not find PackageSources/extras/config for tests.");
    }

    private static (AppSettings Settings, CommandCatalog Catalog, CommandProcessor Processor, RecordingSimConnectClient Sim)
        CreatePipeline(bool requirePositiveClimb = true)
    {
        var root = FindConfigRoot();
        var (settings, catalog) = ConfigLoader.LoadAll(root, "generic");
        settings.Behavior.RequirePositiveClimbForGearUp = requirePositiveClimb;

        var sim = new RecordingSimConnectClient();
        sim.Connect(settings.SimConnect.AppName, settings.SimConnect.ConfigIndex);

        var matcher = new PhraseMatcher(catalog.Commands);
        var conditions = new ConditionEngine();
        var executor = new ActionExecutor(sim);
        var processor = new CommandProcessor(matcher, conditions, executor, settings.Behavior);
        return (settings, catalog, processor, sim);
    }

    [Fact]
    public void LoadAll_Loads_Shipped_BaseCommands_With_Core_Set()
    {
        var root = FindConfigRoot();
        var (settings, catalog) = ConfigLoader.LoadAll(root, "generic");

        Assert.Equal("PrivateCoPilotVoice", settings.SimConnect.AppName);
        Assert.Equal("Co Pilot", settings.Speech.WakeWord);
        Assert.Equal("F12", settings.Speech.PttKey);
        Assert.False(settings.Speech.ContinuousListen);
        Assert.True(settings.Behavior.RequirePositiveClimbForGearUp);

        var ids = catalog.Commands.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[]
                 {
                     "gear_up", "gear_down", "landing_lights_on", "flaps_up", "autopilot_on",
                     "anti_ice_on", "parking_brake_on", "ap_heading_hold", "ap_altitude_hold"
                 })
        {
            Assert.Contains(required, ids);
        }

        Assert.Contains(catalog.Commands, c => c.Id == "gear_up"
            && c.Actions.Any(a => a.Name == "GEAR_UP"));
    }

    [Fact]
    public void PhraseMatcher_Matches_GearUp_And_LandingLights()
    {
        var (_, catalog, _, _) = CreatePipeline();
        var matcher = new PhraseMatcher(catalog.Commands);

        var (cmd1, phrase1, _) = matcher.Match("positive climb gear up", "Co Pilot");
        Assert.NotNull(cmd1);
        Assert.Equal("gear_up", cmd1!.Id);
        Assert.Contains("gear up", phrase1);

        var (cmd2, _, _) = matcher.Match("landing lights on");
        Assert.NotNull(cmd2);
        Assert.Equal("landing_lights_on", cmd2!.Id);

        var (cmd3, _, _) = matcher.Match("Co Pilot gear down", "Co Pilot");
        Assert.NotNull(cmd3);
        Assert.Equal("gear_down", cmd3!.Id);
    }

    [Fact]
    public void GearUp_Allowed_When_PositiveClimb_And_GearDown()
    {
        var (_, _, processor, sim) = CreatePipeline(requirePositiveClimb: true);
        sim.Snapshot.Set("VERTICAL SPEED", 450);
        sim.Snapshot.Set("GEAR POSITION", 1); // down

        var result = processor.Process("positive climb gear up", sim.Snapshot, "Co Pilot");
        Assert.NotNull(result);
        Assert.True(result!.Allowed);
        Assert.Equal("gear_up", result.CommandId);
        Assert.Contains("Gear up", result.SpokenResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.ActionsExecuted, a => a.Type == "event" && a.Name == "GEAR_UP");
        Assert.Contains(sim.TransmittedEvents, e => e.Name == "GEAR_UP");
    }

    [Fact]
    public void GearUp_Denied_When_Not_PositiveClimb()
    {
        var (_, _, processor, sim) = CreatePipeline(requirePositiveClimb: true);
        sim.Snapshot.Set("VERTICAL SPEED", 50); // below 100 fpm
        sim.Snapshot.Set("GEAR POSITION", 1);

        var result = processor.Process("gear up", sim.Snapshot);
        Assert.NotNull(result);
        Assert.False(result!.Allowed);
        Assert.Equal("gear_up", result.CommandId);
        Assert.Empty(result.ActionsExecuted);
        Assert.DoesNotContain(sim.TransmittedEvents, e => e.Name == "GEAR_UP");
        Assert.False(string.IsNullOrWhiteSpace(result.SpokenResponse));
    }

    [Fact]
    public void GearUp_Denied_When_Gear_Already_Up()
    {
        var (_, _, processor, sim) = CreatePipeline(requirePositiveClimb: true);
        sim.Snapshot.Set("VERTICAL SPEED", 800);
        sim.Snapshot.Set("GEAR POSITION", 0); // up

        var result = processor.Process("gear up", sim.Snapshot);
        Assert.NotNull(result);
        Assert.False(result!.Allowed);
        Assert.DoesNotContain(sim.TransmittedEvents, e => e.Name == "GEAR_UP");
    }

    [Fact]
    public void LandingLightsOn_Emits_LANDING_LIGHTS_ON()
    {
        var (_, _, processor, sim) = CreatePipeline();
        var result = processor.Process("landing lights on", sim.Snapshot);
        Assert.NotNull(result);
        Assert.True(result!.Allowed);
        Assert.Contains(sim.TransmittedEvents, e => e.Name == "LANDING_LIGHTS_ON");
        Assert.Equal("Landing lights on.", result.SpokenResponse);
    }

    [Fact]
    public void Autopilot_Flaps_AntiIce_ParkingBrake_Emit_Standard_Events()
    {
        var (_, _, processor, sim) = CreatePipeline();

        void Expect(string phrase, string eventName)
        {
            sim.ClearLog();
            var r = processor.Process(phrase, sim.Snapshot);
            Assert.NotNull(r);
            Assert.True(r!.Allowed, $"Expected allow for '{phrase}'");
            Assert.Contains(sim.TransmittedEvents, e => e.Name == eventName);
        }

        Expect("autopilot on", "AUTOPILOT_ON");
        Expect("flaps up", "FLAPS_UP");
        Expect("anti ice on", "ANTI_ICE_ON");
        Expect("parking brake set", "PARKING_BRAKES");
        Expect("heading select", "AP_HDG_HOLD_ON");
    }

    [Fact]
    public void AircraftProfile_A320_Merges_Extra_Command()
    {
        var root = FindConfigRoot();
        var (_, catalog) = ConfigLoader.LoadAll(root, "a320");
        Assert.Contains(catalog.Commands, c => c.Id == "a320_managed_speed");
        Assert.Contains(catalog.Commands, c => c.Id == "gear_up"); // base retained
    }

    [Fact]
    public void ConditionEngine_Compare_Operators()
    {
        Assert.True(ConditionEngine.Compare(5, ">", 1));
        Assert.True(ConditionEngine.Compare(1, "==", 1));
        Assert.False(ConditionEngine.Compare(1, "==", 2));
        Assert.True(ConditionEngine.Compare(3, "<=", 3));
    }

    [Fact]
    public void UniqueIds_Are_Distinct()
    {
        var defineIds = Enum.GetValues<PrivateCopilotDefineId>().Cast<uint>().ToList();
        var requestIds = Enum.GetValues<PrivateCopilotRequestId>().Cast<uint>().ToList();
        var eventIds = Enum.GetValues<PrivateCopilotEventId>().Cast<uint>().ToList();

        Assert.Equal(defineIds.Count, defineIds.Distinct().Count());
        Assert.Equal(requestIds.Count, requestIds.Distinct().Count());
        Assert.Equal(eventIds.Count, eventIds.Distinct().Count());
        Assert.DoesNotContain(defineIds[0], eventIds);
        Assert.True(StandardEventMap.TryGet("GEAR_UP", out _));
    }
}
