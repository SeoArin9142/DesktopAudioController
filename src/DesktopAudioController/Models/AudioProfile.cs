namespace DesktopAudioController.Models;

/// <summary>
/// 사용자가 수동으로 저장하고 적용할 수 있는 장치 표시와 프로그램별 저장값 묶음입니다.
/// </summary>
public sealed class AudioProfile
{
    /// <summary>
    /// 프로필을 이름 변경과 무관하게 식별하기 위한 내부 ID입니다.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 설정창에 표시할 프로필 이름입니다.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 프로필 적용 시 메인 화면에 표시할 출력 장치 ID 목록입니다.
    /// </summary>
    public List<string> VisibleDeviceIds { get; set; } = [];

    /// <summary>
    /// 연결된 장치만 표시할지 여부입니다.
    /// </summary>
    public bool ShowOnlyConnectedDevices { get; set; } = true;

    /// <summary>
    /// Windows 시스템 사운드 세션도 표시할지 여부입니다.
    /// </summary>
    public bool ShowSystemSounds { get; set; }

    /// <summary>
    /// 현재 소리를 재생 중인 앱 세션만 표시할지 여부입니다.
    /// </summary>
    public bool ShowOnlyActiveSessions { get; set; }

    /// <summary>
    /// 프로필에 묶어 적용할 프로그램별 볼륨/음소거/사용자 지정 이름 저장값입니다.
    /// </summary>
    public List<ProgramAudioPreference> ProgramAudioPreferences { get; set; } = [];

    /// <summary>
    /// 프로필 적용 시 Windows 기본 출력 장치로 바꿀 장치 ID입니다. 비어 있으면 바꾸지 않습니다.
    /// </summary>
    public string DefaultOutputDeviceId { get; set; } = string.Empty;

    /// <summary>
    /// 프로필 적용 시 실행 파일별로 바꿀 앱별 출력 장치 목록입니다.
    /// </summary>
    public List<ProgramOutputRoute> ProgramOutputRoutes { get; set; } = [];

    /// <summary>
    /// 이 프로필을 바로 적용하는 전역 단축키입니다. 예: "Ctrl+Alt+1". 비어 있으면 쓰지 않습니다.
    /// </summary>
    public string Hotkey { get; set; } = string.Empty;
}
