namespace DesktopAudioController.Services;

internal sealed class AudioSessionOutputChangeResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 프로세스 이름 기준 변경에서 실제로 정책을 바꾼 프로세스 수입니다.
    /// </summary>
    public int ChangedProcessCount { get; init; }
}
