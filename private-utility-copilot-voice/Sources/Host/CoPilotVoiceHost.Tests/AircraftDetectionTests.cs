using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using Xunit;

namespace CoPilotVoiceHost.Tests;

/// <summary>
/// Drives shipped AircraftProfileMatcher, ConfigLoader, and HostSession auto-detect paths.
/// </summary>
public class AircraftDetectionTests
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

    private static string CopyConfigToTemp()
    {
        var root = FindConfigRoot();
        var tmp = Path.Combine(Path.GetTempPath(), "copilot-detect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "aircraft"));
        File.Copy(Path.Combine(root, "settings.json"), Path.Combine(tmp, "settings.json"));
        File.Copy(Path.Combine(root, "base_commands.json"), Path.Combine(tmp, "base_commands.json"));
        if (File.Exists(Path.Combine(root, "aircraft_detection.json")))
            File.Copy(Path.Combine(root, "aircraft_detection.json"), Path.Combine(tmp, "aircraft_detection.json"));
        foreach (var f in Directory.GetFiles(Path.Combine(root, "aircraft"), "*.json"))
            File.Copy(f, Path.Combine(tmp, "aircraft", Path.GetFileName(f)));
        return tmp;
    }

    [Fact]
    public void Detection_Config_File_Exists_With_Rules_And_Fallback()
    {
        var root = FindConfigRoot();
        var path = Path.Combine(root, "aircraft_detection.json");
        Assert.True(File.Exists(path), $"Expected {path}");

        var cfg = ConfigLoader.LoadAircraftDetection(root);
        Assert.False(string.IsNullOrWhiteSpace(cfg.FallbackProfile));
        Assert.NotEmpty(cfg.Rules);
        Assert.Contains(cfg.Rules, r =>
            r.Pattern.Contains("fenix", StringComparison.OrdinalIgnoreCase)
            && r.Profile.Contains("fenix", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Matcher_CaseInsensitive_Contains_And_Fallback()
    {
        var cfg = new AircraftDetectionConfig
        {
            FallbackProfile = "generic",
            Rules =
            {
                new AircraftDetectionRule { Pattern = "Fenix", Match = "any", Profile = "fenix_a320" },
                new AircraftDetectionRule { Pattern = "a320", Match = "title", Profile = "a320" },
                new AircraftDetectionRule { Pattern = "B38M", Match = "atc_model", Profile = "b737" },
            }
        };

        Assert.Equal("fenix_a320", AircraftProfileMatcher.ResolveProfile(cfg, "FENIX A320-214", "A320"));
        Assert.Equal("a320", AircraftProfileMatcher.ResolveProfile(cfg, "Asobo A320 Neo", ""));
        Assert.Equal("b737", AircraftProfileMatcher.ResolveProfile(cfg, "Something", "B38M"));
        Assert.Equal("generic", AircraftProfileMatcher.ResolveProfile(cfg, "Cessna 172", "C172"));
        Assert.Equal("generic", AircraftProfileMatcher.ResolveProfile(cfg, "", ""));
        Assert.Equal("generic", AircraftProfileMatcher.ResolveProfile(cfg, null, null));
        Assert.Equal("Unknown", AircraftProfileMatcher.DisplayTitle(null));
        Assert.Equal("Unknown", AircraftProfileMatcher.DisplayTitle("  "));
    }

    [Fact]
    public void Matcher_First_Rule_Wins()
    {
        var cfg = new AircraftDetectionConfig
        {
            FallbackProfile = "generic",
            Rules =
            {
                new AircraftDetectionRule { Pattern = "fenix", Match = "any", Profile = "fenix_a320" },
                new AircraftDetectionRule { Pattern = "a320", Match = "any", Profile = "a320" },
            }
        };

        Assert.Equal("fenix_a320", AircraftProfileMatcher.ResolveProfile(cfg, "Fenix A320", "A320"));
    }

    [Fact]
    public void Shipped_Rules_Map_Fenix_And_737()
    {
        var root = FindConfigRoot();
        var cfg = ConfigLoader.LoadAircraftDetection(root);

        Assert.Equal("fenix_a320", AircraftProfileMatcher.ResolveProfile(cfg, "Fenix A320 CFM", "A320"));
        Assert.Equal("b737", AircraftProfileMatcher.ResolveProfile(cfg, "PMDG 737-800", "B738"));
        Assert.Equal(cfg.FallbackProfile, AircraftProfileMatcher.ResolveProfile(cfg, "Diamond DA40", "DA40"));
    }

    [Fact]
    public void HostSession_Offline_Start_No_Crash_Unknown_Title()
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

        Assert.Equal("Unknown", session.DetectedAircraftTitle);
        Assert.False(string.IsNullOrWhiteSpace(session.AircraftProfile));
        Assert.NotNull(session.DetectionConfig);
    }

    [Fact]
    public void HostSession_AutoDetect_Switches_Profile_And_Skips_Same_Identity()
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

            session.Settings.AutoDetectAircraft = true;
            session.Settings.AircraftProfile = "generic";
            session.ApplySettingsFromUi(session.Settings, saveToDisk: false, rebuildCatalog: true);

            var rebuildBefore = session.CatalogRebuildCount;
            var switchesBefore = session.AutoDetectProfileSwitchCount;

            session.ProcessAircraftIdentity("Fenix A320-214 CFM", "A320");
            Assert.Equal("Fenix A320-214 CFM", session.DetectedAircraftTitle);
            Assert.Equal("fenix_a320", session.AircraftProfile);
            Assert.Equal(switchesBefore + 1, session.AutoDetectProfileSwitchCount);
            Assert.True(session.CatalogRebuildCount > rebuildBefore);

            var log = string.Join("\n", session.Log.Snapshot().Select(e => e.Message));
            Assert.Contains("[AircraftDetect]", log);
            Assert.Contains("fenix_a320", log);
            Assert.Contains(session.Catalog.Commands, c => c.Id == "ap_heading_hold"
                && c.Actions.Count == 0); // fenix Unable override present

            // Same identity again — no extra switch
            var switchesAfter = session.AutoDetectProfileSwitchCount;
            var rebuildMid = session.CatalogRebuildCount;
            session.ProcessAircraftIdentity("Fenix A320-214 CFM", "A320");
            Assert.Equal(switchesAfter, session.AutoDetectProfileSwitchCount);
            Assert.Equal(rebuildMid, session.CatalogRebuildCount);

            // Different aircraft → b737
            session.ProcessAircraftIdentity("Boeing 737-800", "B738");
            Assert.Equal("b737", session.AircraftProfile);
            Assert.Equal(switchesAfter + 1, session.AutoDetectProfileSwitchCount);
            Assert.Contains(session.Catalog.Commands, c => c.Id == "b737_n1_mode");
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void HostSession_AutoDetect_Disabled_Does_Not_Switch()
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

            session.Settings.AutoDetectAircraft = false;
            session.Settings.AircraftProfile = "generic";
            session.ApplySettingsFromUi(
                DetachedEditable(session.Settings, e =>
                {
                    e.AutoDetectAircraft = false;
                    e.AircraftProfile = "generic";
                }),
                saveToDisk: false,
                rebuildCatalog: true);

            var switches = session.AutoDetectProfileSwitchCount;
            session.ProcessAircraftIdentity("Fenix A320", "A320");
            Assert.Equal("Fenix A320", session.DetectedAircraftTitle);
            Assert.Equal("generic", session.AircraftProfile);
            Assert.Equal(switches, session.AutoDetectProfileSwitchCount);

            var log = string.Join("\n", session.Log.Snapshot().Select(e => e.Message));
            Assert.Contains("auto-detect off", log, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void HostSession_AutoOff_Same_Identity_Logs_AircraftDetect_Once()
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

            // Explicitly disable auto (default settings may enable it)
            session.Settings.AutoDetectAircraft = false;
            Assert.False(session.Settings.AutoDetectAircraft);
            session.Log.Clear();

            var statusFires = 0;
            void OnStatus() => statusFires++;
            session.StatusChanged += OnStatus;
            try
            {
                // Simulate live poll: same TITLE/ATC MODEL many times while auto off
                for (var i = 0; i < 25; i++)
                    session.ProcessAircraftIdentity("Fenix A320-214 CFM", "A320");
            }
            finally
            {
                session.StatusChanged -= OnStatus;
            }

            Assert.Equal("Fenix A320-214 CFM", session.DetectedAircraftTitle);
            Assert.Equal("generic", session.AircraftProfile);
            Assert.Equal(0, session.AutoDetectProfileSwitchCount);

            var detectLines = session.Log.Snapshot()
                .Select(e => e.Message)
                .Where(m => m.Contains("[AircraftDetect]", StringComparison.Ordinal))
                .ToList();
            Assert.Single(detectLines);
            Assert.Contains("auto-detect off", detectLines[0], StringComparison.OrdinalIgnoreCase);
            // StatusChanged once for identity change — not 25× poll thrash
            Assert.Equal(1, statusFires);

            // Different identity still notifies once more
            session.Log.Clear();
            statusFires = 0;
            session.StatusChanged += OnStatus;
            try
            {
                session.ProcessAircraftIdentity("Boeing 737-800", "B738");
                session.ProcessAircraftIdentity("Boeing 737-800", "B738");
                session.ProcessAircraftIdentity("Boeing 737-800", "B738");
            }
            finally
            {
                session.StatusChanged -= OnStatus;
            }

            detectLines = session.Log.Snapshot()
                .Select(e => e.Message)
                .Where(m => m.Contains("[AircraftDetect]", StringComparison.Ordinal))
                .ToList();
            Assert.Single(detectLines);
            Assert.Equal(1, statusFires);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void HostSession_CliProfile_Locks_Auto_Switch()
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
            session.Settings.AutoDetectAircraft = true;

            var switches = session.AutoDetectProfileSwitchCount;
            session.ProcessAircraftIdentity("Fenix A320", "A320");
            Assert.Equal("Fenix A320", session.DetectedAircraftTitle);
            Assert.Equal("a320", session.AircraftProfile); // locked
            Assert.Equal(switches, session.AutoDetectProfileSwitchCount);

            var log = string.Join("\n", session.Log.Snapshot().Select(e => e.Message));
            Assert.Contains("CLI --profile lock", log, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Settings_RoundTrip_Preserves_AutoDetect_Flags()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "settings-detect-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var root = FindConfigRoot();
            var settings = ConfigLoader.LoadSettings(Path.Combine(root, "settings.json"));
            settings.AutoDetectAircraft = true;
            settings.AnnounceProfileSwitch = true;
            ConfigLoader.SaveSettings(tmp, settings);
            var reloaded = ConfigLoader.LoadSettings(tmp);
            Assert.True(reloaded.AutoDetectAircraft);
            Assert.True(reloaded.AnnounceProfileSwitch);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Detached editable snapshot so ApplySettingsFromUi can see false→true auto-detect
    /// (must not mutate live session.Settings before Apply).
    /// </summary>
    private static AppSettings DetachedEditable(AppSettings src, Action<AppSettings>? mutate = null)
    {
        var s = new AppSettings
        {
            SimConnect = src.SimConnect,
            Speech = new SpeechSettings
            {
                Engine = src.Speech.Engine,
                Culture = src.Speech.Culture,
                WakeWord = src.Speech.WakeWord,
                PttKey = src.Speech.PttKey,
                ContinuousListen = src.Speech.ContinuousListen,
                ConfidenceThreshold = src.Speech.ConfidenceThreshold,
                PttGraceMs = src.Speech.PttGraceMs
            },
            Tts = new TtsSettings
            {
                Voice = src.Tts.Voice,
                Rate = src.Tts.Rate,
                Volume = src.Tts.Volume
            },
            Behavior = new BehaviorSettings
            {
                ConfirmBeforeAction = src.Behavior.ConfirmBeforeAction,
                RequirePositiveClimbForGearUp = src.Behavior.RequirePositiveClimbForGearUp,
                CalloutDelayMs = src.Behavior.CalloutDelayMs
            },
            AircraftProfile = src.AircraftProfile,
            AutoDetectAircraft = src.AutoDetectAircraft,
            AnnounceProfileSwitch = src.AnnounceProfileSwitch
        };
        mutate?.Invoke(s);
        return s;
    }

    [Fact]
    public void HostSession_Enable_AutoDetect_After_Identity_Observed_Switches_Profile()
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

            // Auto off: observe Fenix identity (display only — must not stamp switch key)
            session.Settings.AutoDetectAircraft = false;
            session.Settings.AircraftProfile = "generic";
            Assert.False(session.Settings.AutoDetectAircraft);
            session.ProcessAircraftIdentity("Fenix A320-214 CFM", "A320");
            Assert.Equal("Fenix A320-214 CFM", session.DetectedAircraftTitle);
            Assert.Equal("generic", session.AircraftProfile);
            Assert.Equal(0, session.AutoDetectProfileSwitchCount);

            // false→true via detached Apply (simulates fixed GUI ReadSettingsFromUi)
            var edited = DetachedEditable(session.Settings, e =>
            {
                e.AutoDetectAircraft = true;
                e.AircraftProfile = "generic"; // UI still shows previous manual profile
            });
            Assert.False(session.Settings.AutoDetectAircraft); // live not mutated yet
            session.ApplySettingsFromUi(edited, saveToDisk: false, rebuildCatalog: true);

            Assert.True(session.Settings.AutoDetectAircraft);
            Assert.Equal("fenix_a320", session.AircraftProfile);
            Assert.Equal(1, session.AutoDetectProfileSwitchCount);
            Assert.Contains(session.Catalog.Commands, c => c.Id == "ap_heading_hold" && c.Actions.Count == 0);

            var log = string.Join("\n", session.Log.Snapshot().Select(e => e.Message));
            Assert.Contains("Profile switched", log, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void HostSession_Apply_After_AutoSwitch_Uses_Synced_Profile_Not_Stale_Generic()
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

            // Enable auto with detached settings, then detect Fenix
            session.ApplySettingsFromUi(
                DetachedEditable(session.Settings, e => e.AutoDetectAircraft = true),
                saveToDisk: false,
                rebuildCatalog: true);
            session.ProcessAircraftIdentity("Fenix A320-214 CFM", "A320");
            Assert.Equal("fenix_a320", session.AircraftProfile);
            var switches = session.AutoDetectProfileSwitchCount;

            // GUI after RefreshStatus sync would send AircraftProfile=fenix_a320 (not stale generic).
            // Apply with synced profile must preserve auto selection and not thrash.
            session.ApplySettingsFromUi(
                DetachedEditable(session.Settings, e =>
                {
                    e.AutoDetectAircraft = true;
                    e.AircraftProfile = "fenix_a320";
                }),
                saveToDisk: false,
                rebuildCatalog: true);

            Assert.Equal("fenix_a320", session.AircraftProfile);
            Assert.Equal(switches, session.AutoDetectProfileSwitchCount);

            // Same identity again must not re-switch (no thrash)
            session.ProcessAircraftIdentity("Fenix A320-214 CFM", "A320");
            Assert.Equal("fenix_a320", session.AircraftProfile);
            Assert.Equal(switches, session.AutoDetectProfileSwitchCount);

            // Stale Apply sending generic while auto stays on is a manual override until identity changes
            // (UI sync prevents accidental clobber; intentional manual still sticks).
            session.ApplySettingsFromUi(
                DetachedEditable(session.Settings, e =>
                {
                    e.AutoDetectAircraft = true;
                    e.AircraftProfile = "generic";
                }),
                saveToDisk: false,
                rebuildCatalog: true);
            Assert.Equal("generic", session.AircraftProfile);
            session.ProcessAircraftIdentity("Fenix A320-214 CFM", "A320");
            Assert.Equal("generic", session.AircraftProfile); // same identity — no force-back
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void MainWindow_Xaml_Exposes_Detect_Status_And_Settings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? xaml = null;
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sources", "Host", "CoPilotVoiceHost", "Ui", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                xaml = File.ReadAllText(candidate);
                break;
            }

            candidate = Path.Combine(dir.FullName, "private-utility-copilot-voice", "Sources", "Host", "CoPilotVoiceHost", "Ui", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                xaml = File.ReadAllText(candidate);
                break;
            }
        }

        Assert.False(string.IsNullOrEmpty(xaml));
        Assert.Contains("TxtDetectedAircraft", xaml);
        Assert.Contains("SetAutoDetect", xaml);
        Assert.Contains("SetAnnounceProfile", xaml);
        Assert.Contains("Detected aircraft", xaml);
    }
}
