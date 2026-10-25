using System.Reflection;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

public static class AppBuildInfo
{
    public static string Version => GetDisplayVersion();

    public static string SourceRevision => GetSourceRevision();

    public static string LogSummary => $"version={Version} sourceRevision={SourceRevision}";

    private static Assembly CurrentAssembly => Assembly.GetExecutingAssembly();

    private static string GetDisplayVersion()
    {
        var informationalVersion = GetInformationalVersion();
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparatorIndex = informationalVersion.IndexOf('+');
            return metadataSeparatorIndex >= 0
                ? informationalVersion[..metadataSeparatorIndex]
                : informationalVersion;
        }

        return CurrentAssembly.GetName().Version?.ToString() ?? Loc.T("알 수 없음");
    }

    private static string GetSourceRevision()
    {
        var metadataRevision = CurrentAssembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "GitCommit", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (!string.IsNullOrWhiteSpace(metadataRevision))
        {
            return metadataRevision;
        }

        var informationalVersion = GetInformationalVersion();
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparatorIndex = informationalVersion.IndexOf('+');
            if (metadataSeparatorIndex >= 0 && metadataSeparatorIndex < informationalVersion.Length - 1)
            {
                return informationalVersion[(metadataSeparatorIndex + 1)..];
            }
        }

        return "unknown";
    }

    private static string? GetInformationalVersion()
    {
        return CurrentAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
    }
}
