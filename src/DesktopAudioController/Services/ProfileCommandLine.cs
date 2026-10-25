using System.Text;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// 명령줄로 받은 프로필 적용·장치 음소거 요청입니다.
/// </summary>
public sealed record ProfileCommand(ProfileCommandKind Kind, string Value);

public enum ProfileCommandKind
{
    /// <summary>
    /// 프로필 이름으로 적용합니다. 예: --profile "이어폰"
    /// </summary>
    ApplyProfileByName,

    /// <summary>
    /// 메인 화면 버튼 순서(1부터)로 적용합니다. 예: --profile-index 2
    /// </summary>
    ApplyProfileByIndex,

    /// <summary>
    /// 이름이 맞는 출력 장치의 음소거를 바꿉니다. 예: --toggle-mute "MR3"
    /// </summary>
    ToggleDeviceMute
}

/// <summary>
/// 스트림덱 "열기" 동작 같은 외부 실행에서 쓰는 명령줄 인수 규약입니다.
/// 실행 중인 인스턴스가 있으면 단일 인스턴스 파이프로 명령만 넘기고 끝납니다.
/// </summary>
public static class ProfileCommandLine
{
    public const string ProfileArgument = "--profile";
    public const string ProfileIndexArgument = "--profile-index";
    public const string ToggleMuteArgument = "--toggle-mute";

    public static bool TryParse(IReadOnlyList<string> args, out ProfileCommand? command)
    {
        command = null;
        for (var index = 0; index < args.Count; index++)
        {
            var kind = args[index].ToLowerInvariant() switch
            {
                ProfileArgument => ProfileCommandKind.ApplyProfileByName,
                ProfileIndexArgument => ProfileCommandKind.ApplyProfileByIndex,
                ToggleMuteArgument => ProfileCommandKind.ToggleDeviceMute,
                _ => (ProfileCommandKind?)null
            };
            if (kind is null)
            {
                continue;
            }

            var value = index + 1 < args.Count ? args[index + 1].Trim() : string.Empty;
            command = new ProfileCommand(kind.Value, value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 파이프 한 줄로 보낼 수 있게 명령을 문자열로 바꿉니다.
    /// </summary>
    public static string Serialize(ProfileCommand command)
    {
        return $"{command.Kind}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(command.Value))}";
    }

    public static bool TryDeserialize(string? text, out ProfileCommand? command)
    {
        command = null;
        var separatorIndex = text?.IndexOf(':') ?? -1;
        if (text is null || separatorIndex <= 0 ||
            !Enum.TryParse<ProfileCommandKind>(text[..separatorIndex], out var kind))
        {
            return false;
        }

        try
        {
            command = new ProfileCommand(kind, Encoding.UTF8.GetString(Convert.FromBase64String(text[(separatorIndex + 1)..])));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// 이름 비교용으로 공백을 한 칸으로 줄이고 대소문자를 무시할 수 있게 만듭니다.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        return string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    /// <summary>
    /// 이름이 정확히 같은 항목을 먼저 찾고, 없으면 이름에 포함되는 항목이 하나뿐일 때 그것을 고릅니다.
    /// </summary>
    public static T? FindByName<T>(IEnumerable<T> items, Func<T, string> nameSelector, string requestedName, out string? error)
        where T : class
    {
        error = null;
        var normalizedRequest = NormalizeName(requestedName);
        if (normalizedRequest.Length == 0)
        {
            error = Loc.T("이름이 비어 있습니다.");
            return null;
        }

        var candidates = items.ToList();
        var exact = candidates.FirstOrDefault(item => NormalizeName(nameSelector(item)) == normalizedRequest);
        if (exact is not null)
        {
            return exact;
        }

        var partial = candidates
            .Where(item => NormalizeName(nameSelector(item)).Contains(normalizedRequest, StringComparison.Ordinal))
            .ToList();
        if (partial.Count == 1)
        {
            return partial[0];
        }

        error = partial.Count == 0
            ? Loc.F("'{0}'에 맞는 항목이 없습니다.", requestedName)
            : Loc.F("'{0}'에 맞는 항목이 여러 개입니다: {1}", requestedName, string.Join(", ", partial.Select(nameSelector)));
        return null;
    }
}
