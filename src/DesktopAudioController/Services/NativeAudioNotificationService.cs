using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DesktopAudioController.Services;

/// <summary>
/// Windows Core Audio 이벤트를 받아 장치 및 세션 변경을 앱에 통합 이벤트로 전달하는 서비스입니다.
/// </summary>
public sealed class NativeAudioNotificationService : IAudioNotificationService
{
    private const int DisposeSyncRootTimeoutMs = 1000;

    // 장치 열거와 시스템 오디오 알림 등록에 사용하는 COM 래퍼입니다.
    private readonly MMDeviceEnumerator _enumerator = new();

    // 장치 및 세션 구독 정보를 동기화할 때 사용하는 잠금 객체입니다.
    private readonly object _syncRoot = new();

    // 현재 구독 중인 장치별 리소스 묶음입니다.
    private readonly Dictionary<string, DeviceSubscription> _deviceSubscriptions = [];

    // 장치 추가/제거/기본 장치 변경 알림을 받는 콜백 객체입니다.
    private readonly EndpointNotificationClient _endpointNotificationClient;

    // 서비스 시작 여부를 나타냅니다.
    private bool _started;

    // 종료 또는 교체 중인 인스턴스는 새 콜백 처리를 시작하지 않도록 막습니다.
    private volatile bool _disposeRequested;

    public NativeAudioNotificationService()
    {
        _endpointNotificationClient = new EndpointNotificationClient(this);
    }

    /// <summary>
    /// 오디오 토폴로지 또는 상태가 바뀌었을 때 발생하는 통합 이벤트입니다.
    /// </summary>
    public event EventHandler<AudioNotificationChangedEventArgs>? Changed;

    /// <summary>
    /// Windows 오디오 엔진 이벤트 구독을 시작합니다.
    /// </summary>
    public void Start()
    {
        lock (_syncRoot)
        {
            if (_started)
            {
                return;
            }

            _disposeRequested = false;
            _enumerator.RegisterEndpointNotificationCallback(_endpointNotificationClient);
            RebuildSubscriptionsLocked();
            _started = true;
        }
    }

    /// <summary>
    /// 장치 추가/제거/기본 장치 변경처럼 토폴로지 변화가 생겼을 때 호출됩니다.
    /// </summary>
    internal void HandleTopologyChanged()
    {
        if (_disposeRequested)
        {
            return;
        }

        try
        {
            lock (_syncRoot)
            {
                if (_disposeRequested || !_started)
                {
                    return;
                }

                RebuildSubscriptionsLocked();
            }

            if (_disposeRequested)
            {
                return;
            }

            RaiseChanged(AudioNotificationChangeKind.Topology);
        }
        catch (Exception exception)
        {
            AppLog.Error("NativeAudioNotificationService", "토폴로지 변경 처리 중 예외", exception);
        }
    }

    /// <summary>
    /// 장치 마스터 볼륨이나 세션 상태가 바뀌었을 때 호출됩니다.
    /// </summary>
    internal void HandleStateChanged()
    {
        if (_disposeRequested)
        {
            return;
        }

        try
        {
            RaiseChanged(AudioNotificationChangeKind.State);
        }
        catch (Exception exception)
        {
            AppLog.Error("NativeAudioNotificationService", "상태 변경 처리 중 예외", exception);
        }
    }

    /// <summary>
    /// 장치 마스터 볼륨이 Windows 볼륨 믹서·키보드 볼륨 키 등으로 바뀌었을 때 새 값을 함께 알립니다.
    /// </summary>
    internal void HandleDeviceVolumeChanged(string deviceId, float masterVolume, bool isMuted)
    {
        if (_disposeRequested)
        {
            return;
        }

        try
        {
            RaiseChanged(new AudioNotificationChangedEventArgs(
                deviceId,
                (int)Math.Round(masterVolume * 100),
                isMuted));
        }
        catch (Exception exception)
        {
            AppLog.Error("NativeAudioNotificationService", "장치 볼륨 변경 처리 중 예외", exception);
        }
    }

    /// <summary>
    /// 세션 생성/만료처럼 구독 집합을 다시 짜야 하는 변화는 내부 구독만 재구성하고 UI에는 상태 갱신으로 알립니다.
    /// </summary>
    internal void HandleSessionCollectionChanged()
    {
        if (_disposeRequested)
        {
            return;
        }

        try
        {
            lock (_syncRoot)
            {
                if (_disposeRequested || !_started)
                {
                    return;
                }

                RebuildSubscriptionsLocked();
            }

            if (_disposeRequested)
            {
                return;
            }

            RaiseChanged(AudioNotificationChangeKind.State);
        }
        catch (Exception exception)
        {
            AppLog.Error("NativeAudioNotificationService", "세션 컬렉션 변경 처리 중 예외", exception);
        }
    }

