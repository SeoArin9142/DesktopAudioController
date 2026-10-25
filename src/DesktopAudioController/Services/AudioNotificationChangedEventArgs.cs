namespace DesktopAudioController.Services;

/// <summary>
/// 오디오 알림이 단순 상태 변경인지, 장치/세션 구성 자체가 바뀐 토폴로지 변경인지를 구분하는 이벤트 인자입니다.
/// </summary>
public sealed class AudioNotificationChangedEventArgs : EventArgs
{
    public AudioNotificationChangedEventArgs(AudioNotificationChangeKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// 장치 마스터 볼륨 알림입니다. 알림에 새 값이 함께 오므로 장치를 다시 열거하지 않고 바로 반영할 수 있습니다.
    /// </summary>
    public AudioNotificationChangedEventArgs(string deviceId, int volume, bool isMuted)
        : this(AudioNotificationChangeKind.State)
    {
        DeviceId = deviceId;
        Volume = volume;
        IsMuted = isMuted;
    }

    /// <summary>
    /// 현재 알림의 변경 종류입니다.
    /// </summary>
    public AudioNotificationChangeKind Kind { get; }

    /// <summary>
    /// 장치 마스터 볼륨 알림이면 대상 장치 ID입니다. 다른 알림이면 null 입니다.
    /// </summary>
    public string? DeviceId { get; }

    /// <summary>
    /// 장치 마스터 볼륨 알림의 새 볼륨(0~100)입니다.
    /// </summary>
    public int? Volume { get; }

    /// <summary>
    /// 장치 마스터 볼륨 알림의 새 음소거 상태입니다.
    /// </summary>
    public bool? IsMuted { get; }
}

/// <summary>
/// UI가 어떤 수준으로 새로고침할지 결정하기 위한 오디오 변경 종류입니다.
/// </summary>
public enum AudioNotificationChangeKind
{
    /// <summary>
    /// 장치/세션 목록 구조는 그대로이고 값만 바뀐 경우입니다.
    /// </summary>
    State = 0,

    /// <summary>
    /// 장치 추가/제거, 기본 장치 변경, 세션 생성/종료처럼 목록 구조가 바뀐 경우입니다.
    /// </summary>
    Topology = 1
}
