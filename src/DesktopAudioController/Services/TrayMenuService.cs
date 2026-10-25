using System.Drawing;
using System.IO;
using DesktopAudioController.Models;
using Forms = System.Windows.Forms;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// Windows 트레이 아이콘과 컨텍스트 메뉴 구성을 담당합니다.
/// </summary>
public sealed class TrayMenuService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly bool _ownsIcon;
    private string? _lastMenuSignature;

    public TrayMenuService(Action restoreFromTray, Action? refreshBeforeOpening = null)
    {
        ArgumentNullException.ThrowIfNull(restoreFromTray);

        var trayIcon = TryLoadTrayIcon();
        _ownsIcon = trayIcon is not null;
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "DesktopAudioController",
            Icon = trayIcon ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _notifyIcon.DoubleClick += (_, _) => restoreFromTray();
        if (refreshBeforeOpening is not null)
        {
            // 슬라이더로 바꾼 볼륨처럼 마지막 갱신 뒤 달라진 값을 메뉴를 여는 순간 다시 반영합니다.
            _notifyIcon.ContextMenuStrip.Opening += (_, _) => refreshBeforeOpening();
        }
    }

    public sealed record Device(
        string Id,
        string Name,
        bool IsDefault,
        bool IsConnected,
        bool IsMuted,
        Action SetAsDefault,
        Action ToggleMute,
        int Volume = 0,
        Action<int>? SetVolume = null);

    public sealed record MenuState(
        IReadOnlyList<Device> Devices,
        AppSettings Settings,
        bool MinimizeToTray,
        Action OpenMainWindow,
        Action OpenSettings,
        Action RefreshDevices,
        Action<string> ApplyProfile,
        Action OpenLogFolder,
        Action ExitApplication,
        AutoProfileMenu? AutoProfile = null);

    /// <summary>
    /// 트레이에 보여 줄 현재 프로필과 자동 전환 상태입니다.
    /// </summary>
    public sealed record AutoProfileMenu(
        string? CurrentProfileId,
        string? CurrentProfileName,
        string CurrentProfileText,
        bool IsAutoSwitchEnabled,
        bool IsAutoSwitchPaused,
        Action ToggleAutoSwitchPause);

    public void Refresh(MenuState state, bool force = false)
    {
        if (_notifyIcon.ContextMenuStrip is null)
        {
            return;
        }

        var menuSignature = BuildMenuSignature(state);
        if (!force && string.Equals(_lastMenuSignature, menuSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastMenuSignature = menuSignature;

        var trayMenu = _notifyIcon.ContextMenuStrip;
        trayMenu.Items.Clear();

        var defaultDevice = state.Devices.FirstOrDefault(device => device.IsDefault);
        _notifyIcon.Text = BuildNotifyText(state.AutoProfile?.CurrentProfileName, defaultDevice?.Name);

        trayMenu.Items.Add(new Forms.ToolStripMenuItem(
            defaultDevice is null
                ? Loc.T("현재 기본 장치: 없음")
                : Loc.F("현재 기본 장치: {0}", defaultDevice.Name))
        {
            Enabled = false
        });
        if (defaultDevice is { IsConnected: true, SetVolume: not null })
        {
            trayMenu.Items.Add(BuildVolumeMenu(defaultDevice));
        }

        if (state.MinimizeToTray)
        {
            trayMenu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("닫기 버튼 -> 트레이 최소화"))
            {
                Enabled = false
            });
        }

        trayMenu.Items.Add(Loc.T("창 열기"), null, (_, _) => state.OpenMainWindow());
        trayMenu.Items.Add(Loc.T("설정 열기"), null, (_, _) => state.OpenSettings());
        trayMenu.Items.Add(Loc.T("장치 다시 읽기"), null, (_, _) => state.RefreshDevices());
        if (state.AutoProfile is not null)
        {
            trayMenu.Items.Add(new Forms.ToolStripMenuItem(state.AutoProfile.CurrentProfileText)
            {
                Enabled = false
            });
        }

        AddAudioProfileItems(trayMenu, state);
        if (state.AutoProfile is { IsAutoSwitchEnabled: true } autoProfile)
        {
            var pauseItem = new Forms.ToolStripMenuItem(Loc.T("자동 전환 일시정지"))
            {
                Checked = autoProfile.IsAutoSwitchPaused,
                CheckOnClick = false
            };
            pauseItem.Click += (_, _) => autoProfile.ToggleAutoSwitchPause();
            trayMenu.Items.Add(pauseItem);
        }
        trayMenu.Items.Add(Loc.T("로그 폴더 열기"), null, (_, _) => state.OpenLogFolder());

        if (state.Devices.Count > 0)
        {
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
        }

        var defaultDeviceMenu = new Forms.ToolStripMenuItem(Loc.T("기본 출력 바꾸기"));
        var muteDeviceMenu = new Forms.ToolStripMenuItem(Loc.T("장치 음소거"));
        foreach (var device in state.Devices)
        {
            var localDevice = device;
            var menuLabel = BuildDeviceMenuLabel(localDevice.Name, localDevice.IsConnected);
            var defaultItem = new Forms.ToolStripMenuItem(menuLabel)
            {
                Checked = localDevice.IsDefault,
                Enabled = localDevice.IsConnected
            };
            defaultItem.Click += (_, _) => localDevice.SetAsDefault();

            var muteItem = new Forms.ToolStripMenuItem(menuLabel)
            {
                Checked = localDevice.IsMuted,
                Enabled = localDevice.IsConnected
            };
            muteItem.Click += (_, _) => localDevice.ToggleMute();

            defaultDeviceMenu.DropDownItems.Add(defaultItem);
            muteDeviceMenu.DropDownItems.Add(muteItem);
        }

        trayMenu.Items.Add(defaultDeviceMenu);
        trayMenu.Items.Add(muteDeviceMenu);
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add(Loc.T("앱 종료"), null, (_, _) => state.ExitApplication());
    }

    public void InvalidateMenu()
    {
        _lastMenuSignature = null;
    }

    public void Hide()
    {
        _notifyIcon.Visible = false;
    }

    public void ShowNotification(string title, string text)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.ShowBalloonTip(5000);
        }
        catch (Exception exception)
        {
            AppLog.Warn("TrayMenuService", $"트레이 알림 표시 실패 title={title}", exception);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        var ownedIcon = _ownsIcon ? _notifyIcon.Icon : null;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        ownedIcon?.Dispose();
    }

    /// <summary>
    /// 현재 기본 출력 장치 볼륨을 10% 단위로 고르는 메뉴입니다. 지금 볼륨과 가장 가까운 단계에 체크합니다.
    /// </summary>
    private static Forms.ToolStripMenuItem BuildVolumeMenu(Device device)
    {
        var volumeMenu = new Forms.ToolStripMenuItem(Loc.F("현재 출력 장치 볼륨: {0}%{1}", device.Volume, (device.IsMuted ? Loc.T(" (음소거)") : string.Empty)));
        var nearestStep = (int)Math.Round(device.Volume / 10.0, MidpointRounding.AwayFromZero) * 10;
        for (var volume = 100; volume >= 0; volume -= 10)
        {
            var localVolume = volume;
            var volumeItem = new Forms.ToolStripMenuItem($"{volume}%")
            {
                Checked = volume == nearestStep
            };
            volumeItem.Click += (_, _) => device.SetVolume?.Invoke(localVolume);
            volumeMenu.DropDownItems.Add(volumeItem);
        }

        return volumeMenu;
    }

    /// <summary>
    /// 프로필을 하위 메뉴가 아니라 메뉴 바로 아래에 번호와 단축키를 붙여 나열해 한 번에 고를 수 있게 합니다.
    /// 번호는 메인 화면 버튼·--profile-index 와 같은 이름순입니다.
    /// </summary>
    private static void AddAudioProfileItems(Forms.ContextMenuStrip trayMenu, MenuState state)
    {
        if (state.Settings.AudioProfiles.Count == 0)
        {
            trayMenu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("저장한 프로필 없음"))
            {
                Enabled = false
            });
            return;
        }

        var appliedProfileId = state.AutoProfile?.CurrentProfileId ?? AudioProfileStore.FindAppliedProfileId(state.Settings);
        var number = 1;
        foreach (var profile in state.Settings.AudioProfiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var localProfileId = profile.Id;
            var profileItem = new Forms.ToolStripMenuItem($"{number}. {profile.Name}")
            {
                Checked = string.Equals(profile.Id, appliedProfileId, StringComparison.Ordinal),
                ShortcutKeyDisplayString = state.Settings.EnableProfileHotkeys ? profile.Hotkey : string.Empty
            };
            profileItem.Click += (_, _) => state.ApplyProfile(localProfileId);
            trayMenu.Items.Add(profileItem);
            number++;
        }
    }

    /// <summary>
    /// 트레이 아이콘 툴팁입니다. Windows 툴팁 길이 제한이 있어 장치·프로필 이름을 줄여 넣습니다.
    /// </summary>
    private static string BuildNotifyText(string? profileName, string? defaultDeviceName)
    {
        var lines = new List<string> { "DesktopAudioController" };
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            lines.Add(Loc.F("프로필: {0}", TrimNotifyText(profileName)));
        }

        if (!string.IsNullOrWhiteSpace(defaultDeviceName))
        {
            lines.Add(Loc.F("기본: {0}", TrimNotifyText(defaultDeviceName)));
        }

        var text = string.Join("\n", lines);
        return text.Length <= 127 ? text : text[..127];
    }

    private static string BuildMenuSignature(MenuState state)
    {
        var deviceSignature = string.Join(
            "\n",
            state.Devices.Select(device =>
                $"{device.Id}|{device.Name}|{device.IsDefault}|{device.IsConnected}|{device.IsMuted}|{(device.IsDefault ? device.Volume : 0)}"));
        var appliedProfileId = AudioProfileStore.FindAppliedProfileId(state.Settings) ?? string.Empty;
        var profileSignature = string.Join(
            "\n",
            state.Settings.AudioProfiles
                .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(profile => $"{profile.Id}|{profile.Name}|{profile.Hotkey}"));

        var autoProfileSignature = state.AutoProfile is null
            ? string.Empty
            : $"{state.AutoProfile.CurrentProfileId}|{state.AutoProfile.CurrentProfileText}|{state.AutoProfile.IsAutoSwitchEnabled}|{state.AutoProfile.IsAutoSwitchPaused}|{state.Settings.EnableProfileHotkeys}";

        return $"{deviceSignature}\nminimizeToTray:{state.MinimizeToTray}\nprofiles:{appliedProfileId}\n{profileSignature}\nauto:{autoProfileSignature}";
    }

    private static Icon? TryLoadTrayIcon()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return null;
            }

            using var extractedIcon = Icon.ExtractAssociatedIcon(executablePath);
            return extractedIcon?.Clone() as Icon;
        }
        catch (Exception exception)
        {
            AppLog.Warn("TrayMenuService", "트레이 아이콘 로드 실패", exception);
            return null;
        }
    }

    private static string TrimNotifyText(string text)
    {
        return text.Length <= 32 ? text : $"{text[..29]}...";
    }

    private static string BuildDeviceMenuLabel(string deviceName, bool isConnected)
    {
        return isConnected ? deviceName : Loc.F("{0} (연결 안 됨)", deviceName);
    }
}
