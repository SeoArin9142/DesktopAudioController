using DesktopAudioController.Infrastructure;
using DesktopAudioController.Models;

namespace DesktopAudioController.ViewModels;

/// <summary>
/// 설정창 콤보 상자에 보여 줄 출력 장치 선택지입니다. Id 가 비어 있으면 "바꾸지 않음/Windows 기본" 같은 특수 항목입니다.
/// </summary>
public sealed record DeviceOptionViewModel(string Id, string Name);

/// <summary>
/// 자동 규칙이 끝났을 때의 동작 선택지입니다.
/// </summary>
public sealed record RestoreModeOptionViewModel(AutoProfileRestoreMode Mode, string Name);

/// <summary>
/// 프로필 편집 영역의 앱별 출력 한 줄입니다.
/// </summary>
public sealed class ProgramOutputRouteViewModel : ObservableObject
{
    private string _processName = string.Empty;
    private string _targetDeviceId = string.Empty;

    public string ProcessName
    {
        get => _processName;
        set => SetProperty(ref _processName, value ?? string.Empty);
    }

    public string TargetDeviceId
    {
        get => _targetDeviceId;
        set => SetProperty(ref _targetDeviceId, value ?? string.Empty);
    }
}

/// <summary>
/// 자동 프로필 전환 규칙 한 줄입니다. 프로그램 이름은 쉼표로 여러 개를 적습니다.
/// </summary>
public sealed class AutoProfileRuleViewModel : ObservableObject
{
    private string _name = string.Empty;
    private bool _isEnabled = true;
    private string _processNamesText = string.Empty;
    private string _profileId = string.Empty;
    private AutoProfileRestoreMode _restoreMode = AutoProfileRestoreMode.PreviousProfile;
    private int _priority;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string ProcessNamesText
    {
        get => _processNamesText;
        set => SetProperty(ref _processNamesText, value ?? string.Empty);
    }

    public string ProfileId
    {
        get => _profileId;
        set => SetProperty(ref _profileId, value ?? string.Empty);
    }

    public AutoProfileRestoreMode RestoreMode
    {
        get => _restoreMode;
        set => SetProperty(ref _restoreMode, value);
    }

    /// <summary>
    /// 목록 순서(1부터)입니다. 화면에 "우선순위 n"으로 보여 줍니다.
    /// </summary>
    public int Priority
    {
        get => _priority;
        set => SetProperty(ref _priority, value);
    }
}

/// <summary>
/// 저장 전까지 프로필별로 들고 있는 편집 값입니다.
/// </summary>
internal sealed class ProfileDetailEdit
{
    public string DefaultOutputDeviceId { get; set; } = string.Empty;

    public List<ProgramOutputRoute> ProgramOutputRoutes { get; set; } = [];

    public string Hotkey { get; set; } = string.Empty;
}
