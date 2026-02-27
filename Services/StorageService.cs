using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SecureMemo.Models;

namespace SecureMemo.Services
{
    public class StorageService
    {
        private readonly string _dataPath;
        private readonly EncryptionService _encryption;
        private string _currentPasswordHash = "default"; // 기본 암호화용

        public StorageService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _dataPath = Path.Combine(appData, "SecureMemo", "memos");
            _encryption = EncryptionService.Instance;
            Directory.CreateDirectory(_dataPath);
        }

        public void SetCurrentPasswordHash(string hash)
        {
            _currentPasswordHash = hash;
        }

        public void SaveMemo(Memo memo)
        {
            var json = JsonConvert.SerializeObject(memo);
            var encrypted = _encryption.Encrypt(json);
            
            // 비밀번호 해시별로 폴더 구분
            var folderPath = Path.Combine(_dataPath, _currentPasswordHash);
            Directory.CreateDirectory(folderPath);
            
            var filePath = Path.Combine(folderPath, $"{memo.Id}.enc");
            File.WriteAllText(filePath, encrypted);
        }

        public Memo? LoadMemo(string id)
        {
            try
            {
                var folderPath = Path.Combine(_dataPath, _currentPasswordHash);
                var filePath = Path.Combine(folderPath, $"{id}.enc");
                if (!File.Exists(filePath)) return null;
                
                var encrypted = File.ReadAllText(filePath);
                var decrypted = _encryption.Decrypt(encrypted);
                return JsonConvert.DeserializeObject<Memo>(decrypted);
            }
            catch
            {
                return null;
            }
        }

        public List<Memo> LoadAllMemos()
        {
            var memos = new List<Memo>();
            var folderPath = Path.Combine(_dataPath, _currentPasswordHash);
            
            if (!Directory.Exists(folderPath))
                return memos;

            var files = Directory.GetFiles(folderPath, "*.enc");

            foreach (var file in files)
            {
                try
                {
                    var encrypted = File.ReadAllText(file);
                    var decrypted = _encryption.Decrypt(encrypted);
                    var memo = JsonConvert.DeserializeObject<Memo>(decrypted);
                    if (memo != null)
                        memos.Add(memo);
                }
                catch
                {
                    // 복호화 실패한 파일은 건너뜀
                }
            }

            return memos.OrderByDescending(m => m.UpdatedAt).ToList();
        }

        public void DeleteMemo(string id)
        {
            var folderPath = Path.Combine(_dataPath, _currentPasswordHash);
            var filePath = Path.Combine(folderPath, $"{id}.enc");
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        public List<string> GetAllPasswordHashes()
        {
            var hashes = new List<string>();
            
            if (!Directory.Exists(_dataPath))
                return hashes;

            var folders = Directory.GetDirectories(_dataPath);
            foreach (var folder in folders)
            {
                var folderName = Path.GetFileName(folder);
                if (!string.IsNullOrEmpty(folderName))
                    hashes.Add(folderName);
            }

            return hashes;
        }

        public int GetMemoCountForPassword(string passwordHash)
        {
            var folderPath = Path.Combine(_dataPath, passwordHash);
            if (!Directory.Exists(folderPath))
                return 0;

            return Directory.GetFiles(folderPath, "*.enc").Length;
        }

        public void MergePasswordData(List<string> sourceHashes, string targetHash)
        {
            foreach (var sourceHash in sourceHashes)
            {
                if (sourceHash == targetHash) continue;

                var sourcePath = Path.Combine(_dataPath, sourceHash);
                var targetPath = Path.Combine(_dataPath, targetHash);
                
                if (!Directory.Exists(sourcePath)) continue;
                
                Directory.CreateDirectory(targetPath);

                var files = Directory.GetFiles(sourcePath, "*.enc");
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var destFile = Path.Combine(targetPath, fileName);
                    
                    // 파일명 충돌 시 새 ID 생성
                    if (File.Exists(destFile))
                    {
                        var newId = Guid.NewGuid().ToString();
                        destFile = Path.Combine(targetPath, $"{newId}.enc");
                    }
                    
                    File.Move(file, destFile);
                }

                // 빈 폴더 삭제
                try
                {
                    Directory.Delete(sourcePath);
                }
                catch { }
            }
        }

        public void SaveApiKey(string apiKey)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var keyPath = Path.Combine(appData, "SecureMemo", "api.enc");
            
            // API 키는 기본 암호화 사용
            var tempKey = _encryption.GetKey();
            _encryption.SetMasterKey("SecureMemoDefaultKey");
            var encrypted = _encryption.Encrypt(apiKey);
            _encryption.RestoreKey(tempKey);
            
            File.WriteAllText(keyPath, encrypted);
        }

        public string? LoadApiKey()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var keyPath = Path.Combine(appData, "SecureMemo", "api.enc");
                if (!File.Exists(keyPath)) return null;
                
                var encrypted = File.ReadAllText(keyPath);
                
                var tempKey = _encryption.GetKey();
                _encryption.SetMasterKey("SecureMemoDefaultKey");
                var decrypted = _encryption.Decrypt(encrypted);
                _encryption.RestoreKey(tempKey);
                
                return decrypted;
            }
            catch
            {
                return null;
            }
        }

        public void SavePasswordHash(string hash)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var hashPath = Path.Combine(appData, "SecureMemo", "pwd.hash");
            Directory.CreateDirectory(Path.GetDirectoryName(hashPath)!);
            File.WriteAllText(hashPath, hash);
        }

        public string? LoadPasswordHash()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var hashPath = Path.Combine(appData, "SecureMemo", "pwd.hash");
            return File.Exists(hashPath) ? File.ReadAllText(hashPath) : null;
        }

        public void DeletePasswordHash()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var hashPath = Path.Combine(appData, "SecureMemo", "pwd.hash");
            if (File.Exists(hashPath))
                File.Delete(hashPath);
        }
    }
}
