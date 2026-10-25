using System.Windows;
using DesktopAudioController.Localization;

namespace DesktopAudioController.Views;

/// <summary>
/// 수동 오디오 프로필 이름을 입력받는 작은 모달 창입니다.
/// </summary>
public partial class ProfileNameWindow : Window
{
    public ProfileNameWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ProfileNameTextBox.Focus();
    }

    public string ProfileName { get; private set; } = string.Empty;

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        var trimmed = ProfileNameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            System.Windows.MessageBox.Show(
                this,
                Loc.T("프로필 이름을 입력해 주세요."),
                Loc.T("프로필 이름 필요"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ProfileName = trimmed;
        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
