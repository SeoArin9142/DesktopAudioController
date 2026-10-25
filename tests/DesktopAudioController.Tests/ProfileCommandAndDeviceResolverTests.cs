using DesktopAudioController.Models;
using DesktopAudioController.Services;

namespace DesktopAudioController.Tests;

public sealed class ProfileCommandAndDeviceResolverTests
{
    [Theory]
    [InlineData(new[] { "--profile", "이어폰" }, ProfileCommandKind.ApplyProfileByName, "이어폰")]
    [InlineData(new[] { "--PROFILE-INDEX", "2" }, ProfileCommandKind.ApplyProfileByIndex, "2")]
    [InlineData(new[] { "--debug-log", "--toggle-mute", " MR3 " }, ProfileCommandKind.ToggleDeviceMute, "MR3")]
    [InlineData(new[] { "--profile" }, ProfileCommandKind.ApplyProfileByName, "")]
    public void TryParse_ReadsCommands(string[] args, ProfileCommandKind kind, string value)
    {
        Assert.True(ProfileCommandLine.TryParse(args, out var command));
        Assert.Equal(kind, command?.Kind);
        Assert.Equal(value, command?.Value);
    }

    [Fact]
    public void TryParse_IgnoresNormalStartupArguments()
    {
        Assert.False(ProfileCommandLine.TryParse(["--startup", "--debug-log"], out var command));
        Assert.Null(command);
    }

    [Fact]
    public void Serialize_RoundTripsValuesWithSpacesAndSymbols()
    {
        var original = new ProfileCommand(ProfileCommandKind.ToggleDeviceMute, "스피커 (Realtek(R) Audio): 1");

        Assert.True(ProfileCommandLine.TryDeserialize(ProfileCommandLine.Serialize(original), out var restored));
        Assert.Equal(original, restored);
        Assert.False(ProfileCommandLine.TryDeserialize("Unknown:abc", out _));
        Assert.False(ProfileCommandLine.TryDeserialize("ApplyProfileByName:%%%", out _));
    }

    [Theory]
    [InlineData("이어폰", "이어폰")]
    [InlineData("  헤드셋   G733 ", "헤드셋 G733")]
    [InlineData("speaker", "Speaker")]
    [InlineData("G733", "헤드셋 G733")]
    public void FindByName_IsLenientAboutCaseSpacesAndPartialNames(string requested, string expected)
    {
        var names = new[] { "Speaker", "이어폰", "헤드셋 G Pro X", "헤드셋 G733" };

        Assert.Equal(expected, ProfileCommandLine.FindByName(names, name => name, requested, out _));
    }

    [Fact]
    public void FindByName_ReportsAmbiguousAndMissingNames()
    {
        var names = new[] { "헤드셋 G Pro X", "헤드셋 G733" };

        Assert.Null(ProfileCommandLine.FindByName(names, name => name, "헤드셋", out var ambiguous));
        Assert.Contains("여러 개", ambiguous);
        Assert.Null(ProfileCommandLine.FindByName(names, name => name, "스피커", out var missing));
        Assert.Contains("없습니다", missing);
    }

    private static AudioDeviceInfo Device(string id, string name, bool connected = true)
    {
        return new AudioDeviceInfo { Id = id, Name = name, IsConnected = connected };
    }

    [Fact]
    public void ChooseFirstAvailable_UsesFallbackWhenFirstDeviceIsMissingOrDisconnected()
    {
        var devices = new[]
        {
            Device("gpro", "G Pro X", connected: false),
            Device("magivant", "Magivant"),
            Device("mr3", "MR3")
        };

        var choice = ProfileDeviceResolver.ChooseFirstAvailable(["gpro", "magivant", "mr3"], devices, []);

        Assert.Equal("magivant", choice.Device?.Id);
        Assert.True(choice.UsedFallback);
        Assert.Equal("G Pro X", choice.RequestedName);
    }

    [Fact]
    public void ChooseFirstAvailable_ReturnsNullWhenNothingIsConnected()
    {
        var choice = ProfileDeviceResolver.ChooseFirstAvailable(
            ["gpro", "g733"],
            [Device("mr3", "MR3")],
            [new KnownDeviceName { DeviceId = "gpro", Name = "G Pro X" }]);

        Assert.Null(choice.Device);
        Assert.Equal("G Pro X", choice.RequestedName);
    }

    [Fact]
    public void Resolve_MatchesByKnownNameWhenDeviceIdChangedOnAnotherPc()
    {
        var devices = new[] { Device("sub-pc-id", "스피커 (Realtek(R) Audio)") };
        var knownNames = new[] { new KnownDeviceName { DeviceId = "main-pc-id", Name = "스피커  (Realtek(R) Audio)" } };

        Assert.Equal("sub-pc-id", ProfileDeviceResolver.Resolve("main-pc-id", devices, knownNames)?.Id);
        Assert.Null(ProfileDeviceResolver.Resolve("other-id", devices, knownNames));
    }

    [Fact]
    public void BuildKnownNames_KeepsOldNameForDevicesNotConnectedNow()
    {
        var profile = new AudioProfile
        {
            DefaultOutputDeviceId = "mr3",
            DefaultInputDeviceId = "gpro-mic",
            OutputFallbackDeviceIds = ["magivant"],
            DeviceLevels = [new ProfileDeviceLevel { DeviceId = "mr3", IsMuted = true }],
            KnownDeviceNames = [new KnownDeviceName { DeviceId = "gpro-mic", Name = "G Pro X 마이크" }]
        };

        var names = ProfileDeviceResolver.BuildKnownNames(profile, [Device("mr3", "MR3"), Device("magivant", "Magivant")]);

        Assert.Equal(3, names.Count);
        Assert.Contains(names, name => name is { DeviceId: "gpro-mic", Name: "G Pro X 마이크" });
        Assert.Contains(names, name => name is { DeviceId: "mr3", Name: "MR3" });
    }
}
