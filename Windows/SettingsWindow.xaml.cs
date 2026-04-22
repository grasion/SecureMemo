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
        private readonly OllamaSetupService _ollamaSetup;
        private bool _passwordVisible = false;
        private string? _updateFilePath;

        public bool PasswordEnabled { get; private set; }
        public bool ServerUrlChanged { get; private set; }

        public SettingsWindow(StorageService storage, EncryptionService encryption, GeminiService gemini)
        {
            InitializeComponent();
            _storage = storage;
            _encryption = encryption;
            _gemini = gemini;
            _update = new UpdateService();
            _ollamaSetup = new OllamaSetupService();
            LoadSettings();
            CheckOllamaStatus();
        }

        private void LoadSettings()
        {
            // 버전 표시
            VersionText.Text = $"현재 버전: v{_update.CurrentVersion}";

            // 비밀번호 설정 확인
            var hasPassword = _storage.LoadPasswordHash() != null;
            UsePasswordCheckBox.IsChecked = hasPassword;
            PasswordPanel.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
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

        private async void CheckOllamaStatus()
        {
            var installed = _ollamaSetup.IsOllamaInstalled();
            var running = installed && await _ollamaSetup.IsOllamaRunningAsync();
            var hasModel = running && await _ollamaSetup.IsModelInstalledAsync();

            if (hasModel)
            {
                SetupStatusText.Text = "✅ Ollama 설치됨 · 서버 실행 중 · gemma4-e2b 모델 준비 완료";
                SetupStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                AutoSetupButton.Content = "✅ AI 설정 완료";
                AutoSetupButton.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x2a, 0x2a, 0x2a));
                TestApiButton.Visibility = Visibility.Visible;
            }
            else if (running)
            {
                SetupStatusText.Text = "⚠ Ollama 실행 중이지만 gemma4-e2b 모델이 없습니다";
                SetupStatusText.Foreground = System.Windows.Media.Brushes.Orange;
            }
            else if (installed)
            {
                SetupStatusText.Text = "⚠ Ollama 설치됨, 서버가 실행되지 않고 있습니다";
                SetupStatusText.Foreground = System.Windows.Media.Brushes.Orange;
            }
            else
            {
                SetupStatusText.Text = "Ollama가 설치되어 있지 않습니다. 위 버튼을 눌러 자동 설정하세요.";
                SetupStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private async void AutoSetup_Click(object sender, RoutedEventArgs e)
        {
            AutoSetupButton.IsEnabled = false;
            AutoSetupButton.Content = "설정 진행 중...";
            SetupProgressBar.Visibility = Visibility.Visible;
            SetupProgressBar.Value = 0;

            _ollamaSetup.StatusChanged += status =>
            {
                Dispatcher.Invoke(() => SetupStatusText.Text = status);
            };

            _ollamaSetup.ProgressChanged += percent =>
            {
                Dispatcher.Invoke(() => SetupProgressBar.Value = percent);
            };

            try
            {
                var success = await _ollamaSetup.AutoSetupAsync();

                if (success)
                {
                    SetupStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    AutoSetupButton.Content = "✅ AI 설정 완료";
                    TestApiButton.Visibility = Visibility.Visible;

                    // 서버 URL 자동 저장 (기본 localhost)
                    var serverUrl = "http://localhost:11434";
                    _storage.SaveServerUrl(serverUrl);
                    _gemini.SetServerUrl(serverUrl);
                    ServerUrlChanged = true;
                }
                else
                {
                    SetupStatusText.Foreground = System.Windows.Media.Brushes.Red;
                    AutoSetupButton.Content = "🚀 원클릭 AI 설정";
                    AutoSetupButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                SetupStatusText.Text = $"설정 실패: {ex.Message}";
                SetupStatusText.Foreground = System.Windows.Media.Brushes.Red;
                AutoSetupButton.Content = "🚀 원클릭 AI 설정";
                AutoSetupButton.IsEnabled = true;
            }
            finally
            {
                SetupProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void TestApi_Click(object sender, RoutedEventArgs e)
        {
            var serverUrl = _storage.LoadServerUrl() ?? "http://localhost:11434";

            try
            {
                TestApiButton.IsEnabled = false;
                TestApiButton.Content = "테스트 중...";
                ApiTestResult.Visibility = Visibility.Visible;
                ApiTestResult.Text = "연결 테스트 중...";
                ApiTestResult.Foreground = System.Windows.Media.Brushes.Gray;

                _gemini.SetServerUrl(serverUrl);
                var (success, message) = await _gemini.TestConnectionAsync();

                ApiTestResult.Text = message;
                ApiTestResult.Foreground = success 
                    ? System.Windows.Media.Brushes.LightGreen 
                    : System.Windows.Media.Brushes.Red;
            }
            catch (Exception ex)
            {
                ApiTestResult.Text = $"❌ 오류: {ex.Message}";
                ApiTestResult.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                TestApiButton.IsEnabled = true;
                TestApiButton.Content = "연결 테스트";
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
