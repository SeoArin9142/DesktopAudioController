using DesktopAudioController.Models;
using DesktopAudioController.Services;

namespace DesktopAudioController.Tests;

public sealed class DeviceIdRemapperTests
{
    private static AudioDeviceInfo Device(string id, string name, bool connected = true)
    {
        return new AudioDeviceInfo { Id = id, Name = name, IsConnected = connected };
    }

    [Fact]
    public void RemapMissingDeviceIds_ReplacesOldIdWithNewIdOfSameNamedDeviceEverywhere()
    {
        // 11-06 실제 사례: USB DAC(G5) 가 새 ID 를 받아 표시 목록에서 빠짐
        var settings = new AppSettings
        {
            VisibleDeviceIds = ["old-g5", "headset"],
            KnownDeviceNames = [new KnownDeviceName { DeviceId = "old-g5", Name = "스피커(2- Sound BlasterX G5)" }],
            AudioProfiles =
            [
                new AudioProfile
                {
                    Id = "p",
                    Name = "기본",
                    VisibleDeviceIds = ["old-g5", "headset"],
                    DefaultOutputDeviceId = "old-g5",
                    OutputFallbackDeviceIds = ["headset", "old-g5"],
                    ProgramOutputRoutes = [new ProgramOutputRoute { ProcessName = "chrome", TargetDeviceId = "old-g5" }],
                    DeviceLevels = [new ProfileDeviceLevel { DeviceId = "old-g5", Volume = 50 }],
                    KnownDeviceNames = [new KnownDeviceName { DeviceId = "old-g5", Name = "스피커(2- Sound BlasterX G5)" }]
                }
            ]
        };
        var devices = new[]
        {
            Device("new-g5", "스피커(2- Sound BlasterX G5)"),
            Device("headset", "스피커(Logitech PRO X Wireless Gaming Headset)")
        };

        var remaps = DeviceIdRemapper.RemapMissingDeviceIds(settings, devices);

        var remap = Assert.Single(remaps);
        Assert.Equal(("old-g5", "new-g5"), (remap.OldId, remap.NewId));
        Assert.Equal(["new-g5", "headset"], settings.VisibleDeviceIds);
        Assert.Equal("new-g5", settings.KnownDeviceNames[0].DeviceId);
        var profile = settings.AudioProfiles[0];
        Assert.Equal(["new-g5", "headset"], profile.VisibleDeviceIds);
        Assert.Equal("new-g5", profile.DefaultOutputDeviceId);
        Assert.Equal(["headset", "new-g5"], profile.OutputFallbackDeviceIds);
        Assert.Equal("new-g5", profile.ProgramOutputRoutes[0].TargetDeviceId);
        Assert.Equal("new-g5", profile.DeviceLevels[0].DeviceId);
        Assert.Equal("new-g5", Assert.Single(profile.KnownDeviceNames).DeviceId);
    }

    [Fact]
    public void RemapMissingDeviceIds_LeavesIdsAloneWhenNameIsUnknownOrAmbiguousOrDeviceStillPresent()
    {
        var settings = new AppSettings
        {
            VisibleDeviceIds = ["unknown-old", "dup-old", "unplugged"],
            KnownDeviceNames =
            [
                new KnownDeviceName { DeviceId = "dup-old", Name = "스피커" },
                new KnownDeviceName { DeviceId = "unplugged", Name = "헤드폰(OpenRun Pro 2)" }
            ]
        };
        var devices = new[]
        {
            Device("a", "스피커"),
            Device("b", "스피커"),
            Device("unplugged", "헤드폰(OpenRun Pro 2)", connected: false),
            Device("other", "헤드폰(OpenRun Pro 2)")
        };

        Assert.Empty(DeviceIdRemapper.RemapMissingDeviceIds(settings, devices));
        Assert.Equal(["unknown-old", "dup-old", "unplugged"], settings.VisibleDeviceIds);
    }

    [Fact]
    public void RemapMissingDeviceIds_MergesWhenNewIdIsAlreadyVisible()
    {
        var settings = new AppSettings
        {
            VisibleDeviceIds = ["old-g5", "new-g5"],
            KnownDeviceNames = [new KnownDeviceName { DeviceId = "old-g5", Name = "G5" }]
        };

        DeviceIdRemapper.RemapMissingDeviceIds(settings, [Device("new-g5", "G5")]);

        Assert.Equal(["new-g5"], settings.VisibleDeviceIds);
    }

    [Fact]
    public void RememberVisibleDeviceNames_RecordsOnlyVisibleDevicesAndUpdatesRenamedOnes()
    {
        var settings = new AppSettings
        {
            VisibleDeviceIds = ["g5", "headset"],
            KnownDeviceNames = [new KnownDeviceName { DeviceId = "headset", Name = "옛 이름" }]
        };

        var changed = DeviceIdRemapper.RememberVisibleDeviceNames(
            settings,
            [Device("g5", "G5"), Device("headset", "PRO X"), Device("hidden", "숨긴 장치")]);

        Assert.True(changed);
        Assert.Equal(2, settings.KnownDeviceNames.Count);
        Assert.Contains(settings.KnownDeviceNames, name => name is { DeviceId: "g5", Name: "G5" });
        Assert.Contains(settings.KnownDeviceNames, name => name is { DeviceId: "headset", Name: "PRO X" });
        Assert.False(DeviceIdRemapper.RememberVisibleDeviceNames(settings, [Device("g5", "G5")]));
    }
}
