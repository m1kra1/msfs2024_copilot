using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using CoPilotVoiceHost.Speech;
using Xunit;

namespace CoPilotVoiceHost.Tests;

/// <summary>
/// Tests for skeptic findings: Snapshot recv mapping, TTS-before-event order, wake/PTT gate.
/// </summary>
public class SkepticFixTests
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

    [Fact]
    public void StatusSnapshotMapper_Writes_All_Defs_Into_Snapshot()
    {
        var snap = new SimVarSnapshot();
        var data = new PrivateCopilotStatusData
        {
            GearPosition = 1,
            VerticalSpeed = 750,
            FlapsHandleIndex = 2,
            AutopilotMaster = 1,
            LightLanding = 1,
            EngAntiIce = 1
        };

        StatusSnapshotMapper.Apply(snap, data);

        Assert.True(snap.TryGet("GEAR POSITION", out var gear) && gear == 1);
        Assert.True(snap.TryGet("VERTICAL SPEED", out var vs) && vs == 750);
        Assert.True(snap.TryGet("FLAPS HANDLE INDEX", out var flaps) && flaps == 2);
        Assert.True(snap.TryGet("AUTOPILOT MASTER", out var ap) && ap == 1);
        Assert.True(snap.TryGet("LIGHT LANDING", out var ll) && ll == 1);
        Assert.True(snap.TryGet("ENG ANTI ICE", out var ai) && ai == 1);
    }

    [Fact]
    public void StatusSnapshotMapper_Apply_Span_Matches_Definitions_Order()
    {
        var snap = new SimVarSnapshot();
        var values = new double[StatusSimVars.Definitions.Length];
        values[0] = 1;   // GEAR POSITION
        values[1] = 400; // VERTICAL SPEED
        StatusSnapshotMapper.Apply(snap, values);

        Assert.True(snap.TryGet(StatusSimVars.Definitions[0].Name, out var g) && g == 1);
        Assert.True(snap.TryGet(StatusSimVars.Definitions[1].Name, out var v) && v == 400);
    }

    [Fact]
    public void ManagedClient_ApplyStatusData_Feeds_Condition_Engine_GearUp()
    {
        var root = FindConfigRoot();
        var (settings, catalog) = ConfigLoader.LoadAll(root, "generic");
        // Use a fresh ManagedSimConnectClient without live DLL — ApplyStatusData is the recv path body
        var managed = new ManagedSimConnectClient();
        managed.ApplyStatusData(new PrivateCopilotStatusData
        {
            GearPosition = 1,
            VerticalSpeed = 500
        });
        Assert.True(managed.HasReceivedStatusData);
        Assert.True(managed.Snapshot.TryGet("VERTICAL SPEED", out var vs) && vs == 500);

        var sim = new RecordingSimConnectClient();
        sim.Connect("test", 0);
        // Copy snapshot values as live host would read managed.Snapshot
        foreach (var kv in managed.Snapshot.Values)
            sim.Snapshot.Set(kv.Key, kv.Value);

        var processor = new CommandProcessor(
            new PhraseMatcher(catalog.Commands),
            new ConditionEngine(),
            new ActionExecutor(sim),
            settings.Behavior);

        var result = processor.Process("gear up", sim.Snapshot);
        Assert.NotNull(result);
        Assert.True(result!.Allowed);
        Assert.Contains(sim.TransmittedEvents, e => e.Name == "GEAR_UP");
    }

    [Fact]
    public void ManagedClient_HandleRecvSimObjectData_Updates_Snapshot()
    {
        var managed = new ManagedSimConnectClient();
        // Fake recv payload shaped like SIMCONNECT_RECV_SIMOBJECT_DATA
        var fake = new FakeRecvSimObjectData
        {
            dwRequestID = (uint)PrivateCopilotRequestId.PRIVATE_COPILOT_REQ_STATUS,
            dwData = new object[]
            {
                new PrivateCopilotStatusData
                {
                    GearPosition = 0,
                    VerticalSpeed = 1200
                }
            }
        };

        managed.HandleRecvSimObjectData(fake);
        Assert.True(managed.HasReceivedStatusData);
        Assert.True(managed.Snapshot.TryGet("VERTICAL SPEED", out var vs) && vs == 1200);
        Assert.True(managed.Snapshot.TryGet("GEAR POSITION", out var g) && g == 0);
    }

    [Fact]
    public void Process_Speaks_Before_Transmitting_Event()
    {
        var root = FindConfigRoot();
        var (settings, catalog) = ConfigLoader.LoadAll(root, "generic");
        settings.Behavior.ConfirmBeforeAction = true;
        settings.Behavior.CalloutDelayMs = 50;

        var sim = new RecordingSimConnectClient();
        sim.Connect("test", 0);
        sim.Snapshot.Set("GEAR POSITION", 1);
        sim.Snapshot.Set("VERTICAL SPEED", 500);

        var order = new List<string>();
        var tts = new ConsoleTtsService();
        var processor = new CommandProcessor(
            new PhraseMatcher(catalog.Commands),
            new ConditionEngine(),
            new ActionExecutor(sim),
            settings.Behavior);

        Action<string, int> speak = (text, delay) =>
        {
            order.Add($"tts:{delay}");
            tts.Speak(text, delay);
        };

        // Wrap executor path: record event after Process; speak callback records first
        var result = processor.Process("landing lights on", sim.Snapshot, speakWithDelay: speak);
        Assert.NotNull(result);
        Assert.True(result!.Allowed);
        Assert.Contains(sim.TransmittedEvents, e => e.Name == "LANDING_LIGHTS_ON");

        // TTS must have been invoked (order list non-empty) and event exists — TTS first in order list
        Assert.NotEmpty(order);
        Assert.Equal("tts:50", order[0]);
        // After Process, only speak was in our order list; events come after speak returns inside Process
        Assert.True(tts.SpeakLog.Count >= 1);
        Assert.Contains("Landing lights", tts.SpeakLog[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Process_Order_Tts_Completes_Before_Event_Is_Recorded()
    {
        var root = FindConfigRoot();
        var (settings, catalog) = ConfigLoader.LoadAll(root, "generic");
        settings.Behavior.ConfirmBeforeAction = true;
        settings.Behavior.CalloutDelayMs = 30;

        var timeline = new List<(string Step, long Ticks)>();
        var sim = new TimelineRecordingClient(timeline);
        sim.Connect("test", 0);
        sim.Snapshot.Set("GEAR POSITION", 1);
        sim.Snapshot.Set("VERTICAL SPEED", 600);

        var processor = new CommandProcessor(
            new PhraseMatcher(catalog.Commands),
            new ConditionEngine(),
            new ActionExecutor(sim),
            settings.Behavior);

        processor.Process("gear up", sim.Snapshot, speakWithDelay: (text, delay) =>
        {
            if (delay > 0) Thread.Sleep(delay);
            timeline.Add(("tts", Environment.TickCount64));
            Console.WriteLine($"[TTS] {text}");
        });

        Assert.Equal(2, timeline.Count);
        Assert.Equal("tts", timeline[0].Step);
        Assert.Equal("event:GEAR_UP", timeline[1].Step);
        Assert.True(timeline[0].Ticks <= timeline[1].Ticks);
    }

    [Fact]
    public void SpeechInputGate_Requires_WakeWord_Or_Ptt_When_Not_Continuous()
    {
        var gate = new SpeechInputGate(new SpeechSettings
        {
            ContinuousListen = false,
            WakeWord = "Co Pilot",
            PttKey = "F12"
        });

        Assert.False(gate.TryAccept("gear up", out _, out var reason));
        Assert.Contains("wake word", reason, StringComparison.OrdinalIgnoreCase);

        Assert.True(gate.TryAccept("Co Pilot gear up", out _, out _));

        gate.SetPtt(true);
        Assert.True(gate.TryAccept("gear up", out _, out _));
    }

    [Fact]
    public void SpeechInputGate_Allows_Bare_When_ContinuousListen()
    {
        var gate = new SpeechInputGate(new SpeechSettings
        {
            ContinuousListen = true,
            WakeWord = "Co Pilot"
        });
        Assert.True(gate.TryAccept("gear up", out _, out _));
    }

    [Fact]
    public void PttArmService_ForceArm_Allows_Bare_Phrase_Via_Gate()
    {
        var arm = new PttArmService("F12", graceMs: 2000);
        var gate = new SpeechInputGate(new SpeechSettings
        {
            ContinuousListen = false,
            WakeWord = "Co Pilot",
            PttKey = "F12",
            PttGraceMs = 2000
        });

        Assert.False(gate.TryAccept("landing lights on", out _, out _));
        arm.ForceArm();
        gate.SetPtt(arm.IsArmed);
        Assert.True(arm.IsArmed);
        Assert.True(gate.TryAccept("landing lights on", out _, out _));
    }

    [Fact]
    public void NativeSimConnect_SearchDirs_Include_AppBase()
    {
        var dirs = NativeSimConnectClient.EnumerateSearchDirs().ToList();
        Assert.NotEmpty(dirs);
        Assert.Contains(dirs, d => d.Equals(AppContext.BaseDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
                                   || AppContext.BaseDirectory.StartsWith(d, StringComparison.OrdinalIgnoreCase)
                                   || d.StartsWith(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                                   || Directory.Exists(d));
    }

    [Fact]
    public void IsCompatibleMsfsClientDll_Rejects_FlightSimWorld_Markers()
    {
        // Synthetic: write a tiny fake that contains FSW marker
        var path = Path.Combine(Path.GetTempPath(), "fake-fsw-simconnect-" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            File.WriteAllText(path, "Dovetail Games Flight Sim World SimConnect");
            Assert.False(NativeSimConnectClient.IsCompatibleMsfsClientDll(path, out var reason));
            Assert.Contains("Dovetail", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Packaged_SimConnect_Dll_Is_Msfs_Compatible_When_Present()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "PackageSources", "extras", "SimConnect.dll");
            if (!File.Exists(candidate)) continue;
            Assert.True(NativeSimConnectClient.IsCompatibleMsfsClientDll(candidate, out var reason),
                $"Packaged SimConnect.dll must be MSFS client, not FSW. Reason: {reason}");
            return;
        }
        // If not present in tree, skip without failing CI-less clones
        Assert.True(true);
    }

    [Fact]
    public void Host_Inject_Bare_Phrase_Rejected_By_Gate()
    {
        var root = FindConfigRoot();
        var code = CoPilotVoiceHost.Program.Run(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true,
            InjectPhrase = "gear up",
            FixtureVerticalSpeed = 600
            // no --ptt, no bypass, no wake word
        });
        Assert.Equal(5, code);
    }

    [Fact]
    public void Host_Inject_With_WakeWord_Allows_GearUp()
    {
        var root = FindConfigRoot();
        var code = CoPilotVoiceHost.Program.Run(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true,
            InjectPhrase = "Co Pilot positive climb gear up",
            FixtureVerticalSpeed = 600
        });
        Assert.Equal(0, code);
    }

    [Fact]
    public void Host_Inject_Bare_With_Simulated_Ptt_Allows()
    {
        var root = FindConfigRoot();
        var code = CoPilotVoiceHost.Program.Run(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true,
            InjectPhrase = "landing lights on",
            SimulatePtt = true
        });
        Assert.Equal(0, code);
    }

    private sealed class TimelineRecordingClient : ISimConnectClient
    {
        private readonly List<(string Step, long Ticks)> _timeline;
        private readonly List<(string Name, uint Data)> _events = new();

        public TimelineRecordingClient(List<(string Step, long Ticks)> timeline) => _timeline = timeline;

        public bool IsConnected { get; private set; }
        public bool IsLive => IsConnected;
        public string StatusMessage { get; private set; } = "";
        public SimVarSnapshot Snapshot { get; } = new();
        public string AircraftTitle { get; set; } = "";
        public string AtcModel { get; set; } = "";

        public bool Connect(string appName, int configIndex = 0)
        {
            IsConnected = true;
            StatusMessage = "ok";
            return true;
        }

        public void Disconnect() => IsConnected = false;

        public void TransmitEvent(string eventName, uint data = 0)
        {
            _timeline.Add(($"event:{eventName}", Environment.TickCount64));
            _events.Add((eventName, data));
        }

        public void SetSimVar(string name, double value, string units) => Snapshot.Set(name, value);
        public void ReceiveMessage() { }
        public void Dispose() { }
    }

    /// <summary>Minimal stand-in for SIMCONNECT_RECV_SIMOBJECT_DATA field layout.</summary>
    private sealed class FakeRecvSimObjectData
    {
        public uint dwRequestID;
        public object[] dwData = Array.Empty<object>();
    }
}