    /// <summary>
    /// 현재 시스템 상태에 맞춰 장치/세션 콜백 구독을 다시 구성합니다.
    /// </summary>
    private void RebuildSubscriptionsLocked()
    {
        ClearSubscriptionsLocked();

        // 연결됨/분리됨 상태 장치를 모두 다시 스캔해 최신 구독 구성을 만듭니다.
        var devices = _enumerator.EnumerateAudioEndPoints(
            DataFlow.Render,
            DeviceState.Active | DeviceState.Unplugged);

        for (int index = 0; index < devices.Count; index++)
        {
            // device는 현재 열거된 출력 장치 한 개입니다.
            var device = devices[index];
            var subscription = new DeviceSubscription(device);

            // 장치 마스터 볼륨 물리 버튼/외부 UI 변경을 잡기 위한 콜백입니다.
            // 알림에 새 볼륨·음소거 값이 들어 있으므로 장치 ID와 함께 그대로 전달합니다.
            var deviceId = device.ID;
            subscription.VolumeHandler = data => HandleDeviceVolumeChanged(deviceId, data.MasterVolume, data.Muted);
            subscription.EndpointVolume = device.AudioEndpointVolume;
            subscription.EndpointVolume.OnVolumeNotification += subscription.VolumeHandler;

            // 새 세션은 UI 상태 변화이면서 동시에 새 세션 콜백 재구독 대상이므로 내부 구독을 다시 짭니다.
            subscription.SessionCreatedHandler = (_, _) => HandleSessionCollectionChanged();
            subscription.SessionManager = device.AudioSessionManager;
            subscription.SessionManager.OnSessionCreated += subscription.SessionCreatedHandler;

            if (device.State == DeviceState.Active)
            {
                AttachSessionHandlers(subscription);
            }

            _deviceSubscriptions[device.ID] = subscription;
        }
    }

    /// <summary>
    /// 지정한 장치의 현재 활성 세션들에 이벤트 핸들러를 붙입니다.
    /// </summary>
    private void AttachSessionHandlers(DeviceSubscription subscription)
    {
        // sessions는 해당 장치의 현재 오디오 세션 컬렉션입니다.
        var sessions = subscription.SessionManager!.Sessions;

        for (int index = 0; index < sessions.Count; index++)
        {
            // session은 현재 장치에서 소리를 내는 앱 세션 한 개입니다.
            var session = sessions[index];

            try
            {
                // 종료된 세션과 시스템 사운드는 실시간 갱신 대상에서 제외합니다.
                if (session.State == AudioSessionState.AudioSessionStateExpired || session.IsSystemSoundsSession)
                {
                    session.Dispose();
                    continue;
                }

                var handler = new SessionEventsHandler(HandleStateChanged, HandleTopologyChanged);
                session.RegisterEventClient(handler);
                subscription.SessionControls.Add(session);
                subscription.SessionHandlers.Add(handler);
            }
            catch
            {
                session.Dispose();
            }
        }
    }

