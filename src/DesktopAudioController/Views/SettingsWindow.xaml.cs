using System.Diagnostics;
using System.IO;
using System.Windows;
using DesktopAudioController.Services;
using DesktopAudioController.ViewModels;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Views;

/// <summary>
/// 표시할 장치와 표시 옵션을 선택하는 설정 창입니다.
/// </summary>
public partial class SettingsWindow : Window
{
    private const double SettingsWindowScreenMargin = 16;

    // 설정 창에 바인딩되는 뷰모델입니다.
    private readonly SettingsViewModel _viewModel;

    // 프로필 생성/삭제처럼 즉시 저장된 변경이 있어 메인 화면 reload가 필요한지 여부입니다.
    private bool _hasPersistedSettingsChange;

    /// <summary>
    /// 설정 창을 초기화하고 뷰모델을 연결합니다.
    /// </summary>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        ApplyScreenBounds();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Closing += SettingsWindow_OnClosing;
    }

    private void ApplyScreenBounds()
    {
        var workArea = SystemParameters.WorkArea;
        var maxHeight = Math.Max(MinHeight, workArea.Height - SettingsWindowScreenMargin);

        MaxHeight = maxHeight;
        if (Height > maxHeight)
        {
            Height = maxHeight;
        }
    }

    /// <summary>
    /// 저장 버튼 클릭 시 설정을 저장하고 대화상자를 성공 상태로 닫습니다.
    /// </summary>
    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        AppLog.Info("SettingsWindow", "설정 저장 시도");
        try
        {
            _viewModel.Save();
            AppLog.Info("SettingsWindow", "설정 저장 성공");
            if (_viewModel.RequiresRestartToEnableDebugLogs)
            {
                AppLog.Info("SettingsWindow", "디버그 로그 활성화로 앱 재시작 요청");
                if (System.Windows.Application.Current is App app && app.TryScheduleRestartForDebugLogging())
                {
                    return;
                }

                AppLog.Warn("SettingsWindow", "디버그 로그 활성화 재시작 예약 실패");
                System.Windows.MessageBox.Show(
                    this,
                    Loc.T("디버그 로그 설정은 저장됐지만 자동 재시작을 예약하지 못했습니다.\n처음부터 디버그 로그를 기록하려면 앱을 직접 다시 시작해 주세요."),
                    Loc.T("디버그 로그 재시작 실패"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            DialogResult = true;
            Close();
        }
        catch (SettingsValidationException exception)
        {
            AppLog.Info("SettingsWindow", $"설정 저장 보류: 입력 확인 필요 problems={exception.Problems.Count}");
            System.Windows.MessageBox.Show(
                this,
                Loc.F("저장하기 전에 아래 항목을 확인해 주세요.\n\n- {0}", string.Join("\n- ", exception.Problems)),
                Loc.T("설정 확인 필요"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (SettingsPersistenceException exception)
        {
            AppLog.Error("SettingsWindow", "설정 파일 저장 실패", exception);
            System.Windows.MessageBox.Show(
                this,
                Loc.F("설정을 파일에 저장하지 못했습니다.\n\n저장 경로: {0}\n원인: {1}\n\n경로 접근 권한과 디스크 상태를 확인한 뒤 다시 시도하세요.", exception.SettingsFilePath, exception.InnerException?.Message ?? exception.Message),
                Loc.T("설정 저장 실패"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (StartupRegistrationException exception)
        {
            AppLog.Error("SettingsWindow", "자동 실행 레지스트리 반영 실패", exception);
            System.Windows.MessageBox.Show(
                this,
                Loc.F("Windows 자동 실행 옵션을 적용하지 못했습니다.\n\n레지스트리 경로: {0}\n값 이름: {1}\n원인: {2}\n\n자동 실행을 끈 상태로 먼저 사용한 뒤 나중에 다시 시도해도 됩니다.", exception.RegistryPath, exception.ValueName, exception.InnerException?.Message ?? exception.Message),
                Loc.T("자동 실행 설정 실패"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            AppLog.Error("SettingsWindow", "설정 저장 중 예상하지 못한 오류", exception);
            System.Windows.MessageBox.Show(
                this,
                Loc.F("설정을 저장하는 중 예상하지 못한 오류가 발생했습니다.\n\n원인: {0}\n\n같은 문제가 반복되면 settings.json 경로와 로그를 함께 확인하세요.", exception.Message),
                Loc.T("설정 저장 실패"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 취소 버튼 클릭 시 변경 내용을 버리고 창을 닫습니다.
    /// </summary>
    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        AppLog.Info("SettingsWindow", "설정 저장 취소");
        DialogResult = _hasPersistedSettingsChange;
        Close();
    }

    private void SettingsWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_hasPersistedSettingsChange && DialogResult != true)
        {
            DialogResult = true;
        }
    }

    private void ExportSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("설정 내보내기"),
            Filter = Loc.T("JSON 설정 파일 (*.json)|*.json|모든 파일 (*.*)|*.*"),
            FileName = $"DesktopAudioController-settings-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _viewModel.ExportSettings(dialog.FileName);
            AppLog.Info("SettingsWindow", "설정 내보내기 성공");
            System.Windows.MessageBox.Show(
                this,
                Loc.F("설정을 파일로 내보냈습니다.\n\n경로: {0}", dialog.FileName),
                Loc.T("설정 내보내기 완료"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (SettingsPersistenceException exception)
        {
            ShowSettingsPersistenceFailure(Loc.T("설정 내보내기 실패"), exception);
        }
        catch (Exception exception)
        {
            ShowUnexpectedSettingsFailure(Loc.T("설정 내보내기 실패"), exception);
        }
    }

    private void ExportDiagnosticPackageButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = CreateDiagnosticPackageSaveDialog(Loc.T("진단 패키지 내보내기"));

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var packagePath = _viewModel.ExportDiagnosticPackage(dialog.FileName);
            AppLog.Info("SettingsWindow", $"진단 패키지 내보내기 성공 path={packagePath}");
            System.Windows.MessageBox.Show(
                this,
                Loc.F("진단 패키지를 생성했습니다.\n\n경로: {0}", packagePath),
                Loc.T("진단 패키지 내보내기 완료"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            AppLog.Error("SettingsWindow", "진단 패키지 내보내기 실패", exception);
            System.Windows.MessageBox.Show(
                this,
                Loc.F("진단 패키지를 만들지 못했습니다.\n\n원인: {0}", exception.Message),
                Loc.T("진단 패키지 내보내기 실패"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ExportDiagnosticIssueDraftButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = CreateDiagnosticPackageSaveDialog(Loc.T("진단 패키지 및 이슈 작성"));

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var draft = _viewModel.ExportDiagnosticIssueDraft(dialog.FileName);
            var copiedToClipboard = TryCopyIssueBodyToClipboard(draft.Body);
            var openedIssuePage = TryOpenIssueDraftPage(draft.IssueUrl);
            var openedPackageFolder = TryOpenPackageFolder(draft.DiagnosticPackagePath);
            AppLog.Info(
                "SettingsWindow",
                $"진단 이슈 초안 생성 성공 packagePath={draft.DiagnosticPackagePath} issuePageOpened={openedIssuePage} folderOpened={openedPackageFolder} clipboardCopied={copiedToClipboard}");

            var clipboardText = copiedToClipboard
                ? Loc.T("이슈 본문도 클립보드에 복사했습니다.")
                : Loc.T("클립보드 복사는 실패했습니다. 열린 이슈 화면의 본문을 확인해 주세요.");
            var issuePageText = openedIssuePage
                ? Loc.T("GitHub 이슈 작성 화면을 열었습니다.")
                : Loc.T("GitHub 이슈 작성 화면을 열지 못했습니다.");
            var folderText = openedPackageFolder
                ? Loc.T("진단 패키지 위치도 열었습니다.")
                : Loc.F("진단 패키지 경로: {0}", draft.DiagnosticPackagePath);

            System.Windows.MessageBox.Show(
                this,
                Loc.F("진단 패키지와 이슈 초안을 만들었습니다.\n\n{0}\n{1}\n{2}\n\n공개 전 zip 내용과 이슈 본문을 확인한 뒤 직접 제출/첨부해 주세요.", issuePageText, clipboardText, folderText),
                Loc.T("진단 이슈 초안 생성 완료"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            AppLog.Error("SettingsWindow", "진단 이슈 초안 생성 실패", exception);
            System.Windows.MessageBox.Show(
                this,
                Loc.F("진단 이슈 초안을 만들지 못했습니다.\n\n원인: {0}", exception.Message),
                Loc.T("진단 이슈 초안 생성 실패"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static Microsoft.Win32.SaveFileDialog CreateDiagnosticPackageSaveDialog(string title)
    {
        return new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            Filter = Loc.T("ZIP 진단 패키지 (*.zip)|*.zip|모든 파일 (*.*)|*.*"),
            FileName = $"DesktopAudioController-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            AddExtension = true,
            DefaultExt = ".zip",
            OverwritePrompt = true
        };
    }

    private static bool TryCopyIssueBodyToClipboard(string issueBody)
    {
        try
        {
            System.Windows.Clipboard.SetText(issueBody);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("SettingsWindow", "진단 이슈 본문 클립보드 복사 실패", exception);
            return false;
        }
    }

    private static bool TryOpenIssueDraftPage(string issueUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo(issueUrl)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("SettingsWindow", "GitHub 이슈 작성 화면 열기 실패", exception);
            return false;
        }
    }

    private static bool TryOpenPackageFolder(string packagePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{packagePath}\"")
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("SettingsWindow", $"진단 패키지 위치 열기 실패 path={packagePath}", exception);
            return false;
        }
    }

    private void ImportSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("설정 가져오기"),
            Filter = Loc.T("JSON 설정 파일 (*.json)|*.json|모든 파일 (*.*)|*.*"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            Loc.T("선택한 파일의 설정으로 현재 설정을 덮어씁니다.\n\n계속할까요?"),
            Loc.T("설정 가져오기"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _viewModel.ImportSettings(dialog.FileName);
            AppLog.Info("SettingsWindow", "설정 가져오기 성공");
            CompleteAppliedSettingsChange(Loc.T("설정 가져오기 완료"), Loc.T("설정을 가져와 적용했습니다."));
        }
        catch (SettingsPersistenceException exception)
        {
            ShowSettingsPersistenceFailure(Loc.T("설정 가져오기 실패"), exception);
        }
        catch (StartupRegistrationException exception)
        {
            ShowStartupRegistrationFailure(exception);
        }
        catch (Exception exception)
        {
            ShowUnexpectedSettingsFailure(Loc.T("설정 가져오기 실패"), exception);
        }
    }

    private void CreateAudioProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileNameWindow
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _viewModel.CreateAudioProfile(dialog.ProfileName);
            _hasPersistedSettingsChange = true;
            AppLog.Info("SettingsWindow", "수동 프로필 생성 성공");
            System.Windows.MessageBox.Show(
                this,
                Loc.T("현재 설정창 상태를 새 프로필로 저장했습니다."),
                Loc.T("프로필 생성 완료"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (SettingsPersistenceException exception)
        {
            ShowSettingsPersistenceFailure(Loc.T("프로필 생성 실패"), exception);
        }
        catch (Exception exception)
        {
            ShowUnexpectedSettingsFailure(Loc.T("프로필 생성 실패"), exception);
        }
    }

    private void ApplyAudioProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var confirmation = System.Windows.MessageBox.Show(
            this,
            Loc.T("선택한 프로필의 장치 표시 설정과 프로그램 저장값을 현재 설정에 적용합니다.\n\n계속할까요?"),
            Loc.T("프로필 적용"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _viewModel.ApplySelectedAudioProfile();
            AppLog.Info("SettingsWindow", "수동 프로필 적용 성공");
            CompleteAppliedSettingsChange(Loc.T("프로필 적용 완료"), Loc.T("선택한 프로필을 적용했습니다."));
        }
        catch (SettingsPersistenceException exception)
        {
            ShowSettingsPersistenceFailure(Loc.T("프로필 적용 실패"), exception);
        }
        catch (StartupRegistrationException exception)
        {
            ShowStartupRegistrationFailure(exception);
        }
        catch (Exception exception)
        {
            ShowUnexpectedSettingsFailure(Loc.T("프로필 적용 실패"), exception);
        }
    }

    private void DeleteAudioProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var confirmation = System.Windows.MessageBox.Show(
            this,
            Loc.T("선택한 프로필을 삭제합니다.\n현재 적용된 설정은 바뀌지 않습니다.\n\n계속할까요?"),
            Loc.T("프로필 삭제"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _viewModel.DeleteSelectedAudioProfile();
            _hasPersistedSettingsChange = true;
            AppLog.Info("SettingsWindow", "수동 프로필 삭제 성공");
            System.Windows.MessageBox.Show(
                this,
                Loc.T("선택한 프로필을 삭제했습니다."),
                Loc.T("프로필 삭제 완료"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (SettingsPersistenceException exception)
        {
            ShowSettingsPersistenceFailure(Loc.T("프로필 삭제 실패"), exception);
        }
        catch (Exception exception)
        {
            ShowUnexpectedSettingsFailure(Loc.T("프로필 삭제 실패"), exception);
        }
    }

    private void AddProfileRouteButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.AddSelectedProfileRoute();
    }

    private void RemoveProfileRouteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProgramOutputRouteViewModel route })
        {
            _viewModel.RemoveSelectedProfileRoute(route);
        }
    }

    private void AddProfileDeviceLevelButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.AddSelectedProfileDeviceLevel();
    }

    private void RemoveProfileDeviceLevelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DeviceLevelViewModel level })
        {
            _viewModel.RemoveSelectedProfileDeviceLevel(level);
        }
    }

    private void AddAutoProfileRuleButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.AddAutoProfileRule();
    }

    private void RemoveAutoProfileRuleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AutoProfileRuleViewModel rule })
        {
            _viewModel.RemoveAutoProfileRule(rule);
        }
    }

    private void MoveAutoProfileRuleUpButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AutoProfileRuleViewModel rule })
        {
            _viewModel.MoveAutoProfileRule(rule, -1);
        }
    }

    private void MoveAutoProfileRuleDownButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AutoProfileRuleViewModel rule })
        {
            _viewModel.MoveAutoProfileRule(rule, 1);
        }
    }

    private void ResetSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var confirmation = System.Windows.MessageBox.Show(
            this,
            Loc.T("모든 앱 설정을 기본값으로 초기화합니다.\n프로그램별 볼륨, 음소거, 이름 변경 저장값도 함께 삭제됩니다.\n\n계속할까요?"),
            Loc.T("설정 초기화"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _viewModel.ResetSettings();
            AppLog.Info("SettingsWindow", "설정 초기화 성공");
            CompleteAppliedSettingsChange(Loc.T("설정 초기화 완료"), Loc.T("설정을 기본값으로 초기화했습니다."));
        }
        catch (SettingsPersistenceException exception)
        {
            ShowSettingsPersistenceFailure(Loc.T("설정 초기화 실패"), exception);
        }
        catch (StartupRegistrationException exception)
        {
            ShowStartupRegistrationFailure(exception);
        }
        catch (Exception exception)
        {
            ShowUnexpectedSettingsFailure(Loc.T("설정 초기화 실패"), exception);
        }
    }

    private void ClearProgramSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var confirmation = System.Windows.MessageBox.Show(
            this,
            Loc.T("프로그램별 볼륨, 음소거, 이름 변경 저장값만 삭제합니다.\n장치 선택과 앱 동작 설정은 유지됩니다.\n\n계속할까요?"),
            Loc.T("프로그램 저장값 초기화"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _viewModel.ClearProgramAudioPreferences();
            AppLog.Info("SettingsWindow", "프로그램 저장값 초기화 성공");
            CompleteAppliedSettingsChange(Loc.T("프로그램 저장값 초기화 완료"), Loc.T("프로그램별 저장값을 초기화했습니다."));
        }
        catch (SettingsPersistenceException exception)
        {
            ShowSettingsPersistenceFailure(Loc.T("프로그램 저장값 초기화 실패"), exception);
        }
        catch (StartupRegistrationException exception)
        {
            ShowStartupRegistrationFailure(exception);
        }
        catch (Exception exception)
        {
            ShowUnexpectedSettingsFailure(Loc.T("프로그램 저장값 초기화 실패"), exception);
        }
    }

    /// <summary>
    /// 로그 폴더를 파일 탐색기로 열어 사용자가 바로 첨부 파일을 찾을 수 있게 합니다.
    /// </summary>
    private void OpenLogFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectoryPath);
            Process.Start(new ProcessStartInfo(AppLog.LogDirectoryPath)
            {
                UseShellExecute = true
            });

            AppLog.Info("SettingsWindow", "로그 폴더 열기 성공");
        }
        catch (Exception exception)
        {
            AppLog.Error("SettingsWindow", "로그 폴더 열기 실패", exception);
            System.Windows.MessageBox.Show(
                this,
                Loc.T("로그 폴더를 열지 못했습니다. 같은 문제가 반복되면 %LocalAppData%\\DesktopAudioController\\logs 경로를 직접 열어 주세요."),
                Loc.T("로그 폴더 열기 실패"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CompleteAppliedSettingsChange(string title, string message)
    {
        if (_viewModel.RequiresRestartToEnableDebugLogs)
        {
            AppLog.Info("SettingsWindow", "설정 관리 작업 후 디버그 로그 활성화로 앱 재시작 요청");
            if (System.Windows.Application.Current is App app && app.TryScheduleRestartForDebugLogging())
            {
                return;
            }

            AppLog.Warn("SettingsWindow", "설정 관리 작업 후 디버그 로그 활성화 재시작 예약 실패");
            System.Windows.MessageBox.Show(
                this,
                Loc.T("설정은 적용됐지만 자동 재시작을 예약하지 못했습니다.\n처음부터 디버그 로그를 기록하려면 앱을 직접 다시 시작해 주세요."),
                Loc.T("디버그 로그 재시작 실패"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        System.Windows.MessageBox.Show(
            this,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }

    private void ShowSettingsPersistenceFailure(string title, SettingsPersistenceException exception)
    {
        AppLog.Error("SettingsWindow", title, exception);
        System.Windows.MessageBox.Show(
            this,
            Loc.F("{0}\n\n경로: {1}\n원인: {2}", exception.Message, exception.SettingsFilePath, exception.InnerException?.Message ?? exception.Message),
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ShowStartupRegistrationFailure(StartupRegistrationException exception)
    {
        AppLog.Error("SettingsWindow", "자동 실행 레지스트리 반영 실패", exception);
        System.Windows.MessageBox.Show(
            this,
            Loc.F("Windows 자동 실행 옵션을 적용하지 못했습니다.\n\n레지스트리 경로: {0}\n값 이름: {1}\n원인: {2}\n\n설정 파일은 변경됐으므로, 필요하면 자동 실행 옵션만 다시 저장해 주세요.", exception.RegistryPath, exception.ValueName, exception.InnerException?.Message ?? exception.Message),
            Loc.T("자동 실행 설정 실패"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ShowUnexpectedSettingsFailure(string title, Exception exception)
    {
        AppLog.Error("SettingsWindow", title, exception);
        System.Windows.MessageBox.Show(
            this,
            Loc.F("설정 관리 작업 중 예상하지 못한 오류가 발생했습니다.\n\n원인: {0}", exception.Message),
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
