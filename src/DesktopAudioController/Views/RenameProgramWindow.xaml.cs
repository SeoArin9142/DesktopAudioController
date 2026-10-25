using System.Windows;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Views;

/// <summary>
/// 프로그램별 사용자 지정 표시 이름을 입력받는 작은 모달 창입니다.
/// </summary>
public partial class RenameProgramWindow : Window
{
    public RenameProgramWindow(string currentDisplayName, string? executablePath)
    {
        InitializeComponent();
        CurrentDisplayNameText.Text = currentDisplayName;
        ExecutablePathText.Text = string.IsNullOrWhiteSpace(executablePath)
            ? Loc.T("실행 경로를 아직 확인하지 못했습니다.")
            : executablePath;
        CustomDisplayNameTextBox.Text = currentDisplayName;
        Loaded += (_, _) =>
        {
            CustomDisplayNameTextBox.SelectAll();
            CustomDisplayNameTextBox.Focus();
        };
    }

    /// <summary>
    /// 저장 시 메인 창이 읽을 사용자 지정 이름 결과입니다. null이면 자동 이름 사용을 의미합니다.
    /// </summary>
    public string? CustomDisplayName { get; private set; }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var trimmed = CustomDisplayNameTextBox.Text?.Trim();
        CustomDisplayName = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        DialogResult = true;
        Close();
    }

    private void ResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        CustomDisplayName = null;
        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
