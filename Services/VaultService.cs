using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;
using Newtonsoft.Json;
using SecureMemo.Models;

namespace SecureMemo.Services
{
    /// <summary>
    /// 하나의 비밀번호로 열리는 메모 공간.
    /// 메모(.enc)와 녹음(.sma)은 모두 비밀번호에서 유도한 키로 암호화되어 저장된다.
    /// </summary>
    public sealed class Vault : IDisposable
    {
        private static readonly byte[] MemoAad = "SecureMemo.memo.v2"u8.ToArray();
        private readonly byte[] _key;

        public string Directory { get; }
        private string MemoDir => Path.Combine(Directory, "memos");
        private string AudioDir => Path.Combine(Directory, "audio");

        internal Vault(string directory, byte[] key)
        {
            Directory = directory;
            _key = key;
            System.IO.Directory.CreateDirectory(MemoDir);
            System.IO.Directory.CreateDirectory(AudioDir);
        }

        #region 메모

        private string MemoPath(string id) => Path.Combine(MemoDir, $"{Path.GetFileName(id)}.enc");

        public bool MemoExists(string id) => File.Exists(MemoPath(id));

        public void SaveMemo(Memo memo)
        {
            var json = JsonConvert.SerializeObject(memo);
            var sealedData = CryptoService.Seal(_key, Encoding.UTF8.GetBytes(json), MemoAad);

            // 임시 파일에 쓴 뒤 교체 → 저장 중 종료돼도 기존 파일이 깨지지 않음
            var path = MemoPath(memo.Id);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, sealedData);
            File.Move(temp, path, overwrite: true);
        }

        public Memo? LoadMemo(string id) => ReadMemoFile(MemoPath(id));

