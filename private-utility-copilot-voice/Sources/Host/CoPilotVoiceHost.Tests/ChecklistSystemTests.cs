using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using Xunit;

namespace CoPilotVoiceHost.Tests;

public class ChecklistSystemTests
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
        var tmp = Path.Combine(Path.GetTempPath(), "copilot-cl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "aircraft"));
        Directory.CreateDirectory(Path.Combine(tmp, "checklists"));
        File.Copy(Path.Combine(root, "settings.json"), Path.Combine(tmp, "settings.json"));
        File.Copy(Path.Combine(root, "base_commands.json"), Path.Combine(tmp, "base_commands.json"));
        foreach (var f in Directory.GetFiles(Path.Combine(root, "aircraft"), "*.json"))
            File.Copy(f, Path.Combine(tmp, "aircraft", Path.GetFileName(f)));
        var clDir = Path.Combine(root, "checklists");
        if (Directory.Exists(clDir))
        {
            foreach (var f in Directory.GetFiles(clDir, "*.json"))
                File.Copy(f, Path.Combine(tmp, "checklists", Path.GetFileName(f)));
        }

        if (File.Exists(Path.Combine(root, "aircraft_detection.json")))
            File.Copy(Path.Combine(root, "aircraft_detection.json"), Path.Combine(tmp, "aircraft_detection.json"));
        return tmp;
    }

    private static HostSession StartOffline(string root, string? profile = null)
    {
        var session = new HostSession(new HostOptions
        {
            ConfigRoot = root,
            Profile = profile,
            ForceOffline = true,
            NoSpeech = true,
            NoTts = true,
            AllowOfflineFallback = true
        });
        session.Start();
        return session;
    }

    [Fact]
    public void LoadAllChecklists_Finds_Shipped_Samples()
    {
        var root = FindConfigRoot();
        var list = ConfigLoader.LoadAllChecklists(root);
        Assert.True(list.Count >= 3);
        Assert.Contains(list, c => c.Id == "before_start");
        Assert.Contains(list, c => c.Id == "before_takeoff");
        Assert.Contains(list, c => c.Id == "after_landing");
        foreach (var cl in list)
        {
            var v = ChecklistValidator.Validate(cl);
            Assert.True(v.IsValid, string.Join("; ", v.Errors));
        }
    }

    [Fact]
    public void Validator_Rejects_Phrase_Without_Checklist_Token()
    {
        var def = new ChecklistDefinition
        {
            Id = "bad",
            Name = "Bad",
            Phrases = new List<string> { "before start only" },
            Items = new List<ChecklistItem>
            {
                new()
                {
                    Mode = ChecklistItemMode.Execute,
                    Challenge = "X",
                    CommandId = "gear_up"
                }
            }
        };
        var r = ChecklistValidator.Validate(def);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("checklist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Profile_Filter_Empty_Assigned_Means_All()
    {
        var cl = new ChecklistDefinition { Id = "a", AssignedProfiles = new List<string>() };
        Assert.True(ChecklistCatalog.IsAssignedToProfile(cl, "generic"));
        Assert.True(ChecklistCatalog.IsAssignedToProfile(cl, "fenix_a320"));

        cl.AssignedProfiles = new List<string> { "fenix_a320" };
        Assert.False(ChecklistCatalog.IsAssignedToProfile(cl, "generic"));
        Assert.True(ChecklistCatalog.IsAssignedToProfile(cl, "fenix_a320"));
    }

    [Fact]
    public void Runner_Execute_Sequence_And_Delays()
    {
        var spoken = new List<string>();
        var executed = new List<string>();
        var catalog = new List<CommandDefinition>
        {
            new()
            {
                Id = "beacon_lights_on",
                Actions = new List<ActionDefinition>
                {
                    new() { Type = "event", Name = "BEACON_LIGHTS_ON" }
                }
            }
        };

        var runner = new ChecklistRunner(
            () => catalog,
            actions =>
            {
                foreach (var a in actions)
                    executed.Add(a.Name);
            },
            (t, _, _, _) => spoken.Add(t));

        var def = new ChecklistDefinition
        {
            Id = "exec_test",
            Name = "Exec Test",
            GlobalDelayMs = 0,
            Phrases = new List<string> { "exec test checklist" },
            Items = new List<ChecklistItem>
            {
                new()
                {
                    Mode = ChecklistItemMode.Execute,
                    Challenge = "Beacon",
                    CommandId = "beacon_lights_on",
                    ResponseOk = "On",
                    DelayAfterMs = 50
                },
                new()
                {
                    Mode = ChecklistItemMode.Execute,
                    Challenge = "Direct",
                    Action = new ActionDefinition { Type = "event", Name = "STROBES_ON" },
                    ResponseOk = "On",
                    DelayAfterMs = 0
                }
            }
        };

        runner.Start(def);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var snap = new SimVarSnapshot();

        // Item 0 execute
        runner.Tick(t0, snap);
        Assert.Contains("BEACON_LIGHTS_ON", executed);
        Assert.True(runner.IsActive);

        // Still delaying
        runner.Tick(t0.AddMilliseconds(10), snap);
        Assert.Single(executed);

        // After delay → item 1
        runner.Tick(t0.AddMilliseconds(60), snap);
        runner.Tick(t0.AddMilliseconds(70), snap);
        Assert.Contains("STROBES_ON", executed);

        // Complete
        runner.Tick(t0.AddMilliseconds(200), snap);
        Assert.False(runner.IsActive);
        Assert.Contains(spoken, s => s.Contains("complete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runner_Verify_Waits_Then_Passes_Or_Continue()
    {
        var spoken = new List<string>();
        var runner = new ChecklistRunner(
            () => Array.Empty<CommandDefinition>(),
            _ => { },
            (t, _, _, _) => spoken.Add(t));

        var def = new ChecklistDefinition
        {
            Id = "verify_test",
            Name = "Verify",
            GlobalDelayMs = 0,
            Phrases = new List<string> { "verify test checklist" },
            Items = new List<ChecklistItem>
            {
                new()
                {
                    Mode = ChecklistItemMode.Verify,
                    Challenge = "Parking brake",
                    Expected = new ConditionDefinition
                    {
                        SimVar = "BRAKE PARKING POSITION",
                        Op = "==",
                        Value = 1
                    },
                    ResponseOk = "Set",
                    DelayAfterMs = 0
                }
            }
        };

        var snap = new SimVarSnapshot();
        snap.Set("BRAKE PARKING POSITION", 0);
        var t0 = DateTime.UtcNow;

        runner.Start(def);
        runner.Tick(t0, snap);
        Assert.True(runner.IsActive);
        Assert.Equal(ChecklistRunState.WaitingVerify, runner.GetProgress().State);

        // Still waiting
        runner.Tick(t0.AddMilliseconds(100), snap);
        Assert.True(runner.IsActive);

        // Pass via sim state
        snap.Set("BRAKE PARKING POSITION", 1);
        runner.Tick(t0.AddMilliseconds(200), snap);
        runner.Tick(t0.AddMilliseconds(250), snap);
        Assert.False(runner.IsActive);
        Assert.Contains(spoken, s => s.Equals("Set", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runner_Verify_Continue_Skips_Wait()
    {
        var spoken = new List<string>();
        var runner = new ChecklistRunner(
            () => Array.Empty<CommandDefinition>(),
            _ => { },
            (t, _, _, _) => spoken.Add(t));

        var def = new ChecklistDefinition
        {
            Id = "verify_cont",
            Name = "Verify Cont",
            GlobalDelayMs = 0,
            Phrases = new List<string> { "verify cont checklist" },
            Items = new List<ChecklistItem>
            {
                new()
                {
                    Mode = ChecklistItemMode.Verify,
                    Challenge = "Something",
                    Expected = new ConditionDefinition
                    {
                        SimVar = "BRAKE PARKING POSITION",
                        Op = "==",
                        Value = 1
                    },
                    DelayAfterMs = 0
                }
            }
        };

        var snap = new SimVarSnapshot();
        snap.Set("BRAKE PARKING POSITION", 0);
        var t0 = DateTime.UtcNow;
        runner.Start(def);
        runner.Tick(t0, snap);
        Assert.Equal(ChecklistRunState.WaitingVerify, runner.GetProgress().State);

        runner.RequestContinue();
        runner.Tick(t0.AddMilliseconds(50), snap);
        runner.Tick(t0.AddMilliseconds(100), snap);
        Assert.False(runner.IsActive);
        Assert.Contains(spoken, s => s.Contains("Continuing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HostSession_Loads_Checklists_And_Voice_Start()
    {
        var root = FindConfigRoot();
        using var session = StartOffline(root, "generic");
        Assert.True(session.Checklists.Count >= 3);

        Assert.Equal(0, session.InjectPhrase("Co Pilot before takeoff checklist"));
        Assert.Contains("checklist:start:before_takeoff", session.LastAction, StringComparison.OrdinalIgnoreCase);

        // Drive ticks until complete (execute-only sample; no poll timer without speech).
        var t0 = DateTime.UtcNow;
        for (var i = 0; i < 80 && session.ChecklistActive; i++)
            session.PumpChecklist(1, t0.AddMilliseconds(i * 80));

        Assert.False(session.ChecklistActive);
    }

    [Fact]
    public void HostSession_ApplyChecklists_Save_RoundTrip()
    {
        var tmp = CopyConfigToTemp();
        try
        {
            using var session = StartOffline(tmp, "generic");
            var working = session.LoadChecklistsFromDisk().ToList();
            Assert.NotEmpty(working);

            var neo = new ChecklistDefinition
            {
                Id = "unit_roundtrip",
                Name = "Unit Roundtrip",
                Phrases = new List<string> { "unit roundtrip checklist" },
                GlobalDelayMs = 100,
                AssignedProfiles = new List<string> { "generic" },
                Items = new List<ChecklistItem>
                {
                    new()
                    {
                        Mode = ChecklistItemMode.Execute,
                        Challenge = "Lights",
                        CommandId = "landing_lights_on",
                        DelayAfterMs = 0
                    }
                }
            };
            working.Add(neo);
            session.ApplyChecklists(working, saveToDisk: true);

            Assert.True(File.Exists(Path.Combine(tmp, "checklists", "unit_roundtrip.json")));
            var reloaded = ConfigLoader.LoadAllChecklists(tmp);
            Assert.Contains(reloaded, c => c.Id == "unit_roundtrip");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void PhraseIndex_Requires_Checklist_Word_For_Start()
    {
        var idx = new ChecklistPhraseIndex(new[]
        {
            new ChecklistDefinition
            {
                Id = "before_start",
                Phrases = new List<string> { "before start checklist" }
            }
        });

        var (miss, _) = idx.MatchStart("run before start");
        Assert.Null(miss);

        var (hit, phrase) = idx.MatchStart("before start checklist");
        Assert.NotNull(hit);
        Assert.Contains("checklist", phrase, StringComparison.OrdinalIgnoreCase);
    }
}
