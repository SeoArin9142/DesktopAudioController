using System.IO;
using System.Text.Json;

namespace DesktopAudioController.Services;

/// <summary>
/// 앱별 출력 변경을 별도 프로세스에서 수행해 네이티브 API 실패가 메인 앱 종료로 이어지지 않게 합니다.
/// </summary>
internal static class AudioSessionOutputChangeWorker
{
    public static int Run(AudioSessionOutputChangeCommand.ParsedCommand command)
    {
        AppLog.Info(
            "AudioSessionOutputChangeWorker",
            $"세션 출력 변경 워커 시작 sourceDeviceId={command.SourceDeviceId} sessionId={command.SessionId} targetDeviceId={command.TargetDeviceId} resultOutputPath={command.ResultOutputPath}");

        try
        {
            var metadataCacheService = new CachedProcessMetadataService();
            using var sessionService = new NativeAudioSessionService(metadataCacheService);
            sessionService.SetSessionOutputDevice(command.SourceDeviceId, command.SessionId, command.TargetDeviceId);
            WriteResult(command.ResultOutputPath, new AudioSessionOutputChangeResult
            {
                Success = true
            });
            AppLog.Info("AudioSessionOutputChangeWorker", "세션 출력 변경 워커 완료");
            return 0;
        }
        catch (Exception exception)
        {
            AppLog.Error("AudioSessionOutputChangeWorker", "세션 출력 변경 워커 실패", exception);
            WriteResult(command.ResultOutputPath, new AudioSessionOutputChangeResult
            {
                Success = false,
                ErrorMessage = exception.Message
            });
            return 1;
        }
    }

    public static int RunForProcess(AudioProcessOutputChangeCommand.ParsedCommand command)
    {
        AppLog.Info(
            "AudioSessionOutputChangeWorker",
            $"프로세스 출력 변경 워커 시작 processName={command.ProcessName} deviceId={command.TargetDeviceId} resultOutputPath={command.ResultOutputPath}");

        try
        {
            var metadataCacheService = new CachedProcessMetadataService();
            using var sessionService = new NativeAudioSessionService(metadataCacheService);
            var changedCount = sessionService.SetProcessOutputDevice(command.ProcessName, command.TargetDeviceId);
            WriteResult(command.ResultOutputPath, new AudioSessionOutputChangeResult
            {
                Success = true,
                ChangedProcessCount = changedCount
            });
            AppLog.Info("AudioSessionOutputChangeWorker", $"프로세스 출력 변경 워커 완료 changedProcessCount={changedCount}");
            return 0;
        }
        catch (Exception exception)
        {
            AppLog.Error("AudioSessionOutputChangeWorker", "프로세스 출력 변경 워커 실패", exception);
            WriteResult(command.ResultOutputPath, new AudioSessionOutputChangeResult
            {
                Success = false,
                ErrorMessage = exception.Message
            });
            return 1;
        }
    }

    private static void WriteResult(string resultOutputPath, AudioSessionOutputChangeResult result)
    {
        try
        {
            var json = JsonSerializer.Serialize(result);
            File.WriteAllText(resultOutputPath, json);
        }
        catch (Exception exception)
        {
            AppLog.Warn("AudioSessionOutputChangeWorker", $"세션 출력 변경 결과 저장 실패 path={resultOutputPath}", exception);
        }
    }
}
