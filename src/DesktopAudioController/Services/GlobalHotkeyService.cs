using System.Runtime.InteropServices;
using System.Windows.Interop;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// 프로필 전환용 전역 단축키를 메시지 전용 창에 등록합니다.
/// 메인 창이 트레이에 숨어 있어도 동작하도록 메인 창 HWND 와 분리합니다.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private const int FirstHotkeyId = 0x5100;

    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _profileIdsByHotkeyId = [];
    private bool _isDisposed;

    public GlobalHotkeyService()
    {
        // HWND_MESSAGE(-3)를 부모로 두면 화면에 보이지 않는 메시지 전용 창이 됩니다.
        _source = new HwndSource(new HwndSourceParameters("DesktopAudioController.Hotkeys")
        {
            ParentWindow = new IntPtr(-3),
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _source.AddHook(WndProc);
    }

    public sealed record Registration(string ProfileId, string ProfileName, string Hotkey);

    /// <summary>
    /// 등록된 단축키가 눌리면 프로필 ID를 UI 스레드에서 전달합니다.
    /// </summary>
    public event Action<string>? ProfileHotkeyPressed;

    /// <summary>
    /// 기존 단축키를 모두 풀고 새로 등록합니다. 등록하지 못한 항목의 안내 문구를 반환합니다.
    /// </summary>
    public IReadOnlyList<string> RegisterAll(IEnumerable<Registration> registrations)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        UnregisterAll();

        var failures = new List<string>();
        var nextId = FirstHotkeyId;
        foreach (var registration in registrations)
        {
            if (!HotkeyGesture.TryParse(registration.Hotkey, out var gesture, out var parseError) || gesture is null)
            {
                failures.Add(Loc.F("'{0}' 단축키 '{1}': {2}", registration.ProfileName, registration.Hotkey, parseError));
                continue;
            }

            var hotkeyId = nextId++;
            if (!RegisterHotKey(_source.Handle, hotkeyId, gesture.Modifiers | ModNoRepeat, gesture.VirtualKey))
            {
                var errorCode = Marshal.GetLastWin32Error();
                var reason = errorCode == ErrorHotkeyAlreadyRegistered
                    ? Loc.T("다른 프로그램이 이미 쓰고 있습니다")
                    : Loc.F("Windows 오류 {0}", errorCode);
                failures.Add(Loc.F("'{0}' 단축키 {1}: {2}", registration.ProfileName, gesture.DisplayText, reason));
                AppLog.Warn("GlobalHotkeyService", $"단축키 등록 실패 profileId={registration.ProfileId} hotkey={gesture.DisplayText} win32Error={errorCode}");
                continue;
            }

            _profileIdsByHotkeyId[hotkeyId] = registration.ProfileId;
            AppLog.Info("GlobalHotkeyService", $"단축키 등록 profileId={registration.ProfileId} hotkey={gesture.DisplayText}");
        }

        return failures;
    }

    public void UnregisterAll()
    {
        foreach (var hotkeyId in _profileIdsByHotkeyId.Keys)
        {
            UnregisterHotKey(_source.Handle, hotkeyId);
        }

        _profileIdsByHotkeyId.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _isDisposed = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _profileIdsByHotkeyId.TryGetValue(wParam.ToInt32(), out var profileId))
        {
            handled = true;
            ProfileHotkeyPressed?.Invoke(profileId);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
