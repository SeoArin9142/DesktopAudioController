using System.Diagnostics;
using System.IO;
using DesktopAudioController.Models;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// 장치 열거는 별도 워커 프로세스로 분리하고, 장치 제어는 현재 프로세스에서 수행합니다.
/// 부팅 직후 Core Audio 열거가 멈추거나 오래 걸려도 메인 앱 UI가 같이 멈추지 않게 하기 위한 경계입니다.
/// </summary>
public sealed class WorkerBackedAudioDeviceCatalogService : IAudioDeviceCatalogService, IDisposable
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private readonly object _probeSyncRoot = new();
    private readonly string _executablePath;
    private readonly AudioDeviceStartupSnapshotService _snapshotService;
    private readonly NativeAudioDeviceCatalogService _controlService;
    private Task<IReadOnlyList<AudioDeviceInfo>>? _currentProbeTask;
    private IReadOnlyList<AudioDeviceInfo> _lastKnownDevices = [];
    private bool _hasLastKnownSnapshot;

    public WorkerBackedAudioDeviceCatalogService(
        string executablePath,
        AudioDeviceStartupSnapshotService snapshotService,
        NativeAudioDeviceCatalogService? controlService = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = executablePath;
        _snapshotService = snapshotService;
        _controlService = controlService ?? new NativeAudioDeviceCatalogService();

        // 첫 probe가 실패하거나 진행 중일 때도 화면을 비워 두지 않도록 마지막 성공 스냅샷을 메모리에 올려 둡니다.
        if (_snapshotService.TryLoad(out var snapshot))
        {
            _lastKnownDevices = snapshot.Devices
                .Select(CloneDevice)
                .ToList();
            _hasLastKnownSnapshot = true;
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetAvailableOutputDevices()
    {
        var stopwatch = Stopwatch.StartNew();
        Task<IReadOnlyList<AudioDeviceInfo>> probeTask;
        var createdProbe = false;

        // 동시에 여러 새로고침이 들어와도 실제 Core Audio 장치 열거 워커는 하나만 띄웁니다.
        lock (_probeSyncRoot)
        {
            if (_currentProbeTask is null || _currentProbeTask.IsCompleted)
            {
                _currentProbeTask = ProbeDevicesAsync();
                createdProbe = true;
            }

            probeTask = _currentProbeTask!;
        }

        if (!createdProbe && _hasLastKnownSnapshot)
        {
            // 이미 probe가 돌고 있으면 기다리지 않고 캐시를 반환해 UI 응답성을 우선합니다.
            AppLog.Warn(
                "WorkerBackedAudioDeviceCatalogService",
                $"GetAvailableOutputDevices 생략: 워커 probe 진행 중 cachedCount={_lastKnownDevices.Count} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return CloneDevices(_lastKnownDevices);
        }

        try
        {
            var devices = probeTask.GetAwaiter().GetResult();
            StoreSnapshot(devices);
            AppLog.Debug(
                "WorkerBackedAudioDeviceCatalogService",
                $"GetAvailableOutputDevices 완료 count={devices.Count} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return CloneDevices(devices);
        }
        catch (Exception exception)
        {
            if (_hasLastKnownSnapshot)
            {
                AppLog.Warn(
                    "WorkerBackedAudioDeviceCatalogService",
                    $"GetAvailableOutputDevices 실패 후 캐시 반환 count={_lastKnownDevices.Count} elapsedMs={stopwatch.ElapsedMilliseconds}",
                    exception);
                return CloneDevices(_lastKnownDevices);
            }

            AppLog.Error(
                "WorkerBackedAudioDeviceCatalogService",
                $"GetAvailableOutputDevices 실패 elapsedMs={stopwatch.ElapsedMilliseconds}",
                exception);
            throw;
        }
        finally
        {
            if (createdProbe)
            {
                lock (_probeSyncRoot)
                {
                    if (ReferenceEquals(_currentProbeTask, probeTask))
                    {
                        _currentProbeTask = null;
                    }
                }
            }
        }
    }

    public void SetVolume(string deviceId, int volume)
    {
        _controlService.SetVolume(deviceId, volume);
    }

    public void SetMuted(string deviceId, bool muted)
    {
        _controlService.SetMuted(deviceId, muted);
    }

    public IReadOnlyList<AudioDeviceInfo> GetAvailableInputDevices()
    {
        // 입력 장치 열거도 가상 장치 환경에서 멈출 수 있어 출력과 같은 워커로 격리합니다.
        // 프로필 편집·적용 때만 가끔 읽으므로 캐시 없이 매번 읽고, 실패하면 빈 목록으로 둡니다.
        try
        {
            return ProbeDevicesAsync(probeInput: true).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            AppLog.Warn("WorkerBackedAudioDeviceCatalogService", "입력 장치 워커 probe 실패, 빈 목록 반환", exception);
            return [];
        }
    }

    public void SetAsDefault(string deviceId)
    {
        _controlService.SetAsDefault(deviceId);
    }

    public void Dispose()
    {
        _controlService.Dispose();
    }

    private async Task<IReadOnlyList<AudioDeviceInfo>> ProbeDevicesAsync(bool probeInput = false)
    {
        var tempOutputPath = Path.Combine(
            Path.GetTempPath(),
            $"DesktopAudioController-audio-probe-{Guid.NewGuid():N}.json");
        try
        {
            var startInfo = new ProcessStartInfo(_executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? Environment.CurrentDirectory
            };
            // 별도 worker exe를 두지 않고 같은 exe를 --probe-audio 모드로 다시 실행합니다.
            AudioDeviceProbeCommand.Apply(startInfo, tempOutputPath, AppLog.IsDebugEnabled, probeInput);
            AppLog.Debug(
                "WorkerBackedAudioDeviceCatalogService",
                $"워커 probe 시작 executablePath={_executablePath} outputPath={tempOutputPath} timeoutMs={(int)ProbeTimeout.TotalMilliseconds}");

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(Loc.T("오디오 probe 워커 프로세스를 시작하지 못했습니다."));

            var waitForExitTask = process.WaitForExitAsync();
            var completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(ProbeTimeout));
            if (!ReferenceEquals(completedTask, waitForExitTask))
            {
                TryKill(process);
                throw new TimeoutException("The operation has timed out.");
            }

            await waitForExitTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"오디오 probe 워커가 비정상 종료했습니다. exitCode={process.ExitCode}");
            }

            if (!File.Exists(tempOutputPath))
            {
                throw new FileNotFoundException(Loc.T("오디오 probe 결과 파일이 생성되지 않았습니다."), tempOutputPath);
            }

            var snapshot = AudioDeviceStartupSnapshotService.ReadProbeOutput(tempOutputPath);
            return snapshot.Devices;
        }
        finally
        {
            TryDelete(tempOutputPath);
        }
    }

    private void StoreSnapshot(IReadOnlyList<AudioDeviceInfo> devices)
    {
        _lastKnownDevices = CloneDevices(devices);
        _hasLastKnownSnapshot = true;
        _snapshotService.Save(_lastKnownDevices);
    }

    private static IReadOnlyList<AudioDeviceInfo> CloneDevices(IReadOnlyList<AudioDeviceInfo> devices)
    {
        return devices.Select(CloneDevice).ToList();
    }

    private static AudioDeviceInfo CloneDevice(AudioDeviceInfo device)
    {
        return new AudioDeviceInfo
        {
            Id = device.Id,
            Name = device.Name,
            IsConnected = device.IsConnected,
            IsDefault = device.IsDefault,
            IsMuted = device.IsMuted,
            Volume = device.Volume
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 타임아웃 경로의 보조 정리이므로 여기서 추가 예외는 만들지 않습니다.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 임시 probe 파일 삭제 실패는 기능보다 우선순위가 낮습니다.
        }
    }
}
