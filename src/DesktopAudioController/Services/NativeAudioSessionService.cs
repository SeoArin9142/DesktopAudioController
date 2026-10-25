using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using DesktopAudioController.Models;
using System.Diagnostics;
using System.IO;

namespace DesktopAudioController.Services;

/// <summary>
/// 지정한 출력 장치에 매달린 애플리케이션 세션을 열거하고 제어하는 서비스입니다.
/// </summary>
public sealed class NativeAudioSessionService : IAudioSessionService, IDisposable
{
    // 프로세스 이름과 실행 파일 경로를 PID 기준으로 재사용하는 메타데이터 캐시 서비스입니다.
    private readonly IProcessMetadataCacheService _processMetadataCacheService;

    // 세션 열거가 UI 지연으로 이어질 수 있는 기준 시간입니다.
    private static readonly long SlowEnumerationThresholdMs = 500;

    /// <summary>
    /// 세션 서비스와 메타데이터 캐시 서비스를 함께 초기화합니다.
    /// </summary>
    public NativeAudioSessionService(IProcessMetadataCacheService processMetadataCacheService)
    {
        _processMetadataCacheService = processMetadataCacheService;
    }

    /// <summary>
    /// 지정한 장치에서 현재 활성화된 세션 목록을 반환합니다.
    /// </summary>
    public IReadOnlyList<AudioSessionInfo> GetSessions(string deviceId, bool includeSystemSounds = false)
    {
        var stopwatch = Stopwatch.StartNew();
        AppLog.Debug(
            "NativeAudioSessionService",
            $"GetSessions 시작 deviceId={deviceId} includeSystemSounds={includeSystemSounds}");

        try
        {
            using var enumerator = new MMDeviceEnumerator();

            // device는 세션을 읽어올 대상 출력 장치입니다.
            using var device = enumerator.GetDevice(deviceId);

            // sessions는 해당 출력 장치에 연결된 현재 오디오 세션 컬렉션입니다.
            var sessions = device.AudioSessionManager.Sessions;

            // UI에 전달할 세션 목록 결과입니다.
            var results = new List<AudioSessionInfo>();

            for (int index = 0; index < sessions.Count; index++)
            {
                // 루프 안의 session은 한 개의 앱 오디오 세션입니다.
                using var session = sessions[index];

                try
                {
                    // 종료된 세션은 화면에 남길 이유가 없습니다.
                    if (session.State == AudioSessionState.AudioSessionStateExpired)
                    {
                        continue;
                    }

                    // Windows 시스템 사운드는 일반 앱 목록과 분리하는 편이 UX가 낫습니다.
                    if (!includeSystemSounds && session.IsSystemSoundsSession)
                    {
                        continue;
                    }

                    var processId = session.GetProcessID;
                    if (!session.IsSystemSoundsSession && processId != 0 && !_processMetadataCacheService.IsProcessAlive(processId))
                    {
                        _processMetadataCacheService.Invalidate(processId);
                        continue;
                    }

                    var displayName = ResolveDisplayName(session);
                    var executablePath = ResolveExecutablePath(session);
                    var iconSourcePath = ResolveIconSourcePath(session, executablePath);

                    results.Add(new AudioSessionInfo
                    {
                        MatchKey = ProgramAudioPreferenceStore.CreateMatchKey(
                            session.GetSessionIdentifier,
                            executablePath,
                            displayName),
                        Id = session.GetSessionIdentifier,
                        DisplayName = displayName,
                        ExecutablePath = executablePath,
                        IconSourcePath = iconSourcePath,
                        Volume = (int)Math.Round(session.SimpleAudioVolume.Volume * 100),
                        IsMuted = session.SimpleAudioVolume.Mute,
                        IsActive = session.State == AudioSessionState.AudioSessionStateActive
                    });
                }
                catch
                {
                    // 세션 조회 중 앱이 종료될 수 있으므로 해당 세션만 건너뜁니다.
                }
            }

            var coalescedSessions = CoalesceSessions(results);
            stopwatch.Stop();
            var message = $"GetSessions 완료 deviceId={deviceId} includeSystemSounds={includeSystemSounds} count={coalescedSessions.Count} elapsedMs={stopwatch.ElapsedMilliseconds}";
            if (stopwatch.ElapsedMilliseconds >= SlowEnumerationThresholdMs)
            {
                AppLog.Warn("NativeAudioSessionService", message);
            }
            else
            {
                AppLog.Debug("NativeAudioSessionService", message);
            }

            return coalescedSessions;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            AppLog.Error(
                "NativeAudioSessionService",
                $"GetSessions 실패 deviceId={deviceId} includeSystemSounds={includeSystemSounds} elapsedMs={stopwatch.ElapsedMilliseconds}",
                exception);
            throw;
        }
    }

