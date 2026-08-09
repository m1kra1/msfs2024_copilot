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
