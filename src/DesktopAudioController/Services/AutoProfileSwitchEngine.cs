using DesktopAudioController.Models;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// 자동 전환 판단 결과입니다. ProfileId 가 null 이면 프로필은 바꾸지 않고 Reason 만 로그에 남깁니다.
/// </summary>
internal sealed record AutoProfileDecision(string? ProfileId, string? RuleId, string Reason, bool IsRestore);

/// <summary>
/// 실행 중인 프로그램 목록과 규칙을 비교해 언제 어떤 프로필을 적용할지 정하는 순수 로직입니다.
/// 같은 규칙이 계속 활성 상태인 동안에는 다시 적용하지 않으므로, 사용자가 중간에 수동으로 바꾼 프로필을 덮어쓰지 않습니다.
/// </summary>
internal sealed class AutoProfileSwitchEngine
{
    private string? _activeProfileId;
    private string? _profileBeforeAuto;

    /// <summary>
    /// 지금 프로필을 잡고 있는 규칙 ID입니다. 활성 규칙이 없으면 null 입니다.
    /// </summary>
    public string? ActiveRuleId { get; private set; }

    /// <summary>
    /// 일시정지·설정 변경 뒤 처음부터 다시 판단하도록 상태를 지웁니다.
    /// </summary>
    public void Reset()
    {
        ActiveRuleId = null;
        _activeProfileId = null;
        _profileBeforeAuto = null;
    }

    public AutoProfileDecision? Evaluate(
        IReadOnlySet<string> runningProcessNames,
        AppSettings settings,
        string? currentProfileId)
    {
        var profileIds = settings.AudioProfiles
            .Select(profile => profile.Id)
            .ToHashSet(StringComparer.Ordinal);
        var match = FindActiveRule(runningProcessNames, settings.AutoProfileRules, profileIds);

        if (match is not null)
        {
            var (rule, priority, processName) = match.Value;
            if (string.Equals(rule.Id, ActiveRuleId, StringComparison.Ordinal) &&
                string.Equals(rule.ProfileId, _activeProfileId, StringComparison.Ordinal))
            {
                return null;
            }

            var previousRuleName = ActiveRuleId is null
                ? null
                : DescribeRule(settings.AutoProfileRules.FirstOrDefault(item => item.Id == ActiveRuleId));
            if (ActiveRuleId is null)
            {
                // 앱을 껐다 켰을 때처럼 이미 규칙 프로필이 적용돼 있으면, 돌아갈 곳으로 기본 프로필을 씁니다.
                _profileBeforeAuto = string.Equals(currentProfileId, rule.ProfileId, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(settings.AutoProfileFallbackProfileId)
                        ? settings.AutoProfileFallbackProfileId
                        : currentProfileId;
            }

            ActiveRuleId = rule.Id;
            _activeProfileId = rule.ProfileId;
            var reason = Loc.F("규칙 '{0}'(우선순위 {1}) - {2} 실행 중", DescribeRule(rule), priority, processName);
            if (previousRuleName is not null)
            {
                reason += Loc.F(", 이전 규칙 '{0}' 대신 적용", previousRuleName);
            }

            return new AutoProfileDecision(rule.ProfileId, rule.Id, reason, IsRestore: false);
        }

        if (ActiveRuleId is null)
        {
            return null;
        }

        var endedRule = settings.AutoProfileRules.FirstOrDefault(item => item.Id == ActiveRuleId);
        var endedRuleName = endedRule is null ? Loc.T("삭제된 규칙") : DescribeRule(endedRule);
        var restoreMode = endedRule?.RestoreMode ?? AutoProfileRestoreMode.PreviousProfile;
        var profileBeforeAuto = _profileBeforeAuto;
        var endedRuleId = ActiveRuleId;
        Reset();

        if (restoreMode == AutoProfileRestoreMode.KeepCurrent)
        {
            return new AutoProfileDecision(null, endedRuleId, Loc.F("규칙 '{0}' 종료 - 설정대로 현재 프로필 유지", endedRuleName), IsRestore: true);
        }

        var (targetProfileId, targetDescription) = restoreMode == AutoProfileRestoreMode.FallbackProfile
            ? (settings.AutoProfileFallbackProfileId, Loc.T("기본 프로필"))
            : (profileBeforeAuto, Loc.T("이전 프로필"));

        if (string.IsNullOrWhiteSpace(targetProfileId) || !profileIds.Contains(targetProfileId))
        {
            return new AutoProfileDecision(
                null,
                endedRuleId,
                Loc.F("규칙 '{0}' 종료 - 돌아갈 {1}이 없어 현재 프로필 유지", endedRuleName, targetDescription),
                IsRestore: true);
        }

        return new AutoProfileDecision(
            targetProfileId,
            endedRuleId,
            Loc.F("규칙 '{0}' 종료 - {1}로 복귀", endedRuleName, targetDescription),
            IsRestore: true);
    }

    /// <summary>
    /// 사용 중이고, 프로필이 남아 있고, 감시 프로그램 중 하나가 실행 중인 첫 규칙을 찾습니다.
    /// </summary>
    internal static (AutoProfileRule Rule, int Priority, string ProcessName)? FindActiveRule(
        IReadOnlySet<string> runningProcessNames,
        IReadOnlyList<AutoProfileRule> rules,
        IReadOnlySet<string> profileIds)
    {
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            if (rule is null || !rule.IsEnabled || !profileIds.Contains(rule.ProfileId))
            {
                continue;
            }

            foreach (var processName in rule.ProcessNames)
            {
                var normalizedName = ProcessNameMatcher.Normalize(processName);
                if (normalizedName.Length > 0 && runningProcessNames.Contains(normalizedName))
                {
                    return (rule, index + 1, normalizedName);
                }
            }
        }

        return null;
    }

    public static string DescribeRule(AutoProfileRule? rule)
    {
        if (rule is null)
        {
            return Loc.T("알 수 없는 규칙");
        }

        return string.IsNullOrWhiteSpace(rule.Name)
            ? string.Join(", ", rule.ProcessNames)
            : rule.Name.Trim();
    }
}
