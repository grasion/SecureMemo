using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SecureMemo.Services;

namespace SecureMemo.Windows
{
    public partial class PasswordMergeWindow : Window
    {
        private readonly StorageService _storage;
        private readonly EncryptionService _encryption;
        private readonly List<string> _passwordHashes;
        private bool _passwordVisible = false;

        public bool Merged { get; private set; }
        public string? SelectedPasswordHash { get; private set; }

        public PasswordMergeWindow(StorageService storage, EncryptionService encryption, List<string> passwordHashes)
        {
            InitializeComponent();
            _storage = storage;
            _encryption = encryption;
            _passwordHashes = passwordHashes;

            var totalMemos = passwordHashes.Sum(h => _storage.GetMemoCountForPassword(h));
            PasswordCountText.Text = $"총 {passwordHashes.Count}개의 다른 비밀번호로 저장된 {totalMemos}개의 노트가 있습니다.";
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            _passwordVisible = !_passwordVisible;
            
            if (_passwordVisible)
            {
                MergePasswordTextBox.Text = MergePasswordBox.Password;
                MergePasswordBox.Visibility = Visibility.Collapsed;
                MergePasswordTextBox.Visibility = Visibility.Visible;
            }
            else
            {
                MergePasswordBox.Password = MergePasswordTextBox.Text;
                MergePasswordTextBox.Visibility = Visibility.Collapsed;
                MergePasswordBox.Visibility = Visibility.Visible;
            }
        }

        private void MergePasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Merge_Click(sender, e);
        }

        private void Merge_Click(object sender, RoutedEventArgs e)
        {
            var password = _passwordVisible ? MergePasswordTextBox.Text : MergePasswordBox.Password;
            
            if (string.IsNullOrWhiteSpace(password))
            {
                StatusText.Text = "비밀번호를 입력하세요";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            var passwordHash = _encryption.HashPassword(password);
            
            // 입력한 비밀번호가 기존 비밀번호 중 하나인지 확인
            if (!_passwordHashes.Contains(passwordHash))
            {
                StatusText.Text = "입력한 비밀번호로 저장된 노트가 없습니다";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            try
            {
                // 암호화 키 설정
                _encryption.SetMasterKey(password);
                
                // 다른 비밀번호의 데이터를 현재 비밀번호로 통합
                var otherHashes = _passwordHashes.Where(h => h != passwordHash).ToList();
                
                if (otherHashes.Any())
                {
                    StatusText.Text = "통합 중...";
                    StatusText.Foreground = System.Windows.Media.Brushes.Gray;
                    
                    _storage.SetCurrentPasswordHash(passwordHash);
                    _storage.MergePasswordData(otherHashes, passwordHash);
                }

                SelectedPasswordHash = passwordHash;
                Merged = true;
                
                MessageBox.Show(
                    $"총 {_passwordHashes.Count}개의 비밀번호가 통합되었습니다.",
                    "완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"오류: {ex.Message}";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "통합하지 않고 계속하시겠습니까?\n첫 번째 비밀번호의 노트만 표시됩니다.",
                "확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SelectedPasswordHash = _passwordHashes.First();
                Close();
            }
        }
    }
}
