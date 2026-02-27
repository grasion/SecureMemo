using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls;
using Microsoft.Win32;
using SecureMemo.Models;
using SecureMemo.Services;
using SecureMemo.Windows;
using NAudio.Wave;
using System.Diagnostics;
using QRCoder;
using System.IO;
using System.Windows.Media.Imaging;

namespace SecureMemo
{
    public partial class MainWindow : Window
    {
        private readonly StorageService _storage = new();
        private readonly EncryptionService _encryption = EncryptionService.Instance;
        private readonly AudioService _audio = new();
        private readonly ExportService _export = new();
        private readonly GeminiService _gemini = new();
        private ObservableCollection<Memo> _memos = new();
        private Memo? _currentMemo;
        private bool _isRecording = false;
        private DispatcherTimer? _autoSaveTimer;
        private bool _hasApiKey = false;
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioFileReader;
        private DispatcherTimer? _audioTimer;

        public MainWindow()
        {
            InitializeComponent();
            InitializeAutoSave();
            CheckPasswordAndLoad();
        }

        private void CheckPasswordAndLoad()
        {
            var hasPassword = _storage.LoadPasswordHash() != null;
            
            if (hasPassword)
            {
                // 여러 비밀번호 확인
                var allHashes = _storage.GetAllPasswordHashes();
                
                if (allHashes.Count > 1)
                {
                    // 여러 비밀번호가 있으면 통합 창 표시
                    var mergeWindow = new PasswordMergeWindow(_storage, _encryption, allHashes)
                    {
                        Owner = this
                    };
                    mergeWindow.ShowDialog();

                    if (mergeWindow.Merged && mergeWindow.SelectedPasswordHash != null)
                    {
                        _storage.SetCurrentPasswordHash(mergeWindow.SelectedPasswordHash);
                        _storage.SavePasswordHash(mergeWindow.SelectedPasswordHash);
                        ShowMainContent();
                    }
                    else if (mergeWindow.SelectedPasswordHash != null)
                    {
                        _storage.SetCurrentPasswordHash(mergeWindow.SelectedPasswordHash);
                        ShowMainContent();
                    }
                    else
                    {
                        // 비밀번호가 설정되어 있으면 로그인 화면 표시
                        PasswordPanel.Visibility = Visibility.Visible;
                        MainContent.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    // 비밀번호가 설정되어 있으면 로그인 화면 표시
                    PasswordPanel.Visibility = Visibility.Visible;
                    MainContent.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                // 비밀번호가 없으면 기본 암호화로 바로 메인 화면
                _encryption.SetMasterKey("SecureMemoDefaultKey");
                _storage.SetCurrentPasswordHash("default");
                PasswordPanel.Visibility = Visibility.Collapsed;
                MainContent.Visibility = Visibility.Visible;
                LoadMemos();
                CheckApiKey();
            }
        }

        private void CheckApiKey()
        {
            var apiKey = _storage.LoadApiKey();
            _hasApiKey = !string.IsNullOrEmpty(apiKey);
            
            if (_hasApiKey)
            {
                _gemini.SetApiKey(apiKey!);
            }

            UpdateAiButtonsVisibility();
        }

        private void UpdateAiButtonsVisibility()
        {
            TranscribeButton.Visibility = _hasApiKey ? Visibility.Visible : Visibility.Collapsed;
            SummarizeButton.Visibility = _hasApiKey ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateToolbarVisibility()
        {
            var hasCurrentMemo = _currentMemo != null;
            var hasAudio = hasCurrentMemo && !string.IsNullOrEmpty(_currentMemo?.AudioPath);
            
            ExportWordButton.Visibility = hasCurrentMemo ? Visibility.Visible : Visibility.Collapsed;
            RecordButton.Visibility = hasCurrentMemo ? Visibility.Visible : Visibility.Collapsed;
            PlayButton.Visibility = hasAudio ? Visibility.Visible : Visibility.Collapsed;
            AudioSlider.Visibility = hasAudio ? Visibility.Visible : Visibility.Collapsed;
        }

        private void InitializeAutoSave()
        {
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        }

        private void AutoSaveTimer_Tick(object? sender, EventArgs e)
        {
            _autoSaveTimer?.Stop();
            AutoSave();
        }

        private void AutoSave()
        {
            if (_currentMemo == null) return;

            _currentMemo.Title = TitleTextBox.Text;
            _currentMemo.Content = ContentTextBox.Text;
            _currentMemo.UpdatedAt = DateTime.Now;
            _storage.SaveMemo(_currentMemo);
            
            MemoListBox.Items.Refresh();
        }

        private void TitleTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Start();
        }

        private void ContentTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Start();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Login_Click(sender, e);
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            var password = PasswordBox.Password;
            if (string.IsNullOrEmpty(password))
            {
                ErrorText.Text = "비밀번호를 입력하세요";
                return;
            }

            var storedHash = _storage.LoadPasswordHash();
            if (storedHash != null && _encryption.VerifyPassword(password, storedHash))
            {
                _encryption.SetMasterKey(password);
                _storage.SetCurrentPasswordHash(storedHash);
                ShowMainContent();
            }
            else
            {
                ErrorText.Text = "비밀번호가 올바르지 않습니다";
            }
        }

        private void ShowMainContent()
        {
            PasswordPanel.Visibility = Visibility.Collapsed;
            MainContent.Visibility = Visibility.Visible;
            LoadMemos();
            CheckApiKey();
        }

        private void LoadMemos()
        {
            _memos = new ObservableCollection<Memo>(_storage.LoadAllMemos());
            MemoListBox.ItemsSource = _memos;
        }

        private void NewMemo_Click(object sender, RoutedEventArgs e)
        {
            _currentMemo = new Memo();
            _memos.Insert(0, _currentMemo);
            MemoListBox.SelectedItem = _currentMemo;
            TitleTextBox.Text = _currentMemo.Title;
            ContentTextBox.Text = _currentMemo.Content;
            UpdateToolbarVisibility();
            TitleTextBox.Focus();
        }

        private void MemoListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (MemoListBox.SelectedItem is Memo memo)
            {
                StopAudio();
                _currentMemo = memo;
                TitleTextBox.Text = memo.Title;
                ContentTextBox.Text = memo.Content;
                UpdateToolbarVisibility();
            }
        }

