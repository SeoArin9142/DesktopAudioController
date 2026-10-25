using DesktopAudioController.Models;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// 오디오 초기화 실패 시 앱을 제한 모드로 유지하기 위한 대체 서비스 모음입니다.
/// </summary>
internal static class UnavailableAudioServices
{
    public static IAudioDeviceCatalogService CreateDeviceCatalogService()
    {
        return new DeviceCatalogService();
    }

    public static IAudioSessionService CreateSessionService()
    {
        return new SessionService();
    }

    public static IAudioNotificationService CreateNotificationService()
    {
        return new NotificationService();
    }

    private static InvalidOperationException CreateUnavailableException()
    {
        return new InvalidOperationException(Loc.T("오디오 서비스가 제한 모드로 비활성화되었습니다."));
    }

    private sealed class DeviceCatalogService : IAudioDeviceCatalogService
    {
        public IReadOnlyList<AudioDeviceInfo> GetAvailableOutputDevices()
        {
            return [];
        }

        public IReadOnlyList<AudioDeviceInfo> GetAvailableInputDevices()
        {
            return [];
        }

        public IReadOnlyList<DeviceLevelSnapshot> ReadDeviceLevels(IReadOnlyCollection<string> deviceIds)
        {
            return [];
        }

        public void SetVolume(string deviceId, int volume)
        {
            throw CreateUnavailableException();
        }

        public void SetMuted(string deviceId, bool muted)
        {
            throw CreateUnavailableException();
        }

        public void SetAsDefault(string deviceId)
        {
            throw CreateUnavailableException();
        }
    }

    private sealed class SessionService : IAudioSessionService
    {
        public IReadOnlyList<AudioSessionInfo> GetSessions(string deviceId, bool includeSystemSounds = false)
        {
            return [];
        }

        public void SetSessionVolume(string deviceId, string sessionId, int volume)
        {
            throw CreateUnavailableException();
        }

        public void SetSessionMuted(string deviceId, string sessionId, bool muted)
        {
            throw CreateUnavailableException();
        }

        public void SetSessionOutputDevice(string deviceId, string sessionId, string targetDeviceId)
        {
            throw CreateUnavailableException();
        }

        public int SetProcessOutputDevice(string processName, string targetDeviceId)
        {
            throw CreateUnavailableException();
        }
    }

    private sealed class NotificationService : IAudioNotificationService
    {
        public event EventHandler<AudioNotificationChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }

        public void Start()
        {
        }
    }
}
