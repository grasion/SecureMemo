using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SecureMemo.Services
{
    public class OllamaSetupService
    {
        private const string OllamaDownloadUrl = "https://ollama.com/download/OllamaSetup.exe";
        private const string ModelName = "gemma4:e2b";
        private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(30) };

        public event Action<string>? StatusChanged;
        public event Action<int>? ProgressChanged;

        private void ReportStatus(string status) => StatusChanged?.Invoke(status);
        private void ReportProgress(int percent) => ProgressChanged?.Invoke(percent);

        /// <summary>
        /// ollama.exe가 PATH에 있거나 기본 설치 경로에 있는지 확인
        /// </summary>
        public bool IsOllamaInstalled()
        {
            // PATH에서 찾기
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(5000);
                return process?.ExitCode == 0;
            }
            catch { }

            // 기본 설치 경로 확인
            var defaultPath = GetOllamaExePath();
            return defaultPath != null && File.Exists(defaultPath);
        }

        /// <summary>
        /// ollama.exe 경로 반환
        /// </summary>
        public string? GetOllamaExePath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
            if (File.Exists(path)) return path;

            // Program Files 경로도 확인
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            path = Path.Combine(programFiles, "Ollama", "ollama.exe");
            if (File.Exists(path)) return path;

            return null;
        }

        /// <summary>
        /// Ollama 서버가 실행 중인지 확인
        /// </summary>
        public async Task<bool> IsOllamaRunningAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("http://localhost:11434/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 모델이 이미 다운로드되어 있는지 확인
        /// </summary>
        public async Task<bool> IsModelInstalledAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("http://localhost:11434/api/tags");
                if (!response.IsSuccessStatusCode) return false;

                var responseText = await response.Content.ReadAsStringAsync();
                var result = JObject.Parse(responseText);
                var models = result["models"] as JArray;
                if (models == null) return false;

                foreach (var model in models)
                {
                    var name = model["name"]?.ToString() ?? "";
                    if (name.StartsWith("gemma4:e2b"))
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ollama 설치 파일 다운로드 및 실행
        /// </summary>
        public async Task<bool> DownloadAndInstallOllamaAsync()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");

            try
            {
                // 기존 파일이 있으면 삭제
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }

                ReportStatus("Ollama 설치 파일 다운로드 중...");
                ReportProgress(0);

                // 다운로드를 별도 블록에서 완료하여 파일 잠금 해제
                using (var response = await _httpClient.GetAsync(OllamaDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    long receivedBytes = 0;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            receivedBytes += bytesRead;

                            if (totalBytes > 0)
                            {
                                var percent = (int)(receivedBytes * 100 / totalBytes);
                                ReportProgress(percent);
                                ReportStatus($"Ollama 다운로드 중... {percent}% ({receivedBytes / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB)");
                            }
                        }

                        fileStream.Flush();
                    } // fileStream, contentStream 닫힘
                } // response 닫힘

                // 파일이 완전히 해제될 때까지 잠시 대기
                await Task.Delay(500);

                ReportProgress(100);
                ReportStatus("Ollama 설치 프로그램 실행 중... (설치 완료까지 기다려주세요)");

                // 설치 프로그램 실행 (/VERYSILENT로 자동 설치)
                var psi = new ProcessStartInfo
                {
                    FileName = tempPath,
                    Arguments = "/VERYSILENT /NORESTART",
                    UseShellExecute = true,
                    Verb = "runas" // 관리자 권한 요청
                };

                var installProcess = Process.Start(psi);
                if (installProcess != null)
                {
                    await installProcess.WaitForExitAsync();
                    installProcess.Dispose();

                    // 설치 후 잠시 대기
                    await Task.Delay(3000);

                    if (IsOllamaInstalled())
                    {
                        ReportStatus("Ollama 설치 완료!");
                        return true;
                    }
                }

                ReportStatus("Ollama 설치를 확인할 수 없습니다. 수동으로 확인해주세요.");
                return false;
            }
            catch (Exception ex)
            {
                ReportStatus($"Ollama 설치 실패: {ex.Message}");
                return false;
            }
            finally
            {
                // 임시 파일 정리
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        /// <summary>
        /// Ollama 서버 시작 (백그라운드)
        /// </summary>
        public async Task<bool> StartOllamaServerAsync()
        {
            try
            {
                // 이미 실행 중인지 확인
                if (await IsOllamaRunningAsync())
                    return true;

                ReportStatus("Ollama 서버 시작 중...");

                var ollamaPath = GetOllamaExePath();
                string fileName = ollamaPath ?? "ollama";

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                Process.Start(psi);

                // 서버가 시작될 때까지 대기 (최대 30초)
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000);
                    if (await IsOllamaRunningAsync())
                    {
                        ReportStatus("Ollama 서버 시작됨!");
                        return true;
                    }
                    ReportStatus($"Ollama 서버 시작 대기 중... ({i + 1}초)");
                }

                ReportStatus("Ollama 서버 시작 시간 초과");
                return false;
            }
            catch (Exception ex)
            {
                ReportStatus($"Ollama 서버 시작 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 모델 다운로드 (ollama pull)
        /// </summary>
        public async Task<bool> PullModelAsync()
        {
            try
            {
                ReportStatus($"{ModelName} 모델 다운로드 중... (수 GB, 시간이 걸릴 수 있습니다)");
                ReportProgress(0);

                // Ollama API를 통해 모델 pull
                var requestBody = $"{{\"name\":\"{ModelName}\",\"stream\":true}}";
                var content = new System.Net.Http.StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/pull");
                request.Content = content;

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var json = JObject.Parse(line);
                        var status = json["status"]?.ToString() ?? "";

                        // 진행률 계산
                        var total = json["total"]?.Value<long>() ?? 0;
                        var completed = json["completed"]?.Value<long>() ?? 0;

                        if (total > 0)
                        {
                            var percent = (int)(completed * 100 / total);
                            ReportProgress(percent);
                            ReportStatus($"{status} {percent}% ({completed / 1024 / 1024}MB / {total / 1024 / 1024}MB)");
                        }
                        else
                        {
                            ReportStatus(status);
                        }

                        // 에러 체크
                        var error = json["error"]?.ToString();
                        if (!string.IsNullOrEmpty(error))
                        {
                            ReportStatus($"모델 다운로드 실패: {error}");
                            return false;
                        }
                    }
                    catch { }
                }

                ReportProgress(100);
                ReportStatus($"{ModelName} 모델 다운로드 완료!");
                return true;
            }
            catch (Exception ex)
            {
                ReportStatus($"모델 다운로드 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 전체 자동 설정: Ollama 설치 확인 → 설치 → 서버 시작 → 모델 다운로드
        /// </summary>
        public async Task<bool> AutoSetupAsync()
        {
            try
            {
                // 1단계: Ollama 설치 확인
                ReportStatus("Ollama 설치 여부 확인 중...");
                ReportProgress(0);

                if (!IsOllamaInstalled())
                {
                    ReportStatus("Ollama가 설치되어 있지 않습니다. 설치를 시작합니다...");
                    var installed = await DownloadAndInstallOllamaAsync();
                    if (!installed)
                        return false;
                }
                else
                {
                    ReportStatus("Ollama 설치 확인됨 ✓");
                }

                // 2단계: 서버 시작
                ReportProgress(30);
                var serverRunning = await StartOllamaServerAsync();
                if (!serverRunning)
                    return false;

                ReportStatus("Ollama 서버 실행 중 ✓");

                // 3단계: 모델 확인 및 다운로드
                ReportProgress(50);
                if (await IsModelInstalledAsync())
                {
                    ReportStatus($"{ModelName} 모델 이미 설치됨 ✓");
                    ReportProgress(100);
                }
                else
                {
                    ReportStatus($"{ModelName} 모델이 없습니다. 다운로드를 시작합니다...");
                    var pulled = await PullModelAsync();
                    if (!pulled)
                        return false;
                }

                ReportProgress(100);
                ReportStatus("모든 설정이 완료되었습니다! AI 기능을 사용할 수 있습니다. ✓");
                return true;
            }
            catch (Exception ex)
            {
                ReportStatus($"자동 설정 실패: {ex.Message}");
                return false;
            }
        }
    }
}
