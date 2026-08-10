using DesktopAudioController.Services;

namespace DesktopAudioController.Tests;

public sealed class DiagnosticIssueDraftServiceTests
{
    [Fact]
    public void BuildDraft_CreatesRedactedIssueBodyAndUrl()
    {
        using var tempDirectory = new TemporaryDirectory();
        var logDirectoryPath = Path.Combine(tempDirectory.Path, "logs");
        Directory.CreateDirectory(logDirectoryPath);
        var logFilePath = Path.Combine(logDirectoryPath, "DesktopAudioController-20260525.log");
        File.WriteAllLines(
            logFilePath,
            [
                "2026-05-25 [INFO] normal line",
                "2026-05-25 [WARN] route failed deviceId={0.0.0.00000000}.{device-secret} executablePath=C:\\Users\\tester\\Apps\\game.exe",
                "2026-05-25 [ERROR] update failed path=C:\\Users\\tester\\DesktopAudioController\\DesktopAudioController.exe"
            ]);
        File.SetLastWriteTimeUtc(logFilePath, new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc));
        var service = new DiagnosticIssueDraftService(
            logDirectoryPath,
            () => new DateTimeOffset(2026, 5, 25, 1, 0, 0, TimeSpan.Zero));
        var packagePath = Path.Combine(tempDirectory.Path, "DesktopAudioController-diagnostics.zip");

        var draft = service.BuildDraft(packagePath);

        Assert.Contains("DesktopAudioController", draft.Title, StringComparison.Ordinal);
        Assert.Contains("## 진단 요약", draft.Body, StringComparison.Ordinal);
        Assert.Contains("## 최근 WARN/ERROR 요약", draft.Body, StringComparison.Ordinal);
        Assert.Contains("[WARN]", draft.Body, StringComparison.Ordinal);
        Assert.Contains("[ERROR]", draft.Body, StringComparison.Ordinal);
        Assert.Contains("[id:", draft.Body, StringComparison.Ordinal);
        Assert.Contains("[path:game.exe]", draft.Body, StringComparison.Ordinal);
        Assert.Contains("[path:DesktopAudioController.exe]", draft.Body, StringComparison.Ordinal);
        Assert.Contains("자동 제출하거나 zip을 자동 업로드하지 않습니다", draft.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("device-secret", draft.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users\\tester", draft.Body, StringComparison.Ordinal);
        Assert.StartsWith("https://github.com/SeoArin9142/DesktopAudioController/issues/new?", draft.IssueUrl, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(draft.Title), draft.IssueUrl, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("## 진단 요약"), draft.IssueUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDraft_UsesEmptySummaryWhenNoWarningsExist()
    {
        using var tempDirectory = new TemporaryDirectory();
        var logDirectoryPath = Path.Combine(tempDirectory.Path, "logs");
        Directory.CreateDirectory(logDirectoryPath);
        var service = new DiagnosticIssueDraftService(
            logDirectoryPath,
            () => new DateTimeOffset(2026, 5, 25, 1, 0, 0, TimeSpan.Zero));

        var draft = service.BuildDraft(Path.Combine(tempDirectory.Path, "diagnostics.zip"));

        Assert.Contains("최근 로그에서 WARN/ERROR를 찾지 못했습니다.", draft.Body, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "dac-issue-draft-test-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
