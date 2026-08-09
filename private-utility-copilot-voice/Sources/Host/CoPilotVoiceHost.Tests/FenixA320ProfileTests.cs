using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using Xunit;

namespace CoPilotVoiceHost.Tests;

/// <summary>
/// Drives shipped ConfigLoader / HostSession / CommandProcessor paths for the fenix_a320 profile.
/// </summary>
public class FenixA320ProfileTests
{
    private const string ProfileId = "fenix_a320";

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

    private static HostSession StartOfflineSession(string configRoot, string? profile = null)
    {
        var session = new HostSession(new HostOptions
        {
            ConfigRoot = configRoot,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true,
            AllowOfflineFallback = true,
            Profile = profile
        });
        session.Start();
        return session;
    }

    [Fact]
    public void FenixProfile_File_Exists_With_Expected_ProfileId()
    {
        var root = FindConfigRoot();
        var path = Path.Combine(root, "aircraft", $"{ProfileId}.json");
        Assert.True(File.Exists(path), $"Expected profile at {path}");

        var profile = ConfigLoader.LoadAircraftProfile(path);
        Assert.Equal(ProfileId, profile.ProfileId);
        Assert.Contains("Fenix", profile.Title, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(profile.Commands);
    }

    [Fact]
    public void ListAircraftProfiles_Includes_Fenix()
    {
        var root = FindConfigRoot();
        var profiles = ConfigLoader.ListAircraftProfiles(root);
        Assert.Contains(ProfileId, profiles);
        Assert.Contains("generic", profiles);
        Assert.Contains("a320", profiles);
        Assert.Contains("b737", profiles);
    }

    [Fact]
    public void LoadAll_Fenix_Merges_Base_Command_Ids_And_Unable_Overrides()
    {
        var root = FindConfigRoot();
        var (_, catalog) = ConfigLoader.LoadAll(root, ProfileId);

        // Base ids still present after merge
        foreach (var id in new[]
                 {
                     "gear_up", "gear_down", "landing_lights_on", "landing_lights_off",
                     "taxi_lights_on", "strobe_lights_on", "beacon_lights_on", "nav_lights_on",
                     "flaps_up", "flaps_1", "flaps_2", "flaps_3",
                     "parking_brake_on", "parking_brake_off",
                     "autopilot_on", "autopilot_off",
                     "flight_director_on", "flight_director_off",
                     "list_commands"
                 })
        {
            Assert.Contains(catalog.Commands, c => c.Id == id);
        }

        // Mapped minimum-group events (from fenix profile / merge)
        Assert.Equal("GEAR_DOWN", EventName(catalog, "gear_down"));
        Assert.Equal("LANDING_LIGHTS_ON", EventName(catalog, "landing_lights_on"));
        Assert.Equal("STROBES_ON", EventName(catalog, "strobe_lights_on"));
        Assert.Equal("BEACON_LIGHTS_ON", EventName(catalog, "beacon_lights_on"));
        Assert.Equal("NAV_LIGHTS_ON", EventName(catalog, "nav_lights_on"));
        Assert.Equal("FLAPS_1", EventName(catalog, "flaps_1"));
        Assert.Equal("PARKING_BRAKES", EventName(catalog, "parking_brake_on"));
        Assert.Equal("AUTOPILOT_ON", EventName(catalog, "autopilot_on"));
        Assert.Equal("TOGGLE_FLIGHT_DIRECTOR", EventName(catalog, "flight_director_on"));

        // Unmapped FCU modes: empty actions + Unable response
        foreach (var id in new[]
                 {
                     "ap_heading_hold", "ap_altitude_hold", "ap_speed_hold", "ap_vs_hold",
                     "ap_nav_mode", "ap_approach", "ap_loc_hold"
                 })
        {
            var cmd = catalog.Commands.Single(c => c.Id == id);
            Assert.Empty(cmd.Actions);
            Assert.Contains("Unable", cmd.Response, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not available", cmd.Response, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void HostSession_ListProfiles_And_Apply_Fenix_Rebuilds_Catalog()
    {
        var root = FindConfigRoot();
        using var session = StartOfflineSession(root, "generic");

        Assert.Contains(ProfileId, session.ListProfiles());

        session.Settings.AircraftProfile = ProfileId;
        session.ApplySettingsFromUi(session.Settings, saveToDisk: false, rebuildCatalog: true);

        Assert.Equal(ProfileId, session.AircraftProfile);
        Assert.Contains(session.Catalog.Commands, c => c.Id == "gear_down");
        var heading = session.Catalog.Commands.Single(c => c.Id == "ap_heading_hold");
        Assert.Empty(heading.Actions);
        Assert.Contains("Unable", heading.Response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostSession_Inject_Fenix_Minimum_Groups_Transmit_Declared_Events()
    {
        var root = FindConfigRoot();
        using var session = StartOfflineSession(root, ProfileId);

        var cases = new (string Phrase, string ExpectedEvent)[]
        {
            ("Co Pilot gear down", "GEAR_DOWN"),
            ("Co Pilot landing lights on", "LANDING_LIGHTS_ON"),
            ("Co Pilot landing lights off", "LANDING_LIGHTS_OFF"),
            ("Co Pilot taxi lights on", "TOGGLE_TAXI_LIGHTS"),
            ("Co Pilot strobe lights on", "STROBES_ON"),
            ("Co Pilot beacon on", "BEACON_LIGHTS_ON"),
            ("Co Pilot nav lights on", "NAV_LIGHTS_ON"),
            ("Co Pilot flaps one", "FLAPS_1"),
            ("Co Pilot flaps up", "FLAPS_UP"),
            ("Co Pilot parking brake set", "PARKING_BRAKES"),
            ("Co Pilot parking brake release", "PARKING_BRAKES"),
            ("Co Pilot autopilot on", "AUTOPILOT_ON"),
            ("Co Pilot autopilot off", "AUTOPILOT_OFF"),
            ("Co Pilot flight director on", "TOGGLE_FLIGHT_DIRECTOR"),
        };

        foreach (var (phrase, expected) in cases)
        {
            session.Log.Clear();
            var code = session.InjectPhrase(phrase);
            Assert.Equal(0, code);
            Assert.Contains(expected, session.LastAction, StringComparison.OrdinalIgnoreCase);
            var log = string.Join("\n", session.Log.Snapshot().Select(e => e.Message));
            Assert.Contains($"event:{expected}", log, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void HostSession_Inject_Fenix_Unmapped_Fcu_Speaks_Unable_No_Actions()
    {
        var root = FindConfigRoot();
        using var session = StartOfflineSession(root, ProfileId);

        foreach (var phrase in new[]
                 {
                     "Co Pilot heading hold",
                     "Co Pilot altitude hold",
                     "Co Pilot speed hold",
                     "Co Pilot vertical speed hold",
                     "Co Pilot nav mode",
                     "Co Pilot approach mode",
                     "Co Pilot localizer"
                 })
        {
            session.Log.Clear();
            var code = session.InjectPhrase(phrase);
            Assert.Equal(0, code);
            Assert.Equal("(no actions)", session.LastAction);
            var log = string.Join("\n", session.Log.Snapshot().Select(e => e.Message));
            Assert.Contains("[Response]", log);
            Assert.Contains("Unable", log, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not available", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[Action] event:", log);
        }
    }

    [Fact]
    public void HostSession_Switch_Fenix_Then_Generic_Without_Process_Restart()
    {
        var root = FindConfigRoot();
        using var session = StartOfflineSession(root, "generic");

        // generic: heading hold still has Boeing-style event
        var genericHeading = session.Catalog.Commands.Single(c => c.Id == "ap_heading_hold");
        Assert.Contains(genericHeading.Actions, a => a.Name == "AP_HDG_HOLD_ON");

        session.Settings.AircraftProfile = ProfileId;
        session.ApplySettingsFromUi(session.Settings, saveToDisk: false, rebuildCatalog: true);
        Assert.Empty(session.Catalog.Commands.Single(c => c.Id == "ap_heading_hold").Actions);

        session.Log.Clear();
        Assert.Equal(0, session.InjectPhrase("Co Pilot landing lights on"));
        Assert.Contains("LANDING_LIGHTS_ON", session.LastAction, StringComparison.OrdinalIgnoreCase);

        // switch back
        session.Settings.AircraftProfile = "generic";
        session.ApplySettingsFromUi(session.Settings, saveToDisk: false, rebuildCatalog: true);
        var back = session.Catalog.Commands.Single(c => c.Id == "ap_heading_hold");
        Assert.Contains(back.Actions, a => a.Name == "AP_HDG_HOLD_ON");

        session.Log.Clear();
        Assert.Equal(0, session.InjectPhrase("Co Pilot heading hold"));
        Assert.Contains("AP_HDG_HOLD_ON", session.LastAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Processor_Fenix_Mapped_And_Unable_Via_Shipped_Path()
    {
        var root = FindConfigRoot();
        var (settings, catalog) = ConfigLoader.LoadAll(root, ProfileId);
        var sim = new RecordingSimConnectClient();
        sim.Connect(settings.SimConnect.AppName, settings.SimConnect.ConfigIndex);

        var processor = new CommandProcessor(
            new PhraseMatcher(catalog.Commands),
            new ConditionEngine(),
            new ActionExecutor(sim),
            settings.Behavior,
            catalog.Commands);

        string? spoken = null;
        var gear = processor.Process(
            "gear down",
            sim.Snapshot,
            "Co Pilot",
            speakWithDelay: (t, _) => spoken = t);
        Assert.NotNull(gear);
        Assert.True(gear!.Allowed);
        Assert.Equal("gear_down", gear.CommandId);
        Assert.Contains(sim.TransmittedEvents, e => e.Name == "GEAR_DOWN");
        Assert.Equal("Checked. Gear down.", spoken);

        sim.ClearLog();
        spoken = null;
        var unable = processor.Process(
            "heading hold",
            sim.Snapshot,
            "Co Pilot",
            speakWithDelay: (t, _) => spoken = t);
        Assert.NotNull(unable);
        Assert.True(unable!.Allowed);
        Assert.Equal("ap_heading_hold", unable.CommandId);
        Assert.Empty(unable.ActionsExecuted);
        Assert.Empty(sim.TransmittedEvents);
        Assert.Contains("Unable", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Other_Profiles_Unchanged_Relative_To_PackageSources_Shipped_Files()
    {
        var root = FindConfigRoot();
        // Structural: existing profiles still load and do not carry fenix Unable overrides.
        foreach (var name in new[] { "generic", "a320", "b737" })
        {
            var path = Path.Combine(root, "aircraft", $"{name}.json");
            Assert.True(File.Exists(path));
            var profile = ConfigLoader.LoadAircraftProfile(path);
            Assert.Equal(name, profile.ProfileId);
            Assert.DoesNotContain(profile.Commands, c =>
                c.Id == "ap_heading_hold"
                && c.Actions.Count == 0
                && c.Response.Contains("Unable", StringComparison.OrdinalIgnoreCase));
        }

        var (_, a320Catalog) = ConfigLoader.LoadAll(root, "a320");
        var heading = a320Catalog.Commands.Single(c => c.Id == "ap_heading_hold");
        Assert.Contains(heading.Actions, a => a.Name == "AP_HDG_HOLD_ON");
        Assert.Contains(a320Catalog.Commands, c => c.Id == "a320_managed_speed");
    }

    private static string EventName(CommandCatalog catalog, string id)
    {
        var cmd = catalog.Commands.Single(c => c.Id == id);
        var action = Assert.Single(cmd.Actions);
        Assert.Equal("event", action.Type, ignoreCase: true);
        return action.Name;
    }
}
