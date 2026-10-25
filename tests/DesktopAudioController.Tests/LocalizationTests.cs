using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Tests;

public sealed class LocalizationTests : IDisposable
{
    private static readonly Regex Hangul = new("[가-힣]", RegexOptions.Compiled);
    private static readonly Regex CsCall = new("""Loc\.[TF]\(\s*"((?:[^"\\]|\\.)*)" """.TrimEnd(), RegexOptions.Compiled);
    private static readonly Regex XamlLoc = new("""\{loc:Loc '((?:[^'\\]|\\.)*)'\}""", RegexOptions.Compiled);
    private static readonly Regex XamlFormat = new("""Format='((?:[^'\\]|\\.)*)'""", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{(\d+)", RegexOptions.Compiled);

    public void Dispose()
    {
        // 다른 테스트가 한국어 문구를 기대하므로 항상 한국어로 되돌립니다.
        Loc.Configure(Loc.KoreanLanguage);
    }

    [Fact]
    public void EveryWrappedKoreanString_HasEnglishTranslationWithSamePlaceholders()
    {
        var keys = CollectSourceKeys();
        Assert.NotEmpty(keys);

        var problems = new List<string>();
        foreach (var (key, location) in keys)
        {
            if (!EnglishStrings.Map.TryGetValue(key, out var english))
            {
                problems.Add($"번역 없음 ({location}): {key}");
                continue;
            }

            if (Hangul.IsMatch(english))
            {
                problems.Add($"번역에 한국어가 남음: {key} -> {english}");
            }

            var koreanPlaceholders = PlaceholderSet(key);
            var englishPlaceholders = PlaceholderSet(english);
            if (!koreanPlaceholders.SetEquals(englishPlaceholders))
            {
                problems.Add($"자리 표시 불일치: {key} -> {english}");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EnglishDictionary_HasNoUnusedEntries()
    {
        var keys = CollectSourceKeys().Keys.ToHashSet(StringComparer.Ordinal);

        var unused = EnglishStrings.Map.Keys.Where(key => !keys.Contains(key)).ToList();

        Assert.True(unused.Count == 0, "쓰이지 않는 번역: " + string.Join(" | ", unused));
    }

    [Fact]
    public void T_And_F_FollowConfiguredLanguage()
    {
        Loc.Configure(Loc.KoreanLanguage);
        Assert.False(Loc.IsEnglish);
        Assert.Equal("저장", Loc.T("저장"));
        Assert.Equal("현재 프로필: 이어폰", Loc.F("현재 프로필: {0}", "이어폰"));

        Loc.Configure(Loc.EnglishLanguage);
        Assert.True(Loc.IsEnglish);
        Assert.Equal("Save", Loc.T("저장"));
        Assert.Equal("Current profile: Earphones", Loc.F("현재 프로필: {0}", "Earphones"));
        Assert.Equal("번역 없는 문구", Loc.T("번역 없는 문구"));
    }

    [Fact]
    public void Configure_RaisesLanguageChangedOnlyWhenLanguageActuallyChanges()
    {
        Loc.Configure(Loc.KoreanLanguage);
        var changedCount = 0;
        var propertyChangedCount = 0;
        EventHandler handler = (_, _) => changedCount++;
        System.ComponentModel.PropertyChangedEventHandler propertyHandler = (_, _) => propertyChangedCount++;
        Loc.LanguageChanged += handler;
        LocSource.Instance.PropertyChanged += propertyHandler;
        try
        {
            Loc.Configure(Loc.KoreanLanguage);
            Loc.Configure(Loc.EnglishLanguage);
            Loc.Configure(Loc.EnglishLanguage);
            Loc.Configure(Loc.KoreanLanguage);
        }
        finally
        {
            Loc.LanguageChanged -= handler;
            LocSource.Instance.PropertyChanged -= propertyHandler;
        }

        Assert.Equal(2, changedCount);
        Assert.Equal(2, propertyChangedCount);
    }

    [Theory]
    [InlineData("", "ko-KR", false)]
    [InlineData("", "en-US", true)]
    [InlineData("", "ja-JP", true)]
    [InlineData("ko", "en-US", false)]
    [InlineData("EN", "ko-KR", true)]
    [InlineData("unknown", "ko-KR", false)]
    public void ResolveIsEnglish_UsesSettingThenWindowsLanguage(string setting, string culture, bool expectedEnglish)
    {
        Assert.Equal(expectedEnglish, Loc.ResolveIsEnglish(setting, CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void ConfigureFromSettingsFile_ReadsUiLanguageWithoutFailingOnBrokenFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DesktopAudioController.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllText(settingsPath, """{"UiLanguage":"en"}""");
            Loc.ConfigureFromSettingsFile(settingsPath);
            Assert.True(Loc.IsEnglish);

            File.WriteAllText(settingsPath, """{"UiLanguage":"ko"}""");
            Loc.ConfigureFromSettingsFile(settingsPath);
            Assert.False(Loc.IsEnglish);

            File.WriteAllText(settingsPath, "{ broken");
            Loc.ConfigureFromSettingsFile(settingsPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HashSet<string> PlaceholderSet(string text)
    {
        return Placeholder.Matches(text).Select(match => match.Groups[1].Value).ToHashSet();
    }

    /// <summary>
    /// 소스에서 Loc.T / Loc.F 와 XAML {loc:Loc} / LocFormat 의 한국어 원문을 모두 모읍니다.
    /// </summary>
    private static Dictionary<string, string> CollectSourceKeys()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "DesktopAudioController");
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, path);
            if (relativePath.StartsWith("obj", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("bin", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("Localization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in CsCall.Matches(text))
                {
                    AddKey(keys, Regex.Unescape(match.Groups[1].Value), relativePath);
                }
            }
            else if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var regex in new[] { XamlLoc, XamlFormat })
                {
                    foreach (Match match in regex.Matches(text))
                    {
                        var value = Regex.Replace(WebUtility.HtmlDecode(match.Groups[1].Value), @"\\(.)", "$1");
                        if (value.StartsWith("{}", StringComparison.Ordinal))
                        {
                            value = value[2..];
                        }

                        AddKey(keys, value, relativePath);
                    }
                }
            }
        }

        return keys;
    }

    private static void AddKey(Dictionary<string, string> keys, string key, string location)
    {
        if (Hangul.IsMatch(key))
        {
            keys.TryAdd(key, location);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "DesktopAudioController")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("저장소 루트를 찾지 못했습니다.");
    }
}