    /// <summary>
    /// 현재 구독 중인 장치/세션 이벤트를 모두 해제합니다.
    /// </summary>
    private void ClearSubscriptionsLocked()
    {
        foreach (var subscription in _deviceSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _deviceSubscriptions.Clear();
    }

    /// <summary>
    /// 통합 변경 이벤트를 발행합니다.
    /// </summary>
    private void RaiseChanged(AudioNotificationChangeKind kind)
    {
        RaiseChanged(new AudioNotificationChangedEventArgs(kind));
    }

    private void RaiseChanged(AudioNotificationChangedEventArgs args)
    {
        if (_disposeRequested || Changed is null)
        {
            return;
        }

        foreach (EventHandler<AudioNotificationChangedEventArgs> handler in Changed.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"변경 이벤트 전달 중 예외 kind={args.Kind}", exception);
            }
        }
    }

    /// <summary>
    /// 앱 종료 시 등록한 COM 알림과 세션 핸들러를 정리합니다.
    /// </summary>
    public void Dispose()
    {
        _disposeRequested = true;
        Changed = null;

        List<DeviceSubscription> subscriptionsToDispose = [];
        var shouldUnregister = false;
        var lockTaken = false;

        try
        {
            Monitor.TryEnter(_syncRoot, TimeSpan.FromMilliseconds(DisposeSyncRootTimeoutMs), ref lockTaken);
            if (!lockTaken)
            {
                AppLog.Warn(
                    "NativeAudioNotificationService",
                    $"Dispose 중 sync lock 획득 타임아웃 timeoutMs={DisposeSyncRootTimeoutMs}");
                return;
            }

            if (_started)
            {
                shouldUnregister = true;
                _started = false;
            }

            subscriptionsToDispose = _deviceSubscriptions.Values.ToList();
            _deviceSubscriptions.Clear();
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(_syncRoot);
            }
        }

        if (shouldUnregister)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_endpointNotificationClient);
            }
            catch (Exception exception)
            {
                AppLog.Warn("NativeAudioNotificationService", "Dispose 중 endpoint notification callback 해제 실패", exception);
            }
        }

        foreach (var subscription in subscriptionsToDispose)
        {
            try
            {
                subscription.Dispose();
            }
            catch (Exception exception)
            {
                AppLog.Warn("NativeAudioNotificationService", "Dispose 중 장치 구독 정리 실패", exception);
            }
        }

        try
        {
            _enumerator.Dispose();
        }
        catch (Exception exception)
        {
            AppLog.Warn("NativeAudioNotificationService", "Dispose 중 enumerator 정리 실패", exception);
        }
    }

    /// <summary>
    /// 장치 한 개에 매달린 이벤트 구독 리소스를 묶어 관리합니다.
    /// </summary>
    private sealed class DeviceSubscription : IDisposable
    {
        public DeviceSubscription(MMDevice device)
        {
            Device = device;
        }

        public MMDevice Device { get; }

        public AudioEndpointVolume? EndpointVolume { get; set; }

        public AudioEndpointVolumeNotificationDelegate? VolumeHandler { get; set; }

        public AudioSessionManager? SessionManager { get; set; }

        public AudioSessionManager.SessionCreatedDelegate? SessionCreatedHandler { get; set; }

        public List<AudioSessionControl> SessionControls { get; } = [];

        public List<SessionEventsHandler> SessionHandlers { get; } = [];

        public void Dispose()
        {
            if (EndpointVolume is not null && VolumeHandler is not null)
            {
                EndpointVolume.OnVolumeNotification -= VolumeHandler;
            }

            if (SessionManager is not null && SessionCreatedHandler is not null)
            {
                SessionManager.OnSessionCreated -= SessionCreatedHandler;
            }

            foreach (var session in SessionControls)
            {
                session.Dispose();
            }

            Device.Dispose();
        }
    }

    /// <summary>
    /// IMMNotificationClient 구현체로 장치 추가/제거/기본 장치 변경을 감지합니다.
    /// </summary>
    private sealed class EndpointNotificationClient : IMMNotificationClient
    {
        private readonly NativeAudioNotificationService _owner;

        public EndpointNotificationClient(NativeAudioNotificationService owner)
        {
            _owner = owner;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            try
            {
                _owner.HandleTopologyChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnDeviceStateChanged 예외 deviceId={deviceId} state={newState}", exception);
            }
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            try
            {
                _owner.HandleTopologyChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnDeviceAdded 예외 deviceId={pwstrDeviceId}", exception);
            }
        }

        public void OnDeviceRemoved(string deviceId)
        {
            try
            {
                _owner.HandleTopologyChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnDeviceRemoved 예외 deviceId={deviceId}", exception);
            }
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render)
            {
                try
                {
                    _owner.HandleTopologyChanged();
                }
                catch (Exception exception)
                {
                    AppLog.Error("NativeAudioNotificationService", $"OnDefaultDeviceChanged 예외 role={role} deviceId={defaultDeviceId}", exception);
                }
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            try
            {
                _owner.HandleTopologyChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnPropertyValueChanged 예외 deviceId={pwstrDeviceId}", exception);
            }
        }
    }

    /// <summary>
    /// 세션별 상태 변경 이벤트를 받아 통합 변경 이벤트로 전달합니다.
    /// </summary>
    private sealed class SessionEventsHandler : IAudioSessionEventsHandler
    {
        private readonly Action _onStateChanged;
        private readonly Action _onTopologyChanged;

        public SessionEventsHandler(Action onStateChanged, Action onTopologyChanged)
        {
            _onStateChanged = onStateChanged;
            _onTopologyChanged = onTopologyChanged;
        }

        public void OnVolumeChanged(float volume, bool isMuted)
        {
            try
            {
                _onStateChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnVolumeChanged 예외 volume={volume} muted={isMuted}", exception);
            }
        }

        public void OnDisplayNameChanged(string displayName)
        {
            try
            {
                _onStateChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnDisplayNameChanged 예외 displayName={displayName}", exception);
            }
        }

        public void OnIconPathChanged(string iconPath)
        {
            try
            {
                _onStateChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnIconPathChanged 예외 iconPath={iconPath}", exception);
            }
        }

        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex)
        {
            try
            {
                _onStateChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnChannelVolumeChanged 예외 channelCount={channelCount} channelIndex={channelIndex}", exception);
            }
        }

        public void OnGroupingParamChanged(ref Guid groupingId)
        {
            try
            {
                _onStateChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnGroupingParamChanged 예외 groupingId={groupingId}", exception);
            }
        }

        public void OnStateChanged(AudioSessionState state)
        {
            try
            {
                _onStateChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnStateChanged 예외 state={state}", exception);
            }
        }

        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
        {
            try
            {
                _onTopologyChanged();
            }
            catch (Exception exception)
            {
                AppLog.Error("NativeAudioNotificationService", $"OnSessionDisconnected 예외 reason={disconnectReason}", exception);
            }
        }
    }
}
