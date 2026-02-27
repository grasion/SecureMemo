using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SecureMemo.Services
{
    public class EncryptionService
    {
        private static EncryptionService? _instance;
        private byte[]? _key;
        private byte[]? _iv;

        public static EncryptionService Instance => _instance ??= new EncryptionService();

        public void SetMasterKey(string password)
        {
            using var sha256 = SHA256.Create();
            _key = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            _iv = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "IV")).Take(16).ToArray();
        }

        public byte[]? GetKey() => _key;

        public void RestoreKey(byte[]? key)
        {
            _key = key;
            if (_key != null)
            {
                using var sha256 = SHA256.Create();
                _iv = sha256.ComputeHash(_key).Take(16).ToArray();
            }
        }

        public string Encrypt(string plainText)
        {
            if (_key == null || _iv == null)
                throw new InvalidOperationException("Master key not set");

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
            using var sw = new StreamWriter(cs);
            sw.Write(plainText);
            sw.Close();
            return Convert.ToBase64String(ms.ToArray());
        }

        public string Decrypt(string cipherText)
        {
            if (_key == null || _iv == null)
                throw new InvalidOperationException("Master key not set");

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(Convert.FromBase64String(cipherText));
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }

        public string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hash);
        }

        public bool VerifyPassword(string password, string storedHash)
        {
            return HashPassword(password) == storedHash;
        }
    }
}
