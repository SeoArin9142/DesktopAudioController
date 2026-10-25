using System.Diagnostics;

namespace DesktopAudioController.Services;

/// <summary>
/// 실행 중인 프로세스 이름 목록을 짧은 주기로 읽어 알려 줍니다.
/// WMI 프로세스 시작 이벤트는 관리자 권한이 필요하거나(Win32_ProcessStartTrace) 내부적으로 폴링하므로(__InstanceCreationEvent),
/// 이름만 읽는 가벼운 폴링을 씁니다. 한 번 읽는 데 보통 수 ms 이고, 자동 전환을 켠 동안에만 돕니다.
/// </summary>
public sealed class ProcessWatchService : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    private readonly System.Threading.Timer _timer;
    private readonly TimeSpan _interval;
    private int _isPolling;
    private bool _isRunning;
    private bool _isDisposed;

    public ProcessWatchService(TimeSpan? interval = null)
    {
        _interval = interval ?? DefaultInterval;
        _timer = new System.Threading.Timer(OnTimer);
    }

    /// <summary>
    /// 백그라운드 스레드에서 실행 중 프로세스 이름(".exe" 없음, 대소문자 무시) 집합을 전달합니다.
    /// </summary>
    public event Action<IReadOnlySet<string>>? SnapshotTaken;

    public bool IsRunning => _isRunning;

    public void Start()
    {
        if (_isDisposed || _isRunning)
        {
            return;
        }

        _isRunning = true;
        _timer.Change(TimeSpan.Zero, _interval);
        AppLog.Info("ProcessWatchService", $"프로세스 감시 시작 intervalMs={(int)_interval.TotalMilliseconds}");
    }

    public void Stop()
    {
        if (_isDisposed || !_isRunning)
        {
            return;
        }

        _isRunning = false;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        AppLog.Info("ProcessWatchService", "프로세스 감시 중지");
    }

    public static IReadOnlySet<string> ReadRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    names.Add(process.ProcessName);
                }
                catch
                {
                    // 읽는 사이 종료된 프로세스는 건너뜁니다.
                }
            }
        }

        return names;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isRunning = false;
        _timer.Dispose();
    }

    private void OnTimer(object? state)
    {
        // 이전 폴링이 아직 처리 중이면 이번 틱은 건너뜁니다.
        if (Interlocked.Exchange(ref _isPolling, 1) == 1)
        {
            return;
        }

        try
        {
            if (!_isRunning)
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var names = ReadRunningProcessNames();
            AppLog.Debug("ProcessWatchService", $"프로세스 목록 읽기 count={names.Count} elapsedMs={stopwatch.ElapsedMilliseconds}");
            SnapshotTaken?.Invoke(names);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ProcessWatchService", "프로세스 목록 읽기 실패", exception);
        }
        finally
        {
            Interlocked.Exchange(ref _isPolling, 0);
        }
    }
}