    /// <summary>
    /// 지정한 장치의 특정 세션 볼륨을 설정합니다.
    /// </summary>
    public void SetSessionVolume(string deviceId, string sessionId, int volume)
    {
        AppLog.Debug("NativeAudioSessionService", $"SetSessionVolume 시작 deviceId={deviceId} sessionId={sessionId} volume={volume}");
        using var enumerator = new MMDeviceEnumerator();
        // device는 세션을 제어할 대상 출력 장치입니다.
        using var device = enumerator.GetDevice(deviceId);

        // targetSession은 sessionId와 일치하는 실제 오디오 세션입니다.
        using var targetSession = FindSession(device.AudioSessionManager.Sessions, sessionId);
        if (targetSession is null)
        {
            AppLog.Warn("NativeAudioSessionService", $"SetSessionVolume 대상 세션 없음 deviceId={deviceId} sessionId={sessionId}");
            return;
        }

        targetSession.SimpleAudioVolume.Volume = Math.Clamp(volume, 0, 100) / 100f;
        AppLog.Debug("NativeAudioSessionService", $"SetSessionVolume 완료 deviceId={deviceId} sessionId={sessionId} volume={volume}");
    }

    /// <summary>
    /// 지정한 장치의 특정 세션 음소거 상태를 설정합니다.
    /// </summary>
    public void SetSessionMuted(string deviceId, string sessionId, bool muted)
    {
        AppLog.Info("NativeAudioSessionService", $"SetSessionMuted 시작 deviceId={deviceId} sessionId={sessionId} muted={muted}");
        using var enumerator = new MMDeviceEnumerator();
        // device는 세션을 제어할 대상 출력 장치입니다.
        using var device = enumerator.GetDevice(deviceId);

        // targetSession은 sessionId와 일치하는 실제 오디오 세션입니다.
        using var targetSession = FindSession(device.AudioSessionManager.Sessions, sessionId);
        if (targetSession is null)
        {
            AppLog.Warn("NativeAudioSessionService", $"SetSessionMuted 대상 세션 없음 deviceId={deviceId} sessionId={sessionId}");
            return;
        }

        targetSession.SimpleAudioVolume.Mute = muted;
        AppLog.Info("NativeAudioSessionService", $"SetSessionMuted 완료 deviceId={deviceId} sessionId={sessionId} muted={muted}");
    }

    /// <summary>
    /// 지정한 세션을 소유한 앱의 Windows 앱별 출력 장치 정책을 변경합니다.
    /// </summary>
    public void SetSessionOutputDevice(string deviceId, string sessionId, string targetDeviceId)
    {
        AppLog.Info(
            "NativeAudioSessionService",
            $"SetSessionOutputDevice 시작 deviceId={deviceId} sessionId={sessionId} targetDeviceId={targetDeviceId}");

        using var enumerator = new MMDeviceEnumerator();
        using var sourceDevice = enumerator.GetDevice(deviceId);
        using var targetDevice = enumerator.GetDevice(targetDeviceId);
        using var targetSession = FindSession(sourceDevice.AudioSessionManager.Sessions, sessionId);
        if (targetSession is null)
        {
            AppLog.Warn("NativeAudioSessionService", $"SetSessionOutputDevice 대상 세션 없음 deviceId={deviceId} sessionId={sessionId}");
            return;
        }

        var processId = targetSession.GetProcessID;
        if (processId == 0)
        {
            throw new InvalidOperationException("프로세스 ID가 없는 오디오 세션은 출력 장치를 변경할 수 없습니다.");
        }

        // 정책 API는 "세션"이 아니라 "프로세스의 앱별 출력 정책"을 바꿉니다.
        // 그래서 먼저 세션 PID를 찾고, 같은 exe를 쓰는 형제 PID까지 후보로 넓힙니다.
        var executablePath = ResolveExecutablePath(targetSession);
        var candidateProcessIds = GetCandidateProcessIds(processId, executablePath);
        var processFailures = new List<string>();
        // Chromium/게임 런처처럼 오디오 세션 PID와 실제 정책 대상 PID가 갈릴 수 있어 같은 실행 파일 PID를 함께 시도합니다.
        AppLog.Info(
            "NativeAudioSessionService",
            $"SetSessionOutputDevice 후보 processIds=[{string.Join(", ", candidateProcessIds)}] executablePath={executablePath ?? "unknown"}");

        foreach (var candidateProcessId in candidateProcessIds)
        {
            try
            {
                ApplicationAudioOutputPolicy.SetPersistedDefaultOutputDevice((uint)candidateProcessId, targetDeviceId);
                AppLog.Info(
                    "NativeAudioSessionService",
                    $"SetSessionOutputDevice 정책 변경 요청 성공 processId={candidateProcessId} targetDevice={targetDevice.FriendlyName}");
                return;
            }
            catch (Exception exception)
            {
                processFailures.Add($"{candidateProcessId}:{exception.Message}");
                AppLog.Warn(
                    "NativeAudioSessionService",
                    $"SetSessionOutputDevice 후보 processId 실패 processId={candidateProcessId} message={exception.Message}");
            }
        }

        throw new InvalidOperationException(
            $"앱별 출력 장치 정책 변경에 실패했습니다. processResults=[{FormatFailures(processFailures)}]");
    }

