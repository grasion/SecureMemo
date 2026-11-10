using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using QRCoder;
using SecureMemo.Models;
using SecureMemo.Services;
using SecureMemo.Windows;

namespace SecureMemo
{
    public partial class MainWindow : Window
    {
        // 녹음 중 입력이 이 시간(초) 이상 끊기면 새 구간으로 분리
        private const double MarkGapSeconds = 4.0;
        // 재생 시 구간이 끝난 뒤에도 테두리를 유지하는 시간(초)
        private const double MarkHoldSeconds = 1.5;
        // 재생 시 구간 시작보다 살짝 먼저 표시
        private const double MarkLeadSeconds = 0.2;

        private readonly AudioService _audio = new();
        private Vault? _vault;           // 현재 열린 메모 공간 (비밀번호별)
        private bool _creatingVault;     // 새 메모 공간 만들기(비밀번호 확인) 단계
        private readonly ExportService _export = new();

        private ObservableCollection<Memo> _memos = new();
        private ICollectionView? _memoView;
        private Memo? _currentMemo;
        private bool _loadingMemo;
        private bool _dirty;
        private readonly DispatcherTimer _autoSaveTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        // 녹음
        private bool _isRecording;
        private Memo? _recordingMemo;
        private readonly Stopwatch _recordClock = new();
        private readonly DispatcherTimer _recordTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private AudioMark? _activeMark;

        // 재생
        private WaveOutEvent? _waveOut;
        private WaveStream? _audioFileReader;
        private readonly DispatcherTimer _audioTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private double _audioDuration;
        private bool _updatingSlider;
        private HashSet<AudioMark> _visibleMarks = new();

        // 토스트
        private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };

