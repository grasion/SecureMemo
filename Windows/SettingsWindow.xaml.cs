using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SecureMemo.Models;
using SecureMemo.Services;

namespace SecureMemo.Windows
{
    public partial class SettingsWindow : Window
    {
        private readonly Func<IReadOnlyList<Memo>> _getMemos;
        private readonly ExportService _export = new();
        private readonly UpdateService _update;
        private string? _updateFilePath;

        public SettingsWindow(Func<IReadOnlyList<Memo>> getMemos)
        {
            InitializeComponent();
            _getMemos = getMemos;
            _update = new UpdateService();
            VersionText.Text = $"현재 버전: v{_update.CurrentVersion}";

            var count = _getMemos().Count;
            ExportDescText.Text = $"현재 메모 공간의 메모 {count}개를 하나의 Word 문서로 저장합니다. (메모마다 새 페이지)";
            ExportAllButton.IsEnabled = count > 0;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ExportAll_Click(object sender, RoutedEventArgs e)
        {
            var memos = _getMemos().OrderByDescending(m => m.UpdatedAt).ToList();
            if (memos.Count == 0) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Word 문서|*.docx",
                FileName = $"SecureMemo_전체메모_{DateTime.Now:yyyyMMdd}.docx"
            };
            if (dialog.ShowDialog(this) != true) return;

            ExportStatusText.Visibility = Visibility.Visible;
            try
            {
                _export.ExportAllToDocx(memos, dialog.FileName);
                ExportStatusText.Text = $"✓ 메모 {memos.Count}개를 내보냈습니다: {Path.GetFileName(dialog.FileName)}";
                ExportStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                ExportStatusText.Text = $"내보내기 실패: {ex.Message}";
                ExportStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
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
