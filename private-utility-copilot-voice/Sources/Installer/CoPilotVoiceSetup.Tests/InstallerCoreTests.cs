using CoPilotVoiceSetup.Core;

namespace CoPilotVoiceSetup.Tests;

public class InstallerCoreTests
{
    [Fact]
    public void ParseInstalledPackagesPath_Quoted_And_Bare()
    {
        Assert.Equal(@"D:\MSFS2024Packages",
            CommunityDetector.ParseInstalledPackagesPath(@"InstalledPackagesPath ""D:\MSFS2024Packages"""));
        Assert.Equal(@"C:\FlightSim\Packages",
            CommunityDetector.ParseInstalledPackagesPath(@"InstalledPackagesPath C:\FlightSim\Packages"));
        Assert.Null(CommunityDetector.ParseInstalledPackagesPath("Something else"));
    }

    [Fact]
    public void ToCommunityPath_Appends_Community()
    {
        var c = CommunityDetector.ToCommunityPath(@"D:\MSFS\Packages");
        Assert.EndsWith("Community", c, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectCommunityFolders_From_Temp_UserCfg()
    {
        var root = Path.Combine(Path.GetTempPath(), "copilot-detect-" + Guid.NewGuid().ToString("N"));
        var community = Path.Combine(root, "Packages", "Community");
        Directory.CreateDirectory(community);
        var cfg = Path.Combine(root, "UserCfg.opt");
        File.WriteAllText(cfg, $"InstalledPackagesPath \"{Path.Combine(root, "Packages")}\"\n");

        try
        {
            var found = CommunityDetector.DetectCommunityFolders(
                appData: Path.Combine(root, "no-appdata"),
                localAppData: Path.Combine(root, "no-local"),
                extraUserCfgPaths: new[] { cfg });

            Assert.Contains(found, p => string.Equals(
                Path.GetFullPath(p), Path.GetFullPath(community), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void InstallState_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "copilot-state-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var state = new InstallState
            {
                Version = "1.5.0",
                CommunityPath = @"D:\Community",
                PackagePath = @"D:\Community\private-utility-copilot-voice",
                InstalledUtc = "2026-01-01T00:00:00Z",
                DesktopShortcut = true,
                StartMenuShortcut = true
            };
            InstallStateStore.Save(state, path);
            var loaded = InstallStateStore.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal("1.5.0", loaded!.Version);
            Assert.Equal(state.PackagePath, loaded.PackagePath);
            Assert.True(loaded.DesktopShortcut);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SafeDestination_Requires_Package_Name_Under_Community()
    {
        var community = @"D:\MSFS\Community";
        var good = Path.Combine(community, InstallerConstants.PackageFolderName);
        Assert.True(InstallService.IsSafePackageDestination(community, good));
        Assert.False(InstallService.IsSafePackageDestination(community, Path.Combine(community, "other-mod")));
        Assert.False(InstallService.IsSafePackageDestination(community, @"D:\elsewhere\private-utility-copilot-voice"));
    }

    [Fact]
    public void CanSafelyDeletePackage_Requires_Valid_Manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "copilot-del-" + Guid.NewGuid().ToString("N"));
        var pkg = Path.Combine(root, InstallerConstants.PackageFolderName);
        Directory.CreateDirectory(pkg);
        try
        {
            Assert.False(InstallService.CanSafelyDeletePackage(pkg));
            File.WriteAllText(Path.Combine(pkg, "manifest.json"),
                """{"package_version":"1.5.0","title":"Private Voice Co-Pilot"}""");
            // Still false until package folder name matches - path is correct name
            Assert.Equal(InstallerConstants.PackageFolderName, Path.GetFileName(pkg), ignoreCase: true);
            // Manifest has title but not package id string - IsValidPackageRoot checks hint OR title
            Assert.True(InstallService.CanSafelyDeletePackage(pkg));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ConfigPreserve_Snapshot_And_Restore()
    {
        var root = Path.Combine(Path.GetTempPath(), "copilot-cfg-" + Guid.NewGuid().ToString("N"));
        var config = Path.Combine(root, "config");
        var aircraft = Path.Combine(config, "aircraft");
        Directory.CreateDirectory(aircraft);
        File.WriteAllText(Path.Combine(config, "settings.json"), """{"aircraft_profile":"fenix_a320"}""");
        File.WriteAllText(Path.Combine(aircraft, "fenix_a320.json"), """{"profile_id":"fenix_a320"}""");
        File.WriteAllText(Path.Combine(config, "base_commands.json"), """{"commands":[]}""");

        try
        {
            var snap = InstallService.SnapshotPreservedConfigFiles(config);
            Assert.True(snap.ContainsKey("settings.json"));
            Assert.True(snap.ContainsKey("aircraft/fenix_a320.json"));
            Assert.False(snap.ContainsKey("base_commands.json"));

            var dest = Path.Combine(root, "dest_config");
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "settings.json"), """{"aircraft_profile":"generic"}""");
            File.WriteAllText(Path.Combine(dest, "base_commands.json"), """{"commands":[{"id":"new"}]}""");
            InstallService.RestorePreservedConfig(dest, snap);

            Assert.Contains("fenix_a320", File.ReadAllText(Path.Combine(dest, "settings.json")));
            Assert.Contains("fenix_a320", File.ReadAllText(Path.Combine(dest, "aircraft", "fenix_a320.json")));
            // base_commands not in snap — left as written (new)
            Assert.Contains("new", File.ReadAllText(Path.Combine(dest, "base_commands.json")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Install_Copy_And_KeepConfig_Upgrade()
    {
        var root = Path.Combine(Path.GetTempPath(), "copilot-inst-" + Guid.NewGuid().ToString("N"));
        var community = Path.Combine(root, "Community");
        var payload = Path.Combine(root, "payload", InstallerConstants.PackageFolderName);
        Directory.CreateDirectory(community);

        // Payload package
        Directory.CreateDirectory(Path.Combine(payload, "extras", "config", "aircraft"));
        Directory.CreateDirectory(Path.Combine(payload, "extras"));
        File.WriteAllText(Path.Combine(payload, "manifest.json"),
            """{"package_version":"1.5.0","title":"Private Voice Co-Pilot"}""");
        File.WriteAllText(Path.Combine(payload, "extras", "CoPilotVoiceHost.exe"), "fake-exe");
        File.WriteAllText(Path.Combine(payload, "extras", "config", "settings.json"), """{"v":"new"}""");
        File.WriteAllText(Path.Combine(payload, "extras", "config", "base_commands.json"), """{"base":"new"}""");
        File.WriteAllText(Path.Combine(payload, "extras", "config", "aircraft", "generic.json"), """{"p":"generic"}""");

        // Pre-existing install with user settings
        var dest = Path.Combine(community, InstallerConstants.PackageFolderName);
        Directory.CreateDirectory(Path.Combine(dest, "extras", "config", "aircraft"));
        File.WriteAllText(Path.Combine(dest, "manifest.json"),
            """{"package_version":"1.4.0","title":"Private Voice Co-Pilot"}""");
        File.WriteAllText(Path.Combine(dest, "extras", "config", "settings.json"), """{"v":"user"}""");
        File.WriteAllText(Path.Combine(dest, "extras", "config", "aircraft", "fenix_a320.json"), """{"p":"fenix"}""");
        File.WriteAllText(Path.Combine(dest, "extras", "config", "base_commands.json"), """{"base":"old"}""");

        var statePath = Path.Combine(root, "install.json");
        // Use real InstallService but avoid shortcuts by desktop/startMenu false
        // InstallStateStore writes to LocalAppData by default — we only check files under community

        try
        {
            var result = InstallService.Install(new InstallOptions
            {
                CommunityPath = community,
                PayloadRoot = payload,
                DesktopShortcut = false,
                StartMenuShortcut = false,
                KeepConfig = true
            });

            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(Path.Combine(dest, "extras", "CoPilotVoiceHost.exe")));
            // settings preserved
            Assert.Contains("user", File.ReadAllText(Path.Combine(dest, "extras", "config", "settings.json")));
            // user aircraft profile preserved
            Assert.True(File.Exists(Path.Combine(dest, "extras", "config", "aircraft", "fenix_a320.json")));
            // new generic from payload present
            Assert.True(File.Exists(Path.Combine(dest, "extras", "config", "aircraft", "generic.json")));
            // base_commands refreshed from payload (overwrite then no restore of base)
            Assert.Contains("new", File.ReadAllText(Path.Combine(dest, "extras", "config", "base_commands.json")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
            // Clean install.json written to real LocalAppData if test left it
            try
            {
                var st = InstallStateStore.Load();
                if (st?.PackagePath is not null && st.PackagePath.Contains("copilot-inst-", StringComparison.Ordinal))
                    InstallStateStore.Delete();
            }
            catch { /* ignore */ }
        }
    }

    [Fact]
    public void LooksLikeCommunityFolder_Name_Check()
    {
        Assert.True(CommunityDetector.LooksLikeCommunityFolder(@"D:\MSFS\Community"));
        Assert.False(CommunityDetector.LooksLikeCommunityFolder(@"D:\MSFS\Packages"));
    }
}
