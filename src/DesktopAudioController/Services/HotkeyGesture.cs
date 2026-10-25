using DesktopAudioController.Localization;

namespace DesktopAudioController.Services;

/// <summary>
/// "Ctrl+Alt+1" 같은 단축키 문자열을 RegisterHotKey 인자로 바꿉니다.
/// </summary>
internal sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string DisplayText)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["Pause"] = 0x13
    };

    public static bool TryParse(string? text, out HotkeyGesture? gesture, out string error)
    {
        gesture = null;
        error = string.Empty;
        var parts = (text ?? string.Empty)
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = Loc.T("단축키가 비어 있습니다.");
            return false;
        }

        uint modifiers = 0;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var modifier = ParseModifier(parts[index]);
            if (modifier == 0)
            {
                error = Loc.F("'{0}'은(는) 알 수 없는 보조 키입니다. Ctrl, Alt, Shift, Win 중에서 고르세요.", parts[index]);
                return false;
            }

            modifiers |= modifier;
        }

        if (!TryParseKey(parts[^1], out var virtualKey, out var keyName))
        {
            error = Loc.F("'{0}'은(는) 쓸 수 없는 키입니다. A~Z, 0~9, F1~F24, Num0~Num9, 방향키 등을 쓰세요.", parts[^1]);
            return false;
        }

        // F13~F24 는 일반 키보드에 없어 매크로 키로 쓰는 경우가 많으므로 보조 키 없이도 허용합니다.
        var isHighFunctionKey = virtualKey is >= 0x7C and <= 0x87;
        if (modifiers == 0 && !isHighFunctionKey)
        {
            error = Loc.T("다른 프로그램 입력을 막지 않도록 Ctrl, Alt, Shift, Win 중 하나 이상을 함께 눌러야 합니다.");
            return false;
        }

        gesture = new HotkeyGesture(modifiers, virtualKey, BuildDisplayText(modifiers, keyName));
        return true;
    }

    private static uint ParseModifier(string text)
    {
        return text.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => ModControl,
            "ALT" => ModAlt,
            "SHIFT" => ModShift,
            "WIN" or "WINDOWS" => ModWin,
            _ => 0
        };
    }

    private static bool TryParseKey(string text, out uint virtualKey, out string keyName)
    {
        virtualKey = 0;
        keyName = text;
        if (text.Length == 1 && char.IsAsciiLetterOrDigit(text[0]))
        {
            var character = char.ToUpperInvariant(text[0]);
            virtualKey = character;
            keyName = character.ToString();
            return true;
        }

        if (text.Length > 1 &&
            (text[0] == 'F' || text[0] == 'f') &&
            int.TryParse(text[1..], out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionNumber - 1);
            keyName = $"F{functionNumber}";
            return true;
        }

        foreach (var prefix in new[] { "NumPad", "Num" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(text[prefix.Length..], out var numberPadDigit) &&
                numberPadDigit is >= 0 and <= 9)
            {
                virtualKey = (uint)(0x60 + numberPadDigit);
                keyName = $"Num{numberPadDigit}";
                return true;
            }
        }

        foreach (var namedKey in NamedKeys)
        {
            if (string.Equals(namedKey.Key, text, StringComparison.OrdinalIgnoreCase))
            {
                virtualKey = namedKey.Value;
                keyName = namedKey.Key;
                return true;
            }
        }

        return false;
    }

    private static string BuildDisplayText(uint modifiers, string keyName)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModWin) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(keyName);
        return string.Join("+", parts);
    }
}
