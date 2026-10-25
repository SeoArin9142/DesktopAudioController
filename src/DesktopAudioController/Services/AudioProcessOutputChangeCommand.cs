using System.Diagnostics;

namespace DesktopAudioController.Services;

/// <summary>
/// 프로필 적용 때 실행 파일 이름 기준으로 앱별 출력을 바꾸는 워커 프로세스 인수 규약입니다.
/// 세션이 아직 없는 프로그램(막 실행된 게임 등)도 PID 만 있으면 바꿀 수 있습니다.
/// </summary>
internal static class AudioProcessOutputChangeCommand
{
    public const string ChangeProcessOutputArgument = "--change-process-output";
    public const string ProcessNameArgument = "--process-name";
    public const string TargetDeviceIdArgument = "--target-device-id";
    public const string ResultOutputPathArgument = "--result-output-path";

    // 빈 인수는 전달 과정에서 사라질 수 있어 "Windows 기본 장치 따르기"는 별도 표식으로 넘깁니다.
    public const string WindowsDefaultTargetValue = "windows-default";

    public sealed class ParsedCommand
    {
        public required string ProcessName { get; init; }

        /// <summary>
        /// 비어 있으면 앱별 지정을 지웁니다.
        /// </summary>
        public required string TargetDeviceId { get; init; }

        public required string ResultOutputPath { get; init; }
    }

    public static bool TryParse(IReadOnlyList<string> args, out ParsedCommand command)
    {
        command = new ParsedCommand
        {
            ProcessName = string.Empty,
            TargetDeviceId = string.Empty,
            ResultOutputPath = string.Empty
        };

        if (!args.Any(argument => string.Equals(argument, ChangeProcessOutputArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var processName = ReadValue(args, ProcessNameArgument);
        var targetDeviceId = ReadValue(args, TargetDeviceIdArgument);
        var resultOutputPath = ReadValue(args, ResultOutputPathArgument);
        if (string.IsNullOrWhiteSpace(processName)
            || string.IsNullOrWhiteSpace(targetDeviceId)
            || string.IsNullOrWhiteSpace(resultOutputPath))
        {
            return false;
        }

        command = new ParsedCommand
        {
            ProcessName = processName,
            TargetDeviceId = string.Equals(targetDeviceId, WindowsDefaultTargetValue, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : targetDeviceId,
            ResultOutputPath = resultOutputPath
        };
        return true;
    }

    public static void Apply(
        ProcessStartInfo startInfo,
        string processName,
        string targetDeviceId,
        string resultOutputPath,
        bool debugLogEnabled = false)
    {
        startInfo.ArgumentList.Add(ChangeProcessOutputArgument);
        if (debugLogEnabled)
        {
            startInfo.ArgumentList.Add(AudioDeviceProbeCommand.DebugLogArgument);
        }

        startInfo.ArgumentList.Add(ProcessNameArgument);
        startInfo.ArgumentList.Add(processName);
        startInfo.ArgumentList.Add(TargetDeviceIdArgument);
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(targetDeviceId) ? WindowsDefaultTargetValue : targetDeviceId);
        startInfo.ArgumentList.Add(ResultOutputPathArgument);
        startInfo.ArgumentList.Add(resultOutputPath);
    }

    private static string ReadValue(IReadOnlyList<string> args, string expectedArgument)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], expectedArgument, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return string.Empty;
    }
}