        private static readonly Brush HighlightStroke = Brushes.White;
        private static readonly Brush HighlightFill = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));

        public MainWindow()
        {
            InitializeComponent();

            _autoSaveTimer.Tick += (_, _) => { _autoSaveTimer.Stop(); SaveCurrentMemo(); };
            _recordTimer.Tick += (_, _) => RecordTimeText.Text = FormatTime(_recordClock.Elapsed.TotalSeconds);
            _audioTimer.Tick += (_, _) => UpdatePlaybackUi();
            _toastTimer.Tick += (_, _) => HideToast();

            ContentTextBox.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, _) => UpdateHighlights()));

            ShowPasswordPanel();
        }

        #region 로그인 / 초기화

        private void ShowPasswordPanel()
        {
            PasswordPanel.Visibility = Visibility.Visible;
            MainContent.Visibility = Visibility.Collapsed;
            _toastTimer.Stop();
            Toast.BeginAnimation(OpacityProperty, null);
            Toast.Visibility = Visibility.Collapsed;
            SetCreateMode(false);
            PasswordBox.Clear();
            ErrorText.Text = "";
            Dispatcher.BeginInvoke(() => PasswordBox.Focus(), DispatcherPriority.Input);
        }

        /// <summary>새 메모 공간 만들기(비밀번호 한 번 더 입력) 단계 전환</summary>
        private void SetCreateMode(bool creating)
        {
            _creatingVault = creating;
            ConfirmPasswordBox.Clear();
            ConfirmArea.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
            BackButton.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
            PasswordBox.IsEnabled = !creating;
            LockIcon.Text = creating ? "" : "";
            LockTitle.Text = creating ? "새 메모 공간 만들기" : "SecureMemo";
            LockDesc.Text = creating
                ? "이 비밀번호로 저장된 메모가 없습니다.\n새 메모 공간을 만들려면 한 번 더 입력하세요."
                : "비밀번호를 입력하세요.\n처음 사용하는 비밀번호는 새 메모 공간이 됩니다.";
            LoginButton.Content = creating ? "새 메모 공간 만들기" : "열기";
            ErrorText.Text = "";
            if (creating)
                Dispatcher.BeginInvoke(() => ConfirmPasswordBox.Focus(), DispatcherPriority.Input);
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            SetCreateMode(false);
            PasswordBox.SelectAll();
            PasswordBox.Focus();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Login_Click(sender, e);
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            if (!LoginButton.IsEnabled) return;

            var password = PasswordBox.Password;
            if (string.IsNullOrEmpty(password))
            {
                ErrorText.Text = "비밀번호를 입력하세요";
                return;
            }

            if (_creatingVault)
            {
                if (password.Length < 4)
                {
                    ErrorText.Text = "새 메모 공간의 비밀번호는 4자 이상이어야 합니다";
                    return;
                }
                if (ConfirmPasswordBox.Password != password)
                {
                    ErrorText.Text = "비밀번호가 일치하지 않습니다";
                    ConfirmPasswordBox.Clear();
                    ConfirmPasswordBox.Focus();
                    return;
                }
            }

            var creating = _creatingVault;
            SetLoginBusy(true);
            try
            {
                var (vault, migrated) = await Task.Run(() =>
                {
                    Vault? v = creating ? VaultManager.Create(password) : VaultManager.TryOpen(password);

                    // 이전 버전에서 이 비밀번호로 저장한 메모가 있으면 확인 없이 새 공간으로 옮김
                    if (v == null && VaultManager.HasLegacyDataFor(password))
                        v = VaultManager.Create(password);

                    var count = v != null ? VaultManager.MigrateLegacy(v, password) : 0;
                    return (v, count);
                });

                if (vault == null)
                {
                    // 기존 비밀번호와 다름 → 새 메모 공간 만들기 단계로
                    SetCreateMode(true);
                    return;
                }

                OpenVault(vault);
                if (migrated > 0)
                    ShowToast($"기존 메모 {migrated}개를 이 비밀번호로 암호화해 옮겼습니다");
                else if (creating)
                    ShowToast("새 메모 공간을 만들었습니다");
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"열기 실패: {ex.Message}";
            }
            finally
            {
                SetLoginBusy(false);
            }
        }

        private void SetLoginBusy(bool busy)
        {
            LoginButton.IsEnabled = !busy;
            BackButton.IsEnabled = !busy;
            ConfirmPasswordBox.IsEnabled = !busy;
            if (busy)
            {
                PasswordBox.IsEnabled = false;
                LoginButton.Content = "확인 중...";
            }
            else
            {
                PasswordBox.IsEnabled = !_creatingVault;
                LoginButton.Content = _creatingVault ? "새 메모 공간 만들기" : "열기";
            }
        }

        private void OpenVault(Vault vault)
        {
            _vault?.Dispose();
            _vault = vault;
            PasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            SetCreateMode(false);
            PasswordPanel.Visibility = Visibility.Collapsed;
            MainContent.Visibility = Visibility.Visible;
            SearchBox.Clear();
            LoadMemos();
        }

        private void Lock_Click(object sender, RoutedEventArgs e)
        {
            if (_vault == null) return;

            StopRecordingIfActive(showToast: false);
            FlushPendingSave();
            LoadMemoIntoEditor(null);

            _memos.Clear();
            UpdateListState();
            _vault.Dispose();
            _vault = null;
            ShowPasswordPanel();
        }

        #endregion

        #region 창 제어

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                // WindowChrome 최대화 시 화면 밖으로 넘치는 프레임만큼 보정
                var t = SystemParameters.WindowResizeBorderThickness;
                RootBorder.Margin = new Thickness(t.Left + 4, t.Top + 4, t.Right + 4, t.Bottom + 4);
                RootBorder.BorderThickness = new Thickness(0);
                MaximizeButton.Content = "";
                MaximizeButton.ToolTip = "이전 크기로 복원";
            }
            else
            {
                RootBorder.Margin = new Thickness(0);
                RootBorder.BorderThickness = new Thickness(1);
                MaximizeButton.Content = "";
                MaximizeButton.ToolTip = "최대화";
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (MainContent.Visibility != Visibility.Visible) return;
            if (Keyboard.Modifiers != ModifierKeys.Control) return;

            switch (e.Key)
            {
                case Key.N:
                    NewMemo_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.F:
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                    e.Handled = true;
                    break;
                case Key.R:
                    if (_currentMemo != null) Record_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.L:
                    Lock_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.S:
                    FlushPendingSave();
                    ShowToast("저장되었습니다");
                    e.Handled = true;
                    break;
            }
        }

        #endregion

        #region 메모 목록

        private void LoadMemos()
        {
            _memos = new ObservableCollection<Memo>(_vault?.LoadAllMemos() ?? new List<Memo>());
            _memoView = CollectionViewSource.GetDefaultView(_memos);
            _memoView.SortDescriptions.Clear();
            _memoView.SortDescriptions.Add(new SortDescription(nameof(Memo.UpdatedAt), ListSortDirection.Descending));
            _memoView.Filter = FilterMemo;
            MemoListBox.ItemsSource = _memoView;

            LoadMemoIntoEditor(null);
            UpdateListState();
        }

        private bool FilterMemo(object obj)
        {
            var query = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query) || obj is not Memo memo) return true;
            return memo.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || memo.Content.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshList();
        }

        private void RefreshList()
        {
            _memoView?.Refresh();
            if (_currentMemo != null && MemoListBox.SelectedItem != _currentMemo && _memoView?.Contains(_currentMemo) == true)
            {
                _loadingMemo = true;
                MemoListBox.SelectedItem = _currentMemo;
                _loadingMemo = false;
            }
            UpdateListState();
        }

        private void UpdateListState()
        {
            MemoCountText.Text = _memos.Count.ToString();
            var isEmpty = _memoView == null || _memoView.IsEmpty;
            ListEmptyText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            ListEmptyText.Text = string.IsNullOrWhiteSpace(SearchBox.Text) ? "메모가 없습니다" : "검색 결과가 없습니다";
        }

        private void NewMemo_Click(object sender, RoutedEventArgs e)
        {
            if (MainContent.Visibility != Visibility.Visible) return;

            if (_vault == null) return;
            FlushPendingSave();
            StopRecordingIfActive();

            var memo = new Memo { Title = "" };
            _vault.SaveMemo(memo);
            _memos.Insert(0, memo);

            if (!string.IsNullOrEmpty(SearchBox.Text))
                SearchBox.Clear(); // 새 메모가 필터에 가려지지 않도록

            RefreshList();
            LoadMemoIntoEditor(memo);
            TitleTextBox.Focus();
        }

        private void MemoListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingMemo) return;
            if (MemoListBox.SelectedItem is not Memo memo || memo == _currentMemo) return;

            FlushPendingSave();
            StopRecordingIfActive();
            LoadMemoIntoEditor(memo);
        }

        private void DeleteMemo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: Memo memo }) return;

            var result = MessageBox.Show($"'{memo.DisplayTitle}' 메모를 삭제하시겠습니까?\n녹음 파일도 함께 삭제됩니다.",
                "삭제", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            if (_recordingMemo == memo) StopRecordingIfActive(showToast: false);

            var isCurrent = _currentMemo == memo;
            if (isCurrent)
            {
                _autoSaveTimer.Stop();
                StopAudio();
            }

            _vault?.DeleteMemo(memo.Id);
            _vault?.DeleteAudio(memo.AudioPath);
            _memos.Remove(memo);

            if (isCurrent) LoadMemoIntoEditor(null);
            UpdateListState();
            ShowToast("메모가 삭제되었습니다");
        }

        #endregion

        #region 에디터

        private void LoadMemoIntoEditor(Memo? memo)
        {
            StopAudio();
            _autoSaveTimer.Stop();
            _dirty = false;
            _currentMemo = memo;
            _activeMark = null;

            _loadingMemo = true;
            TitleTextBox.Text = memo?.Title ?? "";
            ContentTextBox.Text = memo?.Content ?? "";
            ContentTextBox.ScrollToHome();
            // 다른 메모의 내용으로 되돌리기(Ctrl+Z)되지 않도록 실행 취소 기록 초기화
            TitleTextBox.IsUndoEnabled = false;
            TitleTextBox.IsUndoEnabled = true;
            ContentTextBox.IsUndoEnabled = false;
            ContentTextBox.IsUndoEnabled = true;
            _loadingMemo = false;

            if (memo != null)
            {
                memo.AudioMarks ??= new List<AudioMark>();
                if (MemoListBox.SelectedItem != memo)
                {
                    _loadingMemo = true;
                    MemoListBox.SelectedItem = memo;
                    _loadingMemo = false;
                }
            }

            LoadAudioInfo();
            UpdateEditorState();
        }

        private void UpdateEditorState()
        {
            var memo = _currentMemo;
            EmptyState.Visibility = memo == null ? Visibility.Visible : Visibility.Collapsed;
            PlayerBar.Visibility = memo != null && memo.HasAudio && !_isRecording ? Visibility.Visible : Visibility.Collapsed;
            UpdateMetaText();

            if (memo != null)
                MarkCountText.Text = $"연결된 메모 {memo.AudioMarks.Count}";
        }

        private void UpdateMetaText()
        {
            if (_currentMemo == null)
            {
                MetaText.Text = "";
                return;
            }
            MetaText.Text = $"{_currentMemo.UpdatedAt:yyyy년 M월 d일 HH:mm} 수정  ·  {ContentTextBox.Text.Length:N0}자";
        }

        private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingMemo || _currentMemo == null) return;
            MarkDirty();
        }

        private void ContentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingMemo || _currentMemo == null) return;

            var marks = _currentMemo.AudioMarks;
            foreach (var change in e.Changes)
            {
                ShiftMarks(marks, change.Offset, change.RemovedLength, change.AddedLength);

                if (_isRecording && _recordingMemo == _currentMemo && change.AddedLength > 0)
                    RecordTyping(change.Offset, change.AddedLength);
            }

            marks.RemoveAll(m => m.End <= m.Start);
            if (_activeMark != null && !marks.Contains(_activeMark))
                _activeMark = null;

            MarkDirty();
            UpdateMetaText();
            if (_audioFileReader != null) UpdateHighlights();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        private void FlushPendingSave()
        {
            if (_autoSaveTimer.IsEnabled || _dirty)
            {
                _autoSaveTimer.Stop();
                SaveCurrentMemo();
            }
        }

        private void SaveCurrentMemo()
        {
            if (_currentMemo == null || _vault == null) return;

            _currentMemo.Title = TitleTextBox.Text;
            _currentMemo.Content = ContentTextBox.Text;
            if (_dirty) _currentMemo.UpdatedAt = DateTime.Now;
            _dirty = false;

            _vault.SaveMemo(_currentMemo);
            RefreshList();
            UpdateMetaText();
        }

        private void ExportWord_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMemo == null) return;
            FlushPendingSave();

            var safeName = string.Concat(_currentMemo.DisplayTitle.Split(System.IO.Path.GetInvalidFileNameChars()));
            var dialog = new SaveFileDialog
            {
                Filter = "Word 문서|*.docx",
                FileName = $"{safeName}.docx"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                _export.ExportToDocx(_currentMemo, dialog.FileName);
                ShowToast("Word 문서로 내보냈습니다");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"내보내기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            FlushPendingSave();

            var settingsWindow = new SettingsWindow(() => _memos.ToList()) { Owner = this };
            settingsWindow.ShowDialog();
        }

        #endregion

        #region 녹음 ↔ 메모 연결

        /// <summary>
        /// 텍스트 변경(offset 위치에서 removed 글자 삭제, added 글자 삽입)에 맞춰
        /// 기존 구간들의 문자 범위를 이동시킨다.
        /// </summary>
        private static void ShiftMarks(List<AudioMark> marks, int offset, int removed, int added)
        {
            var removedEnd = offset + removed;
            var delta = added - removed;

            foreach (var m in marks)
            {
                if (m.End <= offset)
                    continue; // 변경 위치보다 앞 → 그대로

                if (m.Start >= removedEnd)
                {
                    // 변경 위치보다 뒤 → 통째로 이동
                    m.Start += delta;
                    m.End += delta;
                    continue;
                }

                // 겹침 → 구간 안에서 수정된 것으로 보고 범위 조정
                m.Start = Math.Min(m.Start, offset);
                m.End = m.End >= removedEnd ? m.End + delta : offset + added;
            }
        }

        /// <summary>녹음 중 입력된 텍스트를 현재 녹음 시점과 연결</summary>
        private void RecordTyping(int offset, int added)
        {
            if (_currentMemo == null) return;

            var text = ContentTextBox.Text;
            if (offset < 0 || offset + added > text.Length) return;

            var inserted = text.Substring(offset, added);
            var t = _recordClock.Elapsed.TotalSeconds;

            // 줄바꿈만 입력 → 현재 구간 종료 (줄 단위로 구간 분리)
            if (inserted.All(c => c == '\r' || c == '\n'))
            {
                _activeMark = null;
                return;
            }

            var marks = _currentMemo.AudioMarks;
            var active = _activeMark;

            if (active != null && marks.Contains(active)
                && t - active.EndTime <= MarkGapSeconds
                && offset >= active.Start && offset <= active.End)
            {
                active.Start = Math.Min(active.Start, offset);
                active.End = Math.Max(active.End, offset + added);
                active.EndTime = t;
                return;
            }

            // 공백만으로는 새 구간을 시작하지 않음
            if (string.IsNullOrWhiteSpace(inserted)) return;

            _activeMark = new AudioMark
            {
                Start = offset,
                End = offset + added,
                StartTime = t,
                EndTime = t
            };
            marks.Add(_activeMark);
        }

        private void Record_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecording)
                StopRecordingIfActive();
            else
                StartRecording();
        }

        private void StartRecording()
        {
            if (_currentMemo == null || _vault == null) return;
            var memo = _currentMemo;

            if (memo.HasAudio)
            {
                var result = MessageBox.Show(
                    "이 메모에는 이미 녹음이 있습니다.\n새로 녹음하면 기존 녹음과 메모 연결 표시가 대체됩니다.\n계속하시겠습니까?",
                    "새 녹음", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }

            StopAudio();

            try
            {
                _audio.StartRecording(_vault, memo.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"녹음 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 기존 녹음 정리
            _vault.DeleteAudio(memo.AudioPath);
            memo.AudioPath = null;
            memo.AudioMarks.Clear();
            _activeMark = null;

            _recordingMemo = memo;
            _isRecording = true;
            _recordClock.Restart();
            _recordTimer.Start();

            RecordTimeText.Text = "00:00";
            RecordIcon.Text = "";
            RecordLabel.Text = "중지";
            RecordButton.Foreground = (Brush)FindResource("RecordBrush");
            RecordingIndicator.Visibility = Visibility.Visible;
            RecordDot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0.2, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

            UpdateEditorState();
            ContentTextBox.Focus();
            ContentTextBox.CaretIndex = ContentTextBox.Text.Length;
        }

        private void StopRecordingIfActive(bool showToast = true)
        {
            if (!_isRecording) return;

            var memo = _recordingMemo;
            string? audioPath = null;
            string? error = null;
            try
            {
                audioPath = _audio.StopRecording();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            _isRecording = false;
            _recordingMemo = null;
            _activeMark = null;
            _recordClock.Stop();
            _recordTimer.Stop();

            RecordIcon.Text = "";
            RecordLabel.Text = "녹음";
            RecordButton.ClearValue(ForegroundProperty);
            RecordDot.BeginAnimation(OpacityProperty, null);
            RecordingIndicator.Visibility = Visibility.Collapsed;

            if (memo != null)
            {
                memo.AudioPath = audioPath;
                if (memo == _currentMemo)
                {
                    _dirty = true;
                    _autoSaveTimer.Stop();
                    SaveCurrentMemo();
                }
                else
                {
                    memo.UpdatedAt = DateTime.Now;
                    _vault?.SaveMemo(memo);
                    RefreshList();
                }
            }

            LoadAudioInfo();
            UpdateEditorState();

            if (error != null)
                MessageBox.Show($"녹음 오류: {error}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            else if (showToast && memo != null)
                ShowToast($"녹음이 저장되었습니다  ·  연결된 메모 {memo.AudioMarks.Count}개");
        }

        #endregion

        #region 재생

        private void LoadAudioInfo()
        {
            _audioDuration = 0;
            if (_currentMemo?.AudioPath is { } path && _vault?.AudioExists(path) == true)
            {
                try
                {
                    using var reader = _vault.OpenAudio(path);
                    _audioDuration = reader.TotalTime.TotalSeconds;
                }
                catch
                {
                    _audioDuration = 0;
                }
            }

            _updatingSlider = true;
            AudioSlider.Maximum = Math.Max(_audioDuration, 0.1);
            AudioSlider.Value = 0;
            _updatingSlider = false;

            CurrentTimeText.Text = FormatTime(0);
            TotalTimeText.Text = FormatTime(_audioDuration);
            PlayButton.Content = "";
            DrawMarkTicks();
        }

        private bool EnsurePlayer()
        {
            if (_waveOut != null) return true;

            var path = _currentMemo?.AudioPath;
            if (_vault == null || string.IsNullOrEmpty(path) || !_vault.AudioExists(path))
            {
                ShowToast("녹음 파일을 찾을 수 없습니다");
                return false;
            }

            try
            {
                _audioFileReader = _vault.OpenAudio(path);
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioFileReader);
                _waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                _audioDuration = _audioFileReader.TotalTime.TotalSeconds;
                _audioTimer.Start();
                return true;
            }
            catch (Exception ex)
            {
                StopAudio();
                MessageBox.Show($"재생 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsurePlayer() || _waveOut == null || _audioFileReader == null) return;

            if (_waveOut.PlaybackState == PlaybackState.Playing)
            {
                _waveOut.Pause();
                PlayButton.Content = "";
            }
            else
            {
                if (_audioFileReader.CurrentTime.TotalSeconds >= _audioDuration - 0.05)
                    _audioFileReader.CurrentTime = TimeSpan.Zero;
                _waveOut.Play();
                PlayButton.Content = "";
            }
            UpdatePlaybackUi();
        }

        private void PlayFrom(double seconds)
        {
            if (!EnsurePlayer() || _waveOut == null) return;
            SeekTo(seconds);
            if (_waveOut.PlaybackState != PlaybackState.Playing)
            {
                _waveOut.Play();
                PlayButton.Content = "";
            }
        }

        private void SeekTo(double seconds)
        {
            if (!EnsurePlayer() || _audioFileReader == null) return;
            seconds = Math.Clamp(seconds, 0, Math.Max(0, _audioDuration - 0.05));
            _audioFileReader.CurrentTime = TimeSpan.FromSeconds(seconds);
            UpdatePlaybackUi();
        }

        private void AudioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingSlider || _currentMemo?.HasAudio != true) return;
            SeekTo(e.NewValue);
        }

        private void UpdatePlaybackUi()
        {
            if (_audioFileReader == null) return;

            var t = _audioFileReader.CurrentTime.TotalSeconds;
            _updatingSlider = true;
            AudioSlider.Value = Math.Min(t, AudioSlider.Maximum);
            _updatingSlider = false;
            CurrentTimeText.Text = FormatTime(t);

            UpdateHighlights();
        }

        private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                StopAudio();
                if (e.Exception != null)
                    MessageBox.Show($"재생 오류: {e.Exception.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void StopAudio()
        {
            _audioTimer.Stop();

            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }

            if (_audioFileReader != null)
            {
                _audioFileReader.Dispose();
                _audioFileReader = null;
            }

            PlayButton.Content = "";
            _updatingSlider = true;
            AudioSlider.Value = 0;
            _updatingSlider = false;
            CurrentTimeText.Text = FormatTime(0);

            _visibleMarks.Clear();
            HighlightCanvas.Children.Clear();
        }

        /// <summary>타임라인 위에 메모가 작성된 지점을 표시</summary>
        private void DrawMarkTicks()
        {
            MarkTicksCanvas.Children.Clear();
            if (_currentMemo == null || _audioDuration <= 0) return;

            var width = MarkTicksCanvas.ActualWidth;
            if (width <= 0) return;

            var text = ContentTextBox.Text;
            foreach (var mark in _currentMemo.AudioMarks.OrderBy(m => m.StartTime))
            {
                var x = Math.Clamp(mark.StartTime / _audioDuration, 0, 1) * width;
                var snippet = SafeSubstring(text, mark.Start, mark.End).Replace("\r", " ").Replace("\n", " ").Trim();
                if (snippet.Length > 40) snippet = snippet[..40] + "…";

                var tick = new Rectangle
                {
                    Width = 3,
                    Height = 8,
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Fill = Brushes.White,
                    Opacity = 0.7,
                    Cursor = Cursors.Hand,
                    ToolTip = $"{FormatTime(mark.StartTime)}  {snippet}"
                };
                var target = mark.StartTime;
                tick.MouseEnter += (_, _) => tick.Opacity = 1;
                tick.MouseLeave += (_, _) => tick.Opacity = 0.7;
                tick.MouseLeftButtonDown += (_, args) =>
                {
                    PlayFrom(target - 0.5);
                    args.Handled = true;
                };

                Canvas.SetLeft(tick, x - 1.5);
                Canvas.SetTop(tick, 0);
                MarkTicksCanvas.Children.Add(tick);
            }
        }

        private void MarkTicksCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMarkTicks();

        private void ContentTextBox_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateHighlights();

        /// <summary>Ctrl+클릭: 해당 메모가 작성된 시점부터 재생</summary>
        private void ContentTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || _currentMemo?.HasAudio != true || _isRecording) return;

            var index = ContentTextBox.GetCharacterIndexFromPoint(e.GetPosition(ContentTextBox), true);
            if (index < 0) return;

            var mark = _currentMemo.AudioMarks
                .Where(m => index >= m.Start && index < m.End)
                .OrderBy(m => m.StartTime)
                .FirstOrDefault();
            if (mark == null) return;

            PlayFrom(mark.StartTime - 0.5);
            e.Handled = true;
        }

        /// <summary>
        /// 현재 재생 위치에 해당하는 메모 구간에 흰색 테두리를 그린다.
        /// </summary>
        private void UpdateHighlights()
        {
            if (_audioFileReader == null || _currentMemo == null)
            {
                HighlightCanvas.Children.Clear();
                return;
            }

            var t = _audioFileReader.CurrentTime.TotalSeconds;
            var active = _currentMemo.AudioMarks
                .Where(m => t >= m.StartTime - MarkLeadSeconds && t <= m.EndTime + MarkHoldSeconds)
                .ToList();

            // 새로 나타난 구간이 화면 밖이면 그 위치로 스크롤
            // (스크롤 시 ScrollChanged로 이 메서드가 재진입할 수 있으므로 상태를 먼저 갱신)
            var isPlaying = _waveOut?.PlaybackState == PlaybackState.Playing;
            var newlyActive = active.FirstOrDefault(m => !_visibleMarks.Contains(m));
            _visibleMarks = active.ToHashSet();
            if (isPlaying && newlyActive != null)
                EnsureMarkVisible(newlyActive);

            HighlightCanvas.Children.Clear();
            foreach (var mark in active)
                DrawMarkOutline(mark);
        }

        private void EnsureMarkVisible(AudioMark mark)
        {
            var text = ContentTextBox.Text;
            if (text.Length == 0) return;

            var start = Math.Clamp(mark.Start, 0, text.Length - 1);
            var line = ContentTextBox.GetLineIndexFromCharacterIndex(start);
            var first = ContentTextBox.GetFirstVisibleLineIndex();
            var last = ContentTextBox.GetLastVisibleLineIndex();
            if (line < 0 || first < 0) return;

            if (line < first || line > last - 1)
            {
                ContentTextBox.ScrollToLine(Math.Max(0, line - 2));
                ContentTextBox.UpdateLayout();
            }
        }

        private void DrawMarkOutline(AudioMark mark)
        {
            var text = ContentTextBox.Text;
            var s = Math.Clamp(mark.Start, 0, text.Length);
            var e = Math.Clamp(mark.End, 0, text.Length);
            while (s < e && char.IsWhiteSpace(text[s])) s++;
            while (e > s && char.IsWhiteSpace(text[e - 1])) e--;
            if (s >= e) return;

            var firstLine = ContentTextBox.GetLineIndexFromCharacterIndex(s);
            var lastLine = ContentTextBox.GetLineIndexFromCharacterIndex(e - 1);
            var firstVisible = ContentTextBox.GetFirstVisibleLineIndex();
            var lastVisible = ContentTextBox.GetLastVisibleLineIndex();
            if (firstLine < 0 || lastLine < 0 || firstVisible < 0) return;

            // 화면에 보이는 줄만 그림
            var from = Math.Max(firstLine, firstVisible);
            var to = Math.Min(lastLine, lastVisible);

            for (var line = from; line <= to; line++)
            {
                var lineStart = ContentTextBox.GetCharacterIndexFromLineIndex(line);
                var lineLength = ContentTextBox.GetLineLength(line);
                if (lineStart < 0) continue;

                var a = Math.Max(s, lineStart);
                var b = Math.Min(e, lineStart + lineLength);
                while (b > a && char.IsWhiteSpace(text[b - 1])) b--;
                while (a < b && char.IsWhiteSpace(text[a])) a++;
                if (a >= b) continue;

                var r1 = ContentTextBox.GetRectFromCharacterIndex(a);
                var r2 = ContentTextBox.GetRectFromCharacterIndex(b - 1, true);
                if (r1.IsEmpty || r2.IsEmpty) continue;

                var left = Math.Min(r1.Left, r2.Right);
                var right = Math.Max(r1.Left, r2.Right);
                var top = Math.Min(r1.Top, r2.Top);
                var bottom = Math.Max(r1.Bottom, r2.Bottom);

                var outline = new Border
                {
                    Width = right - left + 8,
                    Height = bottom - top + 4,
                    BorderBrush = HighlightStroke,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(4),
                    Background = HighlightFill
                };
                Canvas.SetLeft(outline, left - 4);
                Canvas.SetTop(outline, top - 2);
                HighlightCanvas.Children.Add(outline);
            }
        }

        #endregion

        #region 유틸

        private static string FormatTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"mm\:ss");
        }

        private static string SafeSubstring(string text, int start, int end)
        {
            start = Math.Clamp(start, 0, text.Length);
            end = Math.Clamp(end, start, text.Length);
            return text[start..end];
        }

        private void ShowToast(string message)
        {
            ToastText.Text = message;
            Toast.BeginAnimation(OpacityProperty, null);
            Toast.Opacity = 1;
            Toast.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void HideToast()
        {
            _toastTimer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
            fade.Completed += (_, _) =>
            {
                if (Toast.Opacity == 0) Toast.Visibility = Visibility.Collapsed;
            };
            Toast.BeginAnimation(OpacityProperty, fade);
        }

        #endregion

        #region 개발자 지원

        private void CoffeeBanner_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var qrWindow = new Window
                {
                    Title = "개발자에게 커피 사주기",
                    Width = 420,
                    Height = 540,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Background = (Brush)FindResource("PanelBrush"),
                    ResizeMode = ResizeMode.NoResize
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var titleText = new TextBlock
                {
                    Text = "☕ 개발자에게 커피 사주기",
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 20),
                    Foreground = (Brush)FindResource("TextBrush")
                };
                Grid.SetRow(titleText, 0);
                grid.Children.Add(titleText);

                var qrBorder = new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(14),
                    Margin = new Thickness(48, 0, 48, 0),
                    Padding = new Thickness(16)
                };

                var qrGenerator = new QRCodeGenerator();
                var qrCodeData = qrGenerator.CreateQrCode("https://qr.kakaopay.com/Ej9Q6cblA9c409206", QRCodeGenerator.ECCLevel.Q);
                var qrCodeBytes = new PngByteQRCode(qrCodeData).GetGraphic(20);

                var qrImage = new Image { Stretch = Stretch.Uniform };
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

                var descText = new TextBlock
                {
                    Text = "이 프로그램이 도움이 되셨나요?\nQR 코드를 스캔하여 개발자에게\n커피 한 잔의 응원을 보내주세요!",
                    FontSize = 13,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 24),
                    Foreground = (Brush)FindResource("MutedBrush")
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

        private void BlogBanner_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://1st-life-2nd.tistory.com/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"블로그를 열 수 없습니다: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        protected override void OnClosing(CancelEventArgs e)
        {
            StopRecordingIfActive(showToast: false);
            FlushPendingSave();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            StopAudio();
            _audio.Dispose();
            _vault?.Dispose();
        }
    }
}
