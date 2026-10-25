using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// GitHub Releases API를 사용해 최신 릴리즈 버전을 확인하는 서비스입니다.
/// </summary>
public sealed class GitHubReleaseUpdateCheckService : IUpdateCheckService
{
    // 최신 릴리즈 조회에 사용할 GitHub Releases API입니다.
    private static readonly Uri ReleasesApiUri = new("https://api.github.com/repos/SeoArin9142/DesktopAudioController/releases?per_page=10");

    // GitHub API는 User-Agent 헤더가 필요합니다.
    private static readonly HttpClient DefaultHttpClient = CreateHttpClient();

    // 현재 프로젝트가 쓰는 태그 규칙(v0.7.3-preview2)을 해석하기 위한 정규식입니다.
    private static readonly Regex VersionRegex = new(
        @"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<label>[A-Za-z]+)(?<number>\d+)?)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // portable zip 한 개만 배포하므로 업데이트 안내도 이 자산명 규칙을 기준으로 연결합니다.
    private const string PortableZipSuffix = "-win-x64.zip";

    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateCheckService()
        : this(DefaultHttpClient)
    {
    }

    public GitHubReleaseUpdateCheckService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 현재 버전보다 더 새로운 GitHub 릴리즈가 있는지 확인합니다.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        string currentVersion,
        bool includePreReleaseUpdates,
        CancellationToken cancellationToken = default)
    {
        // 인터넷 연결이 없거나 GitHub API가 느릴 때 UI가 멈추지 않도록 짧은 타임아웃을 둡니다.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            using var response = await _httpClient.GetAsync(ReleasesApiUri, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warn("UpdateCheck", $"GitHub 릴리즈 조회 실패 statusCode={(int)response.StatusCode}");
                return new UpdateCheckResult
                {
                    HadError = true,
                    StatusMessage = Loc.T("업데이트 확인에 실패했습니다.")
                };
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: timeoutCts.Token);

            if (!TryGetLatestRelease(document.RootElement, includePreReleaseUpdates, out var releaseCandidate))
            {
                return new UpdateCheckResult();
            }

            if (!TryParseVersion(currentVersion, out var currentParsedVersion)
                || !TryParseVersion(releaseCandidate.VersionText, out var latestParsedVersion))
            {
                return new UpdateCheckResult();
            }

