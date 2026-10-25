namespace DesktopAudioController.Services;

/// <summary>
/// 사용자가 입력한 실행 파일 이름을 Process.ProcessName 과 비교할 수 있게 맞춥니다.
/// </summary>
internal static class ProcessNameMatcher
{
    /// <summary>
    /// 앞뒤 공백·따옴표와 경로, ".exe" 확장자를 떼어 냅니다. 대소문자는 그대로 둡니다.
    /// </summary>
    public static string Normalize(string? processName)
    {
        var trimmed = processName?.Trim().Trim('"').Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // 전체 경로를 붙여 넣어도 실행 파일 이름만 씁니다.
        var lastSeparator = trimmed.LastIndexOfAny(['\\', '/']);
        if (lastSeparator >= 0)
        {
            trimmed = trimmed[(lastSeparator + 1)..].Trim();
        }

        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4].Trim()
            : trimmed;
    }

    /// <summary>
    /// 쉼표·세미콜론·줄바꿈으로 구분한 여러 이름을 목록으로 나눕니다.
    /// </summary>
    public static List<string> SplitList(string? text)
    {
        return (text ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static HashSet<string> CreateSet(IEnumerable<string> processNames)
    {
        return new HashSet<string>(
            processNames.Select(Normalize).Where(name => name.Length > 0),
            StringComparer.OrdinalIgnoreCase);
    }
}
