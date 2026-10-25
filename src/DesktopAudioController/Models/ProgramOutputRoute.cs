namespace DesktopAudioController.Models;

/// <summary>
/// 프로필을 적용할 때 특정 실행 파일의 앱별 출력 장치를 어디로 보낼지 나타냅니다.
/// </summary>
public sealed class ProgramOutputRoute
{
    /// <summary>
    /// 작업 관리자 세부 정보 탭에 보이는 실행 파일 이름입니다. ".exe"는 있어도 없어도 됩니다.
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// 보낼 출력 장치 ID입니다. 비어 있으면 앱별 지정을 지워 Windows 기본 장치를 따르게 합니다.
    /// </summary>
    public string TargetDeviceId { get; set; } = string.Empty;
}
