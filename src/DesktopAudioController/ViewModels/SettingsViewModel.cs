using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DesktopAudioController.Infrastructure;
using DesktopAudioController.Models;
using DesktopAudioController.Services;
using DesktopAudioController.Localization;

namespace DesktopAudioController.ViewModels;

/// <summary>
/// 설정 창에서 장치 선택과 표시 옵션을 관리하는 뷰모델입니다.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private sealed class SettingsSnapshot
    {
        public required IReadOnlyList<AudioDeviceInfo> Devices { get; init; }

        public required AppSettings Settings { get; init; }

        public IReadOnlyList<string> RunningProcessNames { get; init; } = [];

        public IReadOnlyList<AudioDeviceInfo> InputDevices { get; init; } = [];
    }

    // 설정 파일 입출력을 담당하는 서비스입니다.
    private readonly ISettingsService _settingsService;

    // 현재 사용 가능한 오디오 장치를 조회하는 서비스입니다.
    private readonly IAudioDeviceCatalogService _audioDeviceCatalogService;

    // Windows 자동 실행 등록을 적용하는 서비스입니다.
    private readonly IStartupLaunchService _startupLaunchService;

    // 제한 모드에서 빈 장치 목록을 저장할 때 기존 표시 장치 선택을 보존할지 여부입니다.
    private readonly bool _preserveConfiguredVisibleDevicesOnEmptySave;

    // 시작 최소화 옵션의 내부 필드입니다.
    private bool _startMinimized;

    // Windows 자동 실행 옵션의 내부 필드입니다.
    private bool _runAtWindowsStartup;

    // 트레이 최소화 옵션의 내부 필드입니다.
    private bool _minimizeToTray;

    // 연결된 장치만 표시할지 여부의 내부 필드입니다.
    private bool _showOnlyConnectedDevices;

    // 시스템 사운드 세션까지 표시할지 여부의 내부 필드입니다.
    private bool _showSystemSounds;

    // 현재 소리를 재생 중인 앱만 표시할지 여부의 내부 필드입니다.
    private bool _showOnlyActiveSessions;

    // prerelease 릴리즈까지 업데이트 대상으로 포함할지 여부의 내부 필드입니다.
    private bool _includePreReleaseUpdates;

    // 상세 디버그 로그를 파일에 기록할지 여부의 내부 필드입니다.
    private bool _enableDebugLogs;

    // 설정창에서 선택한 수동 프로필입니다.
    private AudioProfileSelectionViewModel? _selectedAudioProfile;

    // 설정창을 열었을 때 저장돼 있던 디버그 로그 상태입니다.
    private bool _loadedEnableDebugLogs;

    // 마지막으로 읽은 프로필 원본입니다. 편집 값이 없는 프로필의 기본 장치·앱별 출력을 채울 때 씁니다.
    private Dictionary<string, AudioProfile> _profilesById = new(StringComparer.Ordinal);

    // 저장 전까지 프로필별로 들고 있는 기본 장치·앱별 출력·단축키 편집 값입니다.
    private readonly Dictionary<string, ProfileDetailEdit> _profileDetailEdits = new(StringComparer.Ordinal);

    // 지금 편집 영역에 올라와 있는 프로필 ID입니다.
    private string? _editingProfileId;

    // 선택한 프로필의 기본 출력 장치 편집 값입니다.
    private string _selectedProfileDefaultDeviceId = string.Empty;

    // 선택한 프로필의 단축키 편집 값입니다.
    private string _selectedProfileHotkey = string.Empty;

    // 선택한 프로필의 대체 출력 장치 1·2, 기본 입력 장치, 대체 입력 장치 편집 값입니다.
    private string _selectedProfileOutputFallback1 = string.Empty;
    private string _selectedProfileOutputFallback2 = string.Empty;
    private string _selectedProfileDefaultInputDeviceId = string.Empty;
    private string _selectedProfileInputFallback1 = string.Empty;

    // 마지막으로 읽은 입력 장치 목록입니다. 장치 이름 기록에 씁니다.
    private IReadOnlyList<AudioDeviceInfo> _inputDevices = [];

    // 자동 프로필 전환 사용 여부의 내부 필드입니다.
    private bool _autoProfileSwitchEnabled;

    // 자동 전환 알림 표시 여부의 내부 필드입니다.
    private bool _showAutoProfileNotifications = true;

    // 프로필 단축키 사용 여부의 내부 필드입니다.
    private bool _enableProfileHotkeys;

    // 기본 프로필 ID의 내부 필드입니다.
    private string _autoProfileFallbackProfileId = string.Empty;

    // 화면 언어 설정의 내부 필드입니다. 비어 있으면 Windows 표시 언어를 따릅니다.
    private string _uiLanguage = string.Empty;

    /// <summary>
    /// 설정 창에서 사용할 서비스들을 주입받습니다.
    /// </summary>
    public SettingsViewModel(
        ISettingsService settingsService,
        IAudioDeviceCatalogService audioDeviceCatalogService,
        IStartupLaunchService startupLaunchService,
        bool preserveConfiguredVisibleDevicesOnEmptySave = false)
    {
        _settingsService = settingsService;
        _audioDeviceCatalogService = audioDeviceCatalogService;
        _startupLaunchService = startupLaunchService;
        _preserveConfiguredVisibleDevicesOnEmptySave = preserveConfiguredVisibleDevicesOnEmptySave;
    }

    /// <summary>
    /// 사용자가 선택할 수 있는 전체 출력 장치 목록입니다.
    /// </summary>
    public ObservableCollection<AudioDeviceSelectionViewModel> AvailableDevices { get; } = [];

    /// <summary>
    /// 사용자가 직접 저장한 수동 오디오 프로필 목록입니다.
    /// </summary>
    public ObservableCollection<AudioProfileSelectionViewModel> AudioProfiles { get; } = [];

    /// <summary>
    /// 프로필의 기본 출력 장치 선택지입니다. 첫 항목은 "바꾸지 않음"입니다.
    /// </summary>
    public ObservableCollection<DeviceOptionViewModel> DefaultDeviceOptions { get; } = [];

    /// <summary>
    /// 앱별 출력 대상 장치 선택지입니다. 첫 항목은 "Windows 기본 장치 따름"입니다.
    /// </summary>
    public ObservableCollection<DeviceOptionViewModel> RouteDeviceOptions { get; } = [];

    /// <summary>
    /// 대체 출력 장치 선택지입니다. 첫 항목은 "(없음)"입니다.
    /// </summary>
    public ObservableCollection<DeviceOptionViewModel> OutputFallbackOptions { get; } = [];

    /// <summary>
    /// 기본 입력(마이크) 장치 선택지입니다. 첫 항목은 "(바꾸지 않음)"입니다.
    /// </summary>
    public ObservableCollection<DeviceOptionViewModel> InputDeviceOptions { get; } = [];

    /// <summary>
    /// 대체 입력 장치 선택지입니다. 첫 항목은 "(없음)"입니다.
    /// </summary>
    public ObservableCollection<DeviceOptionViewModel> InputFallbackOptions { get; } = [];

    /// <summary>
    /// 장치 볼륨·음소거 줄에서 고를 출력 장치입니다.
    /// </summary>
    public ObservableCollection<DeviceOptionViewModel> LevelDeviceOptions { get; } = [];

    /// <summary>
    /// 장치 음소거 선택지입니다.
    /// </summary>
    public IReadOnlyList<MuteOptionViewModel> MuteOptions { get; } =
    [
        new(0, Loc.T("음소거 그대로")),
        new(1, Loc.T("음소거")),
        new(2, Loc.T("음소거 해제"))
    ];

    /// <summary>
    /// 선택한 프로필의 장치 볼륨·음소거 목록입니다.
    /// </summary>
    public ObservableCollection<DeviceLevelViewModel> SelectedProfileDeviceLevels { get; } = [];

    public string SelectedProfileOutputFallback1
    {
        get => _selectedProfileOutputFallback1;
        set => SetProperty(ref _selectedProfileOutputFallback1, value ?? string.Empty);
    }

    public string SelectedProfileOutputFallback2
    {
        get => _selectedProfileOutputFallback2;
        set => SetProperty(ref _selectedProfileOutputFallback2, value ?? string.Empty);
    }

    public string SelectedProfileDefaultInputDeviceId
    {
        get => _selectedProfileDefaultInputDeviceId;
        set => SetProperty(ref _selectedProfileDefaultInputDeviceId, value ?? string.Empty);
    }

    public string SelectedProfileInputFallback1
    {
        get => _selectedProfileInputFallback1;
        set => SetProperty(ref _selectedProfileInputFallback1, value ?? string.Empty);
    }

    /// <summary>
    /// 선택한 프로필의 앱별 출력 목록입니다.
    /// </summary>
    public ObservableCollection<ProgramOutputRouteViewModel> SelectedProfileRoutes { get; } = [];

    /// <summary>
    /// 자동 프로필 전환 규칙 목록입니다. 위에 있을수록 우선합니다.
    /// </summary>
    public ObservableCollection<AutoProfileRuleViewModel> AutoProfileRules { get; } = [];

    /// <summary>
    /// 기본 프로필 선택지입니다. 첫 항목은 "(지정 안 함)"입니다.
    /// </summary>
    public ObservableCollection<AudioProfileSelectionViewModel> FallbackProfileOptions { get; } = [];

    /// <summary>
    /// 규칙이 끝났을 때 동작 선택지입니다.
    /// </summary>
    public IReadOnlyList<RestoreModeOptionViewModel> RestoreModeOptions { get; } =
    [
        new(AutoProfileRestoreMode.PreviousProfile, Loc.T("이전 프로필로")),
        new(AutoProfileRestoreMode.FallbackProfile, Loc.T("기본 프로필로")),
        new(AutoProfileRestoreMode.KeepCurrent, Loc.T("그대로 두기"))
    ];

    /// <summary>
    /// 설정창을 열 때 창이 있는 실행 중 프로그램 이름입니다. 프로그램 이름 입력 칸의 추천 목록으로 씁니다.
    /// </summary>
    public ObservableCollection<string> RunningProcessNames { get; } = [];

    /// <summary>
    /// 이번 설정창에서 "프로필 적용"을 누른 프로필 ID입니다. 창이 닫힌 뒤 기본 장치·앱별 출력을 적용하는 데 씁니다.
    /// </summary>
    public string? AppliedProfileIdInSession { get; private set; }

    /// <summary>
    /// 선택한 프로필을 적용할 때 바꿀 기본 출력 장치 ID입니다. 비어 있으면 바꾸지 않습니다.
    /// </summary>
    public string SelectedProfileDefaultDeviceId
    {
        get => _selectedProfileDefaultDeviceId;
        set => SetProperty(ref _selectedProfileDefaultDeviceId, value ?? string.Empty);
    }

    /// <summary>
    /// 선택한 프로필의 전역 단축키입니다.
    /// </summary>
    public string SelectedProfileHotkey
    {
        get => _selectedProfileHotkey;
        set => SetProperty(ref _selectedProfileHotkey, value ?? string.Empty);
    }

    /// <summary>
    /// 실행 중인 프로그램에 맞춰 프로필을 자동으로 바꿀지 여부입니다.
    /// </summary>
    public bool AutoProfileSwitchEnabled
    {
        get => _autoProfileSwitchEnabled;
        set => SetProperty(ref _autoProfileSwitchEnabled, value);
    }

    /// <summary>
    /// 자동 전환·단축키 적용 때 트레이 알림을 띄울지 여부입니다.
    /// </summary>
    public bool ShowAutoProfileNotifications
    {
        get => _showAutoProfileNotifications;
        set => SetProperty(ref _showAutoProfileNotifications, value);
    }

    /// <summary>
    /// 프로필별 전역 단축키를 등록할지 여부입니다.
    /// </summary>
    public bool EnableProfileHotkeys
    {
        get => _enableProfileHotkeys;
        set => SetProperty(ref _enableProfileHotkeys, value);
    }

    /// <summary>
    /// "기본 프로필로" 규칙이 끝났을 때 돌아갈 프로필 ID입니다.
    /// </summary>
    public string AutoProfileFallbackProfileId
    {
        get => _autoProfileFallbackProfileId;
        set => SetProperty(ref _autoProfileFallbackProfileId, value ?? string.Empty);
    }

    /// <summary>
    /// 언어 선택지입니다. 언어 이름은 어느 언어 화면에서도 알아볼 수 있게 각 언어로 적습니다.
    /// </summary>
    public IReadOnlyList<LanguageOptionViewModel> LanguageOptions { get; } =
    [
        new(Loc.AutoLanguage, "Auto (Windows) / 자동"),
        new(Loc.KoreanLanguage, "한국어"),
        new(Loc.EnglishLanguage, "English")
    ];

    /// <summary>
    /// 화면 언어 설정입니다. 저장하면 앱을 다시 켜지 않고 바로 바뀝니다.
    /// </summary>
    public string UiLanguage
    {
        get => _uiLanguage;
        set => SetProperty(ref _uiLanguage, value ?? string.Empty);
    }

    /// <summary>
    /// 자동 전환 규칙이 하나도 없는지 여부입니다.
    /// </summary>
    public bool HasNoAutoProfileRules => AutoProfileRules.Count == 0;

    /// <summary>
    /// Windows 자동 실행 시 최소화 여부입니다.
    /// </summary>
    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetProperty(ref _startMinimized, value);
    }

    /// <summary>
    /// Windows 로그인 후 현재 사용자 세션에서 앱을 자동 실행할지 여부입니다.
    /// </summary>
    public bool RunAtWindowsStartup
    {
        get => _runAtWindowsStartup;
        set => SetProperty(ref _runAtWindowsStartup, value);
    }

    /// <summary>
    /// 닫기 동작을 트레이 최소화로 바꿀지 여부입니다.
    /// </summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    /// <summary>
    /// 연결된 장치만 메인 화면에 표시할지 여부입니다.
    /// </summary>
    public bool ShowOnlyConnectedDevices
    {
        get => _showOnlyConnectedDevices;
        set => SetProperty(ref _showOnlyConnectedDevices, value);
    }

    /// <summary>
    /// Windows 시스템 사운드 세션도 메인 화면 프로그램 목록에 포함할지 여부입니다.
    /// </summary>
    public bool ShowSystemSounds
    {
        get => _showSystemSounds;
        set => SetProperty(ref _showSystemSounds, value);
    }

    /// <summary>
    /// 현재 재생 중인 앱 세션만 메인 화면에 표시할지 여부입니다.
    /// </summary>
    public bool ShowOnlyActiveSessions
    {
        get => _showOnlyActiveSessions;
        set => SetProperty(ref _showOnlyActiveSessions, value);
    }

    /// <summary>
    /// 앱 내 업데이트 확인 시 prerelease 릴리즈까지 함께 안내할지 여부입니다.
    /// </summary>
    public bool IncludePreReleaseUpdates
    {
        get => _includePreReleaseUpdates;
        set => SetProperty(ref _includePreReleaseUpdates, value);
    }

    /// <summary>
    /// 장치/세션 조회 같은 상세 디버그 로그를 기록할지 여부입니다.
    /// </summary>
    public bool EnableDebugLogs
    {
        get => _enableDebugLogs;
        set
        {
            if (SetProperty(ref _enableDebugLogs, value))
            {
                OnPropertyChanged(nameof(ShowsDebugLogRestartWarning));
            }
        }
    }

    /// <summary>
    /// 디버그 로그를 OFF에서 ON으로 바꾼 상태라 저장 후 재시작 안내가 필요한지 여부입니다.
    /// </summary>
    public bool ShowsDebugLogRestartWarning => !_loadedEnableDebugLogs && EnableDebugLogs;

    /// <summary>
    /// 디버그 로그를 처음부터 기록하기 위해 저장 후 앱 재시작이 필요한지 여부입니다.
    /// </summary>
    public bool RequiresRestartToEnableDebugLogs { get; private set; }

    /// <summary>
    /// 현재 설정창에서 선택한 수동 오디오 프로필입니다.
    /// </summary>
    public AudioProfileSelectionViewModel? SelectedAudioProfile
    {
        get => _selectedAudioProfile;
        set
        {
            if (SetProperty(ref _selectedAudioProfile, value))
            {
                OnPropertyChanged(nameof(HasSelectedAudioProfile));
                CommitSelectedProfileDetail();
                LoadSelectedProfileDetail();
            }
        }
    }

    /// <summary>
    /// 저장된 수동 프로필이 하나 이상 있는지 여부입니다.
    /// </summary>
    public bool HasAudioProfiles => AudioProfiles.Count > 0;

    /// <summary>
    /// 현재 적용 또는 삭제할 수동 프로필이 선택되어 있는지 여부입니다.
    /// </summary>
    public bool HasSelectedAudioProfile => SelectedAudioProfile is not null;

    /// <summary>
    /// 설정 파일과 장치 목록을 읽어 현재 설정 창 상태를 구성합니다.
    /// </summary>
    public void Load()
    {
        ApplySnapshot(BuildSnapshot());
    }

    /// <summary>
    /// 설정 창 로딩은 백그라운드에서 수행하고 UI 반영만 Dispatcher에서 처리합니다.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await Task.Run(BuildSnapshot, cancellationToken);

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplySnapshot(snapshot);
        });
    }

    /// <summary>
    /// 설정 창의 현재 상태를 앱 설정 모델로 변환해 저장합니다.
    /// </summary>
    public void Save()
    {
        RequiresRestartToEnableDebugLogs = false;
        var currentSettings = _settingsService.Load();
        var savePlan = BuildSavePlan(currentSettings);
        var problems = AutoProfileSettingsValidator.Validate(savePlan.Settings)
            .Concat(_profileDetailEdits.Values.SelectMany(edit => edit.Problems))
            .ToList();
        if (problems.Count > 0)
        {
            throw new SettingsValidationException(problems);
        }
        if (savePlan.PreservedConfiguredVisibleDevices)
        {
            AppLog.Warn(
                "SettingsViewModel",
                $"제한 모드 저장 보호 적용 preservedVisibleDevices={savePlan.Settings.VisibleDeviceIds.Count}");
        }

        // 설정 파일 저장과 자동 실행 레지스트리 반영은 같은 사용자 의도이므로 함께 적용합니다.
        // 구성된 설정 모델을 파일에 저장합니다.
        _settingsService.Save(savePlan.Settings);
        _startupLaunchService.Apply(RunAtWindowsStartup);
        AppLog.ConfigureDebugLogging(EnableDebugLogs);

        // 언어는 저장하는 즉시 바뀝니다. 화면 문구는 언어 변경 알림으로 다시 그려집니다.
        Loc.Configure(savePlan.Settings.UiLanguage);
        RequiresRestartToEnableDebugLogs = savePlan.RequiresRestartToEnableDebugLogs;
        _loadedEnableDebugLogs = EnableDebugLogs;
        OnPropertyChanged(nameof(ShowsDebugLogRestartWarning));
    }

    /// <summary>
    /// 현재 설정창에 보이는 값을 외부 JSON 파일로 내보냅니다.
    /// </summary>
    public void ExportSettings(string destinationFilePath)
    {
        var savePlan = BuildSavePlan(_settingsService.Load());
        _settingsService.ExportToFile(savePlan.Settings, destinationFilePath);
    }

    /// <summary>
    /// 최근 로그, 설정, 캐시, 버전 정보를 마스킹한 zip 진단 패키지로 내보냅니다.
    /// </summary>
    public string ExportDiagnosticPackage(string destinationZipPath)
    {
        var diagnosticPackageService = new DiagnosticPackageService(_settingsService);
        return diagnosticPackageService.Export(destinationZipPath);
    }

    /// <summary>
    /// 진단 패키지를 만들고 GitHub 이슈 작성 화면에 넣을 redacted 초안을 생성합니다.
    /// </summary>
    public DiagnosticIssueDraft ExportDiagnosticIssueDraft(string destinationZipPath)
    {
        var packagePath = ExportDiagnosticPackage(destinationZipPath);
        var draftService = new DiagnosticIssueDraftService();
        return draftService.BuildDraft(packagePath);
    }

    /// <summary>
    /// 외부 JSON 설정 파일을 현재 앱 설정으로 저장하고 화면 상태에도 반영합니다.
    /// </summary>
    public void ImportSettings(string sourceFilePath)
    {
        ApplyPersistedSettings(_settingsService.ImportFromFile(sourceFilePath));
    }

    /// <summary>
    /// 현재 설정창 상태와 저장된 프로그램별 값을 새 수동 프로필로 저장합니다.
    /// </summary>
    public void CreateAudioProfile(string profileName)
    {
        var currentSettings = _settingsService.Load();
        var profileSourceSettings = BuildSavePlan(currentSettings).Settings;
        var uniqueName = AudioProfileStore.BuildUniqueProfileName(profileName, currentSettings.AudioProfiles);
        var profile = AudioProfileStore.Capture(uniqueName, profileSourceSettings);

        currentSettings.AudioProfiles.Add(profile);
        _settingsService.Save(currentSettings);
        RefreshAudioProfiles(currentSettings.AudioProfiles, profile.Id);
        AppLog.Info(
            "SettingsViewModel",
            $"수동 프로필 생성 profileId={profile.Id} name={profile.Name} visibleDevices={profile.VisibleDeviceIds.Count} programPreferences={profile.ProgramAudioPreferences.Count}");
    }

    /// <summary>
    /// 선택한 수동 프로필을 현재 설정에 명시적으로 적용합니다.
    /// </summary>
    public void ApplySelectedAudioProfile()
    {
        if (SelectedAudioProfile is null)
        {
            throw new InvalidOperationException(Loc.T("적용할 프로필이 선택되지 않았습니다."));
        }

        var persistedSettings = _settingsService.Load();
        var profile = persistedSettings.AudioProfiles.FirstOrDefault(item => item.Id == SelectedAudioProfile.Id)
            ?? throw new InvalidOperationException(Loc.T("선택한 프로필을 설정 파일에서 찾지 못했습니다."));
        var currentUiSettings = BuildSavePlan(persistedSettings).Settings;

        // 저장 전 편집 값(기본 장치·앱별 출력)도 함께 적용되도록 편집 중인 프로필 값을 씁니다.
        profile = currentUiSettings.AudioProfiles.FirstOrDefault(item => item.Id == profile.Id) ?? profile;

        AppLog.Info(
            "SettingsViewModel",
            $"수동 프로필 적용 profileId={profile.Id} name={profile.Name} visibleDevices={profile.VisibleDeviceIds.Count} programPreferences={profile.ProgramAudioPreferences.Count}");
        ApplyPersistedSettings(AudioProfileStore.ApplyProfile(currentUiSettings, profile));
        AppliedProfileIdInSession = profile.Id;
    }

    /// <summary>
    /// 선택한 수동 프로필만 삭제하고 현재 적용된 설정은 유지합니다.
    /// </summary>
    public void DeleteSelectedAudioProfile()
    {
        if (SelectedAudioProfile is null)
        {
            throw new InvalidOperationException(Loc.T("삭제할 프로필이 선택되지 않았습니다."));
        }

        var deletedProfileId = SelectedAudioProfile.Id;
        var currentSettings = _settingsService.Load();
        var removedCount = currentSettings.AudioProfiles.RemoveAll(profile => profile.Id == deletedProfileId);
        if (removedCount == 0)
        {
            throw new InvalidOperationException(Loc.T("선택한 프로필을 설정 파일에서 찾지 못했습니다."));
        }

        if (string.Equals(currentSettings.LastAppliedAudioProfileId, deletedProfileId, StringComparison.Ordinal))
        {
            currentSettings.LastAppliedAudioProfileId = string.Empty;
        }

        _settingsService.Save(currentSettings);
        _profileDetailEdits.Remove(deletedProfileId);
        _editingProfileId = null;
        RefreshAudioProfiles(currentSettings.AudioProfiles, AudioProfileStore.FindAppliedProfileId(currentSettings));
        AppLog.Info("SettingsViewModel", $"수동 프로필 삭제 profileId={deletedProfileId}");
    }

    /// <summary>
    /// 모든 설정을 기본값으로 초기화합니다.
    /// </summary>
    public void ResetSettings()
    {
        ApplyPersistedSettings(new AppSettings());
    }

    /// <summary>
    /// 프로그램별 볼륨, 음소거, 사용자 지정 이름 저장값만 비웁니다.
    /// </summary>
    public void ClearProgramAudioPreferences()
    {
        var settings = _settingsService.Load();
        settings.ProgramAudioPreferences = [];
        ApplyPersistedSettings(settings);
    }

    /// <summary>
    /// 설정 파일과 장치 목록을 백그라운드에서 읽어 스냅샷으로 만듭니다.
    /// </summary>
    private SettingsSnapshot BuildSnapshot()
    {
        return new SettingsSnapshot
        {
            Settings = _settingsService.Load(),
            Devices = _audioDeviceCatalogService.GetAvailableOutputDevices().ToList(),
            RunningProcessNames = ReadWindowedProcessNames(),
            InputDevices = ReadInputDevices()
        };
    }

    /// <summary>
    /// 백그라운드에서 읽은 설정 스냅샷을 현재 설정 창 상태에 반영합니다.
    /// </summary>
    private void ApplySnapshot(SettingsSnapshot snapshot)
    {
        _profileDetailEdits.Clear();
        _editingProfileId = null;
        AvailableDevices.Clear();
        foreach (var device in snapshot.Devices)
        {
            AvailableDevices.Add(new AudioDeviceSelectionViewModel
            {
                Id = device.Id,
                DisplayName = device.Name,
                IsConnected = device.IsConnected,
                IsSelected = snapshot.Settings.VisibleDeviceIds.Contains(device.Id)
            });
        }

        StartMinimized = snapshot.Settings.StartMinimized;
        RunAtWindowsStartup = snapshot.Settings.RunAtWindowsStartup;
        MinimizeToTray = snapshot.Settings.MinimizeToTray;
        ShowOnlyConnectedDevices = snapshot.Settings.ShowOnlyConnectedDevices;
        ShowSystemSounds = snapshot.Settings.ShowSystemSounds;
        ShowOnlyActiveSessions = snapshot.Settings.ShowOnlyActiveSessions;
        IncludePreReleaseUpdates = snapshot.Settings.IncludePreReleaseUpdates;
        _loadedEnableDebugLogs = snapshot.Settings.EnableDebugLogs;
        EnableDebugLogs = snapshot.Settings.EnableDebugLogs;
        if (snapshot.InputDevices.Count > 0 || _inputDevices.Count == 0)
        {
            _inputDevices = snapshot.InputDevices;
        }

        RefreshDeviceOptions(snapshot.Devices);
        if (snapshot.RunningProcessNames.Count > 0)
        {
            RunningProcessNames.Clear();
            foreach (var processName in snapshot.RunningProcessNames)
            {
                RunningProcessNames.Add(processName);
            }
        }

        UiLanguage = snapshot.Settings.UiLanguage;
        AutoProfileSwitchEnabled = snapshot.Settings.AutoProfileSwitchEnabled;
        ShowAutoProfileNotifications = snapshot.Settings.ShowAutoProfileNotifications;
        EnableProfileHotkeys = snapshot.Settings.EnableProfileHotkeys;
        AutoProfileRules.Clear();
        foreach (var rule in snapshot.Settings.AutoProfileRules)
        {
            AutoProfileRules.Add(new AutoProfileRuleViewModel
            {
                Id = rule.Id,
                Name = rule.Name,
                IsEnabled = rule.IsEnabled,
                ProcessNamesText = string.Join(", ", rule.ProcessNames),
                ProfileId = rule.ProfileId,
                RestoreMode = rule.RestoreMode
            });
        }

        RenumberAutoProfileRules();
        RefreshAudioProfiles(snapshot.Settings.AudioProfiles, AudioProfileStore.FindAppliedProfileId(snapshot.Settings));
        AutoProfileFallbackProfileId = snapshot.Settings.AutoProfileFallbackProfileId;
        OnPropertyChanged(nameof(ShowsDebugLogRestartWarning));
        RequiresRestartToEnableDebugLogs = false;
    }

    private SettingsSavePlan BuildSavePlan(AppSettings currentSettings)
    {
        var savePlan = SettingsSavePlanner.Build(
            currentSettings,
            [.. AvailableDevices.Select(device => new VisibleDeviceSelection(device.Id, device.IsSelected))],
            StartMinimized,
            RunAtWindowsStartup,
            MinimizeToTray,
            ShowOnlyConnectedDevices,
            ShowSystemSounds,
            ShowOnlyActiveSessions,
            IncludePreReleaseUpdates,
            EnableDebugLogs,
            _preserveConfiguredVisibleDevicesOnEmptySave);
        ApplyAutoProfileEdits(savePlan.Settings);
        return savePlan;
    }

    /// <summary>
    /// 설정창의 프로필 상세·자동 전환 편집 값을 저장할 설정 모델에 덮어씁니다.
    /// </summary>
    private void ApplyAutoProfileEdits(AppSettings settings)
    {
        CommitSelectedProfileDetail();
        foreach (var profile in settings.AudioProfiles)
        {
            if (!_profileDetailEdits.TryGetValue(profile.Id, out var edit))
            {
                continue;
            }

            profile.DefaultOutputDeviceId = edit.DefaultOutputDeviceId;
            profile.ProgramOutputRoutes = AudioProfileStore.CloneProgramOutputRoutes(edit.ProgramOutputRoutes);
            profile.Hotkey = edit.Hotkey;
            profile.OutputFallbackDeviceIds = [.. edit.OutputFallbackDeviceIds];
            profile.DefaultInputDeviceId = edit.DefaultInputDeviceId;
            profile.InputFallbackDeviceIds = [.. edit.InputFallbackDeviceIds];
            profile.DeviceLevels = AudioProfileStore.CloneDeviceLevels(edit.DeviceLevels);
        }

        // 다른 PC 로 옮겨 장치 ID 가 바뀌어도 이름으로 다시 찾을 수 있게 장치 이름을 함께 저장합니다.
        var knownDevices = BuildCurrentDeviceSnapshot().Concat(_inputDevices).ToList();
        foreach (var profile in settings.AudioProfiles)
        {
            profile.KnownDeviceNames = ProfileDeviceResolver.BuildKnownNames(profile, knownDevices);
        }

        settings.UiLanguage = UiLanguage;
        settings.AutoProfileSwitchEnabled = AutoProfileSwitchEnabled;
        settings.ShowAutoProfileNotifications = ShowAutoProfileNotifications;
        settings.EnableProfileHotkeys = EnableProfileHotkeys;
        settings.AutoProfileFallbackProfileId = AutoProfileFallbackProfileId;
        settings.AutoProfileRules = AutoProfileRules
            .Select(rule => new AutoProfileRule
            {
                Id = rule.Id,
                Name = rule.Name.Trim(),
                IsEnabled = rule.IsEnabled,
                ProcessNames = ProcessNameMatcher.SplitList(rule.ProcessNamesText),
                ProfileId = rule.ProfileId,
                RestoreMode = rule.RestoreMode
            })
            .ToList();
    }

    /// <summary>
    /// 선택한 프로필에 앱별 출력 줄을 하나 추가합니다.
    /// </summary>
    public void AddSelectedProfileRoute(string? processName = null)
    {
        if (SelectedAudioProfile is null)
        {
            return;
        }

        SelectedProfileRoutes.Add(new ProgramOutputRouteViewModel
        {
            ProcessName = processName ?? string.Empty,
            TargetDeviceId = string.Empty
        });
    }

    public void RemoveSelectedProfileRoute(ProgramOutputRouteViewModel route)
    {
        SelectedProfileRoutes.Remove(route);
    }

    /// <summary>
    /// 자동 전환 규칙을 목록 맨 아래(가장 낮은 우선순위)에 추가합니다.
    /// </summary>
    public void AddAutoProfileRule()
    {
        AutoProfileRules.Add(new AutoProfileRuleViewModel
        {
            ProfileId = SelectedAudioProfile?.Id ?? AudioProfiles.FirstOrDefault()?.Id ?? string.Empty
        });
        RenumberAutoProfileRules();
    }

    public void RemoveAutoProfileRule(AutoProfileRuleViewModel rule)
    {
        AutoProfileRules.Remove(rule);
        RenumberAutoProfileRules();
    }

    /// <summary>
    /// 규칙을 위(-1)나 아래(+1)로 옮겨 우선순위를 바꿉니다.
    /// </summary>
    public void MoveAutoProfileRule(AutoProfileRuleViewModel rule, int offset)
    {
        var index = AutoProfileRules.IndexOf(rule);
        var newIndex = index + offset;
        if (index < 0 || newIndex < 0 || newIndex >= AutoProfileRules.Count)
        {
            return;
        }

        AutoProfileRules.Move(index, newIndex);
        RenumberAutoProfileRules();
    }

    private void RenumberAutoProfileRules()
    {
        for (var index = 0; index < AutoProfileRules.Count; index++)
        {
            AutoProfileRules[index].Priority = index + 1;
        }

        OnPropertyChanged(nameof(HasNoAutoProfileRules));
    }

    /// <summary>
    /// 편집 영역의 값을 현재 편집 중인 프로필 편집 값으로 보관합니다.
    /// </summary>
    private void CommitSelectedProfileDetail()
    {
        if (_editingProfileId is null)
        {
            return;
        }

        _profileDetailEdits[_editingProfileId] = new ProfileDetailEdit
        {
            DefaultOutputDeviceId = SelectedProfileDefaultDeviceId,
            ProgramOutputRoutes = SelectedProfileRoutes
                .Select(route => new ProgramOutputRoute
                {
                    ProcessName = ProcessNameMatcher.Normalize(route.ProcessName),
                    TargetDeviceId = route.TargetDeviceId
                })
                .Where(route => route.ProcessName.Length > 0)
                .ToList(),
            Hotkey = SelectedProfileHotkey.Trim(),
            OutputFallbackDeviceIds = new[] { SelectedProfileOutputFallback1, SelectedProfileOutputFallback2 }
                .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            DefaultInputDeviceId = SelectedProfileDefaultInputDeviceId,
            InputFallbackDeviceIds = string.IsNullOrWhiteSpace(SelectedProfileInputFallback1) ? [] : [SelectedProfileInputFallback1],
            DeviceLevels = BuildDeviceLevels(out var levelProblems),
            Problems = levelProblems
        };
    }

    private List<ProfileDeviceLevel> BuildDeviceLevels(out List<string> problems)
    {
        problems = [];
        var levels = new List<ProfileDeviceLevel>();
        var profileName = SelectedAudioProfile?.Name ?? string.Empty;
        foreach (var row in SelectedProfileDeviceLevels)
        {
            if (string.IsNullOrWhiteSpace(row.DeviceId))
            {
                continue;
            }

            int? volume = null;
            var volumeText = row.VolumeText.Trim().TrimEnd('%').Trim();
            if (volumeText.Length > 0)
            {
                if (!int.TryParse(volumeText, out var parsedVolume) || parsedVolume is < 0 or > 100)
                {
                    problems.Add(Loc.F("프로필 '{0}' 장치 볼륨 '{1}': 0~100 사이 숫자로 적거나 비워 두세요.", profileName, row.VolumeText));
                    continue;
                }

                volume = parsedVolume;
            }

            if (volume is null && row.IsMuted is null)
            {
                continue;
            }

            levels.Add(new ProfileDeviceLevel { DeviceId = row.DeviceId, Volume = volume, IsMuted = row.IsMuted });
        }

        return levels;
    }

    public void AddSelectedProfileDeviceLevel()
    {
        if (SelectedAudioProfile is null)
        {
            return;
        }

        SelectedProfileDeviceLevels.Add(new DeviceLevelViewModel
        {
            DeviceId = LevelDeviceOptions.FirstOrDefault()?.Id ?? string.Empty
        });
    }

    public void RemoveSelectedProfileDeviceLevel(DeviceLevelViewModel level)
    {
        SelectedProfileDeviceLevels.Remove(level);
    }

    private void LoadSelectedProfileDetail()
    {
        SelectedProfileRoutes.Clear();
        SelectedProfileDeviceLevels.Clear();
        _editingProfileId = SelectedAudioProfile?.Id;
        if (_editingProfileId is null)
        {
            SelectedProfileDefaultDeviceId = string.Empty;
            SelectedProfileHotkey = string.Empty;
            SelectedProfileOutputFallback1 = string.Empty;
            SelectedProfileOutputFallback2 = string.Empty;
            SelectedProfileDefaultInputDeviceId = string.Empty;
            SelectedProfileInputFallback1 = string.Empty;
            return;
        }

        if (!_profileDetailEdits.TryGetValue(_editingProfileId, out var edit))
        {
            _profilesById.TryGetValue(_editingProfileId, out var profile);
            var knownNames = profile?.KnownDeviceNames ?? [];
            var outputDevices = BuildCurrentDeviceSnapshot();

            // 다른 PC 에서 가져온 프로필이면 장치 ID 가 달라서, 저장해 둔 이름으로 지금 장치를 다시 찾아 넣습니다.
            string Remap(string deviceId, IReadOnlyList<AudioDeviceInfo> devices) =>
                RemapDeviceId(deviceId, devices, knownNames);

            edit = new ProfileDetailEdit
            {
                DefaultOutputDeviceId = Remap(profile?.DefaultOutputDeviceId ?? string.Empty, outputDevices),
                ProgramOutputRoutes = AudioProfileStore.CloneProgramOutputRoutes(profile?.ProgramOutputRoutes)
                    .Select(route => new ProgramOutputRoute
                    {
                        ProcessName = route.ProcessName,
                        TargetDeviceId = Remap(route.TargetDeviceId, outputDevices)
                    })
                    .ToList(),
                Hotkey = profile?.Hotkey ?? string.Empty,
                OutputFallbackDeviceIds = (profile?.OutputFallbackDeviceIds ?? []).Select(deviceId => Remap(deviceId, outputDevices)).ToList(),
                DefaultInputDeviceId = Remap(profile?.DefaultInputDeviceId ?? string.Empty, _inputDevices),
                InputFallbackDeviceIds = (profile?.InputFallbackDeviceIds ?? []).Select(deviceId => Remap(deviceId, _inputDevices)).ToList(),
                DeviceLevels = AudioProfileStore.CloneDeviceLevels(profile?.DeviceLevels)
                    .Select(level => new ProfileDeviceLevel
                    {
                        DeviceId = Remap(level.DeviceId, outputDevices),
                        Volume = level.Volume,
                        IsMuted = level.IsMuted
                    })
                    .ToList()
            };
        }

        EnsureDeviceOption(DefaultDeviceOptions, edit.DefaultOutputDeviceId);
        SelectedProfileDefaultDeviceId = edit.DefaultOutputDeviceId;
        SelectedProfileHotkey = edit.Hotkey;
        var outputFallback1 = edit.OutputFallbackDeviceIds.ElementAtOrDefault(0) ?? string.Empty;
        var outputFallback2 = edit.OutputFallbackDeviceIds.ElementAtOrDefault(1) ?? string.Empty;
        EnsureDeviceOption(OutputFallbackOptions, outputFallback1);
        EnsureDeviceOption(OutputFallbackOptions, outputFallback2);
        SelectedProfileOutputFallback1 = outputFallback1;
        SelectedProfileOutputFallback2 = outputFallback2;
        EnsureDeviceOption(InputDeviceOptions, edit.DefaultInputDeviceId);
        SelectedProfileDefaultInputDeviceId = edit.DefaultInputDeviceId;
        var inputFallback1 = edit.InputFallbackDeviceIds.ElementAtOrDefault(0) ?? string.Empty;
        EnsureDeviceOption(InputFallbackOptions, inputFallback1);
        SelectedProfileInputFallback1 = inputFallback1;
        foreach (var route in edit.ProgramOutputRoutes)
        {
            EnsureDeviceOption(RouteDeviceOptions, route.TargetDeviceId);
            SelectedProfileRoutes.Add(new ProgramOutputRouteViewModel
            {
                ProcessName = route.ProcessName,
                TargetDeviceId = route.TargetDeviceId
            });
        }

        foreach (var level in edit.DeviceLevels)
        {
            EnsureDeviceOption(LevelDeviceOptions, level.DeviceId);
            SelectedProfileDeviceLevels.Add(new DeviceLevelViewModel
            {
                DeviceId = level.DeviceId,
                VolumeText = level.Volume?.ToString() ?? string.Empty,
                IsMuted = level.IsMuted
            });
        }
    }

    /// <summary>
    /// 지금 목록에 없는 장치 ID 를 저장된 장치 이름으로 다시 찾습니다. 못 찾으면 원래 ID 를 둡니다.
    /// </summary>
    private static string RemapDeviceId(string deviceId, IReadOnlyList<AudioDeviceInfo> devices, IEnumerable<KnownDeviceName> knownNames)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || devices.Any(device => device.Id == deviceId))
        {
            return deviceId;
        }

        var knownName = knownNames.FirstOrDefault(name => name.DeviceId == deviceId)?.Name;
        if (string.IsNullOrWhiteSpace(knownName))
        {
            return deviceId;
        }

        var normalizedName = ProfileCommandLine.NormalizeName(knownName);
        return devices.FirstOrDefault(device => ProfileCommandLine.NormalizeName(device.Name) == normalizedName)?.Id ?? deviceId;
    }

    private void RefreshDeviceOptions(IReadOnlyList<AudioDeviceInfo> devices)
    {
        DefaultDeviceOptions.Clear();
        RouteDeviceOptions.Clear();
        OutputFallbackOptions.Clear();
        LevelDeviceOptions.Clear();
        InputDeviceOptions.Clear();
        InputFallbackOptions.Clear();
        DefaultDeviceOptions.Add(new DeviceOptionViewModel(string.Empty, Loc.T("(바꾸지 않음)")));
        RouteDeviceOptions.Add(new DeviceOptionViewModel(string.Empty, Loc.T("Windows 기본 장치 따름")));
        OutputFallbackOptions.Add(new DeviceOptionViewModel(string.Empty, Loc.T("(없음)")));
        InputDeviceOptions.Add(new DeviceOptionViewModel(string.Empty, Loc.T("(바꾸지 않음)")));
        InputFallbackOptions.Add(new DeviceOptionViewModel(string.Empty, Loc.T("(없음)")));
        foreach (var device in devices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = device.IsConnected ? device.Name : Loc.F("{0} (연결 안 됨)", device.Name);
            DefaultDeviceOptions.Add(new DeviceOptionViewModel(device.Id, name));
            RouteDeviceOptions.Add(new DeviceOptionViewModel(device.Id, name));
            OutputFallbackOptions.Add(new DeviceOptionViewModel(device.Id, name));
            LevelDeviceOptions.Add(new DeviceOptionViewModel(device.Id, name));
        }

        foreach (var device in _inputDevices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = device.IsConnected ? device.Name : Loc.F("{0} (연결 안 됨)", device.Name);
            InputDeviceOptions.Add(new DeviceOptionViewModel(device.Id, name));
            InputFallbackOptions.Add(new DeviceOptionViewModel(device.Id, name));
        }
    }

    /// <summary>
    /// 지금 목록에 없는 장치(뽑아 둔 헤드셋 등)를 저장해 둔 경우에도 값이 사라지지 않게 선택지를 추가합니다.
    /// </summary>
    private static void EnsureDeviceOption(ObservableCollection<DeviceOptionViewModel> options, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || options.Any(option => option.Id == deviceId))
        {
            return;
        }

        options.Add(new DeviceOptionViewModel(deviceId, Loc.T("(현재 없는 장치)")));
    }

    private IReadOnlyList<AudioDeviceInfo> ReadInputDevices()
    {
        try
        {
            return _audioDeviceCatalogService.GetAvailableInputDevices();
        }
        catch (Exception exception)
        {
            AppLog.Warn("SettingsViewModel", "입력 장치 목록 읽기 실패", exception);
            return [];
        }
    }

    /// <summary>
    /// 창이 있는 실행 중 프로그램 이름을 읽습니다. 백그라운드 프로세스는 너무 많아 추천 목록에서 뺍니다.
    /// </summary>
    private static IReadOnlyList<string> ReadWindowedProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            names.Add(process.ProcessName);
                        }
                    }
                    catch
                    {
                        // 읽는 사이 종료됐거나 접근이 막힌 프로세스는 건너뜁니다.
                    }
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("SettingsViewModel", "실행 중 프로그램 목록 읽기 실패", exception);
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ApplyPersistedSettings(AppSettings settings)
    {
        var previousSettings = _settingsService.Load();
        var requiresRestartToEnableDebugLogs = !previousSettings.EnableDebugLogs && settings.EnableDebugLogs;

        _settingsService.Save(settings);
        _startupLaunchService.Apply(settings.RunAtWindowsStartup);
        AppLog.ConfigureDebugLogging(settings.EnableDebugLogs);
        Loc.Configure(settings.UiLanguage);

        ApplySnapshot(new SettingsSnapshot
        {
            Settings = settings,
            Devices = BuildCurrentDeviceSnapshot()
        });
        RequiresRestartToEnableDebugLogs = requiresRestartToEnableDebugLogs;
    }

    private List<AudioDeviceInfo> BuildCurrentDeviceSnapshot()
    {
        return AvailableDevices
            .Select(device => new AudioDeviceInfo
            {
                Id = device.Id,
                Name = device.DisplayName,
                IsConnected = device.IsConnected
            })
            .ToList();
    }

    private void RefreshAudioProfiles(IReadOnlyList<AudioProfile> profiles, string? preferredSelectedProfileId = null)
    {
        CommitSelectedProfileDetail();
        _profilesById = profiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);
        _editingProfileId = null;
        _selectedAudioProfile = null;
        OnPropertyChanged(nameof(SelectedAudioProfile));

        // 콤보 상자 목록을 비우면 WPF 가 선택값을 null 로 되돌려 쓰므로, 비우기 전에 규칙·기본 프로필 값을 잡아 둡니다.
        var ruleProfileIds = AutoProfileRules.ToDictionary(rule => rule, rule => rule.ProfileId);
        var fallbackProfileId = AutoProfileFallbackProfileId;
        AudioProfiles.Clear();
        foreach (var profile in profiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            AudioProfiles.Add(new AudioProfileSelectionViewModel(profile.Id, profile.Name));
        }

        FallbackProfileOptions.Clear();
        FallbackProfileOptions.Add(new AudioProfileSelectionViewModel(string.Empty, Loc.T("(지정 안 함)")));
        foreach (var profile in AudioProfiles)
        {
            FallbackProfileOptions.Add(profile);
        }

        // 값을 한 번 비웠다가 다시 넣어 콤보 상자가 새 목록에서 선택을 다시 찾게 합니다.
        AutoProfileFallbackProfileId = string.Empty;
        AutoProfileFallbackProfileId = fallbackProfileId;
        foreach (var (rule, ruleProfileId) in ruleProfileIds)
        {
            rule.ProfileId = string.Empty;
            rule.ProfileId = ruleProfileId;
        }

        SelectedAudioProfile = AudioProfiles.FirstOrDefault(profile => profile.Id == preferredSelectedProfileId);
        if (SelectedAudioProfile is null)
        {
            LoadSelectedProfileDetail();
        }

        OnPropertyChanged(nameof(HasAudioProfiles));
        OnPropertyChanged(nameof(HasSelectedAudioProfile));
    }
}
