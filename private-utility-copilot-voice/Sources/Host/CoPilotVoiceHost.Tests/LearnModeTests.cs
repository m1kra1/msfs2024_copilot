using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using Xunit;

namespace CoPilotVoiceHost.Tests;

public class LearnModeTests
{
    [Fact]
    public void CommandMappingIndex_Finds_SetSimVar_From_Catalog()
    {
        var catalog = new CommandCatalog
        {
            Commands =
            {
                new CommandDefinition
                {
                    Id = "gear_up",
                    Phrases = { "gear up" },
                    Actions =
                    {
                        new ActionDefinition
                        {
                            Type = HostConstants.ActionTypeSetSimVar,
                            Name = "L:S_MIP_GEAR",
                            Value = 0,
                            Units = "number"
                        }
                    }
                }
            }
        };

        var index = new CommandMappingIndex(catalog);
        var hits = index.Lookup("L:S_MIP_GEAR");
        Assert.Single(hits);
        Assert.Equal("gear_up", hits[0].Id);
        Assert.Equal(LearnMappingStatus.Mapped, index.GetStatus("L:S_MIP_GEAR"));
        // Bare / L: variants
        Assert.Single(index.Lookup("S_MIP_GEAR"));
    }

    [Fact]
    public void CommandMappingIndex_Unmapped_When_Missing()
    {
        var index = new CommandMappingIndex(new CommandCatalog());
        Assert.Empty(index.Lookup("LIGHT LANDING"));
        Assert.Equal(LearnMappingStatus.Unmapped, index.GetStatus("LIGHT LANDING"));
    }

    [Fact]
    public void CommandMappingIndex_Ambiguous_When_Multiple()
    {
        var catalog = new CommandCatalog
        {
            Commands =
            {
                new CommandDefinition
                {
                    Id = "a",
                    Phrases = { "a" },
                    Actions =
                    {
                        new ActionDefinition { Type = HostConstants.ActionTypeEvent, Name = "GEAR_UP" }
                    }
                },
                new CommandDefinition
                {
                    Id = "b",
                    Phrases = { "b" },
                    Actions =
                    {
                        new ActionDefinition { Type = HostConstants.ActionTypeEvent, Name = "GEAR_UP" }
                    }
                }
            }
        };

        var index = new CommandMappingIndex(catalog);
        Assert.Equal(2, index.Lookup("GEAR_UP").Count);
        Assert.Equal(LearnMappingStatus.Ambiguous, index.GetStatus("GEAR_UP"));
    }

