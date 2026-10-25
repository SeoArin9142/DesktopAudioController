using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// portable zip 업데이트를 다운로드하고 별도 updater 프로세스로 넘기는 서비스입니다.
/// 실행 중인 exe는 자기 자신을 안정적으로 덮어쓸 수 없으므로, 여기서는 다운로드/검증/인수 전달까지만 담당합니다.
/// </summary>
public sealed class AutomaticUpdateService
{
    private const string UpdaterExecutableName = "DesktopAudioController.Updater.exe";
    private static readonly TimeSpan ChecksumProgressMinimumDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan VerificationProgressMinimumDuration = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan ApplyPreparationProgressMinimumDuration = TimeSpan.FromSeconds(1);
    private static readonly Regex Sha256HashPattern = new(
        @"\b(?<hash>[a-fA-F0-9]{64})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HttpClient DefaultHttpClient = CreateHttpClient();

    private readonly HttpClient _httpClient;

    public AutomaticUpdateService()
        : this(DefaultHttpClient)
    {
    }

    public AutomaticUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AutomaticUpdateStartResult> StartUpdateAsync(
        UpdateCheckResult updateCheckResult,
        string applicationDirectory,
        string applicationExecutablePath,
        int currentProcessId,
        Action<string>? progressChanged = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateCheckResult.DownloadUrl))
        {
            return AutomaticUpdateStartResult.Fail(Loc.T("자동 업데이트에 필요한 zip 다운로드 URL이 없습니다."));
        }

        if (string.IsNullOrWhiteSpace(updateCheckResult.ChecksumDownloadUrl))
        {
            return AutomaticUpdateStartResult.Fail(Loc.T("자동 업데이트에 필요한 sha256 파일이 없습니다."));
        }

        if (string.IsNullOrWhiteSpace(applicationDirectory) || !Directory.Exists(applicationDirectory))
        {
            return AutomaticUpdateStartResult.Fail(Loc.T("현재 실행 폴더를 찾지 못했습니다."));
        }

        if (string.IsNullOrWhiteSpace(applicationExecutablePath) || !File.Exists(applicationExecutablePath))
        {
            return AutomaticUpdateStartResult.Fail(Loc.T("현재 실행 파일 경로를 찾지 못했습니다."));
        }

        var sourceUpdaterPath = Path.Combine(applicationDirectory, UpdaterExecutableName);
        if (!File.Exists(sourceUpdaterPath))
        {
            return AutomaticUpdateStartResult.Fail(Loc.F("{0} 파일이 없습니다. 이번 버전은 수동 업데이트가 필요합니다.", UpdaterExecutableName));
        }

        var updateWorkDirectory = BuildUpdateWorkDirectory();
        Directory.CreateDirectory(updateWorkDirectory);

        var packagePath = Path.Combine(updateWorkDirectory, "DesktopAudioController-update.zip");
        var checksumPath = $"{packagePath}.sha256";
        var tempUpdaterPath = Path.Combine(updateWorkDirectory, UpdaterExecutableName);

        try
        {
            progressChanged?.Invoke(Loc.T("업데이트 파일 다운로드 중..."));
            AppLog.Info("AutomaticUpdate", $"업데이트 패키지 다운로드 시작 version={updateCheckResult.LatestVersion}");
            await DownloadFileAsync(updateCheckResult.DownloadUrl, packagePath, cancellationToken);

            await RunProgressStepAsync(
                progressChanged,
                Loc.T("체크섬 다운로드 중..."),
                ChecksumProgressMinimumDuration,
                () => DownloadFileAsync(updateCheckResult.ChecksumDownloadUrl, checksumPath, cancellationToken),
                cancellationToken);

            string checksumText = string.Empty;
            string actualHash = string.Empty;
            await RunProgressStepAsync(
                progressChanged,
                Loc.T("업데이트 파일 검증 중..."),
                VerificationProgressMinimumDuration,
                async () =>
                {
                    checksumText = await File.ReadAllTextAsync(checksumPath, cancellationToken);
                    actualHash = await ComputeSha256HashAsync(packagePath, cancellationToken);
                },
                cancellationToken);

            if (!TryParseSha256Hash(checksumText, out var expectedHash))
            {
                return AutomaticUpdateStartResult.Fail(Loc.T("sha256 파일에서 체크섬을 읽지 못했습니다."));
            }

            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Warn("AutomaticUpdate", $"업데이트 패키지 sha256 불일치 expected={expectedHash} actual={actualHash}");
                return AutomaticUpdateStartResult.Fail(Loc.T("다운로드한 업데이트 파일의 sha256 검증에 실패했습니다."));
            }

