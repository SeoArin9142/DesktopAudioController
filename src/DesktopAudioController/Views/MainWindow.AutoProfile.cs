using System.Windows;
using System.Windows.Media;
using DesktopAudioController.Models;
using DesktopAudioController.Services;

namespace DesktopAudioController.Views;

/// <summary>
/// 프로필 적용(기본 장치·앱별 출력 포함), 실행 프로그램 기준 자동 전환, 전역 단축키를 담당합니다.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// 프로필 적용을 누가 요청했는지 나타냅니다. 알림·오류 표시 방식이 달라집니다.
    /// </summary>
    private enum ProfileApplySource
    {
        MainWindow,
        Tray,
        Command,
        SettingsWindow,
        AutoRule,
        Hotkey
    }

    private sealed record ProfileApplyResult(bool Success, string ProfileName, IReadOnlyList<string> Warnings, string? ErrorMessage);

    // 실행 중 프로그램 이름을 주기적으로 읽어 자동 전환 판단에 넘깁니다.
    private readonly ProcessWatchService _processWatchService = new();

    // 어떤 규칙이 지금 프로필을 잡고 있는지 기억하는 판단 로직입니다.
    private readonly AutoProfileSwitchEngine _autoProfileEngine = new();

    // 프로필 전역 단축키입니다. 메시지 전용 창을 만들 수 없는 환경이면 null 로 두고 기능만 끕니다.
    private GlobalHotkeyService? _globalHotkeyService;

    // 매 폴링마다 settings.json 을 읽지 않도록 자동 전환에 필요한 설정을 메모리에 둡니다.
    private AppSettings _autoProfileSettings = new();

    // 규칙 구성이 바뀌었는지 비교하기 위한 서명입니다.
    private string _autoProfileRuleSignature = string.Empty;

    // 트레이에서 고른 일시정지 상태입니다. 앱을 다시 켜면 풀립니다.
    private bool _isAutoProfilePaused;

    // 이 앱이 마지막으로 적용한 프로필 ID입니다. 볼륨을 조금 바꿔도 "현재 프로필"이 사라지지 않게 따로 기억합니다.
    private string? _currentProfileId;

    // 자동 전환으로 적용 중인 프로필을 만든 규칙 이름입니다. 트레이 표시에 씁니다.
    private string? _activeAutoRuleName;

    // 직전 폴링의 실행 중 프로그램 목록입니다. 새로 켜진 프로그램을 찾는 데 씁니다.
    private IReadOnlySet<string>? _previousRunningProcessNames;

    // 메인 화면 프로필 버튼을 매번 다시 만들지 않도록 비교하는 서명입니다.
    private string? _mainProfileBarSignature;

    // 프로필 적용이 겹치지 않도록 막는 플래그입니다.
    private bool _isProfileApplying;

    // 설정 창 때문에 자동 전환을 미뤘다는 로그를 한 번만 남기기 위한 플래그입니다.
    private bool _hasLoggedAutoProfileDeferral;

    // 명령으로 처음 실행됐을 때 창을 띄우지 않고 트레이로 시작할지 여부입니다.
    private bool _startHiddenForCommand;

    // 창이 준비되기 전에 받은 명령입니다.
    private readonly Queue<ProfileCommand> _queuedProfileCommands = new();

    /// <summary>
    /// 명령 인수로 처음 실행된 경우 Show 전에 호출해 창을 트레이에 숨긴 채 시작하게 합니다.
    /// </summary>
    public void RequestStartHiddenForCommand()
    {
        _startHiddenForCommand = true;
    }

    /// <summary>
    /// 명령줄·파이프로 받은 프로필 적용/음소거 명령을 실행합니다. 창이 아직 준비 전이면 준비된 뒤 실행합니다.
    /// </summary>
    public void QueueProfileCommand(ProfileCommand command)
    {
        _queuedProfileCommands.Enqueue(command);
        if (IsLoaded)
        {
            RunQueuedProfileCommands();
        }
    }

    private void RunQueuedProfileCommands()
    {
        while (_queuedProfileCommands.Count > 0)
        {
            var command = _queuedProfileCommands.Dequeue();
            _ = ExecuteProfileCommandAsync(command);
        }
    }

    private async Task ExecuteProfileCommandAsync(ProfileCommand command)
    {
        AppLog.Info("MainWindow", $"명령 실행 kind={command.Kind} value={command.Value}");
        if (IsSettingsWindowOpenOrOpening())
        {
            ShowTrayNotification("명령 보류", "설정 창이 열려 있어 명령을 실행하지 않았습니다. 설정 창을 닫은 뒤 다시 눌러 주세요.");
            return;
        }

        // 버튼을 빠르게 연달아 눌러도 앞 적용이 끝난 뒤 순서대로 처리합니다.
        for (var waited = 0; _isProfileApplying && waited < 100; waited++)
        {
            await Task.Delay(100);
        }

        try
        {
            switch (command.Kind)
            {
                case ProfileCommandKind.ApplyProfileByName:
                case ProfileCommandKind.ApplyProfileByIndex:
                    var profiles = GetProfilesInDisplayOrder(_settingsService.Load());
                    var profile = FindProfileForCommand(profiles, command, out var error);
                    if (profile is null)
                    {
                        AppLog.Warn("MainWindow", $"명령 프로필 찾기 실패 value={command.Value} reason={error}");
                        ShowTrayNotification("프로필 명령 실패", error ?? "프로필을 찾지 못했습니다.");
                        return;
                    }

                    var result = await ApplyAudioProfileCoreAsync(profile.Id, ProfileApplySource.Command, "명령");
                    if (!result.Success)
                    {
                        ShowTrayNotification("프로필 적용 실패", result.ErrorMessage ?? "알 수 없는 오류");
                    }

                    return;

                case ProfileCommandKind.ToggleDeviceMute:
                    await ToggleDeviceMuteForCommandAsync(command.Value);
                    return;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("MainWindow", $"명령 실행 실패 kind={command.Kind}", exception);
            ShowTrayNotification("명령 실패", exception.Message);
        }
    }

    private static AudioProfile? FindProfileForCommand(IReadOnlyList<AudioProfile> profiles, ProfileCommand command, out string? error)
    {
        if (profiles.Count == 0)
        {
            error = "저장한 프로필이 없습니다.";
            return null;
        }

        if (command.Kind == ProfileCommandKind.ApplyProfileByIndex)
        {
            if (int.TryParse(command.Value, out var index) && index >= 1 && index <= profiles.Count)
            {
                error = null;
                return profiles[index - 1];
            }

            error = $"프로필 번호 '{command.Value}'가 없습니다. 1~{profiles.Count} 사이로 지정해 주세요.";
            return null;
        }

        return ProfileCommandLine.FindByName(profiles, profile => profile.Name, command.Value, out error);
    }

    private async Task ToggleDeviceMuteForCommandAsync(string deviceName)
    {
        var devices = await _viewModel.GetAvailableOutputDevicesForSessionRoutingAsync();
        var device = ProfileCommandLine.FindByName(devices.ToList(), item => item.Name, deviceName, out var error);
        if (device is null)
        {
            AppLog.Warn("MainWindow", $"명령 음소거 장치 찾기 실패 value={deviceName} reason={error}");
            ShowTrayNotification("음소거 명령 실패", error ?? "장치를 찾지 못했습니다.");
            return;
        }

        var muted = !device.IsMuted;
        AppLog.Info("MainWindow", $"명령 장치 음소거 deviceId={device.Id} name={device.Name} muted={muted}");
        await _viewModel.SetOutputDeviceMutedAsync(device.Id, muted);
        await RefreshStateViewAsync("command_toggle_mute");
        if (_autoProfileSettings.ShowAutoProfileNotifications)
        {
            ShowTrayNotification("장치 음소거", $"{device.Name}: {(muted ? "음소거" : "음소거 해제")}");
        }
    }

    /// <summary>
    /// 메인 화면 버튼·트레이·--profile-index 가 같은 순서를 쓰도록 이름순으로 정렬합니다.
    /// </summary>
    private static List<AudioProfile> GetProfilesInDisplayOrder(AppSettings settings)
    {
        return settings.AudioProfiles
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void InitializeAutoProfileFeatures()
    {
        _processWatchService.SnapshotTaken += ProcessWatchService_OnSnapshotTaken;
        try
        {
            _globalHotkeyService = new GlobalHotkeyService();
            _globalHotkeyService.ProfileHotkeyPressed += GlobalHotkeyService_OnProfileHotkeyPressed;
        }
        catch (Exception exception)
        {
            AppLog.Warn("MainWindow", "전역 단축키 창 생성 실패, 단축키 기능 비활성화", exception);
            _globalHotkeyService = null;
        }

        var settings = LoadSettingsForTrayMenu();
        _currentProfileId = AudioProfileStore.FindAppliedProfileId(settings) ?? NullIfEmpty(settings.LastAppliedAudioProfileId);
        var hotkeyFailures = ReloadAutoProfileConfiguration(settings, "startup");
        if (hotkeyFailures.Count > 0)
        {
            ShowTrayNotification("단축키 등록 실패", string.Join("\n", hotkeyFailures));
        }
    }

    private void DisposeAutoProfileFeatures()
    {
        _processWatchService.SnapshotTaken -= ProcessWatchService_OnSnapshotTaken;
        _processWatchService.Dispose();
        if (_globalHotkeyService is not null)
        {
            _globalHotkeyService.ProfileHotkeyPressed -= GlobalHotkeyService_OnProfileHotkeyPressed;
            _globalHotkeyService.Dispose();
            _globalHotkeyService = null;
        }
    }

    /// <summary>
    /// 설정이 바뀐 뒤 감시 시작/중지, 규칙 상태 초기화, 단축키 재등록을 합니다. 등록 못 한 단축키 안내를 반환합니다.
    /// </summary>
    private IReadOnlyList<string> ReloadAutoProfileConfiguration(AppSettings settings, string reason)
    {
        _autoProfileSettings = settings;
        var ruleSignature = BuildAutoProfileRuleSignature(settings);
        if (!string.Equals(ruleSignature, _autoProfileRuleSignature, StringComparison.Ordinal))
        {
            // 규칙이 바뀌면 이전 판단을 버리고 다음 폴링에서 처음부터 다시 판단합니다.
            _autoProfileRuleSignature = ruleSignature;
            _autoProfileEngine.Reset();
            _activeAutoRuleName = null;
            _previousRunningProcessNames = null;
        }

        UpdateProcessWatchState();
        var hotkeyFailures = RegisterProfileHotkeys(settings);
        AppLog.Info(
            "MainWindow",
            $"자동 프로필 설정 반영 reason={reason} enabled={settings.AutoProfileSwitchEnabled} paused={_isAutoProfilePaused} rules={settings.AutoProfileRules.Count} hotkeysEnabled={settings.EnableProfileHotkeys} hotkeyFailures={hotkeyFailures.Count} watching={_processWatchService.IsRunning}");
        _trayMenuService.InvalidateMenu();
        RefreshTrayMenu(force: true);
        return hotkeyFailures;
    }

    private void UpdateProcessWatchState()
    {
        var shouldWatch = _autoProfileSettings.AutoProfileSwitchEnabled &&
            !_isAutoProfilePaused &&
            (_autoProfileSettings.AutoProfileRules.Any(rule => rule.IsEnabled) ||
             _autoProfileSettings.AudioProfiles.Any(profile => profile.ProgramOutputRoutes.Count > 0));
        if (shouldWatch)
        {
            _processWatchService.Start();
            return;
        }

        _processWatchService.Stop();
        _previousRunningProcessNames = null;
    }

    private IReadOnlyList<string> RegisterProfileHotkeys(AppSettings settings)
    {
        if (_globalHotkeyService is null)
        {
            return settings.EnableProfileHotkeys
                ? ["전역 단축키를 초기화하지 못해 단축키를 쓸 수 없습니다. 로그를 확인해 주세요."]
                : [];
        }

        if (!settings.EnableProfileHotkeys)
        {
            _globalHotkeyService.UnregisterAll();
            return [];
        }

        return _globalHotkeyService.RegisterAll(settings.AudioProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Hotkey))
            .Select(profile => new GlobalHotkeyService.Registration(profile.Id, profile.Name, profile.Hotkey)));
    }

    private void ToggleAutoProfilePause()
    {
        _isAutoProfilePaused = !_isAutoProfilePaused;
        _autoProfileEngine.Reset();
        _activeAutoRuleName = null;
        _previousRunningProcessNames = null;
        AppLog.Info("MainWindow", $"자동 프로필 전환 {(_isAutoProfilePaused ? "일시정지" : "재개")}");
        UpdateProcessWatchState();
        _trayMenuService.InvalidateMenu();
        RefreshTrayMenu(force: true);
        ShowTrayNotification(
            "자동 프로필 전환",
            _isAutoProfilePaused
                ? "자동 전환을 일시정지했습니다. 트레이 메뉴에서 다시 켤 수 있습니다."
                : "자동 전환을 다시 시작했습니다.");
    }

    private TrayMenuService.AutoProfileMenu BuildAutoProfileTrayMenu(AppSettings settings)
    {
        var currentProfile = settings.AudioProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, _currentProfileId, StringComparison.Ordinal));
        var currentProfileText = currentProfile is null
            ? "현재 프로필: 없음"
            : $"현재 프로필: {currentProfile.Name}";
        if (currentProfile is not null && _activeAutoRuleName is not null)
        {
            currentProfileText += $" (자동: {_activeAutoRuleName})";
        }
        else if (settings.AutoProfileSwitchEnabled && _isAutoProfilePaused)
        {
            currentProfileText += " (자동 전환 일시정지)";
        }

        return new TrayMenuService.AutoProfileMenu(
            currentProfile?.Id,
            currentProfile?.Name,
            currentProfileText,
            settings.AutoProfileSwitchEnabled,
            _isAutoProfilePaused,
            () => RunOnUiThread(ToggleAutoProfilePause));
    }

    /// <summary>
    /// 메인 화면 상단 프로필 버튼 줄을 현재 프로필·자동 전환 상태에 맞춰 다시 그립니다.
    /// </summary>
    private void RefreshMainProfileBar(AppSettings settings)
    {
        var autoProfileMenu = BuildAutoProfileTrayMenu(settings);
        ProfileStatusText.Text = autoProfileMenu.CurrentProfileText;
        ProfileStatusText.ToolTip = autoProfileMenu.CurrentProfileText;
        AutoProfilePauseButton.Visibility = settings.AutoProfileSwitchEnabled ? Visibility.Visible : Visibility.Collapsed;
        AutoProfilePauseButton.Content = _isAutoProfilePaused ? "자동 전환 다시 켜기" : "자동 전환 일시정지";

        var profiles = settings.AudioProfiles
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var signature = $"{_currentProfileId}\n{string.Join("\n", profiles.Select(profile => $"{profile.Id}|{profile.Name}|{profile.Hotkey}"))}";
        if (string.Equals(signature, _mainProfileBarSignature, StringComparison.Ordinal))
        {
            return;
        }

        _mainProfileBarSignature = signature;
        ProfileButtonsPanel.Children.Clear();
        ProfileEmptyText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            var profileId = profile.Id;
            var isCurrent = string.Equals(profile.Id, _currentProfileId, StringComparison.Ordinal);
            var button = new System.Windows.Controls.Button
            {
                Content = $"{index + 1}. {profile.Name}",
                MinWidth = 96,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 4),
                FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                BorderBrush = isCurrent
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA)),
                Background = isCurrent
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0xF1, 0xFB))
                    : System.Windows.SystemColors.ControlBrush,
                ToolTip = string.IsNullOrWhiteSpace(profile.Hotkey)
                    ? $"{profile.Name} 프로필 적용"
                    : $"{profile.Name} 프로필 적용 ({profile.Hotkey})"
            };
            button.Click += (_, _) => _ = ApplyAudioProfileFromMainWindowAsync(profileId);
            ProfileButtonsPanel.Children.Add(button);
        }
    }

    private async Task ApplyAudioProfileFromMainWindowAsync(string profileId)
    {
        var result = await ApplyAudioProfileCoreAsync(profileId, ProfileApplySource.MainWindow, "메인 화면");
        if (!result.Success)
        {
            System.Windows.MessageBox.Show(
                this,
                $"프로필을 적용하지 못했습니다.\n\n원인: {result.ErrorMessage}",
                "프로필 적용 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AutoProfilePauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleAutoProfilePause();
    }

    private void ProcessWatchService_OnSnapshotTaken(IReadOnlySet<string> runningProcessNames)
    {
        RunOnUiThread(() => _ = HandleProcessSnapshotAsync(runningProcessNames));
    }

    private async Task HandleProcessSnapshotAsync(IReadOnlySet<string> runningProcessNames)
    {
        if (!_autoProfileSettings.AutoProfileSwitchEnabled || _isAutoProfilePaused || _isProfileApplying)
        {
            return;
        }

        // 설정 창은 저장 전 상태를 들고 있으므로, 열려 있는 동안 자동 적용하면 저장 때 서로 덮어씁니다. 닫힌 뒤 다시 판단합니다.
        if (IsSettingsWindowOpenOrOpening())
        {
            if (!_hasLoggedAutoProfileDeferral)
            {
                AppLog.Info("MainWindow", "설정 창이 열려 있어 자동 프로필 판단을 미룸");
                _hasLoggedAutoProfileDeferral = true;
            }

            return;
        }

        _hasLoggedAutoProfileDeferral = false;
        var previousRunningProcessNames = _previousRunningProcessNames;
        _previousRunningProcessNames = runningProcessNames;

        var currentProfileId = _currentProfileId ?? AudioProfileStore.FindAppliedProfileId(_autoProfileSettings);
        var decision = _autoProfileEngine.Evaluate(runningProcessNames, _autoProfileSettings, currentProfileId);
        if (decision is not null)
        {
            var activeRule = _autoProfileSettings.AutoProfileRules.FirstOrDefault(rule =>
                string.Equals(rule.Id, _autoProfileEngine.ActiveRuleId, StringComparison.Ordinal));
            _activeAutoRuleName = activeRule is null ? null : AutoProfileSwitchEngine.DescribeRule(activeRule);
            AppLog.Info(
                "MainWindow",
                $"자동 프로필 판단 {decision.Reason} profileId={decision.ProfileId ?? "(변경 없음)"} restore={decision.IsRestore}");
            if (decision.ProfileId is not null)
            {
                await ApplyAudioProfileCoreAsync(decision.ProfileId, ProfileApplySource.AutoRule, decision.Reason);
            }
            else
            {
                RefreshTrayMenu(force: true);
            }

            return;
        }

        if (previousRunningProcessNames is not null)
        {
            await ApplyRoutesForNewProcessesAsync(previousRunningProcessNames, runningProcessNames);
        }
    }

    /// <summary>
    /// 현재 프로필의 앱별 출력 대상 프로그램이 나중에 켜지면(예: 게임 중 디스코드 실행) 그 프로그램에만 출력 지정을 다시 적용합니다.
    /// </summary>
    private async Task ApplyRoutesForNewProcessesAsync(
        IReadOnlySet<string> previousRunningProcessNames,
        IReadOnlySet<string> runningProcessNames)
    {
        var currentProfile = _autoProfileSettings.AudioProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, _currentProfileId, StringComparison.Ordinal));
        if (currentProfile is null || currentProfile.ProgramOutputRoutes.Count == 0)
        {
            return;
        }

        var newRoutes = currentProfile.ProgramOutputRoutes
            .Where(route =>
            {
                var processName = ProcessNameMatcher.Normalize(route.ProcessName);
                return runningProcessNames.Contains(processName) && !previousRunningProcessNames.Contains(processName);
            })
            .ToList();
        if (newRoutes.Count == 0)
        {
            return;
        }

        _isProfileApplying = true;
        try
        {
            AppLog.Info(
                "MainWindow",
                $"새로 실행된 프로그램에 프로필 앱별 출력 적용 profileId={currentProfile.Id} name={currentProfile.Name} processes=[{string.Join(", ", newRoutes.Select(route => route.ProcessName))}]");
            var outputDevices = await _viewModel.GetAvailableOutputDevicesForSessionRoutingAsync();
            var warnings = await ApplyProgramOutputRoutesAsync(newRoutes, currentProfile, outputDevices);
            if (warnings.Count > 0 && _autoProfileSettings.ShowAutoProfileNotifications)
            {
                ShowTrayNotification("앱별 출력 적용 일부 실패", string.Join("\n", warnings));
            }
        }
        finally
        {
            _isProfileApplying = false;
        }
    }

    private void GlobalHotkeyService_OnProfileHotkeyPressed(string profileId)
    {
        AppLog.Info("MainWindow", $"프로필 단축키 입력 profileId={profileId}");
        if (IsSettingsWindowOpenOrOpening())
        {
            ShowTrayNotification("프로필 적용 보류", "설정 창이 열려 있어 단축키 프로필을 적용하지 않았습니다.");
            return;
        }

        _ = ApplyAudioProfileCoreAsync(profileId, ProfileApplySource.Hotkey, "단축키");
    }

    private bool IsSettingsWindowOpenOrOpening()
    {
        lock (_settingsWindowSyncRoot)
        {
            return _activeSettingsWindow is not null || _isSettingsWindowOpening;
        }
    }

    /// <summary>
    /// 프로필 저장값(표시 장치·볼륨)을 반영하고, 기본 출력 장치와 앱별 출력까지 적용합니다.
    /// </summary>
    private async Task<ProfileApplyResult> ApplyAudioProfileCoreAsync(string profileId, ProfileApplySource source, string reason)
    {
        if (_isProfileApplying)
        {
            AppLog.Info("MainWindow", $"프로필 적용 요청 무시: 다른 적용 진행 중 profileId={profileId} source={source}");
            return new ProfileApplyResult(false, string.Empty, [], "다른 프로필을 적용하는 중입니다. 잠시 후 다시 시도해 주세요.");
        }

        _isProfileApplying = true;
        try
        {
            _viewModel.FlushPendingProgramPreferenceSave();
            var currentSettings = _settingsService.Load();
            var profile = currentSettings.AudioProfiles.FirstOrDefault(item =>
                string.Equals(item.Id, profileId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("선택한 프로필을 설정 파일에서 찾지 못했습니다.");

            AppLog.Info(
                "MainWindow",
                $"프로필 적용 시작 source={source} reason={reason} profileId={profile.Id} name={profile.Name} visibleDevices={profile.VisibleDeviceIds.Count} programPreferences={profile.ProgramAudioPreferences.Count} defaultDevice={!string.IsNullOrWhiteSpace(profile.DefaultOutputDeviceId)} outputRoutes={profile.ProgramOutputRoutes.Count}");

            var warnings = new List<string>();
            var settingsAlreadyApplied = source == ProfileApplySource.SettingsWindow ||
                string.Equals(AudioProfileStore.FindAppliedProfileId(currentSettings), profile.Id, StringComparison.Ordinal);
            if (!settingsAlreadyApplied)
            {
                var appliedSettings = AudioProfileStore.ApplyProfile(currentSettings, profile);
                _settingsService.Save(appliedSettings);
                _minimizeToTray = appliedSettings.MinimizeToTray;
                _includePreReleaseUpdates = appliedSettings.IncludePreReleaseUpdates;
                _autoProfileSettings = appliedSettings;
                _trayMenuService.InvalidateMenu();
                if (!await ReloadViewModelAsync($"profile_applied source={source}"))
                {
                    warnings.Add("화면 새로고침에 실패했습니다. 장치 다시 읽기를 실행해 주세요.");
                }
            }

            var outputDevices = await _viewModel.GetAvailableOutputDevicesForSessionRoutingAsync();
            ApplyDefaultOutputDevice(profile, outputDevices, warnings);
            await ApplyDefaultInputDeviceAsync(profile, warnings);
            await ApplyDeviceLevelsAsync(profile, outputDevices, warnings);
            warnings.AddRange(await ApplyProgramOutputRoutesAsync(profile.ProgramOutputRoutes, profile, outputDevices));

            // 프로필의 프로그램별 볼륨을 패널을 열지 않아도 바로 맞춥니다. 저장값이 바뀌었으니 이미 맞춘 세션도 다시 봅니다.
            _viewModel.ResetStoredPreferenceEnforcement();
            _ = EnforceStoredProgramPreferencesSoonAsync($"profile_applied source={source}");

            _currentProfileId = profile.Id;
            if (source is not ProfileApplySource.AutoRule)
            {
                // 사용자가 직접 고른 프로필은 자동 규칙 표시를 지웁니다. 규칙 상태는 유지해 같은 게임 중에 다시 덮어쓰지 않습니다.
                _activeAutoRuleName = null;
            }

            RefreshTrayMenu(force: true);
            AppLog.Info("MainWindow", $"프로필 적용 완료 source={source} profileId={profile.Id} warnings={warnings.Count}");
            NotifyProfileApplied(profile.Name, source, reason, warnings);
            return new ProfileApplyResult(true, profile.Name, warnings, null);
        }
        catch (Exception exception)
        {
            AppLog.Error("MainWindow", $"프로필 적용 실패 source={source} profileId={profileId}", exception);
            if (source is ProfileApplySource.AutoRule or ProfileApplySource.Hotkey or ProfileApplySource.Command)
            {
                ShowTrayNotification("프로필 적용 실패", exception.Message);
            }

            return new ProfileApplyResult(false, string.Empty, [], exception.Message);
        }
        finally
        {
            _isProfileApplying = false;
        }
    }

    private void NotifyProfileApplied(string profileName, ProfileApplySource source, string reason, IReadOnlyList<string> warnings)
    {
        var isAutomatic = source is ProfileApplySource.AutoRule or ProfileApplySource.Hotkey or ProfileApplySource.Command;
        if (isAutomatic && !_autoProfileSettings.ShowAutoProfileNotifications && warnings.Count == 0)
        {
            return;
        }

        if (source is ProfileApplySource.SettingsWindow or ProfileApplySource.MainWindow && warnings.Count == 0)
        {
            return;
        }

        var title = source switch
        {
            ProfileApplySource.AutoRule => "자동 프로필 전환",
            ProfileApplySource.Hotkey => "단축키 프로필 적용",
            ProfileApplySource.Command => "명령 프로필 적용",
            _ => "프로필 적용"
        };
        var text = source == ProfileApplySource.AutoRule
            ? $"{profileName} - {reason}"
            : $"{profileName} 프로필을 적용했습니다.";
        if (warnings.Count > 0)
        {
            text += "\n" + string.Join("\n", warnings);
        }

        ShowTrayNotification(title, text);
    }

    /// <summary>
    /// 기본 출력 장치(기본 장치·기본 통신 장치 모두)를 바꿉니다. 없으면 대체 장치 순서대로 고릅니다.
    /// </summary>
    private void ApplyDefaultOutputDevice(
        AudioProfile profile,
        IReadOnlyList<AudioDeviceInfo> outputDevices,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(profile.DefaultOutputDeviceId))
        {
            return;
        }

        try
        {
            var choice = ProfileDeviceResolver.ChooseFirstAvailable(
                [profile.DefaultOutputDeviceId, .. profile.OutputFallbackDeviceIds],
                outputDevices,
                profile.KnownDeviceNames);
            if (choice.Device is null)
            {
                warnings.Add($"출력 장치 {choice.RequestedName}와 대체 장치가 모두 없어 기본 출력을 바꾸지 않았습니다.");
                AppLog.Warn("MainWindow", $"프로필 기본 출력 장치 없음 deviceId={profile.DefaultOutputDeviceId}");
                return;
            }

            if (choice.UsedFallback)
            {
                warnings.Add($"{choice.RequestedName}가 없어 {choice.Device.Name}로 대신 출력합니다.");
            }

            if (choice.Device.IsDefault)
            {
                return;
            }

            AppLog.Info("MainWindow", $"프로필 기본 출력 장치 변경 deviceId={choice.Device.Id} name={choice.Device.Name} fallback={choice.UsedFallback}");
            _viewModel.SetDefaultOutputDevice(choice.Device.Id);
            PromoteDefaultDeviceLocally(choice.Device.Id);
            _ = RefreshAfterDefaultDeviceChangeAsync(choice.Device.Id);
        }
        catch (Exception exception)
        {
            warnings.Add($"기본 출력 장치를 바꾸지 못했습니다: {exception.Message}");
            AppLog.Warn("MainWindow", $"프로필 기본 출력 장치 변경 실패 deviceId={profile.DefaultOutputDeviceId}", exception);
        }
    }

    /// <summary>
    /// 기본 입력(마이크) 장치를 바꿉니다. 기본 장치·기본 통신 장치를 함께 바꿔 디스코드처럼 통신 장치를 쓰는 앱도 따라옵니다.
    /// </summary>
    private async Task ApplyDefaultInputDeviceAsync(AudioProfile profile, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(profile.DefaultInputDeviceId))
        {
            return;
        }

        try
        {
            var inputDevices = await _viewModel.GetAvailableInputDevicesAsync();
            var choice = ProfileDeviceResolver.ChooseFirstAvailable(
                [profile.DefaultInputDeviceId, .. profile.InputFallbackDeviceIds],
                inputDevices,
                profile.KnownDeviceNames);
            if (choice.Device is null)
            {
                warnings.Add($"마이크 {choice.RequestedName}와 대체 장치가 모두 없어 기본 입력을 바꾸지 않았습니다.");
                AppLog.Warn("MainWindow", $"프로필 기본 입력 장치 없음 deviceId={profile.DefaultInputDeviceId}");
                return;
            }

            if (choice.UsedFallback)
            {
                warnings.Add($"마이크 {choice.RequestedName}가 없어 {choice.Device.Name}를 대신 씁니다.");
            }

            if (choice.Device.IsDefault)
            {
                return;
            }

            AppLog.Info("MainWindow", $"프로필 기본 입력 장치 변경 deviceId={choice.Device.Id} name={choice.Device.Name} fallback={choice.UsedFallback}");
            await Task.Run(() => _viewModel.SetDefaultInputDevice(choice.Device.Id));
        }
        catch (Exception exception)
        {
            warnings.Add($"기본 입력 장치를 바꾸지 못했습니다: {exception.Message}");
            AppLog.Warn("MainWindow", $"프로필 기본 입력 장치 변경 실패 deviceId={profile.DefaultInputDeviceId}", exception);
        }
    }

    /// <summary>
    /// 프로필에 저장한 장치 볼륨·음소거를 맞춥니다. 연결되지 않은 장치는 건너뜁니다.
    /// </summary>
    private async Task ApplyDeviceLevelsAsync(
        AudioProfile profile,
        IReadOnlyList<AudioDeviceInfo> outputDevices,
        List<string> warnings)
    {
        if (profile.DeviceLevels.Count == 0)
        {
            return;
        }

        foreach (var level in profile.DeviceLevels)
        {
            var device = ProfileDeviceResolver.Resolve(level.DeviceId, outputDevices, profile.KnownDeviceNames);
            if (device is null)
            {
                AppLog.Info("MainWindow", $"프로필 장치 볼륨 건너뜀(연결 안 됨) deviceId={level.DeviceId}");
                continue;
            }

            try
            {
                if (level.Volume is { } volume)
                {
                    await _viewModel.SetOutputDeviceVolumeAsync(device.Id, volume);
                }

                if (level.IsMuted is { } muted)
                {
                    await _viewModel.SetOutputDeviceMutedAsync(device.Id, muted);
                }

                AppLog.Info("MainWindow", $"프로필 장치 볼륨 적용 deviceId={device.Id} name={device.Name} volume={level.Volume?.ToString() ?? "유지"} muted={level.IsMuted?.ToString() ?? "유지"}");
            }
            catch (Exception exception)
            {
                warnings.Add($"{device.Name} 볼륨·음소거를 바꾸지 못했습니다: {exception.Message}");
                AppLog.Warn("MainWindow", $"프로필 장치 볼륨 적용 실패 deviceId={device.Id}", exception);
            }
        }

        _ = RefreshStateViewAsync("profile_device_levels");
    }

    /// <summary>
    /// 앱별 출력 지정을 적용합니다. 대상 장치가 없으면 프로필의 대체 출력 장치 순서대로 고르고,
    /// 실행 중이 아닌 프로그램은 건너뜁니다.
    /// </summary>
    private async Task<List<string>> ApplyProgramOutputRoutesAsync(
        IReadOnlyList<ProgramOutputRoute> routes,
        AudioProfile profile,
        IReadOnlyList<AudioDeviceInfo> outputDevices)
    {
        var warnings = new List<string>();
        foreach (var route in routes)
        {
            var processName = ProcessNameMatcher.Normalize(route.ProcessName);
            if (processName.Length == 0)
            {
                continue;
            }

            var targetDeviceId = route.TargetDeviceId;
            if (!string.IsNullOrWhiteSpace(targetDeviceId))
            {
                var choice = ProfileDeviceResolver.ChooseFirstAvailable(
                    [targetDeviceId, .. profile.OutputFallbackDeviceIds],
                    outputDevices,
                    profile.KnownDeviceNames);
                if (choice.Device is null)
                {
                    warnings.Add($"{processName}: {choice.RequestedName}와 대체 장치가 없어 Windows 기본 장치를 따릅니다.");
                    targetDeviceId = string.Empty;
                }
                else
                {
                    if (choice.UsedFallback)
                    {
                        warnings.Add($"{processName}: {choice.RequestedName}가 없어 {choice.Device.Name}로 보냅니다.");
                    }

                    targetDeviceId = choice.Device.Id;
                }
            }

            try
            {
                var changedCount = await _viewModel.SetProcessOutputDeviceAsync(processName, targetDeviceId);
                AppLog.Info(
                    "MainWindow",
                    changedCount == 0
                        ? $"앱별 출력 건너뜀(실행 중 아님) processName={processName}"
                        : $"앱별 출력 적용 processName={processName} changedProcesses={changedCount} deviceId={(string.IsNullOrWhiteSpace(targetDeviceId) ? "(Windows 기본)" : targetDeviceId)}");
            }
            catch (Exception exception)
            {
                warnings.Add($"{processName} 출력 변경 실패: {exception.Message}");
                AppLog.Warn("MainWindow", $"앱별 출력 적용 실패 processName={processName}", exception);
            }
        }

        return warnings;
    }

    private static string BuildAutoProfileRuleSignature(AppSettings settings)
    {
        return string.Join(
            "\n",
            settings.AutoProfileRules.Select(rule =>
                $"{rule.Id}|{rule.IsEnabled}|{rule.ProfileId}|{rule.RestoreMode}|{string.Join(",", rule.ProcessNames)}"))
            + $"\nenabled:{settings.AutoProfileSwitchEnabled}\nfallback:{settings.AutoProfileFallbackProfileId}";
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
