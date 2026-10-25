using DesktopAudioController.Models;
using DesktopAudioController.Services;

namespace DesktopAudioController.Tests;

public sealed class AutoProfileSwitchEngineTests
{
    private static AppSettings CreateSettings(params AutoProfileRule[] rules)
    {
        return new AppSettings
        {
            AutoProfileSwitchEnabled = true,
            AudioProfiles =
            [
                new AudioProfile { Id = "work", Name = "영상·작업" },
                new AudioProfile { Id = "fps", Name = "혼자 FPS" },
                new AudioProfile { Id = "voice", Name = "보이스챗" }
            ],
            AutoProfileFallbackProfileId = "work",
            AutoProfileRules = [.. rules]
        };
    }

    private static AutoProfileRule Rule(
        string id,
        string profileId,
        AutoProfileRestoreMode restoreMode,
        params string[] processNames)
    {
        return new AutoProfileRule
        {
            Id = id,
            ProfileId = profileId,
            RestoreMode = restoreMode,
            ProcessNames = [.. processNames]
        };
    }

    private static HashSet<string> Running(params string[] names)
    {
        return ProcessNameMatcher.CreateSet(names);
    }

    [Fact]
    public void Evaluate_AppliesRuleWhenProcessStartsAndRestoresPreviousProfileWhenItEnds()
    {
        var settings = CreateSettings(Rule("pubg", "fps", AutoProfileRestoreMode.PreviousProfile, "TslGame.exe"));
        var engine = new AutoProfileSwitchEngine();

        Assert.Null(engine.Evaluate(Running("explorer"), settings, "work"));

        var start = engine.Evaluate(Running("explorer", "TslGame"), settings, "work");
        Assert.NotNull(start);
        Assert.Equal("fps", start.ProfileId);
        Assert.False(start.IsRestore);
        Assert.Contains("TslGame", start.Reason);

        // 같은 규칙이 계속 활성인 동안에는 다시 적용하지 않습니다.
        Assert.Null(engine.Evaluate(Running("explorer", "tslgame"), settings, "fps"));

        var end = engine.Evaluate(Running("explorer"), settings, "fps");
        Assert.NotNull(end);
        Assert.Equal("work", end.ProfileId);
        Assert.True(end.IsRestore);
        Assert.Null(engine.ActiveRuleId);
    }

    [Fact]
    public void Evaluate_HigherRuleWinsAndLowerRuleTakesOverWhenHigherEnds()
    {
        var settings = CreateSettings(
            Rule("discord", "voice", AutoProfileRestoreMode.PreviousProfile, "Discord"),
            Rule("pubg", "fps", AutoProfileRestoreMode.PreviousProfile, "TslGame"));
        var engine = new AutoProfileSwitchEngine();

        Assert.Equal("fps", engine.Evaluate(Running("TslGame"), settings, "work")?.ProfileId);

        var both = engine.Evaluate(Running("TslGame", "Discord"), settings, "fps");
        Assert.Equal("voice", both?.ProfileId);
        Assert.Contains("대신", both?.Reason);

        Assert.Equal("fps", engine.Evaluate(Running("TslGame"), settings, "voice")?.ProfileId);

        // 마지막 규칙이 끝나면 처음 자동 전환 전 프로필로 돌아갑니다.
        Assert.Equal("work", engine.Evaluate(Running(), settings, "fps")?.ProfileId);
    }

    [Fact]
    public void Evaluate_FallbackAndKeepModes()
    {
        var settings = CreateSettings(
            Rule("sc2", "fps", AutoProfileRestoreMode.FallbackProfile, "SC2_x64"),
            Rule("chrome", "voice", AutoProfileRestoreMode.KeepCurrent, "chrome"));
        var engine = new AutoProfileSwitchEngine();

        engine.Evaluate(Running("SC2_x64"), settings, "voice");
        var fallback = engine.Evaluate(Running(), settings, "fps");
        Assert.Equal("work", fallback?.ProfileId);

        engine.Evaluate(Running("chrome"), settings, "work");
        var keep = engine.Evaluate(Running(), settings, "voice");
        Assert.NotNull(keep);
        Assert.Null(keep.ProfileId);
        Assert.Contains("유지", keep.Reason);
    }

    [Fact]
    public void Evaluate_SkipsDisabledRulesAndRulesWithDeletedProfiles()
    {
        var disabledRule = Rule("disabled", "fps", AutoProfileRestoreMode.PreviousProfile, "notepad");
        disabledRule.IsEnabled = false;
        var settings = CreateSettings(
            disabledRule,
            Rule("missing", "deleted-profile", AutoProfileRestoreMode.PreviousProfile, "notepad"),
            Rule("ok", "voice", AutoProfileRestoreMode.PreviousProfile, "notepad"));
        var engine = new AutoProfileSwitchEngine();

        var decision = engine.Evaluate(Running("notepad"), settings, null);

        Assert.Equal("voice", decision?.ProfileId);
        Assert.Equal("ok", engine.ActiveRuleId);
    }

    [Fact]
    public void Evaluate_DoesNotRestoreWhenThereWasNoPreviousProfile()
    {
        var settings = CreateSettings(Rule("pubg", "fps", AutoProfileRestoreMode.PreviousProfile, "TslGame"));
        var engine = new AutoProfileSwitchEngine();

        engine.Evaluate(Running("TslGame"), settings, null);
        var end = engine.Evaluate(Running(), settings, "fps");

        Assert.NotNull(end);
        Assert.Null(end.ProfileId);
    }