    [Fact]
    public void LearnWatchBuilder_Excludes_Continuous()
    {
        var watches = LearnWatchBuilder.Build(new CommandCatalog());
        Assert.DoesNotContain(watches, w =>
            w.Name.Equals("AIRSPEED INDICATED", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(watches, w =>
            w.Name.Equals("PLANE ALTITUDE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(watches, w =>
            w.Name.Equals("VERTICAL SPEED", StringComparison.OrdinalIgnoreCase));
        // Discrete status still present
        Assert.Contains(watches, w =>
            w.Name.Equals("LIGHT LANDING", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(watches, w =>
            w.Name.Equals("SIM ON GROUND", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LearnWatchBuilder_Includes_Catalog_LVars()
    {
        var catalog = new CommandCatalog
        {
            Commands =
            {
                new CommandDefinition
                {
                    Id = "x",
                    Phrases = { "x" },
                    Actions =
                    {
                        new ActionDefinition
                        {
                            Type = HostConstants.ActionTypeSetSimVar,
                            Name = "L:S_MIP_GEAR",
                            Value = 1,
                            Units = "number"
                        }
                    }
                }
            }
        };

        var watches = LearnWatchBuilder.Build(catalog);
        Assert.Contains(watches, w =>
            w.Name.Equals("L:S_MIP_GEAR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LearnWatchBuilder_Includes_Manual_Even_If_Continuous()
    {
        var manual = new[]
        {
            new LearnWatchEntry { Name = "AIRSPEED INDICATED", Units = "knots", IsManual = true }
        };
        var watches = LearnWatchBuilder.Build(new CommandCatalog(), manual);
        Assert.Contains(watches, w =>
            w.Name.Equals("AIRSPEED INDICATED", StringComparison.OrdinalIgnoreCase) && w.IsManual);
    }

    [Fact]
    public void LearnCapture_Baseline_Does_Not_Emit()
    {
        var capture = new LearnCaptureService();
        var index = new CommandMappingIndex(new CommandCatalog());
        capture.Start(new[]
        {
            new LearnWatchEntry { Name = "LIGHT LANDING", Units = "bool" }
        });

        var snap = new SimVarSnapshot();
        snap.Set("LIGHT LANDING", 0);
        var added = capture.Observe(snap, index);
        Assert.Equal(0, added);
        Assert.Empty(capture.Recent);
    }

    [Fact]
    public void LearnCapture_Emits_On_Change()
    {
        var capture = new LearnCaptureService();
        var index = new CommandMappingIndex(new CommandCatalog());
        capture.Start(new[]
        {
            new LearnWatchEntry { Name = "LIGHT LANDING", Units = "bool" }
        });

        var snap = new SimVarSnapshot();
        snap.Set("LIGHT LANDING", 0);
        capture.Observe(snap, index);

        snap.Set("LIGHT LANDING", 1);
        var added = capture.Observe(snap, index);
        Assert.Equal(1, added);
        Assert.Single(capture.Recent);
        var d = capture.Recent[0];
        Assert.Equal("LIGHT LANDING", d.SignalName);
        Assert.Equal(0, d.OldValue);
        Assert.Equal(1, d.NewValue);
        Assert.Equal(LearnMappingStatus.Unmapped, d.MappingStatus);
        Assert.NotNull(d.SuggestedCommandId);
        Assert.Contains("learn_", d.SuggestedCommandId!);
        Assert.Contains(d.SuggestedActions, a =>
            a.Type.Equals(HostConstants.ActionTypeSetSimVar, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(d.SuggestedActions, a =>
            a.Type.Equals(HostConstants.ActionTypeEvent, StringComparison.OrdinalIgnoreCase)
            && a.Name.Equals("LANDING_LIGHTS_ON", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LearnCapture_Suppress_Works()
    {
        var capture = new LearnCaptureService();
        var index = new CommandMappingIndex(new CommandCatalog());
        capture.Start(new[]
        {
            new LearnWatchEntry { Name = "L:S_MIP_GEAR", Units = "number" }
        });

        var snap = new SimVarSnapshot();
        snap.Set("L:S_MIP_GEAR", 1);
        capture.Observe(snap, index);

        capture.SuppressSignals(new[] { "L:S_MIP_GEAR" }, durationMs: 5_000);
        snap.Set("L:S_MIP_GEAR", 0);
        var added = capture.Observe(snap, index);
        Assert.Equal(0, added);
        Assert.Empty(capture.Recent);
    }

    [Fact]
    public void LearnCapture_Recent_Bounded()
    {
        // Debounce off so rapid same-signal toggles each append a row.
        var capture = new LearnCaptureService(debounceMs: 0);
        var index = new CommandMappingIndex(new CommandCatalog());
        capture.Start(new[]
        {
            new LearnWatchEntry { Name = "LIGHT LANDING", Units = "bool" }
        });

        var snap = new SimVarSnapshot();
        snap.Set("LIGHT LANDING", 0);
        capture.Observe(snap, index);

        for (var i = 1; i <= HostConstants.LearnRecentCapacity + 25; i++)
        {
            snap.Set("LIGHT LANDING", i % 2);
            capture.Observe(snap, index);
        }

        Assert.Equal(HostConstants.LearnRecentCapacity, capture.Recent.Count);
    }

    [Fact]
    public void RecordingClient_SetLearnWatch_DoesNotThrow()
    {
        var client = new RecordingSimConnectClient();
        Assert.True(client.Connect("PrivateCoPilotVoice"));
        client.SetLearnWatchDefinitions(new[]
        {
            ("LIGHT LANDING", "bool"),
            ("L:S_MIP_GEAR", "number")
        });
        Assert.Equal(2, client.LearnWatches.Count);
        client.ClearLearnWatchDefinitions();
        Assert.Empty(client.LearnWatches);
        client.Dispose();
    }

    [Fact]
    public void SuggestCommandId_Sanitizes()
    {
        var id = LearnCaptureService.SuggestCommandId("L:S_MIP_GEAR", 0);
        Assert.StartsWith("learn_", id);
        Assert.Matches("^[a-z0-9_]+$", id);
        Assert.EndsWith("_0", id);
    }

    [Fact]
    public void LearnWatchBuilder_Includes_Global_And_Profile_Watches()
    {
        var global = new LearnWatchlistConfig
        {
            DefaultWatches =
            {
                new LearnWatchEntry { Name = "LIGHT WING", Units = "bool" }
            }
        };
        var profile = new[]
        {
            new LearnWatchEntry { Name = "L:S_MIP_GEAR", Units = "number" }
        };

        var watches = LearnWatchBuilder.Build(new CommandCatalog(), null, profile, global);
        Assert.Contains(watches, w => w.Name.Equals("LIGHT WING", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(watches, w => w.Name.Equals("L:S_MIP_GEAR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LearnWatchBuilder_Global_Exclude_Applies()
    {
        var global = new LearnWatchlistConfig
        {
            ExcludeNames = { "LIGHT LANDING" }
        };
        var watches = LearnWatchBuilder.Build(new CommandCatalog(), null, null, global);
        Assert.DoesNotContain(watches, w =>
            w.Name.Equals("LIGHT LANDING", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LearnActionHints_Suggests_Event_Plus_SetSimVar()
    {
        var actions = LearnActionHints.Suggest("LIGHT LANDING", 1, "bool");
        Assert.Equal(2, actions.Count);
        Assert.Equal(HostConstants.ActionTypeEvent, actions[0].Type);
        Assert.Equal("LANDING_LIGHTS_ON", actions[0].Name);
        Assert.Equal(HostConstants.ActionTypeSetSimVar, actions[1].Type);
        Assert.Equal("LIGHT LANDING", actions[1].Name);
    }

    [Fact]
    public void LearnCapture_Debounce_Updates_Same_Signal()
    {
        var capture = new LearnCaptureService();
        var index = new CommandMappingIndex(new CommandCatalog());
        capture.Start(new[]
        {
            new LearnWatchEntry { Name = "LIGHT LANDING", Units = "bool" }
        });

        var snap = new SimVarSnapshot();
        snap.Set("LIGHT LANDING", 0);
        capture.Observe(snap, index);

        snap.Set("LIGHT LANDING", 1);
        capture.Observe(snap, index);
        Assert.Single(capture.Recent);

        // Within debounce window: still one row, new value updated
        snap.Set("LIGHT LANDING", 0);
        var added = capture.Observe(snap, index);
        Assert.Equal(0, added);
        Assert.Single(capture.Recent);
        Assert.Equal(0, capture.Recent[0].NewValue);
        Assert.Equal(0, capture.Recent[0].OldValue); // original burst old kept from first 0→1? wait first was 0→1 so old=0, then update to 0 keeps old=0
    }

    [Fact]
    public void LearnCapture_GroupId_On_Multi_Var_Change()
    {
        var capture = new LearnCaptureService();
        var index = new CommandMappingIndex(new CommandCatalog());
        capture.Start(new[]
        {
            new LearnWatchEntry { Name = "LIGHT LANDING", Units = "bool" },
            new LearnWatchEntry { Name = "LIGHT STROBE", Units = "bool" }
        });

        var snap = new SimVarSnapshot();
        snap.Set("LIGHT LANDING", 0);
        snap.Set("LIGHT STROBE", 0);
        capture.Observe(snap, index);

        snap.Set("LIGHT LANDING", 1);
        snap.Set("LIGHT STROBE", 1);
        capture.Observe(snap, index);
        Assert.Equal(2, capture.Recent.Count);
        Assert.False(string.IsNullOrEmpty(capture.Recent[0].GroupId));
        Assert.Equal(capture.Recent[0].GroupId, capture.Recent[1].GroupId);
    }

    [Fact]
    public void ConfigLoader_LoadLearnWatchlist_From_PackageSources()
    {
        var root = FindPackageSourcesConfig();
        var wl = ConfigLoader.LoadLearnWatchlist(root);
        Assert.NotEmpty(wl.ExcludeNames);
        Assert.Contains(wl.ExcludeNames, n =>
            n.Equals("AIRSPEED INDICATED", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FenixProfile_Has_LearnWatch_Seed()
    {
        var root = FindPackageSourcesConfig();
        var path = ConfigLoader.AircraftProfilePath(root, "fenix_a320");
        Assert.True(File.Exists(path));
        var profile = ConfigLoader.LoadAircraftProfile(path);
        Assert.NotEmpty(profile.LearnWatch);
        Assert.Contains(profile.LearnWatch, w =>
            w.Name.Equals("L:S_MIP_GEAR", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindPackageSourcesConfig()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "PackageSources", "extras", "config");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "settings.json")))
                return candidate;
            candidate = Path.Combine(dir.FullName, "private-utility-copilot-voice", "PackageSources", "extras", "config");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "settings.json")))
                return candidate;
        }

        // Fallback: test output config (copied by csproj)
        var binConfig = Path.Combine(AppContext.BaseDirectory, "config");
        if (Directory.Exists(binConfig))
            return binConfig;

        throw new DirectoryNotFoundException("Could not locate PackageSources config for tests.");
    }
}
