using CoPilotVoiceHost.Diagnostics;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using Xunit;

namespace CoPilotVoiceHost.Tests;

/// <summary>
/// Drives shipped HostSession / UiLogSink paths for the performance pass:
/// speech restart thrift on no-op Apply, restart when grammar inputs change, bounded log buffer.
/// </summary>
public class PerformanceThriftTests
{
    private static string FindConfigRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "PackageSources", "extras", "config");
            if (File.Exists(Path.Combine(candidate, "base_commands.json")))
                return candidate;
            candidate = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(candidate, "base_commands.json")))
                return candidate;
        }

        var linked = Path.Combine(AppContext.BaseDirectory, "config");
        if (File.Exists(Path.Combine(linked, "base_commands.json")))
            return linked;

        throw new DirectoryNotFoundException("config root with base_commands.json not found");
    }

    private static HostSession StartOfflineSession()
    {
        var session = new HostSession(new HostOptions
        {
            ConfigRoot = FindConfigRoot(),
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true,
            AllowOfflineFallback = true
        });
        session.Start();
        return session;
    }

    [Fact]
    public void ApplySettings_NoOp_WhileListening_DoesNotRestartSpeech()
    {
        using var session = StartOfflineSession();
        session.StartSpeechListening();
        var starts = session.SpeechStartCount;
        var catalogBuilds = session.CatalogRebuildCount;
        Assert.True(starts >= 1);

        // Same values already live in Settings — pure no-op Apply.
        session.ApplySettingsFromUi(session.Settings, saveToDisk: false, rebuildCatalog: true);

        Assert.Equal(starts, session.SpeechStartCount);
        Assert.Equal(catalogBuilds, session.CatalogRebuildCount);

        // Inject still works without a restart.
        var code = session.InjectPhrase("Co Pilot landing lights on", forceGate: false);
        Assert.Equal(0, code);
    }

    [Fact]
    public void ApplySettings_BehaviorOnly_DoesNotRestartSpeech()
    {
        using var session = StartOfflineSession();
        session.StartSpeechListening();
        var starts = session.SpeechStartCount;

        // Detached snapshot so we only flip a non-grammar setting.
        var edited = CloneEditable(session.Settings);
        edited.Behavior.RequirePositiveClimbForGearUp = !edited.Behavior.RequirePositiveClimbForGearUp;
        edited.Tts.Volume = Math.Max(1, edited.Tts.Volume - 1);

        session.ApplySettingsFromUi(edited, saveToDisk: false, rebuildCatalog: true);

        Assert.Equal(starts, session.SpeechStartCount);
        Assert.Equal(edited.Behavior.RequirePositiveClimbForGearUp,
            session.Settings.Behavior.RequirePositiveClimbForGearUp);
    }

    [Fact]
    public void ApplySettings_WakeWordChange_RestartsSpeech()
    {
        using var session = StartOfflineSession();
        session.StartSpeechListening();
        var starts = session.SpeechStartCount;

        var edited = CloneEditable(session.Settings);
        edited.Speech.WakeWord = "Perf Wake " + Guid.NewGuid().ToString("N")[..6];
        session.ApplySettingsFromUi(edited, saveToDisk: false, rebuildCatalog: true);

        Assert.True(session.SpeechStartCount > starts);
        Assert.Equal(edited.Speech.WakeWord, session.Settings.Speech.WakeWord);

        var code = session.InjectPhrase($"{edited.Speech.WakeWord} landing lights on", forceGate: false);
        Assert.Equal(0, code);
    }

    [Fact]
    public void ApplySettings_ProfileChange_RebuildsCatalogAndRestartsSpeech()
    {
        using var session = StartOfflineSession();
        session.StartSpeechListening();
        var starts = session.SpeechStartCount;
        var catalogs = session.CatalogRebuildCount;

        var edited = CloneEditable(session.Settings);
        edited.AircraftProfile = string.Equals(edited.AircraftProfile, "a320", StringComparison.OrdinalIgnoreCase)
            ? "generic"
            : "a320";
        session.ApplySettingsFromUi(edited, saveToDisk: false, rebuildCatalog: true);

        Assert.True(session.CatalogRebuildCount > catalogs);
        Assert.True(session.SpeechStartCount > starts);
        Assert.Equal(edited.AircraftProfile, session.AircraftProfile, ignoreCase: true);
    }

    [Fact]
    public void UiLogSink_TrimsToMaxBuffered_UnderWritePressure()
    {
        const int cap = 150;
        var sink = new UiLogSink(maxBuffered: cap);
        Assert.Equal(cap, sink.MaxBuffered);

        for (var i = 0; i < cap + 400; i++)
            sink.Info($"line-{i}");

        var snap = sink.Snapshot();
        Assert.True(snap.Count <= cap, $"snapshot {snap.Count} exceeded cap {cap}");
        Assert.True(sink.Count <= cap, $"Count {sink.Count} exceeded cap {cap}");
        // Oldest dropped: last lines retained
        Assert.Contains(snap, e => e.Message.Contains($"line-{cap + 399}", StringComparison.Ordinal));
        Assert.DoesNotContain(snap, e => e.Message.Equals("line-0", StringComparison.Ordinal));
    }

    [Fact]
    public void UiLogSink_DefaultCap_IsPositiveAndMatchesConstant()
    {
        var sink = new UiLogSink();
        Assert.Equal(UiLogSink.DefaultMaxBuffered, sink.MaxBuffered);
        Assert.True(sink.MaxBuffered >= 100);
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
            Voice = s.Tts.Voice,
            Rate = s.Tts.Rate,
            Volume = s.Tts.Volume
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
