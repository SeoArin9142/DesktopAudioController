namespace DesktopAudioController.Models;

/// <summary>
/// 지정한 프로그램이 실행 중일 때 자동으로 적용할 프로필 규칙입니다.
/// 목록에서 위에 있는 규칙이 우선합니다.
/// </summary>
public sealed class AutoProfileRule
{
    /// <summary>
    /// 규칙을 이름 변경과 무관하게 식별하기 위한 내부 ID입니다.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 알림과 로그에 표시할 규칙 이름입니다. 비어 있으면 프로그램 이름을 사용합니다.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 규칙을 켤지 여부입니다.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 감시할 실행 파일 이름 목록입니다. 하나라도 실행 중이면 규칙이 활성화됩니다.
    /// </summary>
    public List<string> ProcessNames { get; set; } = [];

    /// <summary>
    /// 규칙이 활성화될 때 적용할 프로필 ID입니다.
    /// </summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>
    /// 감시하던 프로그램이 모두 꺼졌을 때의 동작입니다.
    /// </summary>
    public AutoProfileRestoreMode RestoreMode { get; set; } = AutoProfileRestoreMode.PreviousProfile;
}

/// <summary>
/// 자동 규칙이 끝났을 때 어떤 프로필로 돌아갈지 정합니다.
/// </summary>
public enum AutoProfileRestoreMode
{
    /// <summary>
    /// 자동 전환 직전에 쓰던 프로필로 돌아갑니다.
    /// </summary>
    PreviousProfile = 0,

    /// <summary>
    /// 설정의 기본 프로필로 돌아갑니다.
    /// </summary>
    FallbackProfile = 1,

    /// <summary>
    /// 현재 프로필을 그대로 둡니다.
    /// </summary>
    KeepCurrent = 2
}
