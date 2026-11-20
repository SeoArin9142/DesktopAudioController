using DesktopAudioController.Models;
using DesktopAudioController.Services;

namespace DesktopAudioController.Tests;

public sealed class AudioProfileStoreTests
{
    [Fact]
    public void Capture_CreatesManualProfileFromProfileScopedSettings()
    {
        var settings = new AppSettings
        {
            VisibleDeviceIds = ["device-a", "device-b"],
            ShowOnlyConnectedDevices = false,
            ShowSystemSounds = true,
            ShowOnlyActiveSessions = true,
            ProgramAudioPreferences =
            [
                new ProgramAudioPreference
                {
                    MatchKey = "path:C:\\Apps\\game.exe",
                    DisplayName = "Game",
                    CustomDisplayName = "게임",
                    Volume = 35,
                    IsMuted = true
                }
            ]
        };

        var profile = AudioProfileStore.Capture(" 게임 ", settings);

        Assert.False(string.IsNullOrWhiteSpace(profile.Id));
        Assert.Equal("게임", profile.Name);
        Assert.Equal(["device-a", "device-b"], profile.VisibleDeviceIds);
        Assert.False(profile.ShowOnlyConnectedDevices);
        Assert.True(profile.ShowSystemSounds);
        Assert.True(profile.ShowOnlyActiveSessions);
        var preference = Assert.Single(profile.ProgramAudioPreferences);
        Assert.Equal("path:C:\\Apps\\game.exe", preference.MatchKey);
        Assert.Equal("게임", preference.CustomDisplayName);
        Assert.Equal(35, preference.Volume);
        Assert.True(preference.IsMuted);
    }

    [Fact]
    public void ApplyProfile_ChangesOnlyProfileScopedSettings()
    {
        var currentSettings = new AppSettings
        {
            VisibleDeviceIds = ["device-before"],
            StartMinimized = true,
            RunAtWindowsStartup = true,
            MinimizeToTray = false,
            ShowOnlyConnectedDevices = true,
            ShowSystemSounds = false,
            ShowOnlyActiveSessions = false,
            IncludePreReleaseUpdates = true,
            EnableDebugLogs = true,
            AudioProfiles =
            [
                new AudioProfile
                {
                    Id = "profile-id",
                    Name = "작업"
                }
            ]
        };
        var profile = new AudioProfile
        {
            Id = "profile-id",
            Name = "작업",
            VisibleDeviceIds = ["device-profile"],
            ShowOnlyConnectedDevices = false,
            ShowSystemSounds = true,
            ShowOnlyActiveSessions = true,
            ProgramAudioPreferences =
            [
                new ProgramAudioPreference
                {
                    MatchKey = "name:Player",
                    DisplayName = "Player",
                    Volume = 44
                }
            ]
        };

        var applied = AudioProfileStore.ApplyProfile(currentSettings, profile);

        Assert.Equal(currentSettings.VisibleDeviceIds, applied.VisibleDeviceIds);
        Assert.True(applied.StartMinimized);
        Assert.True(applied.RunAtWindowsStartup);
        Assert.False(applied.MinimizeToTray);
        Assert.False(applied.ShowOnlyConnectedDevices);
        Assert.True(applied.ShowSystemSounds);
        Assert.True(applied.ShowOnlyActiveSessions);
        Assert.True(applied.IncludePreReleaseUpdates);
        Assert.True(applied.EnableDebugLogs);
        Assert.Equal("profile-id", applied.LastAppliedAudioProfileId);
        Assert.Single(applied.AudioProfiles);
        Assert.Equal("name:Player", Assert.Single(applied.ProgramAudioPreferences).MatchKey);
    }

