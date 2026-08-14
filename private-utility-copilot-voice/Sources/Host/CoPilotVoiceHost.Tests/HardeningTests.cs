using CoPilotVoiceHost;
using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using Xunit;

namespace CoPilotVoiceHost.Tests;

public class HardeningTests
{
    [Fact]
    public void SimVarSnapshot_Concurrent_Set_And_Get()
    {
        var snap = new SimVarSnapshot();
        var errors = 0;
        Parallel.For(0, 200, i =>
        {
            try
            {
                snap.Set("VERTICAL SPEED", i);
                snap.TryGet("VERTICAL SPEED", out _);
                snap.Set("L:TEST", i % 3);
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
        });
        Assert.Equal(0, errors);
        Assert.True(snap.TryGet("VERTICAL SPEED", out _));
    }

    [Fact]
    public void SafeConfigPath_Rejects_Traversal_And_Confines_Export()
    {
        Assert.False(SafeConfigPath.IsSafeId(@"..\settings"));
        Assert.False(SafeConfigPath.IsSafeId("C:\\temp\\x"));
        Assert.False(SafeConfigPath.IsSafeId("before/start"));
        Assert.True(SafeConfigPath.IsSafeId("before_start"));
        Assert.Equal("generic", HostConstants.NormalizeProfileId(@"..\evil"));

        var tmp = Path.Combine(Path.GetTempPath(), "copilot-safe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var confined = SafeConfigPath.ConfineFile(tmp, @"C:\Windows\evil.json", "fallback.json");
            Assert.True(SafeConfigPath.IsUnderDirectory(tmp, confined));
            Assert.Equal("evil.json", Path.GetFileName(confined));

            var profilePath = ConfigLoader.AircraftProfilePath(tmp, @"..\settings");
            Assert.EndsWith(Path.Combine("aircraft", "generic.json"), profilePath);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void HostOptions_MissingValue_DoesNotThrow_And_Help_DoesNotExit()
    {
        var missing = HostOptions.Parse(new[] { "--config" });
        Assert.Equal("--config requires a value.", missing.ParseError);

        var help = HostOptions.Parse(new[] { "--help" });
        Assert.True(help.ShowHelp);
        Assert.Null(help.ParseError);

        var vsBad = HostOptions.Parse(new[] { "--vs", "nope" });
        Assert.Equal("--vs requires a number.", vsBad.ParseError);
    }

    [Fact]
    public void ActionExecutor_History_Is_Capped()
    {
        var rec = new RecordingSimConnectClient();
        rec.Connect("t", 0);
        var exec = new ActionExecutor(rec);
        for (var i = 0; i < HostConstants.ActionHistoryMax + 50; i++)
            exec.Execute(new[] { new ActionDefinition { Type = "event", Name = "GEAR_UP" } });

        Assert.Equal(HostConstants.ActionHistoryMax, exec.History.Count);
    }

    [Fact]
    public void PhraseMatcher_Normalize_Is_Single_Pass()
    {
        Assert.Equal("gear up", PhraseMatcher.Normalize("  Gear   UP!!  "));
        Assert.Equal("co pilot landing lights on", PhraseMatcher.Normalize("Co-Pilot landing lights on"));
    }

    [Fact]
    public void NativeSimConnect_SearchDirs_Only_AppBase()
    {
        var dirs = NativeSimConnectClient.EnumerateSearchDirs().ToList();
        Assert.Single(dirs);
        Assert.True(dirs[0].Equals(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)
                    || AppContext.BaseDirectory.StartsWith(dirs[0], StringComparison.OrdinalIgnoreCase)
                    || dirs[0].StartsWith(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackageVersionReader_Parses_Json()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "copilot-man-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(tmp, """{"package_version":"1.7.1","package_name":"private-utility-copilot-voice"}""");
        try
        {
            Assert.Equal("1.7.1", PackageVersionReader.TryRead(tmp));
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }
}
