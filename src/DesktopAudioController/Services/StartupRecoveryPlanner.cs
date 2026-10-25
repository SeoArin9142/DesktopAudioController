using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

internal static class StartupRecoveryPlanner
{
    internal const string StartupRecoveryRetryArgument = "--startup-recovery-retry";

    public static bool ShouldAutoRetry(
        bool isStartupLaunch,
        bool isStartupRecoveryRetry,
        bool isInDegradedMode,
        bool requiresRestartForRecovery)
    {
        return isStartupLaunch &&
               !isStartupRecoveryRetry &&
               isInDegradedMode &&
               requiresRestartForRecovery;
    }

    public static string[] BuildAutomaticRetryArguments(string startupLaunchArgument)
    {
        return
        [
            startupLaunchArgument,
            StartupRecoveryRetryArgument
        ];
    }

    public static string BuildAutomaticRetryWarningMessage(string warningMessage, TimeSpan delay)
    {
        var totalSeconds = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds));
        return Loc.F("{0} {1}초 뒤 프로그램을 자동으로 다시 시작합니다.", warningMessage, totalSeconds);
    }
}
