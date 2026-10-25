using System.Collections.ObjectModel;
using DesktopAudioController.Infrastructure;
using DesktopAudioController.Services;
using System.Windows.Threading;

namespace DesktopAudioController.ViewModels;

/// <summary>
/// 메인 화면에 노출되는 출력 장치 카드 한 개를 표현하는 뷰모델입니다.
/// </summary>
public sealed class VisibleDeviceViewModel : ObservableObject
{
    // 외부 상태 동기화 중 서비스 재호출을 막기 위한 플래그입니다.
    private bool _suppressCallbacks;

    // 화면에 표시할 장치 이름입니다.
    private string _name;

    // 현재 기본 출력 장치 여부입니다.
    private bool _isDefault;

    // 현재 장치 연결 여부입니다.
    private bool _isConnected;

    // 슬라이더와 바인딩되는 현재 볼륨 값입니다.
    private int _volume;

    // 슬라이더 드래그 중 과도한 네이티브 호출을 줄이기 위한 지연 커밋 타이머입니다.
    private readonly DispatcherTimer _volumeCommitTimer;

    // 아직 서비스에 반영되지 않은 볼륨 변경이 남아 있는지 여부입니다.
    private bool _hasPendingVolumeCommit;

    // 음소거 체크 상태와 바인딩되는 현재 값입니다.
    private bool _isMuted;

    // 장치 카드의 세션 목록 펼침 상태입니다.
    private bool _isExpanded;

    // 장치 볼륨 변경을 실제 오디오 서비스로 전달하는 콜백입니다.
    private readonly Action<string, int> _onVolumeChanged;

    // 장치 음소거 변경을 실제 오디오 서비스로 전달하는 콜백입니다.
    private readonly Action<string, bool> _onMutedChanged;

    // 장치를 기본 장치로 바꿀 때 호출하는 콜백입니다.
    private readonly Action<string> _onSetAsDefault;

    /// <summary>
    /// 뷰모델 생성 시 초기 장치 상태와 변경 콜백을 함께 받습니다.
    /// </summary>
    public VisibleDeviceViewModel(
        string id,
        string name,
        bool isDefault,
        bool isConnected,
        int initialVolume,
        bool initialMuted,
        Action<string, int> onVolumeChanged,
        Action<string, bool> onMutedChanged,
        Action<string> onSetAsDefault)
    {
        Id = id;
        _name = name;
        _isDefault = isDefault;
        _isConnected = isConnected;
        _volume = initialVolume;
        _isMuted = initialMuted;
        _isExpanded = false;
        _onVolumeChanged = onVolumeChanged;
        _onMutedChanged = onMutedChanged;
        _onSetAsDefault = onSetAsDefault;
        _volumeCommitTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _volumeCommitTimer.Tick += VolumeCommitTimer_OnTick;
    }

    /// <summary>
    /// 장치 고유 ID입니다.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 메인 화면에 표시할 장치 이름입니다.
    /// </summary>
    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// 기본 출력 장치 여부입니다.
    /// </summary>
    public bool IsDefault
    {
        get => _isDefault;
        private set => SetProperty(ref _isDefault, value);
    }

