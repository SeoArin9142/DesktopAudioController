using DesktopAudioController.Models;

namespace DesktopAudioController.Services;

/// <summary>
/// 설정창에서 입력한 값이 저장할 수 없는 상태일 때 사용자에게 보여 줄 문구를 담습니다.
/// </summary>
public sealed class SettingsValidationException(IReadOnlyList<string> problems)
    : Exception(string.Join(Environment.NewLine, problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// 자동 전환 규칙과 프로필 단축키를 저장 전에 검사합니다.
/// </summary>
internal static class AutoProfileSettingsValidator
{
    public static IReadOnlyList<string> Validate(AppSettings settings)
    {
        var problems = new List<string>();
        var profileIds = settings.AudioProfiles
            .Select(profile => profile.Id)
            .ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < settings.AutoProfileRules.Count; index++)
        {
            var rule = settings.AutoProfileRules[index];
            var label = $"자동 전환 규칙 {index + 1}";
            if (!string.IsNullOrWhiteSpace(rule.Name))
            {
                label += $" '{rule.Name.Trim()}'";
            }

            if (rule.ProcessNames.All(name => ProcessNameMatcher.Normalize(name).Length == 0))
            {
                problems.Add($"{label}: 감시할 프로그램 이름을 적어 주세요. (예: TslGame.exe)");
            }

            if (!profileIds.Contains(rule.ProfileId))
            {
                problems.Add($"{label}: 적용할 프로필을 골라 주세요.");
            }

            if (rule.IsEnabled &&
                rule.RestoreMode == AutoProfileRestoreMode.FallbackProfile &&
                !profileIds.Contains(settings.AutoProfileFallbackProfileId))
            {
                problems.Add($"{label}: '기본 프로필로'를 고르려면 위의 기본 프로필을 지정해 주세요.");
            }
        }

        var hotkeyOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in settings.AudioProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Hotkey))
            {
                continue;
            }

            if (!HotkeyGesture.TryParse(profile.Hotkey, out var gesture, out var error) || gesture is null)
            {
                problems.Add($"프로필 '{profile.Name}' 단축키: {error}");
                continue;
            }

            if (hotkeyOwners.TryGetValue(gesture.DisplayText, out var ownerName))
            {
                problems.Add($"프로필 '{profile.Name}' 단축키 {gesture.DisplayText}가 프로필 '{ownerName}'와 겹칩니다.");
                continue;
            }

            hotkeyOwners[gesture.DisplayText] = profile.Name;
        }

        return problems;
    }

    public static void ThrowIfInvalid(AppSettings settings)
    {
        var problems = Validate(settings);
        if (problems.Count > 0)
        {
            throw new SettingsValidationException(problems);
        }
    }
}
