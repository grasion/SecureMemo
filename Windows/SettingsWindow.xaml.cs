using System;
using System.Windows;
using System.Windows.Input;
using SecureMemo.Services;

namespace SecureMemo.Windows
{
    public partial class SettingsWindow : Window
    {
        private readonly StorageService _storage;
        private readonly EncryptionService _encryption;
        private readonly GeminiService _gemini;
        private readonly UpdateService _update;
        private bool _passwordVisible = false;
        private bool _apiKeyVisible = false;
        private string? _updateFilePath;

        public bool PasswordEnabled { get; private set; }
        public bool ApiKeyChanged { get; private set; }

        public SettingsWindow(StorageService storage, EncryptionService encryption, GeminiService gemini)
        {
            InitializeComponent();
            _storage = storage;
            _encryption = encryption;
            _gemini = gemini;
            _update = new UpdateService();
            LoadSettings();
        }

        private void LoadSettings()
        {
            // 버전 표시
            VersionText.Text = $"현재 버전: v{_update.CurrentVersion}";

            // 비밀번호 설정 확인
            var hasPassword = _storage.LoadPasswordHash() != null;
            UsePasswordCheckBox.IsChecked = hasPassword;
            PasswordPanel.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;

            // API 키 로드
            var apiKey = _storage.LoadApiKey();
            if (!string.IsNullOrEmpty(apiKey))
            {
                ApiKeyPasswordBox.Password = apiKey;
                ApiKeyTextBox.Text = apiKey;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void UsePasswordCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            PasswordPanel.Visibility = Visibility.Visible;
        }

        private void UsePasswordCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "비밀번호 보호를 해제하시겠습니까?\n데이터는 기본 암호화로 유지됩니다.",
                "확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _storage.DeletePasswordHash();
                PasswordPanel.Visibility = Visibility.Collapsed;
                PasswordEnabled = false;
                
                // 기본 암호화 키 설정
                _encryption.SetMasterKey("SecureMemoDefaultKey");
            }
            else
            {
                UsePasswordCheckBox.IsChecked = true;
            }
        }

        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            _passwordVisible = !_passwordVisible;
            