            // updater도 현재 실행 폴더에서 직접 실행하지 않고 임시 폴더로 복사해 실행합니다.
            // 그래야 updater 자신이 새 zip 안의 updater.exe로 교체되는 상황에서도 파일 잠금 충돌을 피할 수 있습니다.
            await RunProgressStepAsync(
                progressChanged,
                Loc.T("업데이트 적용 준비 중..."),
                ApplyPreparationProgressMinimumDuration,
                () =>
                {
                    File.Copy(sourceUpdaterPath, tempUpdaterPath, overwrite: true);
                    StartUpdaterProcess(
                        tempUpdaterPath,
                        currentProcessId,
                        applicationDirectory,
                        applicationExecutablePath,
                        packagePath,
                        updateCheckResult.LatestVersion);

                    return Task.CompletedTask;
                },
                cancellationToken);

            AppLog.Info("AutomaticUpdate", $"업데이트 적용 프로세스 시작 version={updateCheckResult.LatestVersion}");
            return AutomaticUpdateStartResult.Success(updateWorkDirectory);
        }
        catch (Exception exception)
        {
            AppLog.Error("AutomaticUpdate", "자동 업데이트 시작 실패", exception);
            return AutomaticUpdateStartResult.Fail(exception.Message);
        }
    }

    public static bool TryParseSha256Hash(string checksumText, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(checksumText))
        {
            return false;
        }

        var match = Sha256HashPattern.Match(checksumText);
        if (!match.Success)
        {
            return false;
        }

        hash = match.Groups["hash"].Value.ToLowerInvariant();
        return true;
    }

    internal static TimeSpan CalculateRemainingProgressDelay(TimeSpan elapsed, TimeSpan minimumDuration)
    {
        if (minimumDuration <= TimeSpan.Zero || elapsed >= minimumDuration)
        {
            return TimeSpan.Zero;
        }

        return minimumDuration - elapsed;
    }

    private static string BuildUpdateWorkDirectory()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "DesktopAudioController",
            "updates",
            DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            Guid.NewGuid().ToString("N"));
    }

    private async Task DownloadFileAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var targetStream = File.Create(targetPath);
        await sourceStream.CopyToAsync(targetStream, cancellationToken);
    }

    private static async Task RunProgressStepAsync(
        Action<string>? progressChanged,
        string message,
        TimeSpan minimumDuration,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        progressChanged?.Invoke(message);
        var startedTimestamp = Stopwatch.GetTimestamp();

        await operation();

        // 체크섬 읽기처럼 너무 빨리 끝나는 단계는 사용자가 상태 변화를 인지하기 어렵습니다.
        // 단계별 최소 표시 시간을 보장해 진행 문구가 깜빡이지 않게 합니다.
        var remainingDelay = CalculateRemainingProgressDelay(
            Stopwatch.GetElapsedTime(startedTimestamp),
            minimumDuration);
        if (remainingDelay > TimeSpan.Zero)
        {
            await Task.Delay(remainingDelay, cancellationToken);
        }
    }

    private static async Task<string> ComputeSha256HashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static void StartUpdaterProcess(
        string updaterPath,
        int currentProcessId,
        string applicationDirectory,
        string applicationExecutablePath,
        string packagePath,
        string? version)
    {
        var startInfo = new ProcessStartInfo(updaterPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(updaterPath) ?? applicationDirectory
        };

        startInfo.ArgumentList.Add("--pid");
        startInfo.ArgumentList.Add(currentProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--target-dir");
        startInfo.ArgumentList.Add(applicationDirectory);
        startInfo.ArgumentList.Add("--app-exe");
        startInfo.ArgumentList.Add(applicationExecutablePath);
        startInfo.ArgumentList.Add("--package");
        startInfo.ArgumentList.Add(packagePath);
        if (!string.IsNullOrWhiteSpace(version))
        {
            startInfo.ArgumentList.Add("--version");
            startInfo.ArgumentList.Add(version);
        }

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException(Loc.T("업데이트 적용 프로세스를 시작하지 못했습니다."));
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopAudioController/automatic-update");
        return client;
    }
}

public sealed record AutomaticUpdateStartResult(
    bool Started,
    string? ErrorMessage,
    string? WorkDirectory)
{
    public static AutomaticUpdateStartResult Success(string workDirectory)
    {
        return new AutomaticUpdateStartResult(true, null, workDirectory);
    }

    public static AutomaticUpdateStartResult Fail(string errorMessage)
    {
        return new AutomaticUpdateStartResult(false, errorMessage, null);
    }
}
