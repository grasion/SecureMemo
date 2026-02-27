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
        private string? _apiKey;
        private readonly HttpClient _httpClient = new();

        public void SetApiKey(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<string> TranscribeAudio(string audioPath)
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("API key not set");

            var audioBytes = File.ReadAllBytes(audioPath);
            var base64Audio = Convert.ToBase64String(audioBytes);

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = "이 음성 파일을 텍스트로 변환해주세요." },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = "audio/wav",
                                    data = base64Audio
                                }
                            }
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}",
                content);

            var responseText = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseText);
            
            return result["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString() 
                   ?? "변환 실패";
        }

        public async Task<string> SummarizeText(string text)
        {
            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("API key not set");

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $"다음 텍스트를 요약해주세요:\n\n{text}" }
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}",
                content);

            var responseText = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseText);
            
            return result["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString() 
                   ?? "요약 실패";
        }
    }
}
