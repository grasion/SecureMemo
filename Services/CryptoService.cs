using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SecureMemo.Services
{
    /// <summary>
    /// 비밀번호 기반 키 유도(PBKDF2) + AES-256-GCM 암복호화
    /// </summary>
    public static class CryptoService
    {
        public const int KeySize = 32;
        public const int NonceSize = 12;
        public const int TagSize = 16;
        public const int Pbkdf2Iterations = 200_000;

        public static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeySize);

        public static byte[] RandomBytes(int length) => RandomNumberGenerator.GetBytes(length);

        /// <summary>결과: nonce(12) | tag(16) | ciphertext</summary>
        public static byte[] Seal(byte[] key, ReadOnlySpan<byte> plain, ReadOnlySpan<byte> aad)
        {
            var output = new byte[NonceSize + TagSize + plain.Length];
            var nonce = output.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);

            using var gcm = new AesGcm(key, TagSize);
            gcm.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), aad);
            return output;
        }

        /// <summary>비밀번호(키)가 틀리거나 데이터가 변조되면 CryptographicException</summary>
        public static byte[] Open(byte[] key, ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> aad)
        {
            if (sealedData.Length < NonceSize + TagSize)
                throw new CryptographicException("데이터가 손상되었습니다");

            var plain = new byte[sealedData.Length - NonceSize - TagSize];
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(sealedData[..NonceSize], sealedData[(NonceSize + TagSize)..],
                sealedData.Slice(NonceSize, TagSize), plain, aad);
            return plain;
        }

        #region 이전 버전(v1.0.x) 형식 — 마이그레이션 전용

        public const string LegacyDefaultPassword = "SecureMemoDefaultKey";

        /// <summary>이전 버전이 폴더 이름/pwd.hash로 쓰던 값</summary>
        public static string LegacyHash(string password) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

        public static string LegacyDecrypt(string password, string cipherText)
        {
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            var iv = SHA256.HashData(Encoding.UTF8.GetBytes(password + "IV")).Take(16).ToArray();

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(Convert.FromBase64String(cipherText));
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }

        #endregion
    }
}
