using CoPilotVoiceHost;
using Xunit;

namespace CoPilotVoiceHost.Tests;

public class HostEntryTests
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

    [Fact]
    public void Host_Once_Offline_Loads_Config_And_Exits_Zero()
    {
        var root = FindConfigRoot();
        var code = Program.Run(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true,
            Once = true
        });
        Assert.Equal(0, code);
    }

    [Fact]
    public void Host_Inject_PositiveClimb_GearUp_Emits_GEAR_UP()
    {
        var root = FindConfigRoot();
        var code = Program.Run(new HostOptions
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
    public void Host_Inject_GearUp_Denied_Low_VS()
    {
        var root = FindConfigRoot();
        var code = Program.Run(new HostOptions
        {
            ConfigRoot = root,
            ForceOffline = true,
            NoTts = true,
            NoSpeech = true,
            InjectPhrase = "Co Pilot gear up",
            FixtureVerticalSpeed = 0
        });
        Assert.Equal(4, code); // denied by conditions
    }
}