        private Memo? ReadMemoFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var plain = CryptoService.Open(_key, File.ReadAllBytes(path), MemoAad);
                var memo = JsonConvert.DeserializeObject<Memo>(Encoding.UTF8.GetString(plain));
                if (memo != null) memo.AudioMarks ??= new List<AudioMark>();
                return memo;
            }
            catch
            {
                return null;
            }
        }

        public List<Memo> LoadAllMemos()
        {
            return System.IO.Directory.GetFiles(MemoDir, "*.enc")
                .Select(ReadMemoFile)
                .Where(m => m != null)
                .Select(m => m!)
                .OrderByDescending(m => m.UpdatedAt)
                .ToList();
        }

        public void DeleteMemo(string id)
        {
            var path = MemoPath(id);
            if (File.Exists(path)) File.Delete(path);
        }

        #endregion

        #region 녹음

        private string AudioPath(string name) => Path.Combine(AudioDir, Path.GetFileName(name));

        public static string NewAudioFileName(string memoId) => $"{memoId}_{DateTime.Now.Ticks}.sma";

        public bool AudioExists(string? name) => !string.IsNullOrEmpty(name) && File.Exists(AudioPath(name));

        public EncryptedAudioWriter CreateAudioWriter(string name, WaveFormat format) =>
            new(AudioPath(name), _key, format);

        public WaveStream OpenAudio(string name) =>
            new EncryptedWaveStream(new EncryptedAudioStream(AudioPath(name), _key));

        public void DeleteAudio(string? name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                var path = AudioPath(name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 삭제 실패 무시
            }
        }

        /// <summary>암호화되지 않은 WAV 파일을 암호화된 녹음으로 변환</summary>
        public string ImportPlainWav(string wavPath, string memoId)
        {
            var name = NewAudioFileName(memoId);
            using (var reader = new WaveFileReader(wavPath))
            using (var writer = CreateAudioWriter(name, reader.WaveFormat))
            {
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    writer.Write(buffer, 0, read);
            }

            // 변환 결과 확인 후 반환
            using (var check = OpenAudio(name))
            {
                if (check.Length <= 0 && new FileInfo(wavPath).Length > 100)
                    throw new InvalidDataException("녹음 변환 확인 실패");
            }
            return name;
        }

        #endregion

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(_key);
        }
    }

    /// <summary>비밀번호별 메모 공간(Vault) 관리 + 이전 버전 데이터 이전</summary>
    public static class VaultManager
    {
        private static readonly byte[] CheckAad = "SecureMemo.vault.v2"u8.ToArray();
        private static readonly byte[] CheckPlain = "SecureMemo vault check"u8.ToArray();

        public static string Root { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SecureMemo");

        private static string VaultsDir => Path.Combine(Root, "vaults");
        private static string LegacyMemosDir => Path.Combine(Root, "memos");
        private static string LegacyAudioDir => Path.Combine(Root, "audio");

        private class VaultMeta
        {
            public int Version { get; set; } = 2;
            public string Salt { get; set; } = "";
            public int Iterations { get; set; }
            public string Check { get; set; } = "";
        }

        /// <summary>비밀번호에 맞는 메모 공간을 찾는다. 없으면 null</summary>
        public static Vault? TryOpen(string password)
        {
            if (!Directory.Exists(VaultsDir)) return null;

            foreach (var dir in Directory.GetDirectories(VaultsDir))
            {
                try
                {
                    var metaPath = Path.Combine(dir, "vault.json");
                    if (!File.Exists(metaPath)) continue;

                    var meta = JsonConvert.DeserializeObject<VaultMeta>(File.ReadAllText(metaPath));
                    if (meta == null) continue;

                    var key = CryptoService.DeriveKey(password, Convert.FromBase64String(meta.Salt), meta.Iterations);
                    try
                    {
                        CryptoService.Open(key, Convert.FromBase64String(meta.Check), CheckAad);
                        return new Vault(dir, key);
                    }
                    catch (CryptographicException)
                    {
                        CryptographicOperations.ZeroMemory(key);
                    }
                }
                catch
                {
                    // 손상된 메모 공간은 건너뜀
                }
            }
            return null;
        }

        /// <summary>새 비밀번호로 새 메모 공간 생성</summary>
        public static Vault Create(string password)
        {
            var dir = Path.Combine(VaultsDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            var salt = CryptoService.RandomBytes(16);
            var key = CryptoService.DeriveKey(password, salt, CryptoService.Pbkdf2Iterations);
            var meta = new VaultMeta
            {
                Salt = Convert.ToBase64String(salt),
                Iterations = CryptoService.Pbkdf2Iterations,
                Check = Convert.ToBase64String(CryptoService.Seal(key, CheckPlain, CheckAad))
            };
            File.WriteAllText(Path.Combine(dir, "vault.json"), JsonConvert.SerializeObject(meta, Formatting.Indented));
            return new Vault(dir, key);
        }

        #region 이전 버전 데이터 이전

        /// <summary>이 비밀번호로 저장된 이전 버전 메모가 있는지</summary>
        public static bool HasLegacyDataFor(string password)
        {
            var dir = Path.Combine(LegacyMemosDir, CryptoService.LegacyHash(password));
            return Directory.Exists(dir) && Directory.GetFiles(dir, "*.enc").Length > 0;
        }

        /// <summary>
        /// 이전 버전 형식의 메모를 열린 메모 공간으로 옮겨 새 방식으로 다시 암호화한다.
        ///  - 같은 비밀번호로 저장된 메모
        ///  - 비밀번호 없이(기본 키로) 저장된 메모
        ///  - 암호화되지 않은 녹음 파일
        /// 새 형식으로 저장·확인된 원본만 삭제한다.
        /// </summary>
        public static int MigrateLegacy(Vault vault, string password)
        {
            var migrated = 0;

            if (Directory.Exists(LegacyMemosDir))
            {
                var sources = new List<(string File, string[] Passwords)>();

                void AddDir(string dir, string pw)
                {
                    if (Directory.Exists(dir))
                        sources.AddRange(Directory.GetFiles(dir, "*.enc").Select(f => (f, new[] { pw })));
                }

                AddDir(Path.Combine(LegacyMemosDir, CryptoService.LegacyHash(password)), password);
                AddDir(Path.Combine(LegacyMemosDir, "default"), CryptoService.LegacyDefaultPassword);
                // 아주 예전 버전은 폴더 구분 없이 저장
                sources.AddRange(Directory.GetFiles(LegacyMemosDir, "*.enc")
                    .Select(f => (f, new[] { password, CryptoService.LegacyDefaultPassword })));

                foreach (var (file, passwords) in sources)
                {
                    var memo = TryReadLegacyMemo(file, passwords);
                    if (memo == null) continue;

                    try
                    {
                        if (vault.MemoExists(memo.Id)) memo.Id = Guid.NewGuid().ToString();
                        memo.AudioMarks ??= new List<AudioMark>();
                        vault.SaveMemo(memo);
                        if (vault.LoadMemo(memo.Id) == null) continue;

                        File.Delete(file);
                        migrated++;
                    }
                    catch
                    {
                        // 이 파일은 원본 유지
                    }
                }

                // 비워진 폴더 정리
                foreach (var dir in Directory.GetDirectories(LegacyMemosDir, "*", SearchOption.AllDirectories)
                             .OrderByDescending(d => d.Length))
                    TryDeleteEmptyDir(dir);
                TryDeleteEmptyDir(LegacyMemosDir);
            }

            MigratePlainAudio(vault);
            TryDeleteEmptyDir(LegacyAudioDir);

            // 이전 버전의 pwd.hash 는 사실상 암호화 키이므로, 해당 데이터가 모두 옮겨졌으면 삭제
            var hashFile = Path.Combine(Root, "pwd.hash");
            if (File.Exists(hashFile))
            {
                var hash = File.ReadAllText(hashFile).Trim();
                if (!Directory.Exists(Path.Combine(LegacyMemosDir, hash)))
                    TryDelete(hashFile);
            }
            TryDelete(Path.Combine(Root, "server.url")); // 제거된 AI 기능 설정

            return migrated;
        }

        private static Memo? TryReadLegacyMemo(string file, string[] passwords)
        {
            string cipher;
            try { cipher = File.ReadAllText(file); }
            catch { return null; }

            foreach (var pw in passwords)
            {
                try
                {
                    var memo = JsonConvert.DeserializeObject<Memo>(CryptoService.LegacyDecrypt(pw, cipher));
                    if (memo != null && !string.IsNullOrEmpty(memo.Id)) return memo;
                }
                catch
                {
                    // 다른 비밀번호로 저장된 파일
                }
            }
            return null;
        }

        /// <summary>메모가 가리키는 평문 WAV 녹음을 암호화된 녹음으로 변환</summary>
        private static void MigratePlainAudio(Vault vault)
        {
            foreach (var memo in vault.LoadAllMemos())
            {
                var path = memo.AudioPath;
                if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) continue;

                try
                {
                    if (File.Exists(path))
                    {
                        memo.AudioPath = vault.ImportPlainWav(path, memo.Id);
                        vault.SaveMemo(memo);
                        TryDelete(path);
                    }
                    else
                    {
                        memo.AudioPath = null;
                        memo.AudioMarks.Clear();
                        vault.SaveMemo(memo);
                    }
                }
                catch
                {
                    // 변환 실패 시 원본 유지 (다음 실행 때 다시 시도)
                }
            }
        }

        private static void TryDeleteEmptyDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch { }
        }

        private static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }

        #endregion
    }
}