            if (latestParsedVersion.CompareTo(currentParsedVersion) <= 0)
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    LatestVersion = releaseCandidate.VersionText,
                    ReleasePageUrl = releaseCandidate.ReleasePageUrl,
                    DownloadUrl = releaseCandidate.DownloadUrl,
                    ChecksumDownloadUrl = releaseCandidate.ChecksumDownloadUrl,
                    PublishedAtUtc = releaseCandidate.PublishedAtUtc,
                    IsPreRelease = releaseCandidate.IsPreRelease
                };
            }

            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                LatestVersion = releaseCandidate.VersionText,
                ReleasePageUrl = releaseCandidate.ReleasePageUrl,
                DownloadUrl = releaseCandidate.DownloadUrl,
                ChecksumDownloadUrl = releaseCandidate.ChecksumDownloadUrl,
                PublishedAtUtc = releaseCandidate.PublishedAtUtc,
                IsPreRelease = releaseCandidate.IsPreRelease
            };
        }
        catch (Exception exception)
        {
            AppLog.Warn("UpdateCheck", "업데이트 확인 실패", exception);
            return new UpdateCheckResult
            {
                HadError = true,
                StatusMessage = Loc.T("업데이트 확인에 실패했습니다.")
            };
        }
    }

    /// <summary>
    /// 릴리즈 배열에서 채널 정책에 맞는 가장 높은 버전을 찾습니다.
    /// </summary>
    private static bool TryGetLatestRelease(
        JsonElement releasesElement,
        bool includePreReleaseUpdates,
        out ReleaseCandidate releaseCandidate)
    {
        releaseCandidate = default;

        if (releasesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var hasCandidate = false;

        foreach (var releaseElement in releasesElement.EnumerateArray())
        {
            // draft 릴리즈는 사용자에게 업데이트 대상으로 보여주지 않습니다.
            if (releaseElement.TryGetProperty("draft", out var draftProperty) && draftProperty.GetBoolean())
            {
                continue;
            }

            if (!releaseElement.TryGetProperty("tag_name", out var tagNameProperty))
            {
                continue;
            }

            var tagName = tagNameProperty.GetString();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            var normalizedVersion = NormalizeVersionString(tagName);
            if (!TryParseVersion(normalizedVersion, out var comparableVersion))
            {
                continue;
            }

            var releasePageUrl = releaseElement.TryGetProperty("html_url", out var htmlUrlProperty)
                ? htmlUrlProperty.GetString() ?? string.Empty
                : string.Empty;
            var (downloadUrl, checksumDownloadUrl) = TryGetPortableZipAssets(releaseElement);
            var publishedAtUtc = TryGetPublishedAtUtc(releaseElement);
            var isPreRelease = releaseElement.TryGetProperty("prerelease", out var prereleaseProperty)
                && prereleaseProperty.GetBoolean();
            if (isPreRelease && !includePreReleaseUpdates)
            {
                continue;
            }

            var candidate = new ReleaseCandidate(
                normalizedVersion,
                comparableVersion,
                releasePageUrl,
                downloadUrl,
                checksumDownloadUrl,
                publishedAtUtc,
                isPreRelease);

            if (!hasCandidate || candidate.CompareTo(releaseCandidate) > 0)
            {
                releaseCandidate = candidate;
                hasCandidate = true;
            }
        }

        return hasCandidate;
    }

    /// <summary>
    /// 태그 문자열에서 v 접두사를 제거하고 비교 가능한 표준 형태로 만듭니다.
    /// </summary>
    private static string NormalizeVersionString(string versionText)
    {
        return versionText.StartsWith('v') || versionText.StartsWith('V')
            ? versionText[1..]
            : versionText;
    }

    /// <summary>
    /// 프로젝트 태그 규칙에 맞는 버전 문자열을 비교용 구조체로 변환합니다.
    /// </summary>
    private static bool TryParseVersion(string versionText, out ComparableVersion version)
    {
        version = default;

        var match = VersionRegex.Match(versionText);
        if (!match.Success)
        {
            return false;
        }

        var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        var minor = int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture);
        var patch = int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture);
        var preReleaseLabel = match.Groups["label"].Success
            ? match.Groups["label"].Value
            : string.Empty;
        var preReleaseNumber = match.Groups["number"].Success
            ? int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture)
            : 0;

        version = new ComparableVersion(major, minor, patch, preReleaseLabel, preReleaseNumber);
        return true;
    }

    /// <summary>
    /// GitHub API 요청에 공통으로 쓸 HttpClient를 생성합니다.
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopAudioController/update-check");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static (string? DownloadUrl, string? ChecksumDownloadUrl) TryGetPortableZipAssets(JsonElement releaseElement)
    {
        if (!releaseElement.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        string? exactZipUrl = null;
        string? exactChecksumUrl = null;
        string? fallbackZipUrl = null;
        string? fallbackChecksumUrl = null;

        foreach (var assetElement in assetsElement.EnumerateArray())
        {
            if (!assetElement.TryGetProperty("name", out var nameProperty))
            {
                continue;
            }

            var assetName = nameProperty.GetString();
            if (string.IsNullOrWhiteSpace(assetName))
            {
                continue;
            }

            if (!assetElement.TryGetProperty("browser_download_url", out var urlProperty))
            {
                continue;
            }

            var assetUrl = urlProperty.GetString();
            if (string.IsNullOrWhiteSpace(assetUrl))
            {
                continue;
            }

            if (assetName.EndsWith($"{PortableZipSuffix}.sha256", StringComparison.OrdinalIgnoreCase))
            {
                exactChecksumUrl = assetUrl;
                continue;
            }

            if (assetName.EndsWith(PortableZipSuffix, StringComparison.OrdinalIgnoreCase))
            {
                exactZipUrl = assetUrl;
                continue;
            }

            if (assetName.EndsWith(".zip.sha256", StringComparison.OrdinalIgnoreCase) && fallbackChecksumUrl is null)
            {
                fallbackChecksumUrl = assetUrl;
                continue;
            }

            if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && fallbackZipUrl is null)
            {
                fallbackZipUrl = assetUrl;
            }
        }

        return (exactZipUrl ?? fallbackZipUrl, exactChecksumUrl ?? fallbackChecksumUrl);
    }

    private static DateTimeOffset? TryGetPublishedAtUtc(JsonElement releaseElement)
    {
        if (!releaseElement.TryGetProperty("published_at", out var publishedAtProperty))
        {
            return null;
        }

        var publishedAtText = publishedAtProperty.GetString();
        if (string.IsNullOrWhiteSpace(publishedAtText))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            publishedAtText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var publishedAtUtc)
            ? publishedAtUtc
            : null;
    }

    /// <summary>
    /// 현재 프로젝트 버전 규칙에 맞춘 단순 비교용 구조체입니다.
    /// </summary>
    private readonly record struct ComparableVersion(
        int Major,
        int Minor,
        int Patch,
        string PreReleaseLabel,
        int PreReleaseNumber) : IComparable<ComparableVersion>
    {
        public int CompareTo(ComparableVersion other)
        {
            var majorCompare = Major.CompareTo(other.Major);
            if (majorCompare != 0)
            {
                return majorCompare;
            }

            var minorCompare = Minor.CompareTo(other.Minor);
            if (minorCompare != 0)
            {
                return minorCompare;
            }

            var patchCompare = Patch.CompareTo(other.Patch);
            if (patchCompare != 0)
            {
                return patchCompare;
            }

            var isPreRelease = !string.IsNullOrWhiteSpace(PreReleaseLabel);
            var otherIsPreRelease = !string.IsNullOrWhiteSpace(other.PreReleaseLabel);

            if (isPreRelease != otherIsPreRelease)
            {
                return isPreRelease ? -1 : 1;
            }

            if (!isPreRelease)
            {
                return 0;
            }

            var labelCompare = string.Compare(PreReleaseLabel, other.PreReleaseLabel, StringComparison.OrdinalIgnoreCase);
            if (labelCompare != 0)
            {
                return labelCompare;
            }

            return PreReleaseNumber.CompareTo(other.PreReleaseNumber);
        }
    }

    private readonly record struct ReleaseCandidate(
        string VersionText,
        ComparableVersion Version,
        string ReleasePageUrl,
        string? DownloadUrl,
        string? ChecksumDownloadUrl,
        DateTimeOffset? PublishedAtUtc,
        bool IsPreRelease) : IComparable<ReleaseCandidate>
    {
        public int CompareTo(ReleaseCandidate other)
        {
            var versionCompare = Version.CompareTo(other.Version);
            if (versionCompare != 0)
            {
                return versionCompare;
            }

            if (PublishedAtUtc.HasValue && other.PublishedAtUtc.HasValue)
            {
                return PublishedAtUtc.Value.CompareTo(other.PublishedAtUtc.Value);
            }

            if (PublishedAtUtc.HasValue != other.PublishedAtUtc.HasValue)
            {
                return PublishedAtUtc.HasValue ? 1 : -1;
            }

            return string.Compare(VersionText, other.VersionText, StringComparison.OrdinalIgnoreCase);
        }
    }
}
