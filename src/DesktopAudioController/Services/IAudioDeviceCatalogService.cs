using DesktopAudioController.Models;

namespace DesktopAudioController.Services;

/// <summary>
/// 출력 장치 목록 조회와 장치 수준 볼륨 제어를 담당하는 서비스 계약입니다.
/// </summary>
public interface IAudioDeviceCatalogService
{
    /// <summary>
    /// 현재 사용 가능한 출력 장치 목록을 반환합니다.
    /// </summary>
    IReadOnlyList<AudioDeviceInfo> GetAvailableOutputDevices();

    /// <summary>
    /// 현재 사용 가능한 입력(마이크) 장치 목록을 반환합니다. 프로필의 기본 입력 장치 선택에 씁니다.
    /// </summary>
    IReadOnlyList<AudioDeviceInfo> GetAvailableInputDevices();

    /// <summary>
    /// 지정한 장치의 마스터 볼륨을 설정합니다.
    /// </summary>
    void SetVolume(string deviceId, int volume);

    /// <summary>
    /// 지정한 장치의 음소거 상태를 설정합니다.
    /// </summary>
    void SetMuted(string deviceId, bool muted);

    /// <summary>
    /// 지정한 장치를 기본 출력 장치로 설정합니다.
    /// </summary>
    void SetAsDefault(string deviceId);

    /// <summary>
    /// 지정한 연결된 장치들의 현재 볼륨·음소거·기본 장치 여부를 장치 열거 없이 읽습니다.
    /// 읽지 못한 장치(빠졌거나 오류)는 결과에서 빠집니다.
    /// </summary>
    IReadOnlyList<DeviceLevelSnapshot> ReadDeviceLevels(IReadOnlyCollection<string> deviceIds);
}