            if (_passwordVisible)
            {
                NewPasswordTextBox.Text = NewPasswordBox.Password;
                NewPasswordBox.Visibility = Visibility.Collapsed;
                NewPasswordTextBox.Visibility = Visibility.Visible;
            }
            else
            {
                NewPasswordBox.Password = NewPasswordTextBox.Text;
                NewPasswordTextBox.Visibility = Visibility.Collapsed;
                NewPasswordBox.Visibility = Visibility.Visible;
            }
        }

        private void SetPassword_Click(object sender, RoutedEventArgs e)
        {
            var password = _passwordVisible ? NewPasswordTextBox.Text : NewPasswordBox.Password;
            
            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("비밀번호를 입력하세요", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (password.Length < 4)
            {
                MessageBox.Show("비밀번호는 최소 4자 이상이어야 합니다", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var hasExistingPassword = _storage.LoadPasswordHash() != null;
            
            if (hasExistingPassword)
            {
                var result = MessageBox.Show(
                    "비밀번호를 변경하시겠습니까?\n기존 데이터는 새 비밀번호로 다시 암호화됩니다.",
                    "확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            _encryption.SetMasterKey(password);
            _storage.SavePasswordHash(_encryption.HashPassword(password));
            PasswordEnabled = true;

            MessageBox.Show("비밀번호가 설정되었습니다", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ToggleApiKeyVisibility_Click(object sender, RoutedEventArgs e)
        {
            _apiKeyVisible = !_apiKeyVisible;
            
            if (_apiKeyVisible)
            {
                ApiKeyTextBox.Text = ApiKeyPasswordBox.Password;
                ApiKeyPasswordBox.Visibility = Visibility.Collapsed;
                ApiKeyTextBox.Visibility = Visibility.Visible;
            }
            else
            {
                ApiKeyPasswordBox.Password = ApiKeyTextBox.Text;
                ApiKeyTextBox.Visibility = Visibility.Collapsed;
                ApiKeyPasswordBox.Visibility = Visibility.Visible;
            }
        }

        private void SaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = _apiKeyVisible ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password;
            
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("API 키를 입력하세요", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _storage.SaveApiKey(apiKey);
            _gemini.SetApiKey(apiKey);
            ApiKeyChanged = true;

            MessageBox.Show("API 키가 저장되었습니다", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void TestApi_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = _apiKeyVisible ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password;
            
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ApiTestResult.Text = "❌ API 키를 입력하세요";
                ApiTestResult.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            try
            {
                TestApiButton.IsEnabled = false;
                TestApiButton.Content = "테스트 중...";
                ApiTestResult.Text = "테스트 중...";
                ApiTestResult.Foreground = System.Windows.Media.Brushes.Gray;

                _gemini.SetApiKey(apiKey);
                var result = await _gemini.SummarizeText("테스트");

                if (!string.IsNullOrEmpty(result) && result != "요약 실패")
                {
                    ApiTestResult.Text = "✅ API 연결 성공!";
                    ApiTestResult.Foreground = System.Windows.Media.Brushes.LightGreen;
                }
                else
                {
                    ApiTestResult.Text = "❌ API 연결 실패";
                    ApiTestResult.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                ApiTestResult.Text = $"❌ 오류: {ex.Message}";
                ApiTestResult.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                TestApiButton.IsEnabled = true;
                TestApiButton.Content = "API 테스트";
            }
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckUpdateButton.IsEnabled = false;
                CheckUpdateButton.Content = "확인 중...";
                UpdateStatusText.Text = "업데이트를 확인하는 중...";
                UpdateStatusText.Foreground = System.Windows.Media.Brushes.Gray;

                var (hasUpdate, latestVersion, downloadUrl, releaseNotes) = await _update.CheckForUpdatesAsync();

                if (hasUpdate)
                {
                    UpdateStatusText.Text = $"새 버전 발견: v{latestVersion}\n\n{releaseNotes}";
                    UpdateStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    InstallUpdateButton.Visibility = Visibility.Visible;
                    InstallUpdateButton.Tag = downloadUrl; // URL 저장
                }
                else
                {
                    UpdateStatusText.Text = "최신 버전을 사용 중입니다.";
                    UpdateStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    InstallUpdateButton.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"업데이트 확인 실패: {ex.Message}";
                UpdateStatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
                CheckUpdateButton.Content = "업데이트 확인";
            }
        }

        private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            var downloadUrl = InstallUpdateButton.Tag as string;
            if (string.IsNullOrEmpty(downloadUrl))
            {
                MessageBox.Show("다운로드 URL을 찾을 수 없습니다", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = MessageBox.Show(
                "업데이트를 다운로드하고 설치하시겠습니까?\n프로그램이 자동으로 재시작됩니다.",
                "업데이트 설치",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                InstallUpdateButton.IsEnabled = false;
                InstallUpdateButton.Content = "다운로드 중...";
                UpdateProgressBar.Visibility = Visibility.Visible;
                UpdateProgressBar.Value = 0;

                var progress = new Progress<int>(percent =>
                {
                    UpdateProgressBar.Value = percent;
                    UpdateStatusText.Text = $"다운로드 중... {percent}%";
                });

                _updateFilePath = await _update.DownloadUpdateAsync(downloadUrl, progress);

                UpdateStatusText.Text = "다운로드 완료. 설치 중...";
                UpdateStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;

                // 잠시 대기 후 설치
                await System.Threading.Tasks.Task.Delay(1000);
                _update.InstallUpdate(_updateFilePath);
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"업데이트 실패: {ex.Message}";
                UpdateStatusText.Foreground = System.Windows.Media.Brushes.Red;
                InstallUpdateButton.IsEnabled = true;
                InstallUpdateButton.Content = "업데이트 설치";
                UpdateProgressBar.Visibility = Visibility.Collapsed;
            }
        }

    }
}