    [Fact]
    public void Evaluate_WhenRuleProfileWasAlreadyCurrent_RestoresFallbackProfile()
    {
        var settings = CreateSettings(Rule("pubg", "fps", AutoProfileRestoreMode.PreviousProfile, "TslGame"));
        var engine = new AutoProfileSwitchEngine();

        engine.Evaluate(Running("TslGame"), settings, "fps");

        Assert.Equal("work", engine.Evaluate(Running(), settings, "fps")?.ProfileId);
    }

    [Fact]
    public void Reset_ReappliesActiveRuleOnNextEvaluation()
    {
        var settings = CreateSettings(Rule("pubg", "fps", AutoProfileRestoreMode.PreviousProfile, "TslGame"));
        var engine = new AutoProfileSwitchEngine();
        engine.Evaluate(Running("TslGame"), settings, "work");

        engine.Reset();

        Assert.Equal("fps", engine.Evaluate(Running("TslGame"), settings, "fps")?.ProfileId);
    }

    [Theory]
    [InlineData("TslGame.exe", "TslGame")]
    [InlineData("  \"C:\\Games\\PUBG\\TslGame.EXE\" ", "TslGame")]
    [InlineData("chrome", "chrome")]
    [InlineData("/usr/bin/app.exe", "app")]
    [InlineData("   ", "")]
    public void ProcessNameMatcher_Normalize(string input, string expected)
    {
        Assert.Equal(expected, ProcessNameMatcher.Normalize(input));
    }

    [Fact]
    public void ProcessNameMatcher_SplitList_RemovesDuplicatesAndExtensions()
    {
        Assert.Equal(["TslGame", "SC2_x64"], ProcessNameMatcher.SplitList("TslGame.exe, SC2_x64.exe; tslgame"));
    }

    [Theory]
    [InlineData("Ctrl+Alt+1", HotkeyGesture.ModControl | HotkeyGesture.ModAlt, 0x31u, "Ctrl+Alt+1")]
    [InlineData("alt + ctrl + f", HotkeyGesture.ModControl | HotkeyGesture.ModAlt, 0x46u, "Ctrl+Alt+F")]
    [InlineData("Shift+Win+F5", HotkeyGesture.ModShift | HotkeyGesture.ModWin, 0x74u, "Shift+Win+F5")]
    [InlineData("Ctrl+NumPad3", HotkeyGesture.ModControl, 0x63u, "Ctrl+Num3")]
    [InlineData("F13", 0u, 0x7Cu, "F13")]
    [InlineData("Ctrl+PageUp", HotkeyGesture.ModControl, 0x21u, "Ctrl+PageUp")]
    public void HotkeyGesture_ParsesSupportedKeys(string text, uint modifiers, uint virtualKey, string displayText)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture, out var error), error);
        Assert.NotNull(gesture);
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(virtualKey, gesture.VirtualKey);
        Assert.Equal(displayText, gesture.DisplayText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("F5")]
    [InlineData("Ctrl+Hyper+1")]
    [InlineData("Ctrl+Enter")]
    [InlineData("Ctrl+F25")]
    public void HotkeyGesture_RejectsUnsupportedOrUnsafeKeys(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Validator_ReportsMissingProcessProfileFallbackAndDuplicateHotkeys()
    {
        var settings = CreateSettings(
            Rule("empty", "fps", AutoProfileRestoreMode.PreviousProfile),
            Rule("no-profile", "", AutoProfileRestoreMode.PreviousProfile, "notepad"),
            Rule("fallback", "fps", AutoProfileRestoreMode.FallbackProfile, "notepad"));
        settings.AutoProfileFallbackProfileId = string.Empty;
        settings.AudioProfiles[0].Hotkey = "Ctrl+Alt+1";
        settings.AudioProfiles[1].Hotkey = "alt+ctrl+1";
        settings.AudioProfiles[2].Hotkey = "Q";

        var problems = AutoProfileSettingsValidator.Validate(settings);

        Assert.Equal(5, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("프로그램 이름"));
        Assert.Contains(problems, problem => problem.Contains("프로필을 골라"));
        Assert.Contains(problems, problem => problem.Contains("기본 프로필"));
        Assert.Contains(problems, problem => problem.Contains("겹칩니다"));
        Assert.Contains(problems, problem => problem.Contains("보이스챗"));
    }

    [Fact]
    public void Validator_AcceptsValidSettings()
    {
        var settings = CreateSettings(Rule("pubg", "fps", AutoProfileRestoreMode.FallbackProfile, "TslGame"));
        settings.AudioProfiles[1].Hotkey = "Ctrl+Alt+2";

        Assert.Empty(AutoProfileSettingsValidator.Validate(settings));
    }

    [Fact]
    public void ProcessOutputCommand_RoundTripsWindowsDefaultTarget()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("app.exe");
        AudioProcessOutputChangeCommand.Apply(startInfo, "TslGame", string.Empty, "result.json");

        Assert.True(AudioProcessOutputChangeCommand.TryParse([.. startInfo.ArgumentList], out var command));
        Assert.Equal("TslGame", command.ProcessName);
        Assert.Equal(string.Empty, command.TargetDeviceId);
        Assert.Equal("result.json", command.ResultOutputPath);
        Assert.False(AudioProcessOutputChangeCommand.TryParse(["--change-session-output", "--target-device-id", "x"], out _));
    }
}
