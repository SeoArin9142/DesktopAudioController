using System.Diagnostics;

namespace DesktopAudioController.Services;

/// <summary>
/// 동일 exe를 장치 열거 워커 프로세스로 다시 실행할 때 사용하는 인수 규약입니다.
/// </summary>
internal static class AudioDeviceProbeCommand
{
    public const string ProbeAudioArgument = "--probe-audio";
    public const string ProbeOutputPathArgument = "--probe-output-path";
    public const string DebugLogArgument = "--debug-log";

    // 출력 장치 대신 입력(마이크) 장치를 열거하라는 표시입니다.
    public const string ProbeInputArgument = "--probe-input";

    public static bool TryParse(IReadOnlyList<string> args, out string outputPath)
    {
        outputPath = string.Empty;
        var hasProbeFlag = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, ProbeAudioArgument, StringComparison.OrdinalIgnoreCase))
            {
                hasProbeFlag = true;
                continue;
            }

            if (!string.Equals(argument, ProbeOutputPathArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return false;
            }

            outputPath = args[index + 1];
            index++;
        }

        return hasProbeFlag && !string.IsNullOrWhiteSpace(outputPath);
    }

    public static bool IsInputProbe(IReadOnlyList<string> args)
    {
        return args.Any(argument => string.Equals(argument, ProbeInputArgument, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasDebugLogFlag(IReadOnlyList<string> args)
    {
        return args.Any(argument => string.Equals(argument, DebugLogArgument, StringComparison.OrdinalIgnoreCase));
    }

    public static void Apply(ProcessStartInfo startInfo, string outputPath, bool debugLogEnabled = false, bool probeInput = false)
    {
        startInfo.ArgumentList.Add(ProbeAudioArgument);
        if (probeInput)
        {
            startInfo.ArgumentList.Add(ProbeInputArgument);
        }

        if (debugLogEnabled)
        {
            startInfo.ArgumentList.Add(DebugLogArgument);
        }

        startInfo.ArgumentList.Add(ProbeOutputPathArgument);
        startInfo.ArgumentList.Add(outputPath);
    }
}