        private void DeleteMemo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is Memo memo)
            {
                var result = MessageBox.Show("정말 삭제하시겠습니까?", "삭제", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    _storage.DeleteMemo(memo.Id);
                    _memos.Remove(memo);
                    
                    if (_currentMemo?.Id == memo.Id)
                    {
                        StopAudio();
                        _currentMemo = null;
                        TitleTextBox.Clear();
                        ContentTextBox.Clear();
                        UpdateToolbarVisibility();
                    }
                }
            }
        }

        private void ExportWord_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMemo == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Word 문서|*.docx",
                FileName = $"{_currentMemo.Title}.docx"
            };

            if (dialog.ShowDialog() == true)
            {
                _export.ExportToDocx(_currentMemo, dialog.FileName);
                MessageBox.Show("내보내기 완료", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Record_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMemo == null) return;

            try
            {
                if (!_isRecording)
                {
                    _audio.StartRecording(_currentMemo.Id);
                    RecordButton.Content = "⏹ 중지";
                    _isRecording = true;
                }
                else
                {
                    var audioPath = _audio.StopRecording();
                    _currentMemo.AudioPath = audioPath;
                    _storage.SaveMemo(_currentMemo);
                    RecordButton.Content = "🎤 녹음";
                    _isRecording = false;
                    UpdateToolbarVisibility();
                    MessageBox.Show("녹음이 저장되었습니다", "녹음", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _isRecording = false;
                RecordButton.Content = "🎤 녹음";
                MessageBox.Show($"녹음 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TranscribeAudio_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMemo?.AudioPath == null)
            {
                MessageBox.Show("녹음된 음성이 없습니다", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                TranscribeButton.IsEnabled = false;
                TranscribeButton.Content = "변환 중...";

                var transcription = await _gemini.TranscribeAudio(_currentMemo.AudioPath);
                ContentTextBox.Text += $"\n\n{transcription}";
                AutoSave();

                MessageBox.Show("음성이 텍스트로 변환되었습니다", "완료", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"변환 실패: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TranscribeButton.IsEnabled = true;
                TranscribeButton.Content = "음성→텍스트";
            }
        }

        private async void SummarizeText_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ContentTextBox.Text))
            {
                MessageBox.Show("요약할 내용이 없습니다", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SummarizeButton.IsEnabled = false;
                SummarizeButton.Content = "요약 중...";

                var summary = await _gemini.SummarizeText(ContentTextBox.Text);
                ContentTextBox.Text += $"\n\n--- 요약 ---\n{summary}";
                AutoSave();

                MessageBox.Show("요약이 완료되었습니다", "완료", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"요약 실패: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SummarizeButton.IsEnabled = true;
                SummarizeButton.Content = "요약";
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_storage, _encryption, _gemini)
            {
                Owner = this
            };
            
            settingsWindow.ShowDialog();

            // 설정 창에서 변경사항 확인
            if (settingsWindow.PasswordEnabled)
            {
                // 비밀번호가 새로 설정되었으면 로그인 화면으로
                PasswordPanel.Visibility = Visibility.Visible;
                MainContent.Visibility = Visibility.Collapsed;
            }

            if (settingsWindow.ApiKeyChanged)
            {
                CheckApiKey();
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMemo?.AudioPath == null) return;

            try
            {
                if (_waveOut == null || _waveOut.PlaybackState != PlaybackState.Playing)
                {
                    if (_waveOut == null)
                    {
                        _audioFileReader = new AudioFileReader(_currentMemo.AudioPath);
                        _waveOut = new WaveOutEvent();
                        _waveOut.Init(_audioFileReader);
                        _waveOut.PlaybackStopped += WaveOut_PlaybackStopped;

                        AudioSlider.Maximum = _audioFileReader.TotalTime.TotalSeconds;
                        
                        _audioTimer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(100)
                        };
                        _audioTimer.Tick += AudioTimer_Tick;
                        _audioTimer.Start();
                    }

                    _waveOut.Play();
                    PlayButton.Content = "⏸ 일시정지";
                }
                else
                {
                    _waveOut.Pause();
                    PlayButton.Content = "▶ 재생";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"재생 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AudioTimer_Tick(object? sender, EventArgs e)
        {
            if (_audioFileReader != null && _waveOut?.PlaybackState == PlaybackState.Playing)
            {
                AudioSlider.Value = _audioFileReader.CurrentTime.TotalSeconds;
            }
        }

        private void AudioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioFileReader != null && _waveOut != null && Math.Abs(_audioFileReader.CurrentTime.TotalSeconds - e.NewValue) > 0.5)
            {
                _audioFileReader.CurrentTime = TimeSpan.FromSeconds(e.NewValue);
            }
        }

        private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                PlayButton.Content = "▶ 재생";
                AudioSlider.Value = 0;
                StopAudio();
            });
        }

        private void StopAudio()
        {
            _audioTimer?.Stop();
            _audioTimer = null;

            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }

            if (_audioFileReader != null)
            {
                _audioFileReader.Dispose();
                _audioFileReader = null;
            }

            PlayButton.Content = "▶ 재생";
            AudioSlider.Value = 0;
        }

        private void CoffeeBanner_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var qrWindow = new Window
                {
                    Title = "개발자에게 커피 사주기",
                    Width = 450,
                    Height = 550,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Background = System.Windows.Media.Brushes.White,
                    ResizeMode = ResizeMode.NoResize
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // 제목
                var titleText = new TextBlock
                {
                    Text = "☕ 개발자에게 커피 사주기",
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20),
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0))
                };
                Grid.SetRow(titleText, 0);
                grid.Children.Add(titleText);

                // QR 코드 영역
                var qrBorder = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0)),
                    CornerRadius = new CornerRadius(12),
                    Margin = new Thickness(40, 0, 40, 0),
                    Padding = new Thickness(20)
                };

                // QR 코드 생성
                var qrGenerator = new QRCodeGenerator();
                var qrCodeData = qrGenerator.CreateQrCode("https://qr.kakaopay.com/Ej9Q6cblA9c409206", QRCodeGenerator.ECCLevel.Q);
                var qrCode = new PngByteQRCode(qrCodeData);
                var qrCodeBytes = qrCode.GetGraphic(20);

                var qrImage = new Image
                {
                    Stretch = System.Windows.Media.Stretch.Uniform
                };

                using (var ms = new MemoryStream(qrCodeBytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    qrImage.Source = bitmap;
                }

                qrBorder.Child = qrImage;
                Grid.SetRow(qrBorder, 1);
                grid.Children.Add(qrBorder);

                // 설명 텍스트
                var descText = new TextBlock
                {
                    Text = "이 프로그램이 도움이 되셨나요?\nQR 코드를 스캔하여 개발자에게\n커피 한 잔의 응원을 보내주세요!",
                    FontSize = 14,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20),
                    Foreground = System.Windows.Media.Brushes.Gray
                };
                Grid.SetRow(descText, 2);
                grid.Children.Add(descText);

                qrWindow.Content = grid;
                qrWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"QR 코드를 표시할 수 없습니다: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BlogBanner_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var blogUrl = "https://1st-life-2nd.tistory.com/";
                Process.Start(new ProcessStartInfo
                {
                    FileName = blogUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"블로그를 열 수 없습니다: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            StopAudio();
            _audio.Dispose();
        }
    }
}
