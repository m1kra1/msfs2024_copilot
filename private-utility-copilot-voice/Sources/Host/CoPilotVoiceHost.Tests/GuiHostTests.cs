using System.Xml.Linq;
using CoPilotVoiceHost;
using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Host;
using Xunit;

namespace CoPilotVoiceHost.Tests;

public class GuiHostTests
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

        throw new DirectoryNotFoundException("config root not found");
    }

    private static string FindHostCsproj()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sources", "Host", "CoPilotVoiceHost", "CoPilotVoiceHost.csproj");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("CoPilotVoiceHost.csproj not found");
    }

    [Fact]
    public void Csproj_Is_WinExe_Net8Windows_With_Wpf()
    {
        var xml = XDocument.Load(FindHostCsproj());
        var props = string.Concat(xml.Descendants("PropertyGroup").Select(p => p.ToString()));
        Assert.Contains("WinExe", props);
        Assert.Contains("net8.0-windows", props);
        Assert.Contains("<UseWPF>true</UseWPF>", props.Replace(" ", ""));
    }

    [Fact]
    public void HostOptions_Parse_Headless_Flag()
    {
        var o = HostOptions.Parse(new[] { "--headless", "--offline", "--once" });
        Assert.True(o.Headless);
        Assert.True(o.ForceOffline);
        Assert.True(o.Once);
    }

    [Fact]
    public void ConfigLoader_SaveSettings_RoundTrip_RealFile()
    {
        var root = FindConfigRoot();
        var src = Path.Combine(root, "settings.json");
        var tmp = Path.Combine(Path.GetTempPath(), "copilot-settings-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.Copy(src, tmp, overwrite: true);
            var settings = ConfigLoader.LoadSettings(tmp);
            settings.Speech.WakeWord = "Sky Boss";
            settings.Speech.PttGraceMs = 2500;
            settings.AircraftProfile = "generic";
            ConfigLoader.SaveSettings(tmp, settings);

            var reloaded = ConfigLoader.LoadSettings(tmp);
            Assert.Equal("Sky Boss", reloaded.Speech.WakeWord);
            Assert.Equal(2500, reloaded.Speech.PttGraceMs);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void HostSession_Inject_Offline_LandingLights_Updates_LastAction()
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
        var code = session.InjectPhrase("Co Pilot landing lights on", forceGate: false);
        Assert.Equal(0, code);
        Assert.Contains("LANDING_LIGHTS_ON", session.LastAction, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.IsLive);
    }

    [Fact]
    public void ApplySettings_InMemory_Does_Not_Wipe_Edits_With_Disk_Reload()
    {
        var root = FindConfigRoot();
        // Isolate disk: copy settings so we can prove Apply does not re-read and overwrite
        var tmpRoot = Path.Combine(Path.GetTempPath(), "copilot-apply-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(tmpRoot, "aircraft"));
            File.Copy(Path.Combine(root, "settings.json"), Path.Combine(tmpRoot, "settings.json"));
            File.Copy(Path.Combine(root, "base_commands.json"), Path.Combine(tmpRoot, "base_commands.json"));
            foreach (var f in Directory.GetFiles(Path.Combine(root, "aircraft"), "*.json"))
                File.Copy(f, Path.Combine(tmpRoot, "aircraft", Path.GetFileName(f)));

            using var session = new HostSession(new HostOptions
            {
                ConfigRoot = tmpRoot,
                ForceOffline = true,
                NoTts = true,
                NoSpeech = true,
                AllowOfflineFallback = true
            });
            session.Start();

            var diskWake = ConfigLoader.LoadSettings(Path.Combine(tmpRoot, "settings.json")).Speech.WakeWord;
            Assert.False(string.IsNullOrWhiteSpace(diskWake));

            var edited = session.Settings;
            edited.Speech.WakeWord = "Sky Boss Apply";
            edited.Speech.PttGraceMs = 2222;
            edited.Behavior.RequirePositiveClimbForGearUp = !edited.Behavior.RequirePositiveClimbForGearUp;
            var climbAfter = edited.Behavior.RequirePositiveClimbForGearUp;

            // The bug: reloadProfiles/LoadAll wiped memory. Apply must keep in-memory values.
            session.ApplySettingsFromUi(edited, saveToDisk: false, rebuildCatalog: true);

            Assert.Equal("Sky Boss Apply", session.Settings.Speech.WakeWord);
            Assert.Equal(2222, session.Settings.Speech.PttGraceMs);
            Assert.Equal(climbAfter, session.Settings.Behavior.RequirePositiveClimbForGearUp);

            // Disk unchanged
            var stillDisk = ConfigLoader.LoadSettings(Path.Combine(tmpRoot, "settings.json"));
            Assert.Equal(diskWake, stillDisk.Speech.WakeWord);
            Assert.NotEqual("Sky Boss Apply", stillDisk.Speech.WakeWord);
        }
        finally
        {
            try { Directory.Delete(tmpRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ApplySettings_Restarts_Speech_And_Rebuilds_Grammar_When_Listening()
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
        session.StartSpeechListening();
        var starts = session.SpeechStartCount;
        Assert.True(starts >= 1);

        var edited = session.Settings;
        edited.Speech.WakeWord = "Sky Boss Live";
        edited.AircraftProfile = "a320";
        session.ApplySettingsFromUi(edited, saveToDisk: false, rebuildCatalog: true);

        Assert.Equal("Sky Boss Live", session.Settings.Speech.WakeWord);
        Assert.Equal("a320", session.Settings.AircraftProfile);
        Assert.True(session.SpeechStartCount > starts,
            "Apply must restart speech so wake word / grammar take effect on the live path");
        Assert.NotNull(session.Matcher);
        // a320 profile merges extra command — catalog should still include gear_up + a320 command
        var ids = session.Catalog.Commands.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("gear_up", ids);
        Assert.Contains("a320_managed_speed", ids);

        // New wake word must work for inject gate via Process path
        var code = session.InjectPhrase("Sky Boss Live landing lights on", forceGate: false);
        Assert.Equal(0, code);
        Assert.Contains("LANDING_LIGHTS_ON", session.LastAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_Then_ReloadFromDisk_Restores_Saved_WakeWord_And_Restarts_Speech()
    {
        var root = FindConfigRoot();
        var tmpRoot = Path.Combine(Path.GetTempPath(), "copilot-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(tmpRoot, "aircraft"));
            File.Copy(Path.Combine(root, "settings.json"), Path.Combine(tmpRoot, "settings.json"));
            File.Copy(Path.Combine(root, "base_commands.json"), Path.Combine(tmpRoot, "base_commands.json"));
            foreach (var f in Directory.GetFiles(Path.Combine(root, "aircraft"), "*.json"))
                File.Copy(f, Path.Combine(tmpRoot, "aircraft", Path.GetFileName(f)));

            using var session = new HostSession(new HostOptions
            {
                ConfigRoot = tmpRoot,
                ForceOffline = true,
                NoTts = true,
                NoSpeech = true,
                AllowOfflineFallback = true
            });
            session.Start();
            session.StartSpeechListening();
            var starts = session.SpeechStartCount;

            var edited = session.Settings;
            edited.Speech.WakeWord = "Saved Wake";
            session.ApplySettingsFromUi(edited, saveToDisk: true, rebuildCatalog: true);
            Assert.Equal("Saved Wake", session.Settings.Speech.WakeWord);
            Assert.True(session.SpeechStartCount > starts);

            // Mutate memory, then ReloadFromDisk must bring Saved Wake back and restart speech
            session.Settings.Speech.WakeWord = "Tampered";
            var starts2 = session.SpeechStartCount;
            session.ReloadFromDisk();
            Assert.Equal("Saved Wake", session.Settings.Speech.WakeWord);
            Assert.True(session.SpeechStartCount > starts2);
        }
        finally
        {
            try { Directory.Delete(tmpRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void MainWindow_Xaml_Has_Status_Settings_Debug_Shell()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? xaml = null;
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Sources", "Host", "CoPilotVoiceHost", "Ui", "MainWindow.xaml");
            if (File.Exists(candidate)) { xaml = File.ReadAllText(candidate); break; }
        }

        Assert.NotNull(xaml);
        Assert.Contains("Header=\"Status\"", xaml);
        Assert.Contains("Header=\"Settings\"", xaml);
        Assert.Contains("Header=\"Debug\"", xaml);
        Assert.Contains("Always on Top", xaml);
        Assert.Contains("BottomBar", xaml);
        Assert.Contains("Inject", xaml);
        Assert.Contains("Force Reconnect", xaml);
    }
}
