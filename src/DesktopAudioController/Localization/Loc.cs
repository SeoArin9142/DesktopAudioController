using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DesktopAudioController.Localization;

/// <summary>
/// 화면 문구를 현재 언어로 바꿉니다. 한국어 원문을 그대로 키로 쓰고(gettext 방식),
/// 영어일 때만 <see cref="EnglishStrings"/> 에서 번역을 찾습니다. 번역이 없으면 한국어 원문을 그대로 보여 줍니다.
/// </summary>
public static class Loc
{
    /// <summary>
    /// 설정에 저장하는 언어 값입니다. 비어 있으면 Windows 표시 언어를 따릅니다.
    /// </summary>
    public const string AutoLanguage = "";
    public const string KoreanLanguage = "ko";
    public const string EnglishLanguage = "en";

    private static bool _isEnglish;

    /// <summary>
    /// 지금 화면 언어가 영어인지 여부입니다.
    /// </summary>
    public static bool IsEnglish => _isEnglish;

    /// <summary>
    /// 설정 값("", "ko", "en")으로 화면 언어를 정합니다. 비어 있으면 Windows 표시 언어가 한국어일 때만 한국어를 씁니다.
    /// </summary>
    public static void Configure(string? languageSetting)
    {
        var isEnglish = ResolveIsEnglish(languageSetting, CultureInfo.CurrentUICulture);
        if (isEnglish == _isEnglish)
        {
            return;
        }

        _isEnglish = isEnglish;

        // XAML 문구는 LocSource 바인딩으로, 코드에서 만든 문구(트레이 메뉴 등)는 이 이벤트로 다시 그립니다.
        LocSource.Instance.RaiseLanguageChanged();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// 앱을 다시 켜지 않고 언어가 바뀌었을 때 알립니다. 코드에서 만든 문구를 다시 그리는 데 씁니다.
    /// </summary>
    public static event EventHandler? LanguageChanged;

    public static bool ResolveIsEnglish(string? languageSetting, CultureInfo uiCulture)
    {
        return languageSetting?.Trim().ToLowerInvariant() switch
        {
            KoreanLanguage => false,
            EnglishLanguage => true,
            _ => !string.Equals(uiCulture.TwoLetterISOLanguageName, "ko", StringComparison.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// 앱 시작 직후(워커 실행 포함) 설정 파일에서 언어 값만 가볍게 읽어 적용합니다.
    /// 설정 복구·백업 같은 부수 효과가 있는 SettingsService.Load 를 거치지 않습니다.
    /// </summary>
    public static void ConfigureFromSettingsFile(string settingsFilePath)
    {
        string? languageSetting = null;
        try
        {
            if (File.Exists(settingsFilePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(settingsFilePath));
                if (document.RootElement.TryGetProperty("UiLanguage", out var languageElement) &&
                    languageElement.ValueKind == JsonValueKind.String)
                {
                    languageSetting = languageElement.GetString();
                }
            }
        }
        catch
        {
            // 설정 파일이 깨졌어도 언어는 Windows 표시 언어로 정하고 계속 진행합니다.
        }

        Configure(languageSetting);
    }

    /// <summary>
    /// 한국어 원문을 현재 언어 문구로 바꿉니다.
    /// </summary>
    public static string T(string korean)
    {
        if (!_isEnglish)
        {
            return korean;
        }

        return EnglishStrings.Map.TryGetValue(korean, out var english) ? english : korean;
    }

    /// <summary>
    /// "{0}" 자리 표시가 있는 한국어 원문을 현재 언어로 바꾼 뒤 값을 채웁니다.
    /// </summary>
    public static string F(string koreanFormat, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, T(koreanFormat), args);
    }
}

/// <summary>
/// XAML 바인딩이 언어 변경을 알 수 있게 하는 원본입니다. 언어가 바뀌면 Language 변경을 알려
/// 이 값에 연결된 모든 화면 문구가 앱을 다시 켜지 않아도 새 언어로 다시 그려집니다.
/// </summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language => Loc.IsEnglish ? Loc.EnglishLanguage : Loc.KoreanLanguage;

    internal void RaiseLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }
}
