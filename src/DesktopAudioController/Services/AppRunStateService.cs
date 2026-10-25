using System.IO;
using System.Text.Json;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// 이전 실행의 정상 종료 여부를 기록하고 다음 시작 시 비정상 종료를 감지합니다.
/// </summary>
public sealed class AppRunStateService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private bool _skipCleanShutdownMark;

    public AppRunStateService()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopAudioController",
                "run-state.json"))
    {
    }

    public AppRunStateService(string stateFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateFilePath);
        StateFilePath = stateFilePath;
    }

    public string StateFilePath { get; }

    public PreviousRunIncident BeginRun()
    {
        _skipCleanShutdownMark = false;
        AppRunState? previousState = null;

        try
        {
            previousState = LoadState();
        }
        catch (Exception exception)
        {
            AppLog.Warn("AppRunState", $"상태 파일 읽기 실패 path={StateFilePath}", exception);
        }

        var incident = previousState is { IsRunning: true }
            ? new PreviousRunIncident(
                true,
                previousState.StartedAtUtc,
                Loc.T("이전 실행이 정상 종료되지 않았습니다. 로그를 확인하고 필요하면 이슈를 남겨 주세요."))
            : PreviousRunIncident.None;

        SaveState(new AppRunState
        {
            IsRunning = true,
            StartedAtUtc = DateTimeOffset.UtcNow,
            LastCleanExitAtUtc = previousState?.LastCleanExitAtUtc
        });

        return incident;
    }

    public void MarkCleanShutdown()
    {
        if (_skipCleanShutdownMark)
        {
            AppLog.Warn("AppRunState", "치명적 종료가 감지되어 정상 종료 상태 기록을 건너뜁니다.");
            return;
        }

        AppRunState? currentState = null;

        try
        {
            currentState = LoadState();
        }
        catch (Exception exception)
        {
            AppLog.Warn("AppRunState", $"상태 파일 읽기 실패 path={StateFilePath}", exception);
        }

        SaveState(new AppRunState
        {
            IsRunning = false,
            StartedAtUtc = currentState?.StartedAtUtc ?? DateTimeOffset.UtcNow,
            LastCleanExitAtUtc = DateTimeOffset.UtcNow
        });
    }

    public void MarkUnexpectedTermination()
    {
        _skipCleanShutdownMark = true;
    }

    private AppRunState? LoadState()
    {
        if (!File.Exists(StateFilePath))
        {
            return null;
        }

        var json = File.ReadAllText(StateFilePath);
        return JsonSerializer.Deserialize<AppRunState>(json, SerializerOptions);
    }

    private void SaveState(AppRunState state)
    {
        var tempFilePath = $"{StateFilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
            var json = JsonSerializer.Serialize(state, SerializerOptions);
            File.WriteAllText(tempFilePath, json);

            if (File.Exists(StateFilePath))
            {
                File.Replace(tempFilePath, StateFilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempFilePath, StateFilePath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // 상태 파일 temp 정리 실패는 앱 동작을 막지 않습니다.
            }
        }
    }

    private sealed class AppRunState
    {
        public bool IsRunning { get; init; }

        public DateTimeOffset StartedAtUtc { get; init; }

        public DateTimeOffset? LastCleanExitAtUtc { get; init; }
    }
}

public readonly record struct PreviousRunIncident(
    bool Detected,
    DateTimeOffset StartedAtUtc,
    string Message)
{
    public static PreviousRunIncident None => new(false, default, string.Empty);
}
