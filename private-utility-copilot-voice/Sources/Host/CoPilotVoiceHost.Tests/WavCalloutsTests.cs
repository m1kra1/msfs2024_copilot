using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using CoPilotVoiceHost.Speech;
using Xunit;

namespace CoPilotVoiceHost.Tests;

/// <summary>B2 WAV callouts: manifest lookup, Wav/Hybrid engines, settings round-trip.</summary>
public class WavCalloutsTests
{
    private static string FindConfigRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "PackageSources", "extras", "config");
            if (File.Exists(Path.Combine(candidate, "settings.json")))
                return candidate;
        }

        var linked = Path.Combine(AppContext.BaseDirectory, "config");
        if (File.Exists(Path.Combine(linked, "settings.json")))
            return linked;

        throw new DirectoryNotFoundException("Could not find PackageSources/extras/config for tests.");
    }

    private static string FindExtrasRoot() => ConfigLoader.ResolveExtrasRoot(FindConfigRoot());

    private static string CreateTempPack(Action<string, string> writeFiles)
    {
        var root = Path.Combine(Path.GetTempPath(), "copilot-wav-" + Guid.NewGuid().ToString("N"));
        var packDir = Path.Combine(root, "voices", "test_pack");
        Directory.CreateDirectory(packDir);
        writeFiles(root, packDir);
        return root;
    }

    [Fact]
    public void Settings_Load_Defaults_Engine_Hybrid_And_Austrian_Pack()
    {
        var root = FindConfigRoot();
        var (settings, _) = ConfigLoader.LoadAll(root, "generic");
        Assert.Equal(TtsEngineKind.Hybrid, settings.Tts.Engine, ignoreCase: true);
        Assert.Equal("austrian_airlines_en_us", settings.Tts.VoicePack, ignoreCase: true);
    }

    [Fact]
    public void Shipped_Austrian_Manifest_Resolves_Core_Commands()
    {
        var extras = FindExtrasRoot();
        var manifest = VoicePackManifest.Load(extras, "austrian_airlines_en_us");
        Assert.True(manifest.EntryCount >= 8, $"expected mapped entries, got {manifest.EntryCount}");

        var gear = manifest.TryResolve("gear_up", TtsResponseKind.Success);
        Assert.NotNull(gear);
        Assert.EndsWith("gear_up_checked.wav", gear, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(gear));

        var lights = manifest.TryResolve("landing_lights_on", TtsResponseKind.Success);
        Assert.NotNull(lights);
        Assert.True(File.Exists(lights));

        // Specific reject
        var gearReject = manifest.TryResolve("gear_up", TtsResponseKind.Reject);
        Assert.NotNull(gearReject);
        Assert.EndsWith("unable.wav", gearReject, StringComparison.OrdinalIgnoreCase);

        // Wildcard reject for unmapped id
        var wild = manifest.TryResolve("some_unknown_cmd", TtsResponseKind.Reject);
        Assert.NotNull(wild);
        Assert.EndsWith("unable.wav", wild, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_Rejects_Unsafe_Paths_And_Missing_Pack()
    {
        var extras = CreateTempPack((_, packDir) =>
        {
            // Create a real wav placeholder file for a safe entry
            File.WriteAllBytes(Path.Combine(packDir, "ok.wav"), new byte[] { 0 });
            File.WriteAllText(Path.Combine(packDir, "manifest.json"), """
                {
                  "entries": [
                    { "command_id": "safe", "kind": "success", "file": "ok.wav" },
                    { "command_id": "bad1", "kind": "success", "file": "../secret.wav" },
                    { "command_id": "bad2", "kind": "success", "file": "C:\\Windows\\x.wav" },
                    { "command_id": "bad3", "kind": "success", "file": "nested/x.wav" }
                  ]
                }
                """);
        });

        try
        {
            var manifest = VoicePackManifest.Load(extras, "test_pack");
            Assert.NotNull(manifest.TryResolve("safe", TtsResponseKind.Success));
            Assert.Null(manifest.TryResolve("bad1", TtsResponseKind.Success));
            Assert.Null(manifest.TryResolve("bad2", TtsResponseKind.Success));
            Assert.Null(manifest.TryResolve("bad3", TtsResponseKind.Success));

            var missing = VoicePackManifest.Load(extras, "does_not_exist");
            Assert.Equal(0, missing.EntryCount);
            Assert.Null(missing.TryResolve("safe", TtsResponseKind.Success));
        }
        finally
        {
            try { Directory.Delete(extras, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void WavTts_Plays_Matching_And_Silent_On_Miss()
    {
        var extras = CreateTempPack((_, packDir) =>
        {
            File.WriteAllBytes(Path.Combine(packDir, "ok.wav"), new byte[] { 0 });
            File.WriteAllText(Path.Combine(packDir, "manifest.json"), """
                {
                  "entries": [
                    { "command_id": "gear_up", "kind": "success", "file": "ok.wav" }
                  ]
                }
                """);
        });

        try
        {
            var player = new RecordingWavPlayer();
            using var wav = new WavTtsService(extras, "test_pack", player);

            Assert.True(wav.TrySpeak("Checked. Gear up.", 0, "gear_up", TtsResponseKind.Success));
            Assert.Single(player.PlayedPaths);
            Assert.EndsWith("ok.wav", player.PlayedPaths[0], StringComparison.OrdinalIgnoreCase);

            player.PlayedPaths.Clear();
            Assert.False(wav.TrySpeak("Hello", 0, "list_commands", TtsResponseKind.Success));
            Assert.Empty(player.PlayedPaths);
        }
        finally
        {
            try { Directory.Delete(extras, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Hybrid_Falls_Back_To_Windows_Stub_On_Miss()
    {
        var extras = CreateTempPack((_, packDir) =>
        {
            File.WriteAllBytes(Path.Combine(packDir, "ok.wav"), new byte[] { 0 });
            File.WriteAllText(Path.Combine(packDir, "manifest.json"), """
                {
                  "entries": [
                    { "command_id": "gear_up", "kind": "success", "file": "ok.wav" }
                  ]
                }
                """);
        });

        try
        {
            var player = new RecordingWavPlayer();
            var wav = new WavTtsService(extras, "test_pack", player);
            var console = new ConsoleTtsService();
            using var hybrid = new HybridTtsService(wav, console, ownsFallback: true);

            hybrid.Speak("Checked. Gear up.", 0, "gear_up", TtsResponseKind.Success);
            Assert.Single(player.PlayedPaths);
            Assert.Empty(console.SpeakLog);

            hybrid.Speak("What can you do?", 0, "list_commands", TtsResponseKind.Success);
            Assert.Single(player.PlayedPaths); // unchanged
            Assert.Single(console.SpeakLog);
            Assert.Equal("list_commands", console.SpeakLog[0].CommandId);
            Assert.Equal(TtsResponseKind.Success, console.SpeakLog[0].ResponseKind);
        }
        finally
        {
            try { Directory.Delete(extras, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CommandProcessor_Passes_Success_And_Reject_Kinds()
    {
        var root = FindConfigRoot();
        var (settings, catalog) = ConfigLoader.LoadAll(root, "generic");
        settings.Behavior.ConfirmBeforeAction = false;
        settings.Behavior.CalloutDelayMs = 0;
        settings.Behavior.RequirePositiveClimbForGearUp = true;

        var sim = new RecordingSimConnectClient();
        sim.Connect("test", 0);
        // Gear up denied: VS = 0
        sim.Snapshot.Set("GEAR POSITION", 1);
        sim.Snapshot.Set("VERTICAL SPEED", 0);

        var processor = new CommandProcessor(
            new PhraseMatcher(catalog.Commands),
            new ConditionEngine(),
            new ActionExecutor(sim),
            settings.Behavior);

        string? kind = null;
        string? id = null;
        processor.Process(
            "gear up",
            sim.Snapshot,
            speakWithDelay: (_, _, cmdId, responseKind) =>
            {
                id = cmdId;
                kind = responseKind;
            });

        Assert.Equal("gear_up", id);
        Assert.Equal(TtsResponseKind.Reject, kind);

        sim.Snapshot.Set("VERTICAL SPEED", 500);
        id = null;
        kind = null;
        processor.Process(
            "landing lights on",
            sim.Snapshot,
            speakWithDelay: (_, _, cmdId, responseKind) =>
            {
                id = cmdId;
                kind = responseKind;
            });
        Assert.Equal("landing_lights_on", id);
        Assert.Equal(TtsResponseKind.Success, kind);
    }

    [Fact]
    public void HostSession_Engine_And_VoicePack_Change_Rebuilds_Pipeline()
    {
        var root = FindConfigRoot();
        using var session = new HostSession(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true
        });
        session.Start();

        var before = session.Settings.Tts.Engine;
        var edited = CloneEditable(session.Settings);
        edited.Tts.Engine = string.Equals(before, TtsEngineKind.Windows, StringComparison.OrdinalIgnoreCase)
            ? TtsEngineKind.Hybrid
            : TtsEngineKind.Windows;
        edited.Tts.VoicePack = "lufthansa_en_us";

        // NoTts keeps ConsoleTtsService, but fingerprint must still accept the change without throw
        session.ApplySettingsFromUi(edited, saveToDisk: false, rebuildCatalog: false);
        Assert.Equal(edited.Tts.Engine, session.Settings.Tts.Engine, ignoreCase: true);
        Assert.Equal("lufthansa_en_us", session.Settings.Tts.VoicePack, ignoreCase: true);
    }

    [Fact]
    public void Settings_Engine_VoicePack_RoundTrip_Save_Reload()
    {
        var root = FindConfigRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "copilot-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            // Minimal config: copy settings only is not enough for HostSession Start — use LoadSettings/SaveSettings
            var settingsPath = ConfigLoader.SettingsPath(root);
            var json = File.ReadAllText(settingsPath);
            var tempSettings = Path.Combine(tempRoot, "settings.json");
            File.WriteAllText(tempSettings, json);

            var loaded = ConfigLoader.LoadSettings(tempSettings);
            loaded.Tts.Engine = TtsEngineKind.Wav;
            loaded.Tts.VoicePack = "lufthansa_en_us";
            ConfigLoader.SaveSettings(tempSettings, loaded);

            var reloaded = ConfigLoader.LoadSettings(tempSettings);
            Assert.Equal(TtsEngineKind.Wav, reloaded.Tts.Engine, ignoreCase: true);
            Assert.Equal("lufthansa_en_us", reloaded.Tts.VoicePack, ignoreCase: true);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void NoTts_Uses_Console_Regardless_Of_Engine()
    {
        var root = FindConfigRoot();
        using var session = new HostSession(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true
        });
        session.Start();

        var edited = CloneEditable(session.Settings);
        edited.Tts.Engine = TtsEngineKind.Hybrid;
        edited.Tts.VoicePack = "austrian_airlines_en_us";
        session.ApplySettingsFromUi(edited, saveToDisk: false, rebuildCatalog: false);

        // Inject must not throw; NoTts path keeps console service
        var code = session.InjectPhrase("Co Pilot landing lights on");
        Assert.True(code is 0 or 4 or 5, $"unexpected exit {code}");
    }

    [Fact]
    public void ListAvailableVoicePacks_Finds_Shipped_Samples()
    {
        var root = FindConfigRoot();
        using var session = new HostSession(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true
        });
        session.Start();

        var packs = session.ListAvailableVoicePacks();
        Assert.Contains(packs, p => string.Equals(p, "austrian_airlines_en_us", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(packs, p => string.Equals(p, "lufthansa_en_us", StringComparison.OrdinalIgnoreCase));
    }

    private static AppSettings CloneEditable(AppSettings s) => new()
    {
        SimConnect = s.SimConnect,
        Speech = new SpeechSettings
        {
            Engine = s.Speech.Engine,
            Culture = s.Speech.Culture,
            WakeWord = s.Speech.WakeWord,
            PttKey = s.Speech.PttKey,
            ContinuousListen = s.Speech.ContinuousListen,
            ConfidenceThreshold = s.Speech.ConfidenceThreshold,
            PttGraceMs = s.Speech.PttGraceMs
        },
        Tts = new TtsSettings
        {
            Engine = s.Tts.Engine,
            Voice = s.Tts.Voice,
            Rate = s.Tts.Rate,
            Volume = s.Tts.Volume,
            VoicePack = s.Tts.VoicePack
        },
        Behavior = new BehaviorSettings
        {
            ConfirmBeforeAction = s.Behavior.ConfirmBeforeAction,
            RequirePositiveClimbForGearUp = s.Behavior.RequirePositiveClimbForGearUp,
            CalloutDelayMs = s.Behavior.CalloutDelayMs
        },
        AircraftProfile = s.AircraftProfile,
        AutoDetectAircraft = s.AutoDetectAircraft,
        AnnounceProfileSwitch = s.AnnounceProfileSwitch
    };
}
