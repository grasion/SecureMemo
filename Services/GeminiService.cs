using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SecureMemo.Services
{
    public class GeminiService
    {
        private string _serverUrl = "http://localhost:11434";
        private const string DefaultModel = "gemma4:e2b";
        private readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public void SetServerUrl(string serverUrl)
        {
            _serverUrl = serverUrl.TrimEnd('/');
        }

        public string GetServerUrl() => _serverUrl;

        // 하위 호환성을 위해 유지 (API 키는 로컬 모델에서 불필요)
        public void SetApiKey(string apiKey) { }

        public async Task<string> TranscribeAudio(string audioPath)
        {
            if (string.IsNullOrEmpty(_serverUrl))
                throw new InvalidOperationException("서버 URL이 설정되지 않았습니다");

            var audioBytes = File.ReadAllBytes(audioPath);
            var base64Audio = Convert.ToBase64String(audioBytes);

            // Ollama OpenAI 호환 API (chat/completions) 사용
            var requestBody = new
            {
                model = DefaultModel,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "이 음성 파일을 텍스트로 변환해주세요. 음성 내용만 출력하세요." },
                            new { type = "image_url", image_url = new { url = $"data:audio/wav;base64,{base64Audio}" } }
                        }
                    }
                },
                stream = false
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_serverUrl}/v1/chat/completions", content);

            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Ollama 네이티브 API로 폴백
                return await TranscribeAudioNative(audioPath);
            }

            var result = JObject.Parse(responseText);
            return result["choices"]?[0]?["message"]?["content"]?.ToString()
                   ?? "변환 실패";
        }

        private async Task<string> TranscribeAudioNative(string audioPath)
        {
            var audioBytes = File.ReadAllBytes(audioPath);
            var base64Audio = Convert.ToBase64String(audioBytes);

            var requestBody = new
            {
                model = DefaultModel,
                prompt = "이 음성 파일을 텍스트로 변환해주세요. 음성 내용만 출력하세요.",
                images = new[] { base64Audio },
                stream = false
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_serverUrl}/api/generate", content);

            var responseText = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseText);
            return result["response"]?.ToString() ?? "변환 실패";
        }

        public async Task<string> SummarizeText(string text)
        {
            if (string.IsNullOrEmpty(_serverUrl))
                throw new InvalidOperationException("서버 URL이 설정되지 않았습니다");

            // Ollama OpenAI 호환 API 사용
            var requestBody = new
            {
                model = DefaultModel,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = $"다음 텍스트를 요약해주세요:\n\n{text}"
                    }
                },
                stream = false
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_serverUrl}/v1/chat/completions", content);

            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Ollama 네이티브 API로 폴백
                return await SummarizeTextNative(text);
            }

            var result = JObject.Parse(responseText);
            return result["choices"]?[0]?["message"]?["content"]?.ToString()
                   ?? "요약 실패";
        }

        private async Task<string> SummarizeTextNative(string text)
        {
            var requestBody = new
            {
                model = DefaultModel,
                prompt = $"다음 텍스트를 요약해주세요:\n\n{text}",
                stream = false
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_serverUrl}/api/generate", content);

            var responseText = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseText);
            return result["response"]?.ToString() ?? "요약 실패";
        }

        /// <summary>
        /// 서버 연결 테스트 - Ollama 서버가 실행 중이고 모델이 있는지 확인
        /// </summary>
        public async Task<(bool success, string message)> TestConnectionAsync()
        {
            try
            {
                // 1. 서버 연결 확인
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/tags");
                if (!response.IsSuccessStatusCode)
                    return (false, $"서버 연결 실패 (HTTP {(int)response.StatusCode})");

                var responseText = await response.Content.ReadAsStringAsync();
                var result = JObject.Parse(responseText);
                var models = result["models"] as JArray;

                if (models == null || models.Count == 0)
                    return (false, "서버에 설치된 모델이 없습니다.\nollama pull gemma4-e2b 명령으로 모델을 설치하세요.");

                // 2. gemma4-e2b 모델 확인
                bool hasModel = false;
                foreach (var model in models)
                {
                    var name = model["name"]?.ToString() ?? "";
                    if (name.StartsWith("gemma4:e2b"))
                    {
                        hasModel = true;
                        break;
                    }
                }

                if (!hasModel)
                {
                    var modelNames = string.Join(", ", models.Select(m => m["name"]?.ToString()));
                    return (true, $"서버 연결 성공!\n설치된 모델: {modelNames}\n⚠ gemma4:e2b 모델이 없습니다.\nollama pull gemma4:e2b 명령으로 설치하세요.");
                }

                return (true, "서버 연결 성공! gemma4:e2b 모델 확인됨 ✓");
            }
            catch (HttpRequestException)
            {
                return (false, "서버에 연결할 수 없습니다.\nOllama가 실행 중인지 확인하세요.\n(ollama serve 명령으로 시작)");
            }
            catch (TaskCanceledException)
            {
                return (false, "서버 응답 시간 초과");
            }
            catch (Exception ex)
            {
                return (false, $"연결 오류: {ex.Message}");
            }
        }
    }
}
