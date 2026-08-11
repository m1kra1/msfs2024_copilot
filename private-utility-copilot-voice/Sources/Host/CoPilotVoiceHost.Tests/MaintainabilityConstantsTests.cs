using CoPilotVoiceHost.Config;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.SimConnect;
using Xunit;

namespace CoPilotVoiceHost.Tests;

/// <summary>
/// Locks maintainability constants to shipped defaults so renames do not silently change product behavior.
/// </summary>
public class MaintainabilityConstantsTests
{
    [Fact]
    public void HostConstants_MatchShippedProductDefaults()
    {
        Assert.Equal("generic", HostConstants.DefaultProfileId);
        Assert.Equal("PrivateCoPilotVoice", HostConstants.SimConnectAppName);
        Assert.Equal("list_commands", CommandListBuilder.CommandId);
        Assert.Equal("settings.json", HostConstants.SettingsFileName);
        Assert.Equal("base_commands.json", HostConstants.BaseCommandsFileName);
        Assert.Equal("event", HostConstants.ActionTypeEvent);
        Assert.Equal("gear_up", HostConstants.GearUpCommandId);
        Assert.Equal("VERTICAL SPEED", HostConstants.VerticalSpeedSimVar);
        Assert.Equal(100, HostConstants.GearUpMinVerticalSpeedFpm);
        Assert.Equal(500, HostConstants.DefaultOfflineVerticalSpeedFpm);
    }

    /// <summary>
    /// Live event transmit must use GROUPID_IS_PRIORITY; Flags=0 caused S_OK with no cockpit effect.
    /// </summary>
    [Fact]
    public void NativeSimConnect_TransmitEventFlags_MatchSdkPriorityContract()
    {
        Assert.Equal(1u, NativeSimConnectClient.GroupPriorityHighest);
        Assert.Equal(0x10u, NativeSimConnectClient.EventFlagGroupIdIsPriority);
    }

    [Fact]
    public void NormalizeProfileId_AndConfigPaths_AreStable()
    {
        Assert.Equal(HostConstants.DefaultProfileId, HostConstants.NormalizeProfileId(null));
        Assert.Equal(HostConstants.DefaultProfileId, HostConstants.NormalizeProfileId("  "));
        Assert.Equal("fenix_a320", HostConstants.NormalizeProfileId(" fenix_a320 "));

        var root = Path.Combine(Path.GetTempPath(), "copilot-const-paths");
        Assert.Equal(
            Path.Combine(root, "settings.json"),
            ConfigLoader.SettingsPath(root));
        Assert.Equal(
            Path.Combine(root, "base_commands.json"),
            ConfigLoader.BaseCommandsPath(root));
        Assert.Equal(
            Path.Combine(root, "aircraft", "generic.json"),
            ConfigLoader.AircraftProfilePath(root, null));
        Assert.Equal(
            Path.Combine(root, "aircraft", "a320.json"),
            ConfigLoader.AircraftProfilePath(root, "a320"));
    }

    [Fact]
    public void ModelDefaults_UseHostConstants()
    {
        Assert.Equal(HostConstants.DefaultProfileId, new AppSettings().AircraftProfile);
        Assert.Equal(HostConstants.SimConnectAppName, new SimConnectSettings().AppName);
        Assert.Equal(HostConstants.DefaultProfileId, new AircraftDetectionConfig().FallbackProfile);
        Assert.Equal(HostConstants.ActionTypeEvent, new ActionDefinition().Type);
    }
}