    /// <summary>
    /// 실행 중인 같은 이름의 프로세스 전부에 앱별 출력 장치 정책을 적용합니다.
    /// targetDeviceId 가 비어 있으면 앱별 지정을 지웁니다. 바꾼 프로세스 수를 반환합니다.
    /// </summary>
    public int SetProcessOutputDevice(string processName, string targetDeviceId)
    {
        var normalizedName = ProcessNameMatcher.Normalize(processName);
        if (normalizedName.Length == 0)
        {
            throw new ArgumentException("프로세스 이름이 비어 있습니다.", nameof(processName));
        }

        var processIds = new List<uint>();
        foreach (var process in Process.GetProcessesByName(normalizedName))
        {
            using (process)
            {
                processIds.Add((uint)process.Id);
            }
        }

        AppLog.Info(
            "NativeAudioSessionService",
            $"SetProcessOutputDevice 시작 processName={normalizedName} processIds=[{string.Join(", ", processIds)}] deviceId={(string.IsNullOrWhiteSpace(targetDeviceId) ? "(Windows 기본)" : targetDeviceId)}");
        if (processIds.Count == 0)
        {
            return 0;
        }

        var changedCount = 0;
        var processFailures = new List<string>();
        foreach (var processId in processIds)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetDeviceId))
                {
                    try
                    {
                        ApplicationAudioOutputPolicy.ClearPersistedDefaultOutputDevice(processId);
                    }
                    catch (InvalidOperationException exception) when (exception.Message.Contains("0x80070057", StringComparison.Ordinal))
                    {
                        // 앱별 지정이 원래 없던 프로세스는 해제 요청에 E_INVALIDARG 를 돌려줍니다. 이미 원하는 상태이므로 성공으로 봅니다.
                        AppLog.Info("NativeAudioSessionService", $"SetProcessOutputDevice 해제할 앱별 지정 없음 processId={processId}");
                    }
                }
                else
                {
                    ApplicationAudioOutputPolicy.SetPersistedDefaultOutputDevice(processId, targetDeviceId);
                }

                changedCount++;
            }
            catch (Exception exception)
            {
                // 보호된 프로세스나 이미 종료된 PID 는 실패할 수 있으므로 나머지 PID 를 계속 시도합니다.
                processFailures.Add($"{processId}:{exception.Message}");
            }
        }

        if (changedCount == 0)
        {
            throw new InvalidOperationException(
                $"앱별 출력 장치 정책 변경에 실패했습니다. processResults=[{FormatFailures(processFailures)}]");
        }

        if (processFailures.Count > 0)
        {
            AppLog.Warn(
                "NativeAudioSessionService",
                $"SetProcessOutputDevice 일부 PID 실패 processName={normalizedName} failures=[{FormatFailures(processFailures)}]");
        }

        return changedCount;
    }

    private static string FormatFailures(IReadOnlyList<string> failures)
    {
        const int MaxVisibleFailures = 5;
        var visibleFailures = failures.Take(MaxVisibleFailures).ToArray();
        var suffix = failures.Count > MaxVisibleFailures
            ? $" | ... (+{failures.Count - MaxVisibleFailures} more)"
            : string.Empty;
        return string.Join(" | ", visibleFailures) + suffix;
    }

    private static IReadOnlyList<uint> GetCandidateProcessIds(uint sessionProcessId, string? executablePath)
    {
        var result = new List<uint> { sessionProcessId };
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return result;
        }

        var processName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return result;
        }

        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if ((uint)process.Id == sessionProcessId)
                {
                    continue;
                }

                var currentExecutablePath = TryGetProcessExecutablePath(process);
                if (!string.Equals(currentExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add((uint)process.Id);
            }
        }

        return result.Distinct().ToArray();
    }

    private static string? TryGetProcessExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 세션 컬렉션에서 지정한 식별자와 일치하는 세션을 찾습니다.
    /// </summary>
    private static AudioSessionControl? FindSession(SessionCollection sessions, string sessionId)
    {
        for (int index = 0; index < sessions.Count; index++)
        {
            // session은 비교 대상 오디오 세션입니다.
            var session = sessions[index];

            try
            {
                if (session.GetSessionIdentifier == sessionId)
                {
                    return session;
                }
            }
            catch
            {
                // 세션 식별자 조회 중 오류가 나면 해당 세션은 버립니다.
            }

            session.Dispose();
        }

        return null;
    }

    /// <summary>
    /// 세션 표시 이름을 결정합니다.
    /// </summary>
    private string ResolveDisplayName(AudioSessionControl session)
    {
        if (session.IsSystemSoundsSession)
        {
            return "Windows 시스템 사운드";
        }

        // displayName은 앱이 직접 제공한 표시 이름입니다.
        var displayName = session.DisplayName;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        // 메타데이터 캐시는 PID 기준으로 프로세스 이름을 재사용해 세션 재로딩 비용을 줄입니다.
        var metadata = _processMetadataCacheService.GetProcessMetadata(session.GetProcessID);
        return metadata.PreferredDisplayName;
    }

    /// <summary>
    /// 세션을 소유한 프로세스의 실행 파일 경로를 확인합니다.
    /// </summary>
    private string? ResolveExecutablePath(AudioSessionControl session)
    {
        // 메타데이터 캐시는 실행 경로도 함께 재사용하므로 아이콘 조회 전 비용을 한 번 줄일 수 있습니다.
        var metadata = _processMetadataCacheService.GetProcessMetadata(session.GetProcessID);
        return metadata.ExecutablePath;
    }

    /// <summary>
    /// 세션이 따로 제공하는 파일 아이콘 경로가 있으면 우선 사용하고, 없으면 실행 파일 경로로 폴백합니다.
    /// </summary>
    private static string? ResolveIconSourcePath(AudioSessionControl session, string? executablePath)
    {
        string? sessionIconPath = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(session.IconPath))
            {
                sessionIconPath = session.IconPath;
            }
        }
        catch
        {
            // 일부 세션은 아이콘 경로 조회 자체가 실패할 수 있으므로 실행 파일 경로 폴백으로 진행합니다.
        }

        return IconSourcePathResolver.ResolvePreferredIconSourcePath(sessionIconPath, executablePath);
    }

    /// <summary>
    /// 내부 COM 열거자를 정리합니다.
    /// </summary>
    public void Dispose()
    {
        // 메서드별로 열거자를 생성/정리하므로 종료 시 추가 정리할 리소스가 없습니다.
    }

    /// <summary>
    /// Core Audio가 동일 세션 ID를 중복으로 내놓을 수 있어, 세션 ID 기준으로 하나로 합칩니다.
    /// </summary>
    private static IReadOnlyList<AudioSessionInfo> CoalesceSessions(IReadOnlyList<AudioSessionInfo> sessions)
    {
        var sessionsById = new Dictionary<string, AudioSessionInfo>();
        foreach (var session in sessions)
        {
            if (!sessionsById.TryGetValue(session.Id, out var existing))
            {
                sessionsById[session.Id] = session;
                continue;
            }

            sessionsById[session.Id] = MergeSession(existing, session);
        }

        return sessionsById.Values.ToList();
    }

    /// <summary>
    /// 중복 세션 둘 중 메타데이터가 더 풍부한 쪽을 우선해 하나의 스냅샷으로 합칩니다.
    /// </summary>
    private static AudioSessionInfo MergeSession(AudioSessionInfo current, AudioSessionInfo incoming)
    {
        return new AudioSessionInfo
        {
            MatchKey = current.MatchKey ?? incoming.MatchKey,
            Id = current.Id,
            DisplayName = PreferDisplayName(current.DisplayName, incoming.DisplayName),
            ExecutablePath = PreferExecutablePath(current.ExecutablePath, incoming.ExecutablePath),
            IconSourcePath = PreferIconSourcePath(current.IconSourcePath, incoming.IconSourcePath),
            Volume = incoming.Volume,
            IsMuted = incoming.IsMuted,
            IsActive = current.IsActive || incoming.IsActive
        };
    }

    private static string PreferDisplayName(string current, string incoming)
    {
        if (IsPidFallback(current) && !IsPidFallback(incoming))
        {
            return incoming;
        }

        return !string.IsNullOrWhiteSpace(current) ? current : incoming;
    }

    private static string? PreferExecutablePath(string? current, string? incoming)
    {
        return !string.IsNullOrWhiteSpace(current) ? current : incoming;
    }

    private static string? PreferIconSourcePath(string? current, string? incoming)
    {
        return !string.IsNullOrWhiteSpace(current) ? current : incoming;
    }

    private static bool IsPidFallback(string value)
    {
        return value.StartsWith("PID ", StringComparison.Ordinal);
    }
}
