using System.Drawing;
using System.IO;
using DesktopAudioController.Models;
using Forms = System.Windows.Forms;

namespace DesktopAudioController.Services;

/// <summary>
/// Windows 트레이 아이콘과 컨텍스트 메뉴 구성을 담당합니다.
/// </summary>
public sealed class TrayMenuService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly bool _ownsIcon;
    private string? _lastMenuSignature;

    public TrayMenuService(Action restoreFromTray)
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
    }

    public sealed record Device(
        string Id,
        string Name,
        bool IsDefault,
        bool IsConnected,
        bool IsMuted,
        Action SetAsDefault,
        Action ToggleMute);

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
        _notifyIcon.Text = defaultDevice is null
            ? "DesktopAudioController"
            : $"DesktopAudioController - 기본: {TrimNotifyText(defaultDevice.Name)}";

        trayMenu.Items.Add(new Forms.ToolStripMenuItem(
            defaultDevice is null
                ? "현재 기본 장치: 없음"
                : $"현재 기본 장치: {defaultDevice.Name}")
        {
            Enabled = false
        });

        if (state.MinimizeToTray)
        {
            trayMenu.Items.Add(new Forms.ToolStripMenuItem("닫기 버튼 -> 트레이 최소화")
            {
                Enabled = false
            });
        }

        trayMenu.Items.Add("창 열기", null, (_, _) => state.OpenMainWindow());
        trayMenu.Items.Add("설정 열기", null, (_, _) => state.OpenSettings());
        trayMenu.Items.Add("장치 다시 읽기", null, (_, _) => state.RefreshDevices());
        if (state.AutoProfile is not null)
        {
            trayMenu.Items.Add(new Forms.ToolStripMenuItem(state.AutoProfile.CurrentProfileText)
            {
                Enabled = false
            });
        }

        trayMenu.Items.Add(BuildAudioProfileMenu(state));
        if (state.AutoProfile is { IsAutoSwitchEnabled: true } autoProfile)
        {
            var pauseItem = new Forms.ToolStripMenuItem("자동 전환 일시정지")
            {
                Checked = autoProfile.IsAutoSwitchPaused,
                CheckOnClick = false
            };
            pauseItem.Click += (_, _) => autoProfile.ToggleAutoSwitchPause();
            trayMenu.Items.Add(pauseItem);
        }
        trayMenu.Items.Add("로그 폴더 열기", null, (_, _) => state.OpenLogFolder());

        if (state.Devices.Count > 0)
        {
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
        }

        var defaultDeviceMenu = new Forms.ToolStripMenuItem("기본 출력 바꾸기");
        var muteDeviceMenu = new Forms.ToolStripMenuItem("장치 음소거");
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
        trayMenu.Items.Add("앱 종료", null, (_, _) => state.ExitApplication());
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

    private static Forms.ToolStripMenuItem BuildAudioProfileMenu(MenuState state)
    {
        var profileMenu = new Forms.ToolStripMenuItem("프로필 적용");
        if (state.Settings.AudioProfiles.Count == 0)
        {
            profileMenu.Enabled = false;
            return profileMenu;
        }

        var appliedProfileId = AudioProfileStore.FindAppliedProfileId(state.Settings);
        foreach (var profile in state.Settings.AudioProfiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var localProfileId = profile.Id;
            var profileItem = new Forms.ToolStripMenuItem(profile.Name)
            {
                Checked = string.Equals(profile.Id, appliedProfileId, StringComparison.Ordinal)
            };
            profileItem.Click += (_, _) => state.ApplyProfile(localProfileId);
            profileMenu.DropDownItems.Add(profileItem);
        }

        return profileMenu;
    }

    private static string BuildMenuSignature(MenuState state)
    {
        var deviceSignature = string.Join(
            "\n",
            state.Devices.Select(device =>
                $"{device.Id}|{device.Name}|{device.IsDefault}|{device.IsConnected}|{device.IsMuted}"));
        var appliedProfileId = AudioProfileStore.FindAppliedProfileId(state.Settings) ?? string.Empty;
        var profileSignature = string.Join(
            "\n",
            state.Settings.AudioProfiles
                .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(profile => $"{profile.Id}|{profile.Name}"));

        var autoProfileSignature = state.AutoProfile is null
            ? string.Empty
            : $"{state.AutoProfile.CurrentProfileText}|{state.AutoProfile.IsAutoSwitchEnabled}|{state.AutoProfile.IsAutoSwitchPaused}";

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
        return isConnected ? deviceName : $"{deviceName} (연결 안 됨)";
    }
}
