using DesktopAudioController.Models;
using DesktopAudioController.Services;
using DesktopAudioController.ViewModels;

namespace DesktopAudioController.Tests;

public sealed class AutoProfileSettingsPersistenceTests
{
    private static AppSettings CreateSettings()
    {
        return new AppSettings
        {
            AutoProfileSwitchEnabled = true,
            ShowAutoProfileNotifications = false,
            EnableProfileHotkeys = true,
            AutoProfileFallbackProfileId = "work",
            AudioProfiles =
            [
                new AudioProfile { Id = "work", Name = "영상·작업", DefaultOutputDeviceId = "mr3" },
                new AudioProfile
                {
                    Id = "fps",
                    Name = "혼자 FPS",
                    Hotkey = " Ctrl+Alt+1 ",
                    ProgramOutputRoutes =
                    [
                        new ProgramOutputRoute { ProcessName = "TslGame.exe", TargetDeviceId = "magivant" },
                        new ProgramOutputRoute { ProcessName = "chrome", TargetDeviceId = "" },
                        new ProgramOutputRoute { ProcessName = "  ", TargetDeviceId = "mr3" }
                    ]
                }
            ],
            AutoProfileRules =
            [
                new AutoProfileRule
                {
                    Id = "",
                    Name = " PUBG ",
                    ProcessNames = ["TslGame.exe", "tslgame", " "],
                    ProfileId = "fps",
                    RestoreMode = AutoProfileRestoreMode.FallbackProfile
                }
            ]
        };
    }

    [Fact]
    public void SettingsService_RoundTripsAndNormalizesAutoProfileSettings()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new SettingsService(Path.Combine(tempDirectory.Path, "settings.json"));

        service.Save(CreateSettings());
        var loaded = service.Load();

        Assert.True(loaded.AutoProfileSwitchEnabled);
        Assert.False(loaded.ShowAutoProfileNotifications);
        Assert.True(loaded.EnableProfileHotkeys);
        Assert.Equal("work", loaded.AutoProfileFallbackProfileId);
        Assert.Equal("mr3", loaded.AudioProfiles[0].DefaultOutputDeviceId);

        var fps = loaded.AudioProfiles[1];
        Assert.Equal("Ctrl+Alt+1", fps.Hotkey);
        Assert.Equal(2, fps.ProgramOutputRoutes.Count);
        Assert.Equal("TslGame", fps.ProgramOutputRoutes[0].ProcessName);
        Assert.Equal("magivant", fps.ProgramOutputRoutes[0].TargetDeviceId);
        Assert.Equal(string.Empty, fps.ProgramOutputRoutes[1].TargetDeviceId);

        var rule = Assert.Single(loaded.AutoProfileRules);
        Assert.False(string.IsNullOrWhiteSpace(rule.Id));
        Assert.Equal("PUBG", rule.Name);
        Assert.Equal(["TslGame"], rule.ProcessNames);
        Assert.Equal(AutoProfileRestoreMode.FallbackProfile, rule.RestoreMode);
    }

    [Fact]
    public void SettingsService_ClearsFallbackProfileThatNoLongerExists()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new SettingsService(Path.Combine(tempDirectory.Path, "settings.json"));
        var settings = CreateSettings();
        settings.AutoProfileFallbackProfileId = "deleted";

        service.Save(settings);

        Assert.Equal(string.Empty, service.Load().AutoProfileFallbackProfileId);
    }

    [Fact]
    public void SettingsService_LoadsOldSettingsFileWithoutAutoProfileFields()
    {
        using var tempDirectory = new TemporaryDirectory();
        var settingsFilePath = Path.Combine(tempDirectory.Path, "settings.json");
        File.WriteAllText(
            settingsFilePath,
            """{"VisibleDeviceIds":["a"],"AudioProfiles":[{"Id":"p","Name":"게임","VisibleDeviceIds":["a"]}]}""");

        var loaded = new SettingsService(settingsFilePath).Load();

        Assert.False(loaded.AutoProfileSwitchEnabled);
        Assert.True(loaded.ShowAutoProfileNotifications);
        Assert.Empty(loaded.AutoProfileRules);
        var profile = Assert.Single(loaded.AudioProfiles);
        Assert.Equal(string.Empty, profile.DefaultOutputDeviceId);
        Assert.Empty(profile.ProgramOutputRoutes);
        Assert.Equal(string.Empty, profile.Hotkey);
    }

    [Fact]
    public void ApplyProfile_KeepsAutoProfileSettingsAndProfileRouting()
    {
        var settings = CreateSettings();

        var applied = AudioProfileStore.ApplyProfile(settings, settings.AudioProfiles[1]);

        Assert.Equal("fps", applied.LastAppliedAudioProfileId);
        Assert.True(applied.AutoProfileSwitchEnabled);
        Assert.False(applied.ShowAutoProfileNotifications);
        Assert.True(applied.EnableProfileHotkeys);
        Assert.Equal("work", applied.AutoProfileFallbackProfileId);
        Assert.Single(applied.AutoProfileRules);
        Assert.NotSame(settings.AutoProfileRules[0], applied.AutoProfileRules[0]);
        Assert.Equal(3, applied.AudioProfiles[1].ProgramOutputRoutes.Count);
        Assert.Equal("mr3", applied.AudioProfiles[0].DefaultOutputDeviceId);
    }

    [Fact]
    public void SettingsSavePlanner_KeepsAutoProfileSettings()
    {
        var settings = CreateSettings();

        var savePlan = SettingsSavePlanner.Build(
            settings,
            [],
            startMinimized: false,
            runAtWindowsStartup: false,
            minimizeToTray: true,
            showOnlyConnectedDevices: true,
            showSystemSounds: false,
            showOnlyActiveSessions: false,
            includePreReleaseUpdates: false,
            enableDebugLogs: false,
            preserveConfiguredVisibleDevicesOnEmptySave: false);

        Assert.True(savePlan.Settings.AutoProfileSwitchEnabled);
        Assert.True(savePlan.Settings.EnableProfileHotkeys);
        Assert.Single(savePlan.Settings.AutoProfileRules);
        Assert.Equal("Ctrl+Alt+1", savePlan.Settings.AudioProfiles[1].Hotkey.Trim());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DesktopAudioController.Tests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // 테스트 정리 실패는 본문 검증보다 우선순위가 낮습니다.
            }
        }
    }
}