    [Fact]
    public void FindAppliedProfileId_PrefersLastAppliedProfileWhenProfilesShareSettings()
    {
        var settings = new AppSettings
        {
            VisibleDeviceIds = ["device-a"],
            LastAppliedAudioProfileId = "profile-b",
            AudioProfiles =
            [
                new AudioProfile
                {
                    Id = "profile-a",
                    Name = "A",
                    VisibleDeviceIds = ["device-a"]
                },
                new AudioProfile
                {
                    Id = "profile-b",
                    Name = "B",
                    VisibleDeviceIds = ["device-a"]
                }
            ]
        };

        var appliedProfileId = AudioProfileStore.FindAppliedProfileId(settings);

        Assert.Equal("profile-b", appliedProfileId);
    }

    [Fact]
    public void FindAppliedProfileId_ReturnsMatchingProfileWhenLastAppliedIsMissing()
    {
        var settings = new AppSettings
        {
            VisibleDeviceIds = ["device-b", "device-a"],
            ShowOnlyConnectedDevices = false,
            ShowSystemSounds = true,
            ShowOnlyActiveSessions = true,
            ProgramAudioPreferences =
            [
                new ProgramAudioPreference
                {
                    MatchKey = "name:Player",
                    DisplayName = "Player",
                    CustomDisplayName = "Music",
                    Volume = 44,
                    IsMuted = true
                }
            ],
            AudioProfiles =
            [
                new AudioProfile
                {
                    Id = "profile-a",
                    Name = "A",
                    VisibleDeviceIds = ["device-a"],
                    ShowOnlyConnectedDevices = true
                },
                new AudioProfile
                {
                    Id = "profile-b",
                    Name = "B",
                    VisibleDeviceIds = ["device-a", "device-b"],
                    ShowOnlyConnectedDevices = false,
                    ShowSystemSounds = true,
                    ShowOnlyActiveSessions = true,
                    ProgramAudioPreferences =
                    [
                        new ProgramAudioPreference
                        {
                            MatchKey = "name:Player",
                            DisplayName = "Player",
                            CustomDisplayName = "Music",
                            Volume = 44,
                            IsMuted = true
                        }
                    ]
                }
            ]
        };

        var appliedProfileId = AudioProfileStore.FindAppliedProfileId(settings);

        Assert.Equal("profile-b", appliedProfileId);
    }

    [Fact]
    public void FindAppliedProfileId_ReturnsNullWhenSettingsNoLongerMatchProfile()
    {
        var settings = new AppSettings
        {
            VisibleDeviceIds = ["device-custom"],
            ShowSystemSounds = true,
            LastAppliedAudioProfileId = "profile-b",
            AudioProfiles =
            [
                new AudioProfile
                {
                    Id = "profile-b",
                    Name = "B",
                    VisibleDeviceIds = ["device-profile"],
                    ShowSystemSounds = false
                }
            ]
        };

        var appliedProfileId = AudioProfileStore.FindAppliedProfileId(settings);

        Assert.Null(appliedProfileId);
    }

    [Fact]
    public void BuildUniqueProfileName_AppendsSuffixWhenNameAlreadyExists()
    {
        var existingProfiles = new[]
        {
            new AudioProfile { Id = "1", Name = "게임" },
            new AudioProfile { Id = "2", Name = "게임 (2)" }
        };

        var name = AudioProfileStore.BuildUniqueProfileName("게임", existingProfiles);

        Assert.Equal("게임 (3)", name);
    }

    [Fact]
    public void CloneProfiles_DetachesNestedCollections()
    {
        var source = new[]
        {
            new AudioProfile
            {
                Id = "profile-id",
                Name = "방송",
                VisibleDeviceIds = ["device-a"],
                ProgramAudioPreferences =
                [
                    new ProgramAudioPreference
                    {
                        MatchKey = "name:OBS",
                        DisplayName = "OBS",
                        Volume = 70
                    }
                ]
            }
        };

        var cloned = AudioProfileStore.CloneProfiles(source);
        source[0].VisibleDeviceIds[0] = "changed-device";
        source[0].ProgramAudioPreferences[0].Volume = 1;

        var profile = Assert.Single(cloned);
        Assert.Equal(["device-a"], profile.VisibleDeviceIds);
        Assert.Equal(70, Assert.Single(profile.ProgramAudioPreferences).Volume);
    }
}
