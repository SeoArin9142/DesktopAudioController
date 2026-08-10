using System.IO;
using System.Text;

namespace DesktopAudioController.Services;

/// <summary>
/// 진단 패키지를 GitHub 이슈로 보고하기 위한 짧은 redacted 초안을 만듭니다.
/// </summary>
public sealed class DiagnosticIssueDraftService
{
    private const int MaxRecentLogFiles = 5;
    private const int MaxSummaryLines = 8;
    private const int MaxSummaryLineLength = 220;
    private static readonly TimeSpan RecentLogWindow = TimeSpan.FromDays(7);
    private readonly string _logDirectoryPath;
    private readonly Func<DateTimeOffset> _nowProvider;

    public DiagnosticIssueDraftService()
        : this(AppLog.LogDirectoryPath, () => DateTimeOffset.UtcNow)
    {
    }

    internal DiagnosticIssueDraftService(string logDirectoryPath, Func<DateTimeOffset> nowProvider)
    {
        _logDirectoryPath = logDirectoryPath;
        _nowProvider = nowProvider;
    }

    public DiagnosticIssueDraft BuildDraft(string diagnosticPackagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticPackagePath);

        var fullPackagePath = Path.GetFullPath(diagnosticPackagePath);
        var title = $"[진단] DesktopAudioController {AppBuildInfo.Version} 문제 보고";
        var body = BuildBody(fullPackagePath);
        var issueUrl = BuildIssueUrl(title, body);
        return new DiagnosticIssueDraft(title, body, issueUrl, fullPackagePath);
    }

    private string BuildBody(string diagnosticPackagePath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## 진단 요약");
        builder.AppendLine($"- Version: {AppBuildInfo.Version}");
        builder.AppendLine($"- SourceRevision: {AppBuildInfo.SourceRevision}");
        builder.AppendLine($"- OS: {Environment.OSVersion}");
        builder.AppendLine($"- GeneratedAtUtc: {_nowProvider():O}");
        builder.AppendLine($"- DiagnosticPackage: {DiagnosticRedactor.RedactPath(diagnosticPackagePath)}");
        builder.AppendLine();
        builder.AppendLine("## 최근 WARN/ERROR 요약");

        var summaryLines = BuildRecentWarningSummary().Take(MaxSummaryLines).ToList();
        if (summaryLines.Count == 0)
        {
            builder.AppendLine("- 최근 로그에서 WARN/ERROR를 찾지 못했습니다.");
        }
        else
        {
            foreach (var line in summaryLines)
            {
                builder.AppendLine($"- {line}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## 실패 기능");
        builder.AppendLine("- [ ] 라우팅");
        builder.AppendLine("- [ ] 업데이트");
        builder.AppendLine("- [ ] 장치 감지");
        builder.AppendLine("- [ ] 트레이");
        builder.AppendLine("- [ ] 기타");
        builder.AppendLine();
        builder.AppendLine("## 재현 절차");
        builder.AppendLine("1. ");
        builder.AppendLine("2. ");
        builder.AppendLine("3. ");
        builder.AppendLine();
        builder.AppendLine("## 첨부");
        builder.AppendLine("- 생성된 진단 패키지 zip을 직접 확인한 뒤 첨부해 주세요.");
        builder.AppendLine("- 앱이 이슈를 자동 제출하거나 zip을 자동 업로드하지 않습니다.");
        builder.AppendLine();
        builder.AppendLine("## 공개 전 확인");
        builder.AppendLine("- 진단 요약은 경로/ID 중심으로 마스킹됩니다.");
        builder.AppendLine("- 앱 표시명, 실행 파일명, 장치 표시명, 사용자 지정 별칭은 남을 수 있으니 공개 전 내용을 확인해 주세요.");

        return builder.ToString().TrimEnd();
    }

    private IEnumerable<string> BuildRecentWarningSummary()
    {
        if (!Directory.Exists(_logDirectoryPath))
        {
            yield break;
        }

        foreach (var logFile in EnumerateRecentLogFiles())
        {
            IReadOnlyList<string> lines;
            try
            {
                lines = File.ReadLines(logFile.FullName).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var line in lines.Where(IsWarningOrErrorLine))
            {
                yield return TrimLine(DiagnosticRedactor.RedactText(line));
            }
        }
    }

    private IReadOnlyList<FileInfo> EnumerateRecentLogFiles()
    {
        var cutoffUtc = _nowProvider().UtcDateTime - RecentLogWindow;
        return Directory
            .EnumerateFiles(_logDirectoryPath, "DesktopAudioController*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists && file.LastWriteTimeUtc >= cutoffUtc)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentLogFiles)
            .ToList();
    }

    private static bool IsWarningOrErrorLine(string line)
    {
        return line.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimLine(string line)
    {
        var normalized = line.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= MaxSummaryLineLength
            ? normalized
            : normalized[..MaxSummaryLineLength] + "...";
    }

    private static string BuildIssueUrl(string title, string body)
    {
        var encodedTitle = Uri.EscapeDataString(title);
        var encodedBody = Uri.EscapeDataString(body);
        return $"https://github.com/SeoArin9142/DesktopAudioController/issues/new?labels=bug&title={encodedTitle}&body={encodedBody}";
    }
}