    /// <summary>
    /// 현재 장치 연결 여부입니다.
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    /// <summary>
    /// 현재 마스터 볼륨 값입니다.
    /// </summary>
    public int Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, value))
            {
                return;
            }

            if (_suppressCallbacks)
            {
                return;
            }

            _hasPendingVolumeCommit = true;
            _volumeCommitTimer.Stop();
            _volumeCommitTimer.Start();
        }
    }

    /// <summary>
    /// 현재 음소거 상태입니다.
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (!SetProperty(ref _isMuted, value))
            {
                return;
            }

            if (_suppressCallbacks)
            {
                return;
            }

            try
            {
                AppLog.Info("VisibleDeviceViewModel", $"장치 음소거 변경 요청 deviceId={Id} muted={value}");
                _onMutedChanged(Id, value);
            }
            catch (Exception exception)
            {
                // 장치가 갑자기 사라진 경우 다음 새로고침까지 현재 화면 값을 유지합니다.
                AppLog.Warn("VisibleDeviceViewModel", $"장치 음소거 변경 실패 deviceId={Id} muted={value}", exception);
            }
        }
    }

    /// <summary>
    /// 장치 카드 아래 세션 목록 표시 여부입니다.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// 이 장치에서 현재 소리를 내고 있는 애플리케이션 세션 목록입니다.
    /// </summary>
    public ObservableCollection<AudioSessionViewModel> Sessions { get; } = [];

    /// <summary>
    /// 아직 서비스에 반영되지 않은 장치 볼륨 변경이 남아 있는지 여부입니다.
    /// </summary>
    public bool HasPendingVolumeCommit => _hasPendingVolumeCommit;

    /// <summary>
    /// 현재 장치를 기본 출력 장치로 설정합니다.
    /// </summary>
    public void SetAsDefault()
    {
        if (IsDefault)
        {
            return;
        }

        AppLog.Info("VisibleDeviceViewModel", $"기본 장치 변경 요청 deviceId={Id}");
        _onSetAsDefault(Id);
    }

    /// <summary>
    /// 서비스에서 읽어온 최신 장치 상태를 UI에만 반영하고, 서비스 재호출은 막습니다.
    /// </summary>
    public void UpdateSnapshot(string name, bool isDefault, bool isConnected, int volume, bool isMuted)
    {
        _suppressCallbacks = true;
        _hasPendingVolumeCommit = false;
        _volumeCommitTimer.Stop();
        try
        {
            Name = name;
            IsDefault = isDefault;
            IsConnected = isConnected;
            Volume = volume;
            IsMuted = isMuted;
        }
        finally
        {
            _suppressCallbacks = false;
        }
    }

    /// <summary>
    /// Windows 쪽에서 바뀐 장치 볼륨·음소거를 화면에만 반영합니다(장치로 다시 보내지 않음).
    /// 사용자가 슬라이더를 끄는 중이라 아직 보내지 않은 값이 있으면 그 값을 우선합니다.
    /// </summary>
    public void ApplyExternalVolume(int volume, bool isMuted)
    {
        ApplyExternalState(volume, isMuted, isDefault: null);
    }

    /// <summary>
    /// Windows 쪽 실제 값으로 장치 카드를 맞춥니다. isDefault 가 null 이면 기본 장치 표시는 그대로 둡니다.
    /// </summary>
    public void ApplyExternalState(int volume, bool isMuted, bool? isDefault)
    {
        if (isDefault is { } defaultState)
        {
            IsDefault = defaultState;
        }

        if (_hasPendingVolumeCommit)
        {
            return;
        }

        _suppressCallbacks = true;
        try
        {
            Volume = Math.Clamp(volume, 0, 100);
            IsMuted = isMuted;
        }
        finally
        {
            _suppressCallbacks = false;
        }
    }

    /// <summary>
    /// 사용자가 슬라이더 조작을 잠시 멈추면 마지막 값만 서비스에 반영합니다.
    /// </summary>
    private void VolumeCommitTimer_OnTick(object? sender, EventArgs e)
    {
        _volumeCommitTimer.Stop();
        if (_suppressCallbacks || !_hasPendingVolumeCommit)
        {
            return;
        }

        _hasPendingVolumeCommit = false;

        try
        {
            AppLog.Debug("VisibleDeviceViewModel", $"장치 볼륨 반영 deviceId={Id} volume={_volume}");
            _onVolumeChanged(Id, _volume);
        }
        catch (Exception exception)
        {
            AppLog.Warn("VisibleDeviceViewModel", $"장치 볼륨 반영 실패 deviceId={Id} volume={_volume}", exception);
        }
    }
}
