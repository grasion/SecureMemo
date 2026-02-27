using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SecureMemo.Services
{
    public class UpdateService
    {
        private readonly HttpClient _httpClient = new();
        private const string GITHUB_REPO = "grasion/SecureMemo";
        private const string GITHUB_API_URL = $"https://api.github.com/repos/{GITHUB_REPO}/releases/latest";

        public string CurrentVersion { get; }

        public UpdateService()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            CurrentVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        public async Task<(bool hasUpdate, string latestVersion, string downloadUrl, string releaseNotes)> CheckForUpdatesAsync()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SecureMemo/1.0");
                
                var response = await _httpClient.GetStringAsync(GITHUB_API_URL);
                var release = JObject.Parse(response);

                var tagName = release["tag_name"]?.ToString() ?? "";
                var latestVersion = tagName.TrimStart('v');
                var releaseNotes = release["body"]?.ToString() ?? "업데이트 내용이 없습니다.";
                
                // assets에서 .exe 파일 찾기
                var assets = release["assets"] as JArray;
                string downloadUrl = "";
                
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        var name = asset["name"]?.ToString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset["browser_download_url"]?.ToString() ?? "";
                            break;
                        }
                    }
                }

                var hasUpdate = CompareVersions(CurrentVersion, latestVersion) < 0;
                
                return (hasUpdate, latestVersion, downloadUrl, releaseNotes);
            }
            catch (Exception ex)
            {
                throw new Exception($"업데이트 확인 실패: {ex.Message}");
            }
        }

        public async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<int> progress)
        {
            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), "SecureMemo_Update.exe");
                
                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var buffer = new byte[8192];
                var totalRead = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progressPercentage = (int)((totalRead * 100) / totalBytes);
                        progress?.Report(progressPercentage);
                    }
                }

                return tempPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"다운로드 실패: {ex.Message}");
            }
        }

        public void InstallUpdate(string updateFilePath)
        {
            try
            {
                // 배치 파일로 업데이트 설치
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                var batchPath = Path.Combine(Path.GetTempPath(), "update.bat");

                var batchContent = $@"@echo off
timeout /t 2 /nobreak > nul
taskkill /F /IM SecureMemo.exe > nul 2>&1
timeout /t 1 /nobreak > nul
copy /Y ""{updateFilePath}"" ""{currentExePath}""
del ""{updateFilePath}""
start """" ""{currentExePath}""
del ""%~f0""
";

                File.WriteAllText(batchPath, batchContent);

                var processInfo = new ProcessStartInfo
                {
                    FileName = batchPath,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(processInfo);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                throw new Exception($"업데이트 설치 실패: {ex.Message}");
            }
        }

        private int CompareVersions(string version1, string version2)
        {
            var v1Parts = version1.Split('.');
            var v2Parts = version2.Split('.');
            var maxLength = Math.Max(v1Parts.Length, v2Parts.Length);

            for (int i = 0; i < maxLength; i++)
            {
                var v1Part = i < v1Parts.Length ? int.Parse(v1Parts[i]) : 0;
                var v2Part = i < v2Parts.Length ? int.Parse(v2Parts[i]) : 0;

                if (v1Part < v2Part) return -1;
                if (v1Part > v2Part) return 1;
            }

            return 0;
        }
    }
}
