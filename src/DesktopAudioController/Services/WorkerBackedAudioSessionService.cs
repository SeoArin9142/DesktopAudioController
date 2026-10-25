using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DesktopAudioController.Models;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// 세션 조회/볼륨 제어는 현재 프로세스에서 수행하고, 앱별 출력 변경만 워커 프로세스로 격리합니다.
/// 출력 변경은 Windows 내부 정책 API를 호출하므로 실패/행/크래시가 메인 UI까지 번지지 않게 분리합니다.
/// </summary>
public sealed class WorkerBackedAudioSessionService : IAudioSessionService, IDisposable
{
    private static readonly TimeSpan OutputChangeTimeout = TimeSpan.FromSeconds(3);
    private readonly string _executablePath;
    private readonly NativeAudioSessionService _nativeSessionService;

    public WorkerBackedAudioSessionService(
        string executablePath,
        NativeAudioSessionService nativeSessionService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = executablePath;
        _nativeSessionService = nativeSessionService;
    }

    public IReadOnlyList<AudioSessionInfo> GetSessions(string deviceId, bool includeSystemSounds = false)
    {
        return _nativeSessionService.GetSessions(deviceId, includeSystemSounds);
    }

    public void SetSessionVolume(string deviceId, string sessionId, int volume)
    {
        _nativeSessionService.SetSessionVolume(deviceId, sessionId, volume);
    }

    public void SetSessionMuted(string deviceId, string sessionId, bool muted)
    {
        _nativeSessionService.SetSessionMuted(deviceId, sessionId, muted);
    }

    public void SetSessionOutputDevice(string deviceId, string sessionId, string targetDeviceId)
    {
        // 같은 exe를 --change-session-output 모드로 실행하고 JSON 결과 파일로 성공/실패를 받습니다.
        RunOutputChangeWorker(
            Loc.T("세션 출력 변경"),
            $"deviceId={deviceId} sessionId={sessionId} targetDeviceId={targetDeviceId}",
            (startInfo, resultOutputPath) => AudioSessionOutputChangeCommand.Apply(
                startInfo,
                deviceId,
                sessionId,
                targetDeviceId,
                resultOutputPath,
                AppLog.IsDebugEnabled));
    }

    public int SetProcessOutputDevice(string processName, string targetDeviceId)
    {
        var result = RunOutputChangeWorker(
            Loc.T("프로세스 출력 변경"),
            $"processName={processName} deviceId={(string.IsNullOrWhiteSpace(targetDeviceId) ? "(Windows 기본)" : targetDeviceId)}",
            (startInfo, resultOutputPath) => AudioProcessOutputChangeCommand.Apply(
                startInfo,
                processName,
                targetDeviceId,
                resultOutputPath,
                AppLog.IsDebugEnabled));
        return result?.ChangedProcessCount ?? 0;
    }

    private AudioSessionOutputChangeResult? RunOutputChangeWorker(
        string operationName,
        string logDetail,
        Action<ProcessStartInfo, string> applyArguments)
    {
        var stopwatch = Stopwatch.StartNew();
        var tempOutputPath = Path.Combine(
            Path.GetTempPath(),
            $"DesktopAudioController-session-output-{Guid.NewGuid():N}.json");

        try
        {
            var startInfo = new ProcessStartInfo(_executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? Environment.CurrentDirectory
            };
            applyArguments(startInfo, tempOutputPath);

            AppLog.Info(
                "WorkerBackedAudioSessionService",
                $"{operationName} 워커 시작 {logDetail} timeoutMs={(int)OutputChangeTimeout.TotalMilliseconds}");

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(Loc.F("{0} 워커 프로세스를 시작하지 못했습니다.", operationName));

            if (!process.WaitForExit((int)OutputChangeTimeout.TotalMilliseconds))
            {
                TryKill(process);
                throw new TimeoutException(Loc.F("{0} 워커가 제한 시간 안에 끝나지 않았습니다.", operationName));
            }

            var result = TryReadResult(tempOutputPath);
            if (process.ExitCode != 0)
            {
                var message = result?.ErrorMessage;
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = $"{operationName} 워커가 비정상 종료했습니다. exitCode={process.ExitCode}";
                }

                throw new InvalidOperationException(message);
            }

            if (result is { Success: false })
            {
                throw new InvalidOperationException(result.ErrorMessage ?? Loc.F("{0} 워커가 실패했습니다.", operationName));
            }

            AppLog.Info(
                "WorkerBackedAudioSessionService",
                $"{operationName} 워커 완료 elapsedMs={stopwatch.ElapsedMilliseconds}");
            return result;
        }
        finally
        {
            TryDelete(tempOutputPath);
        }
    }

    public void Dispose()
    {
        _nativeSessionService.Dispose();
    }

    private static AudioSessionOutputChangeResult? TryReadResult(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AudioSessionOutputChangeResult>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            AppLog.Warn("WorkerBackedAudioSessionService", $"세션 출력 변경 결과 읽기 실패 path={path}", exception);
            return null;
        }
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
            // 임시 결과 파일 삭제 실패는 기능보다 우선순위가 낮습니다.
        }
    }
}
