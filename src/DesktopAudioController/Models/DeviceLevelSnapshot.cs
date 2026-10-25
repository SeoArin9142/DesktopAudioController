namespace DesktopAudioController.Models;

/// <summary>
/// 장치 목록을 다시 열거하지 않고 장치 하나에서 바로 읽은 볼륨·음소거·기본 장치 여부입니다.
/// </summary>
public sealed record DeviceLevelSnapshot(string DeviceId, int Volume, bool IsMuted, bool IsDefault);
