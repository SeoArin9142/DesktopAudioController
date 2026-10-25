using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using DesktopAudioController.Infrastructure;
using DesktopAudioController.Models;
using DesktopAudioController.Services;
using System.Windows.Threading;

namespace DesktopAudioController.ViewModels;

/// <summary>
/// 메인 화면에 표시할 장치 목록과 첫 실행 상태를 관리하는 뷰모델입니다.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private sealed class DeviceSnapshot
    {
        public required AudioDeviceInfo Device { get; init; }

        public required IReadOnlyList<AudioSessionInfo> Sessions { get; init; }
    }

    private sealed class LoadSnapshot
    {
        public required bool ShowSystemSounds { get; init; }

        public required bool ShowOnlyActiveSessions { get; init; }

        public required bool HasConfiguredDevices { get; init; }

        public required IReadOnlyList<DeviceSnapshot> Devices { get; init; }

        public required IReadOnlyList<ProgramAudioPreference> ProgramAudioPreferences { get; init; }
    }

    private sealed class StateSnapshot
    {
        public required IReadOnlyDictionary<string, DeviceSnapshot> DevicesById { get; init; }
    }

    private readonly record struct VisibleDeviceSessionTarget(string DeviceId, bool IsConnected);

    private sealed class SessionOnlySnapshot
    {
        public required IReadOnlyDictionary<string, IReadOnlyList<AudioSessionInfo>> SessionsByDeviceId { get; init; }
    }

    // 저장된 사용자 설정을 읽기 위한 서비스입니다.
    private readonly ISettingsService _settingsService;

    // 현재 출력 장치 상태를 조회하기 위한 서비스입니다.
    private readonly IAudioDeviceCatalogService _audioDeviceCatalogService;

    // 장치별 앱 세션을 조회하기 위한 서비스입니다.
    private readonly IAudioSessionService _audioSessionService;

    // 실행 파일 경로 기준으로 세션 아이콘을 조회하는 서비스입니다.
    private readonly IAppIconService _appIconService;

    // 사용자가 한 번이라도 표시 장치를 설정했는지 여부입니다.
    private bool _hasConfiguredDevices;

    // 마지막 전체 로드 시점의 시스템 사운드 표시 옵션입니다.
    private bool _showSystemSounds;

    // 마지막 전체 로드 시점의 재생 중 세션만 표시 옵션입니다.
    private bool _showOnlyActiveSessions;

    // 사용자가 저장한 프로그램별 볼륨/음소거 설정입니다.
    private Dictionary<string, ProgramAudioPreference> _programAudioPreferencesByKey = new(StringComparer.OrdinalIgnoreCase);

    // 마지막으로 읽거나 저장한 설정 사본입니다. 프로그램 설정 저장 시 settings.json 재로드를 피합니다.
    private AppSettings _cachedSettings = new();

    // 한 번 복원한 라이브 세션은 같은 프로세스 수명 동안 반복 복원하지 않도록 추적합니다.
    private readonly HashSet<string> _restoredSessionIds = new(StringComparer.Ordinal);

    // 패널을 열지 않아도 저장값을 맞춘 세션("장치ID|세션ID")입니다. 백그라운드 스레드에서도 쓰므로 잠금으로 보호합니다.
    private readonly HashSet<string> _enforcedSessionKeys = new(StringComparer.Ordinal);

    // 슬라이더 드래그 중 연속 저장을 줄이기 위해 마지막 변경만 저장하는 타이머입니다.
    private readonly DispatcherTimer _programPreferenceSaveDebounceTimer;

    // 아직 파일에 기록되지 않은 프로그램 설정 변경이 있는지 나타냅니다.
    private bool _hasPendingProgramPreferenceSave;

    // 오래 걸리는 백그라운드 스냅샷 작업이 늦게 끝나도 최신 요청만 UI에 적용하기 위한 세대 번호입니다.
    private int _loadGeneration;

    // 상태 부분 갱신 역시 마지막 요청만 반영하도록 별도 세대 번호를 둡니다.
    private int _stateGeneration;

    // 세션 전용 부분 갱신도 마지막 요청만 반영하도록 별도 세대 번호를 둡니다.
    private int _sessionGeneration;

    // 프로그램별 볼륨/음소거 저장은 짧게 모아 마지막 값만 파일에 기록합니다.
    private static readonly TimeSpan ProgramPreferenceSaveDebounceDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// 메인 화면이 사용할 서비스들을 주입받습니다.
    /// </summary>
    public MainViewModel(
        ISettingsService settingsService,
        IAudioDeviceCatalogService audioDeviceCatalogService,
        IAudioSessionService audioSessionService,
        IAppIconService appIconService)
    {
        _settingsService = settingsService;
        _audioDeviceCatalogService = audioDeviceCatalogService;
        _audioSessionService = audioSessionService;
        _appIconService = appIconService;
        _programPreferenceSaveDebounceTimer = new DispatcherTimer
        {
            Interval = ProgramPreferenceSaveDebounceDelay
        };
        _programPreferenceSaveDebounceTimer.Tick += ProgramPreferenceSaveDebounceTimer_OnTick;
    }

    /// <summary>
    /// 메인 화면에 실제로 렌더링될 장치 카드 목록입니다.
    /// </summary>
    public ObservableCollection<VisibleDeviceViewModel> VisibleDevices { get; } = [];

    /// <summary>
    /// 표시 장치가 한 번이라도 저장되었는지 나타냅니다.
    /// </summary>
    public bool HasConfiguredDevices
    {
        get => _hasConfiguredDevices;
        private set => SetProperty(ref _hasConfiguredDevices, value);
    }

    /// <summary>
    /// 장치 또는 세션 볼륨이 아직 서비스에 반영 대기 중인지 나타냅니다.
    /// </summary>
    public bool HasPendingAudioControlChanges =>
        VisibleDevices.Any(device => device.HasPendingVolumeCommit || device.Sessions.Any(session => session.HasPendingVolumeCommit));

    /// <summary>
    /// 프로그램별 볼륨 영역을 펼쳐둔 장치가 있는지 나타냅니다.
    /// </summary>
    public bool HasExpandedSessionDevices => VisibleDevices.Any(device => device.IsExpanded);

    /// <summary>
    /// 설정 파일과 장치 목록을 조합해 메인 화면 상태를 다시 계산합니다.
    /// </summary>
    public void Load()
    {
        ApplyLoadSnapshot(BuildLoadSnapshot());
    }

    /// <summary>
    /// 디스크에 남겨둔 최근 장치 스냅샷으로 먼저 화면 뼈대를 구성합니다.
    /// 세션 목록은 이후 백그라운드 전체 새로고침에서 채웁니다.
    /// </summary>
    public void LoadFromCachedDevices(IReadOnlyList<AudioDeviceInfo> cachedDevices)
    {
        ApplyLoadSnapshot(BuildLoadSnapshot(cachedDevices, includeSessions: false));
        AppLog.Info(
            "MainViewModel",
            $"시작 캐시 장치 스냅샷 적용 deviceCount={cachedDevices.Count} visibleDevices={VisibleDevices.Count} hasConfiguredDevices={HasConfiguredDevices}");
    }

    /// <summary>
    /// 장치/세션 조회는 백그라운드에서 수행하고, UI 반영만 Dispatcher에서 처리합니다.
    /// </summary>
    public async Task LoadAsync()
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        var snapshot = await Task.Run(BuildLoadSnapshot);
        if (generation != Volatile.Read(ref _loadGeneration))
        {
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (generation != Volatile.Read(ref _loadGeneration))
            {
                return;
            }

            ApplyLoadSnapshot(snapshot);
        });
    }

    /// <summary>
    /// 현재 보이는 장치 카드 구조를 유지한 채 상태 값만 부분 갱신합니다.
    /// </summary>
    public void RefreshStateOnly()
    {
        ApplyStateSnapshot(BuildStateSnapshot(
            VisibleDevices.Select(device => device.Id).ToList(),
            _showSystemSounds,
            _showOnlyActiveSessions));
    }

    /// <summary>
    /// 상태 조회는 백그라운드에서 수행하고, UI에는 결과만 적용합니다.
    /// </summary>
    public async Task RefreshStateOnlyAsync()
    {
        var visibleDeviceIds = VisibleDevices.Select(device => device.Id).ToList();
        var showSystemSounds = _showSystemSounds;
        var showOnlyActiveSessions = _showOnlyActiveSessions;
        var generation = Interlocked.Increment(ref _stateGeneration);
        var snapshot = await Task.Run(() => BuildStateSnapshot(visibleDeviceIds, showSystemSounds, showOnlyActiveSessions));
        if (generation != Volatile.Read(ref _stateGeneration))
        {
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (generation != Volatile.Read(ref _stateGeneration))
            {
                return;
            }

            ApplyStateSnapshot(snapshot);
        });
    }

    /// <summary>
    /// 장치 열거 없이 현재 보이는 장치들의 프로그램 세션 목록만 갱신합니다.
    /// </summary>
    public async Task RefreshSessionsOnlyAsync()
    {
        var visibleDeviceTargets = VisibleDevices
            .Where(device => device.IsExpanded)
            .Select(device => new VisibleDeviceSessionTarget(device.Id, device.IsConnected))
            .ToList();
        var showSystemSounds = _showSystemSounds;
        var showOnlyActiveSessions = _showOnlyActiveSessions;
        var preferencesByKey = _programAudioPreferencesByKey.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var generation = Interlocked.Increment(ref _sessionGeneration);
        var snapshot = await Task.Run(() => BuildSessionOnlySnapshot(
            visibleDeviceTargets,
            showSystemSounds,
            showOnlyActiveSessions,
            preferencesByKey));
        if (generation != Volatile.Read(ref _sessionGeneration))
        {
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (generation != Volatile.Read(ref _sessionGeneration))
            {
                return;
            }

            ApplySessionOnlySnapshot(snapshot);
        });
    }

    /// <summary>
    /// 타임아웃된 백그라운드 스냅샷이 늦게 돌아와도 이후 UI를 덮어쓰지 못하게 무효화합니다.
    /// </summary>
    public void InvalidatePendingSnapshots()
    {
        Interlocked.Increment(ref _loadGeneration);
        Interlocked.Increment(ref _stateGeneration);
        Interlocked.Increment(ref _sessionGeneration);
    }

    /// <summary>
    /// 백그라운드에서 전체 메인 화면 스냅샷을 만듭니다.
    /// </summary>
    private LoadSnapshot BuildLoadSnapshot()
    {
        return BuildLoadSnapshot(_audioDeviceCatalogService.GetAvailableOutputDevices(), includeSessions: true);
    }

    private LoadSnapshot BuildLoadSnapshot(IReadOnlyList<AudioDeviceInfo> devices, bool includeSessions)
    {
        // 파일에서 읽어온 현재 사용자 설정입니다.
        var settings = _settingsService.Load();
        _cachedSettings = CloneSettings(settings);

        // 조회 성능을 위해 선택된 ID 목록을 해시셋으로 변환합니다.
        var selectedIds = settings.VisibleDeviceIds.ToHashSet();

        // 메인 화면에는 사용자가 선택한 장치만 남기고 필요 시 연결된 장치만 필터링합니다.
        var loadedProgramAudioPreferencesByKey = settings.ProgramAudioPreferences
            .Where(preference => !string.IsNullOrWhiteSpace(preference.MatchKey))
            .GroupBy(preference => preference.MatchKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var visibleDevices = devices
            .Where(device => selectedIds.Contains(device.Id))
            .Where(device => !settings.ShowOnlyConnectedDevices || device.IsConnected)
            .Select(device => includeSessions
                ? BuildDeviceSnapshot(device, settings.ShowSystemSounds, settings.ShowOnlyActiveSessions, loadedProgramAudioPreferencesByKey)
                : BuildDeviceSkeletonSnapshot(device))
            .ToList();

        return new LoadSnapshot
        {
            ShowSystemSounds = settings.ShowSystemSounds,
            ShowOnlyActiveSessions = settings.ShowOnlyActiveSessions,
            HasConfiguredDevices = selectedIds.Count > 0,
            Devices = visibleDevices,
            ProgramAudioPreferences = settings.ProgramAudioPreferences
        };
    }

    /// <summary>
    /// 백그라운드에서 현재 보이는 장치들의 상태 스냅샷만 만듭니다.
    /// </summary>
    private StateSnapshot BuildStateSnapshot(IReadOnlyList<string> visibleDeviceIds, bool includeSystemSounds, bool showOnlyActiveSessions)
    {
        // devicesById는 최신 장치 상태를 빠르게 찾기 위한 인덱스입니다.
        var devicesById = _audioDeviceCatalogService
            .GetAvailableOutputDevices()
            .Where(device => visibleDeviceIds.Contains(device.Id))
            .ToDictionary(device => device.Id);

        var snapshotsById = new Dictionary<string, DeviceSnapshot>();
        foreach (var visibleDeviceId in visibleDeviceIds)
        {
            if (!devicesById.TryGetValue(visibleDeviceId, out var device))
            {
                continue;
            }

            snapshotsById[visibleDeviceId] = BuildDeviceSnapshot(device, includeSystemSounds, showOnlyActiveSessions, _programAudioPreferencesByKey);
        }

        return new StateSnapshot
        {
            DevicesById = snapshotsById
        };
    }

    /// <summary>
    /// 장치 열거 없이 현재 보이는 장치들의 세션 스냅샷만 만듭니다.
    /// </summary>
    private SessionOnlySnapshot BuildSessionOnlySnapshot(
        IReadOnlyList<VisibleDeviceSessionTarget> visibleDeviceTargets,
        bool includeSystemSounds,
        bool showOnlyActiveSessions,
        IReadOnlyDictionary<string, ProgramAudioPreference> preferencesByKey)
    {
        var sessionsByDeviceId = new Dictionary<string, IReadOnlyList<AudioSessionInfo>>(StringComparer.Ordinal);
        foreach (var target in visibleDeviceTargets)
        {
            sessionsByDeviceId[target.DeviceId] = target.IsConnected
                ? BuildSessionSnapshots(target.DeviceId, includeSystemSounds, showOnlyActiveSessions, preferencesByKey)
                : [];
        }

        return new SessionOnlySnapshot
        {
            SessionsByDeviceId = sessionsByDeviceId
        };
    }

    /// <summary>
    /// 장치 한 개에 대한 현재 상태와 세션 목록 스냅샷을 만듭니다.
    /// </summary>
    private DeviceSnapshot BuildDeviceSnapshot(
        AudioDeviceInfo device,
        bool includeSystemSounds,
        bool showOnlyActiveSessions,
        IReadOnlyDictionary<string, ProgramAudioPreference> preferencesByKey)
    {
        var sessions = device.IsConnected
            ? BuildSessionSnapshots(device.Id, includeSystemSounds, showOnlyActiveSessions, preferencesByKey)
            : [];

        return new DeviceSnapshot
        {
            Device = device,
            Sessions = sessions
        };
    }

    private IReadOnlyList<AudioSessionInfo> BuildSessionSnapshots(
        string deviceId,
        bool includeSystemSounds,
        bool showOnlyActiveSessions,
        IReadOnlyDictionary<string, ProgramAudioPreference> preferencesByKey)
    {
        try
        {
            var sessions = _audioSessionService.GetSessions(deviceId, includeSystemSounds).ToList();
            sessions = ApplyStoredSessionPresentations(sessions, preferencesByKey).ToList();
            sessions = FilterSessionsByPlaybackState(sessions, showOnlyActiveSessions).ToList();
            sessions = DisambiguateSessionDisplayNames(sessions).ToList();
            sessions = AnnotateSessionPlaybackState(sessions).ToList();
            return sessions;
        }
        catch (Exception exception)
        {
            AppLog.Warn("MainViewModel", $"세션 스냅샷 조회 실패 deviceId={deviceId}", exception);
            return [];
        }
    }

    private static DeviceSnapshot BuildDeviceSkeletonSnapshot(AudioDeviceInfo device)
    {
        return new DeviceSnapshot
        {
            Device = device,
            Sessions = []
        };
    }

    /// <summary>
    /// 전체 로드 스냅샷을 현재 UI 컬렉션에 반영합니다.
    /// </summary>
    private void ApplyLoadSnapshot(LoadSnapshot snapshot)
    {
        var expandedDeviceIds = VisibleDevices
            .Where(device => device.IsExpanded)
            .Select(device => device.Id)
            .ToHashSet(StringComparer.Ordinal);
        _showSystemSounds = snapshot.ShowSystemSounds;
        _showOnlyActiveSessions = snapshot.ShowOnlyActiveSessions;
        var loadedProgramAudioPreferencesByKey = snapshot.ProgramAudioPreferences
            .Where(preference => !string.IsNullOrWhiteSpace(preference.MatchKey))
            .GroupBy(preference => preference.MatchKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        if (_hasPendingProgramPreferenceSave)
        {
            foreach (var pendingPreference in _programAudioPreferencesByKey)
            {
                loadedProgramAudioPreferencesByKey[pendingPreference.Key] = pendingPreference.Value;
            }
        }

        _programAudioPreferencesByKey = loadedProgramAudioPreferencesByKey;

        VisibleDevices.Clear();
        foreach (var deviceSnapshot in snapshot.Devices)
        {
            var deviceViewModel = CreateVisibleDeviceViewModel(deviceSnapshot);
            deviceViewModel.IsExpanded = expandedDeviceIds.Contains(deviceViewModel.Id);
            VisibleDevices.Add(deviceViewModel);
        }

        MoveDefaultDeviceToTop();
        HasConfiguredDevices = snapshot.HasConfiguredDevices;
    }

    /// <summary>
    /// 상태 변경 스냅샷을 현재 보이는 장치 카드에만 부분 반영합니다.
    /// </summary>
    private void ApplyStateSnapshot(StateSnapshot snapshot)
    {
        foreach (var visibleDevice in VisibleDevices)
        {
            if (!snapshot.DevicesById.TryGetValue(visibleDevice.Id, out var deviceSnapshot))
            {
                continue;
            }

            var currentDevice = deviceSnapshot.Device;
            visibleDevice.UpdateSnapshot(
                currentDevice.Name,
                currentDevice.IsDefault,
                currentDevice.IsConnected,
                currentDevice.Volume,
                currentDevice.IsMuted);

            if (currentDevice.IsConnected)
            {
                ApplySessionSnapshots(visibleDevice, deviceSnapshot.Sessions);
            }
            else
            {
                foreach (var session in visibleDevice.Sessions)
                {
                    _restoredSessionIds.Remove(session.Id);
                }

                visibleDevice.Sessions.Clear();
            }
        }

        MoveDefaultDeviceToTop();
    }

    /// <summary>
    /// 지금 Windows 기본 출력 장치 카드를 맨 위로 올립니다. 나머지 장치는 원래 순서를 유지합니다.
    /// 카드 객체를 옮기기만 하므로 열어 둔 프로그램별 볼륨 패널은 그대로 유지됩니다.
    /// </summary>
    public void MoveDefaultDeviceToTop()
    {
        var defaultDevice = VisibleDevices.FirstOrDefault(device => device.IsDefault);
        if (defaultDevice is null)
        {
            return;
        }

        var index = VisibleDevices.IndexOf(defaultDevice);
        if (index > 0)
        {
            VisibleDevices.Move(index, 0);
        }
    }

    /// <summary>
    /// 세션 전용 스냅샷을 현재 장치 카드 구조에만 반영합니다.
    /// </summary>
    private void ApplySessionOnlySnapshot(SessionOnlySnapshot snapshot)
    {
        foreach (var visibleDevice in VisibleDevices)
        {
            if (!snapshot.SessionsByDeviceId.TryGetValue(visibleDevice.Id, out var sessions))
            {
                continue;
            }

            ApplySessionSnapshots(visibleDevice, sessions);
        }
    }

    /// <summary>
    /// 장치 스냅샷을 화면용 VisibleDeviceViewModel로 변환합니다.
    /// </summary>
    private VisibleDeviceViewModel CreateVisibleDeviceViewModel(DeviceSnapshot snapshot)
    {
        var device = snapshot.Device;

        var deviceViewModel = new VisibleDeviceViewModel(
            device.Id,
            device.Name,
            device.IsDefault,
            device.IsConnected,
            device.Volume,
            device.IsMuted,
            OnDeviceVolumeChanged,
            OnDeviceMutedChanged,
            OnSetDefaultDevice);

        foreach (var session in snapshot.Sessions)
        {
            deviceViewModel.Sessions.Add(CreateSessionViewModel(device.Id, GetEffectiveSessionSnapshot(device.Id, session)));
        }

        return deviceViewModel;
    }

    /// <summary>
    /// 세션 상태 스냅샷을 기존 장치 카드에 부분 반영합니다.
    /// </summary>
    private void ApplySessionSnapshots(VisibleDeviceViewModel device, IReadOnlyList<AudioSessionInfo> sessionSnapshots)
    {
        var snapshotById = BuildSessionSnapshotMap(sessionSnapshots);
        var existingById = BuildExistingSessionMap(device);

        for (var index = device.Sessions.Count - 1; index >= 0; index--)
        {
            var session = device.Sessions[index];
            if (!snapshotById.ContainsKey(session.Id))
            {
                _restoredSessionIds.Remove(session.Id);
                device.Sessions.RemoveAt(index);
            }
        }

        foreach (var rawSnapshot in snapshotById.Values)
        {
            var snapshot = GetEffectiveSessionSnapshot(device.Id, rawSnapshot);
            if (existingById.TryGetValue(snapshot.Id, out var existingSession))
            {
                // cachedIcon은 이미 메모리에 올라와 있으면 즉시 쓸 수 있는 아이콘입니다.
                var cachedIcon = _appIconService.TryGetCachedIcon(snapshot.IconSourcePath);

                // iconImageForSnapshot은 경로가 그대로일 때 기존 아이콘을 유지해 불필요한 깜빡임을 줄이기 위한 값입니다.
                var iconImageForSnapshot =
                    cachedIcon ??
                    (string.Equals(existingSession.IconSourcePath, snapshot.IconSourcePath, StringComparison.OrdinalIgnoreCase)
                        ? existingSession.IconImage
                        : null);

                existingSession.UpdateSnapshot(
                    snapshot.MatchKey,
                    snapshot.DisplayName,
                    snapshot.DisambiguationText,
                    snapshot.ExecutablePath,
                    snapshot.IconSourcePath,
                    iconImageForSnapshot,
                    snapshot.Volume,
                    snapshot.IsMuted,
                    snapshot.IsActive);

                // 캐시에 아이콘이 없을 때만 비동기 로딩을 예약합니다.
                if (cachedIcon is null)
                {
                    _ = LoadSessionIconAsync(existingSession);
                }

                continue;
            }

            device.Sessions.Add(CreateSessionViewModel(device.Id, snapshot));
        }
    }

    /// <summary>
    /// 저장된 프로그램 설정이 있으면 세션 첫 등장 시 한 번만 복원하고, UI에도 그 값을 우선 반영합니다.
    /// </summary>
    private AudioSessionInfo GetEffectiveSessionSnapshot(string deviceId, AudioSessionInfo session)
    {
        if (!TryGetStoredSessionPreference(session, out var preference))
        {
            return session;
        }

        session = ProgramAudioPreferenceStore.ApplyStoredPresentation(session, preference);

        if (_restoredSessionIds.Contains(session.Id))
        {
            return session;
        }

        _restoredSessionIds.Add(session.Id);

        if (session.Volume == preference.Volume && session.IsMuted == preference.IsMuted)
        {
            return session;
        }

        AppLog.Info(
            "MainViewModel",
            $"저장된 프로그램 설정 복원 deviceId={deviceId} sessionId={session.Id} displayName={session.DisplayName} volume={preference.Volume} muted={preference.IsMuted}");

        _ = RunAudioControlAsync(
            $"프로그램 설정 복원 deviceId={deviceId} sessionId={session.Id}",
            () =>
            {
                _audioSessionService.SetSessionVolume(deviceId, session.Id, preference.Volume);
                _audioSessionService.SetSessionMuted(deviceId, session.Id, preference.IsMuted);
            });

        return ProgramAudioPreferenceStore.CreateRestoredSessionSnapshot(session, preference);
    }

    /// <summary>
    /// 저장소에서 현재 세션과 매칭되는 프로그램별 설정을 찾습니다.
    /// </summary>
    private bool TryGetStoredSessionPreference(AudioSessionInfo session, out ProgramAudioPreference preference)
    {
        return ProgramAudioPreferenceStore.TryGetStoredPreference(_programAudioPreferencesByKey, session, out preference);
    }

    /// <summary>
    /// 사용자 조작으로 바뀐 프로그램별 볼륨/음소거 값을 설정 파일에 저장합니다.
    /// </summary>
    private void PersistSessionPreference(string deviceId, string sessionId, int? volume = null, bool? muted = null)
    {
        var session = VisibleDevices
            .FirstOrDefault(visibleDevice => visibleDevice.Id == deviceId)?
            .Sessions
            .FirstOrDefault(currentSession => currentSession.Id == sessionId);
        if (session is null)
        {
            AppLog.Debug("MainViewModel", $"프로그램 설정 저장 생략: 세션 없음 deviceId={deviceId} sessionId={sessionId}");
            return;
        }

        var sessionSnapshot = new AudioSessionInfo
        {
            MatchKey = session.MatchKey,
            Id = session.Id,
            DisplayName = session.DisplayName,
            DisambiguationText = session.DisambiguationText,
            ExecutablePath = session.ExecutablePath,
            IconSourcePath = session.IconSourcePath,
            Volume = session.Volume,
            IsMuted = session.IsMuted,
            IsActive = session.IsActive
        };

        var matchKey = ProgramAudioPreferenceStore.ResolveMatchKey(sessionSnapshot);
        if (matchKey is null)
        {
            AppLog.Debug("MainViewModel", $"프로그램 설정 저장 생략: 매칭 키 없음 deviceId={deviceId} sessionId={sessionId}");
            return;
        }

        _programAudioPreferencesByKey.TryGetValue(matchKey, out var preference);
        preference = ProgramAudioPreferenceStore.CreateOrUpdatePreference(preference, sessionSnapshot, volume, muted);
        _programAudioPreferencesByKey[matchKey] = preference;
        QueueProgramPreferenceSave();
    }

    /// <summary>
    /// 새로 읽은 세션 스냅샷 목록을 세션 ID 기준으로 하나로 합칩니다.
    /// </summary>
    private static Dictionary<string, AudioSessionInfo> BuildSessionSnapshotMap(IReadOnlyList<AudioSessionInfo> sessionSnapshots)
    {
        var snapshotsById = new Dictionary<string, AudioSessionInfo>();
        foreach (var sessionSnapshot in sessionSnapshots)
        {
            snapshotsById[sessionSnapshot.Id] = sessionSnapshot;
        }

        return snapshotsById;
    }

    /// <summary>
    /// 기존 UI 세션 목록에도 중복 ID가 남아 있을 수 있어, 마지막 항목만 남기고 정리합니다.
    /// </summary>
    private static Dictionary<string, AudioSessionViewModel> BuildExistingSessionMap(VisibleDeviceViewModel device)
    {
        var existingById = new Dictionary<string, AudioSessionViewModel>();
        for (var index = device.Sessions.Count - 1; index >= 0; index--)
        {
            var session = device.Sessions[index];
            if (existingById.TryAdd(session.Id, session))
            {
                continue;
            }

            device.Sessions.RemoveAt(index);
        }

        return existingById;
    }

    /// <summary>
    /// 서비스 모델을 화면용 세션 뷰모델로 변환합니다.
    /// </summary>
    private AudioSessionViewModel CreateSessionViewModel(string deviceId, AudioSessionInfo session)
    {
        // cachedIcon은 캐시에 이미 있는 경우 초기 렌더링에 바로 사용할 수 있는 값입니다.
        var cachedIcon = _appIconService.TryGetCachedIcon(session.IconSourcePath);

        var viewModel = new AudioSessionViewModel(
            deviceId,
            session.Id,
            session.MatchKey,
            session.DisplayName,
            session.DisambiguationText,
            session.ExecutablePath,
            session.IconSourcePath,
            cachedIcon,
            session.Volume,
            session.IsMuted,
            session.IsActive,
            OnSessionVolumeChanged,
            OnSessionMutedChanged);

        // 캐시에 없을 때만 백그라운드 아이콘 적재를 시작합니다.
        if (cachedIcon is null)
        {
            _ = LoadSessionIconAsync(viewModel);
        }

        return viewModel;
    }

    /// <summary>
    /// 이름이 같은 세션이 여러 개면 보조 라벨을 붙여 UI에서 구분 가능하게 만듭니다.
    /// </summary>
    private static IReadOnlyList<AudioSessionInfo> DisambiguateSessionDisplayNames(IReadOnlyList<AudioSessionInfo> sessions)
    {
        var duplicateGroups = sessions
            .GroupBy(session => session.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();
        if (duplicateGroups.Count == 0)
        {
            return sessions;
        }

        var disambiguationById = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var duplicateGroup in duplicateGroups)
        {
            var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in duplicateGroup)
            {
                var label = BuildSessionDisambiguationText(session, usedLabels);
                usedLabels.Add(label);
                disambiguationById[session.Id] = label;
            }
        }

        return sessions
            .Select(session => disambiguationById.TryGetValue(session.Id, out var label)
                ? new AudioSessionInfo
                {
                    MatchKey = session.MatchKey,
                    Id = session.Id,
                    DisplayName = session.DisplayName,
                    DisambiguationText = label,
                    ExecutablePath = session.ExecutablePath,
                    IconSourcePath = session.IconSourcePath,
                    Volume = session.Volume,
                    IsMuted = session.IsMuted,
                    IsActive = session.IsActive
                }
                : session)
            .ToList();
    }

    /// <summary>
    /// 설정에 따라 현재 재생 중이 아닌 세션을 숨깁니다.
    /// </summary>
    private static IReadOnlyList<AudioSessionInfo> FilterSessionsByPlaybackState(
        IReadOnlyList<AudioSessionInfo> sessions,
        bool showOnlyActiveSessions)
    {
        if (!showOnlyActiveSessions)
        {
            return sessions;
        }

        return sessions
            .Where(session => session.IsActive)
            .ToList();
    }

    /// <summary>
    /// 목록에 유지하는 Inactive 세션에는 현재 재생 중이 아님을 보조 문구로 붙입니다.
    /// </summary>
    private static IReadOnlyList<AudioSessionInfo> AnnotateSessionPlaybackState(IReadOnlyList<AudioSessionInfo> sessions)
    {
        return sessions
            .Select(session =>
            {
                if (session.IsActive)
                {
                    return session;
                }

                var statusLabel = BuildPlaybackStateLabel(session.DisambiguationText);
                return new AudioSessionInfo
                {
                    MatchKey = session.MatchKey,
                    Id = session.Id,
                    DisplayName = session.DisplayName,
                    DisambiguationText = statusLabel,
                    ExecutablePath = session.ExecutablePath,
                    IconSourcePath = session.IconSourcePath,
                    Volume = session.Volume,
                    IsMuted = session.IsMuted,
                    IsActive = false
                };
            })
            .ToList();
    }

    private static string BuildPlaybackStateLabel(string? currentLabel)
    {
        const string inactiveLabel = "현재 재생 중 아님";

        if (string.IsNullOrWhiteSpace(currentLabel))
        {
            return inactiveLabel;
        }

        return currentLabel.Contains(inactiveLabel, StringComparison.Ordinal)
            ? currentLabel
            : $"{currentLabel} · {inactiveLabel}";
    }

    /// <summary>
    /// 저장된 사용자 지정 이름이 있으면 세션 표시 이름에 먼저 반영합니다.
    /// </summary>
    private static IReadOnlyList<AudioSessionInfo> ApplyStoredSessionPresentations(
        IReadOnlyList<AudioSessionInfo> sessions,
        IReadOnlyDictionary<string, ProgramAudioPreference> preferencesByKey)
    {
        return sessions
            .Select(session => ProgramAudioPreferenceStore.TryGetStoredPreference(preferencesByKey, session, out var preference)
                ? ProgramAudioPreferenceStore.ApplyStoredPresentation(session, preference)
                : session)
            .ToList();
    }

    /// <summary>
    /// 중복 세션 하나에 대한 사람이 읽을 수 있는 보조 식별자를 만듭니다.
    /// </summary>
    private static string BuildSessionDisambiguationText(AudioSessionInfo session, ISet<string> usedLabels)
    {
        var candidates = new[]
        {
            BuildExecutablePathHint(session.ExecutablePath),
            BuildSessionPathHint(TryExtractSessionPathToken(session.Id)),
            BuildShortSessionHint(session.Id)
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (!usedLabels.Contains(candidate))
            {
                return candidate;
            }
        }

        var fallback = BuildShortSessionHint(session.Id);
        var suffix = 2;
        while (usedLabels.Contains($"{fallback} #{suffix}"))
        {
            suffix++;
        }

        return $"{fallback} #{suffix}";
    }

    /// <summary>
    /// 세션 ID에서 안정적으로 보이는 실행 경로 조각만 추출합니다.
    /// </summary>
    private static string? TryExtractSessionPathToken(string sessionId)
    {
        var separatorIndex = sessionId.IndexOf('|');
        if (separatorIndex < 0 || separatorIndex == sessionId.Length - 1)
        {
            return null;
        }

        var tail = sessionId[(separatorIndex + 1)..];
        var exeIndex = tail.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex < 0)
        {
            return null;
        }

        var pathToken = tail[..(exeIndex + 4)].Trim();
        return pathToken.Contains('\\', StringComparison.Ordinal) ? pathToken : null;
    }

    /// <summary>
    /// 경로 전체 대신 마지막 2개 세그먼트만 보여 줘 같은 이름 항목을 구분합니다.
    /// </summary>
    private static string? BuildReadablePathTail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var segments = path
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        if (segments.Length == 1)
        {
            return segments[0];
        }

        return $"{segments[^2]}\\{segments[^1]}";
    }

    /// <summary>
    /// 실행 파일 경로를 사용자에게 보여줄 짧은 설명으로 바꿉니다.
    /// </summary>
    private static string? BuildExecutablePathHint(string? path)
    {
        var tail = BuildReadablePathTail(path);
        return string.IsNullOrWhiteSpace(tail) ? null : $"실행 파일: {tail}";
    }

    /// <summary>
    /// 세션 ID 안에 포함된 경로 조각을 보조 라벨로 바꿉니다.
    /// </summary>
    private static string? BuildSessionPathHint(string? path)
    {
        var tail = BuildReadablePathTail(path);
        return string.IsNullOrWhiteSpace(tail) ? null : $"세션 경로: {tail}";
    }

    /// <summary>
    /// 경로를 못 얻은 세션을 UI에서만 구분하기 위한 짧은 힌트입니다.
    /// </summary>
    private static string BuildShortSessionHint(string sessionId)
    {
        var markerIndex = sessionId.IndexOf("%b{", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var guidStart = markerIndex + 3;
            var guidLength = Math.Min(8, sessionId.Length - guidStart);
            if (guidLength > 0)
            {
                return $"세션 ID: {sessionId.Substring(guidStart, guidLength)}";
            }
        }

        var trimmedSessionId = sessionId.Trim();
        return trimmedSessionId.Length <= 8
            ? $"세션 ID: {trimmedSessionId}"
            : $"세션 ID: {trimmedSessionId[^8..]}";
    }

    /// <summary>
    /// 세션 아이콘을 백그라운드에서 읽고 완료되면 현재 세션 상태와 경로가 맞을 때만 UI에 반영합니다.
    /// </summary>
    private async Task LoadSessionIconAsync(AudioSessionViewModel session)
    {
        // executablePath는 비동기 작업 시작 시점의 경로 스냅샷입니다.
        var iconSourcePath = session.IconSourcePath;
        if (string.IsNullOrWhiteSpace(iconSourcePath))
        {
            return;
        }

        // iconImage는 캐시 미스일 때만 실제 아이콘 추출을 수행한 결과입니다.
        var iconImage = await _appIconService.GetIconAsync(iconSourcePath);
        if (iconImage is null)
        {
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            session.TryApplyLoadedIcon(iconSourcePath, iconImage);
        });
    }

    /// <summary>
    /// Core Audio 제어 호출은 UI 스레드를 막지 않도록 백그라운드에서 실행합니다.
    /// </summary>
    private static Task RunAudioControlAsync(string operation, Action action)
    {
        return Task.Run(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                AppLog.Error("MainViewModel", $"{operation} 실패", exception);
                throw;
            }
        });
    }

    /// <summary>
    /// 슬라이더 드래그 중 연속 저장을 줄이기 위해 마지막 변경만 저장하도록 타이머를 다시 예약합니다.
    /// </summary>
    private void QueueProgramPreferenceSave()
    {
        _hasPendingProgramPreferenceSave = true;
        _programPreferenceSaveDebounceTimer.Stop();
        _programPreferenceSaveDebounceTimer.Start();
    }

    /// <summary>
    /// debounce 시간이 지나면 마지막 프로그램 설정 상태만 한 번 파일에 저장합니다.
    /// </summary>
    private void ProgramPreferenceSaveDebounceTimer_OnTick(object? sender, EventArgs e)
    {
        _programPreferenceSaveDebounceTimer.Stop();
        SaveProgramPreferencesNow();
    }

    /// <summary>
    /// 종료 직전이나 설정창 진입 전에는 대기 중인 프로그램 설정 저장을 즉시 반영합니다.
    /// </summary>
    public void FlushPendingProgramPreferenceSave()
    {
        if (!_hasPendingProgramPreferenceSave)
        {
            return;
        }

        _programPreferenceSaveDebounceTimer.Stop();
        SaveProgramPreferencesNow();
    }

    /// <summary>
    /// 메모리에 모아둔 프로그램별 설정을 현재 settings.json에 한 번만 기록합니다.
    /// </summary>
    private void SaveProgramPreferencesNow()
    {
        if (!_hasPendingProgramPreferenceSave)
        {
            return;
        }

        try
        {
            var settings = CloneSettings(_settingsService.Load());
            settings.ProgramAudioPreferences = ProgramAudioPreferenceStore.BuildPersistedPreferences(_programAudioPreferencesByKey).ToList();
            _settingsService.Save(settings);
            _cachedSettings = CloneSettings(settings);
            _hasPendingProgramPreferenceSave = false;
            AppLog.Info(
                "MainViewModel",
                $"프로그램 설정 저장 완료 count={settings.ProgramAudioPreferences.Count}");
        }
        catch (Exception exception)
        {
            AppLog.Warn("MainViewModel", "프로그램 설정 저장 실패", exception);
        }
    }

    /// <summary>
    /// 장치 마스터 볼륨 변경을 서비스 계층으로 전달합니다.
    /// </summary>
    private void OnDeviceVolumeChanged(string deviceId, int volume)
    {
        AppLog.Debug("MainViewModel", $"장치 볼륨 서비스 전달 deviceId={deviceId} volume={volume}");
        _ = RunAudioControlAsync(
            $"장치 볼륨 제어 deviceId={deviceId} volume={volume}",
            () => _audioDeviceCatalogService.SetVolume(deviceId, volume));
    }

    /// <summary>
    /// 장치 음소거 변경을 서비스 계층으로 전달합니다.
    /// </summary>
    private void OnDeviceMutedChanged(string deviceId, bool muted)
    {
        AppLog.Info("MainViewModel", $"장치 음소거 서비스 전달 deviceId={deviceId} muted={muted}");
        _ = RunAudioControlAsync(
            $"장치 음소거 제어 deviceId={deviceId} muted={muted}",
            () => _audioDeviceCatalogService.SetMuted(deviceId, muted));
    }

    /// <summary>
    /// 기본 출력 장치 변경을 서비스 계층으로 전달합니다.
    /// </summary>
    private void OnSetDefaultDevice(string deviceId)
    {
        AppLog.Info("MainViewModel", $"기본 장치 서비스 전달 deviceId={deviceId}");
        _audioDeviceCatalogService.SetAsDefault(deviceId);
    }

    /// <summary>
    /// 세션 출력 변경 메뉴에 표시할 출력 장치 목록을 최신 상태로 가져옵니다.
    /// </summary>
    public async Task<IReadOnlyList<AudioDeviceInfo>> GetAvailableOutputDevicesForSessionRoutingAsync()
    {
        var devices = await Task.Run(_audioDeviceCatalogService.GetAvailableOutputDevices);
        return devices
            .Where(device => device.IsConnected)
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 지정한 프로그램 세션의 앱별 출력 장치 정책을 변경합니다.
    /// </summary>
    public Task SetSessionOutputDeviceAsync(string deviceId, string sessionId, string targetDeviceId)
    {
        AppLog.Info(
            "MainViewModel",
            $"세션 출력 장치 변경 서비스 전달 deviceId={deviceId} sessionId={sessionId} targetDeviceId={targetDeviceId}");

        return Task.Run(() => _audioSessionService.SetSessionOutputDevice(deviceId, sessionId, targetDeviceId));
    }

    /// <summary>
    /// 프로필의 앱별 출력 규칙을 실행 파일 이름 기준으로 적용하고, 바꾼 프로세스 수를 반환합니다.
    /// </summary>
    public Task<int> SetProcessOutputDeviceAsync(string processName, string targetDeviceId)
    {
        return Task.Run(() => _audioSessionService.SetProcessOutputDevice(processName, targetDeviceId));
    }

    /// <summary>
    /// 프로필이 바뀌어 저장값이 달라졌을 때, 이미 맞춘 세션도 다시 맞추도록 기록을 지웁니다.
    /// </summary>
    public void ResetStoredPreferenceEnforcement()
    {
        lock (_enforcedSessionKeys)
        {
            _enforcedSessionKeys.Clear();
        }

        _restoredSessionIds.Clear();
    }

    /// <summary>
    /// 프로그램별 볼륨 패널을 열지 않아도, 연결된 모든 출력 장치의 세션에 저장된 볼륨·음소거를 적용합니다.
    /// 기본 장치를 바꾸면 앱이 새 장치에 새 세션을 만들기 때문에, 패널을 열 때까지 저장값이 늦게 들어가던 문제를 막습니다.
    /// 세션마다 한 번만 맞추므로 그 뒤에 사용자가 Windows 믹서 등에서 바꾼 값은 덮어쓰지 않습니다.
    /// </summary>
    public async Task<int> EnforceStoredProgramPreferencesAsync(string reason)
    {
        var preferencesByKey = _programAudioPreferencesByKey.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        if (preferencesByKey.Count == 0)
        {
            return 0;
        }

        var includeSystemSounds = _showSystemSounds;
        var devices = await GetAvailableOutputDevicesForSessionRoutingAsync();
        return await Task.Run(() =>
        {
            var appliedCount = 0;
            foreach (var device in devices)
            {
                IReadOnlyList<AudioSessionInfo> sessions;
                try
                {
                    sessions = _audioSessionService.GetSessions(device.Id, includeSystemSounds);
                }
                catch (Exception exception)
                {
                    AppLog.Warn("MainViewModel", $"저장값 적용용 세션 조회 실패 deviceId={device.Id}", exception);
                    continue;
                }

                foreach (var session in sessions)
                {
                    if (!ProgramAudioPreferenceStore.TryGetStoredPreference(preferencesByKey, session, out var preference))
                    {
                        continue;
                    }

                    lock (_enforcedSessionKeys)
                    {
                        if (!_enforcedSessionKeys.Add($"{device.Id}|{session.Id}"))
                        {
                            continue;
                        }
                    }

                    if (session.Volume == preference.Volume && session.IsMuted == preference.IsMuted)
                    {
                        continue;
                    }

                    try
                    {
                        _audioSessionService.SetSessionVolume(device.Id, session.Id, preference.Volume);
                        _audioSessionService.SetSessionMuted(device.Id, session.Id, preference.IsMuted);
                        appliedCount++;
                        AppLog.Info(
                            "MainViewModel",
                            $"저장된 프로그램 설정 즉시 적용 reason={reason} deviceId={device.Id} sessionId={session.Id} displayName={session.DisplayName} volume={preference.Volume} muted={preference.IsMuted}");
                    }
                    catch (Exception exception)
                    {
                        AppLog.Warn("MainViewModel", $"저장된 프로그램 설정 즉시 적용 실패 deviceId={device.Id} sessionId={session.Id}", exception);
                    }
                }
            }

            return appliedCount;
        });
    }

    /// <summary>
    /// 메인 화면에 표시하지 않는 장치도 프로필에 따라 기본 출력 장치로 바꿀 수 있게 서비스로 바로 전달합니다.
    /// </summary>
    public void SetDefaultOutputDevice(string deviceId)
    {
        OnSetDefaultDevice(deviceId);
    }

    /// <summary>
    /// 메인 화면에 표시하지 않는 장치도 명령·프로필로 음소거를 바꿀 수 있게 서비스로 바로 전달합니다.
    /// </summary>
    public Task SetOutputDeviceMutedAsync(string deviceId, bool muted)
    {
        return Task.Run(() => _audioDeviceCatalogService.SetMuted(deviceId, muted));
    }

    /// <summary>
    /// 프로필에 저장한 장치 볼륨을 적용합니다.
    /// </summary>
    public Task SetOutputDeviceVolumeAsync(string deviceId, int volume)
    {
        return Task.Run(() => _audioDeviceCatalogService.SetVolume(deviceId, Math.Clamp(volume, 0, 100)));
    }

    /// <summary>
    /// 입력(마이크) 장치 목록을 가져옵니다.
    /// </summary>
    public Task<IReadOnlyList<AudioDeviceInfo>> GetAvailableInputDevicesAsync()
    {
        return Task.Run(_audioDeviceCatalogService.GetAvailableInputDevices);
    }

    /// <summary>
    /// 기본 입력·기본 통신 입력 장치를 바꿉니다. 출력과 같은 PolicyConfig 경로를 씁니다.
    /// </summary>
    public void SetDefaultInputDevice(string deviceId)
    {
        AppLog.Info("MainViewModel", $"기본 입력 장치 서비스 전달 deviceId={deviceId}");
        _audioDeviceCatalogService.SetAsDefault(deviceId);
    }

    /// <summary>
    /// 세션 볼륨 변경을 서비스 계층으로 전달합니다.
    /// </summary>
    private void OnSessionVolumeChanged(string deviceId, string sessionId, int volume)
    {
        PersistSessionPreference(deviceId, sessionId, volume: volume);
        AppLog.Debug("MainViewModel", $"세션 볼륨 서비스 전달 deviceId={deviceId} sessionId={sessionId} volume={volume}");
        _ = RunAudioControlAsync(
            $"세션 볼륨 제어 deviceId={deviceId} sessionId={sessionId} volume={volume}",
            () => _audioSessionService.SetSessionVolume(deviceId, sessionId, volume));
    }

    /// <summary>
    /// 세션 음소거 변경을 서비스 계층으로 전달합니다.
    /// </summary>
    private void OnSessionMutedChanged(string deviceId, string sessionId, bool muted)
    {
        PersistSessionPreference(deviceId, sessionId, muted: muted);
        AppLog.Info("MainViewModel", $"세션 음소거 서비스 전달 deviceId={deviceId} sessionId={sessionId} muted={muted}");
        _ = RunAudioControlAsync(
            $"세션 음소거 제어 deviceId={deviceId} sessionId={sessionId} muted={muted}",
            () => _audioSessionService.SetSessionMuted(deviceId, sessionId, muted));
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        return new AppSettings
        {
            VisibleDeviceIds = [.. settings.VisibleDeviceIds],
            StartMinimized = settings.StartMinimized,
            RunAtWindowsStartup = settings.RunAtWindowsStartup,
            MinimizeToTray = settings.MinimizeToTray,
            ShowOnlyConnectedDevices = settings.ShowOnlyConnectedDevices,
            ShowSystemSounds = settings.ShowSystemSounds,
            ShowOnlyActiveSessions = settings.ShowOnlyActiveSessions,
            IncludePreReleaseUpdates = settings.IncludePreReleaseUpdates,
            EnableDebugLogs = settings.EnableDebugLogs,
            ProgramAudioPreferences = settings.ProgramAudioPreferences
                .Select(preference => new ProgramAudioPreference
                {
                    MatchKey = preference.MatchKey,
                    ExecutablePath = preference.ExecutablePath,
                    DisplayName = preference.DisplayName,
                    CustomDisplayName = preference.CustomDisplayName,
                    Volume = preference.Volume,
                    IsMuted = preference.IsMuted
                })
                .ToList(),
            LastAppliedAudioProfileId = settings.LastAppliedAudioProfileId,
            AudioProfiles = AudioProfileStore.CloneProfiles(settings.AudioProfiles),
            AutoProfileSwitchEnabled = settings.AutoProfileSwitchEnabled,
            AutoProfileRules = AudioProfileStore.CloneAutoProfileRules(settings.AutoProfileRules),
            AutoProfileFallbackProfileId = settings.AutoProfileFallbackProfileId,
            ShowAutoProfileNotifications = settings.ShowAutoProfileNotifications,
            EnableProfileHotkeys = settings.EnableProfileHotkeys
        };
    }

    /// <summary>
    /// 현재 세션에 대한 사용자 지정 표시 이름을 저장하고 즉시 파일에 반영합니다.
    /// </summary>
    public bool SetCustomSessionDisplayName(string deviceId, string sessionId, string? customDisplayName)
    {
        var session = VisibleDevices
            .FirstOrDefault(visibleDevice => visibleDevice.Id == deviceId)?
            .Sessions
            .FirstOrDefault(currentSession => currentSession.Id == sessionId);
        if (session is null)
        {
            return false;
        }

        var matchKey = session.MatchKey;
        if (string.IsNullOrWhiteSpace(matchKey))
        {
            matchKey = ProgramAudioPreferenceStore.CreateMatchKey(session.Id, session.ExecutablePath, session.DisplayName);
        }

        if (string.IsNullOrWhiteSpace(matchKey))
        {
            return false;
        }

        if (!_programAudioPreferencesByKey.TryGetValue(matchKey, out var preference))
        {
            preference = new ProgramAudioPreference
            {
                MatchKey = matchKey,
                ExecutablePath = session.ExecutablePath,
                DisplayName = session.DisplayName,
                Volume = session.Volume,
                IsMuted = session.IsMuted
            };
        }

        var normalizedCustomDisplayName = NormalizeCustomDisplayName(customDisplayName, preference.DisplayName);
        preference.ExecutablePath = session.ExecutablePath;
        preference.CustomDisplayName = normalizedCustomDisplayName;
        _programAudioPreferencesByKey[matchKey] = preference;

        QueueProgramPreferenceSave();
        FlushPendingProgramPreferenceSave();
        return true;
    }

    private static string? NormalizeCustomDisplayName(string? customDisplayName, string baseDisplayName)
    {
        var trimmed = customDisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return string.Equals(trimmed, baseDisplayName, StringComparison.Ordinal)
            ? null
            : trimmed;
    }
}
