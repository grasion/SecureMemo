using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SecureMemo.Models
{
    public class Memo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "새 메모";
        public string Content { get; set; } = "";
        public string? AudioPath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>녹음 중 작성된 텍스트 구간과 녹음 시점의 매핑</summary>
        public List<AudioMark> AudioMarks { get; set; } = new();

        [JsonIgnore]
        public bool HasAudio => !string.IsNullOrEmpty(AudioPath);

        [JsonIgnore]
        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "제목 없음" : Title;

        [JsonIgnore]
        public string Preview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Content)) return "내용 없음";
                var text = Content.Replace("\r", " ").Replace("\n", " ").Trim();
                while (text.Contains("  ")) text = text.Replace("  ", " ");
                return text.Length > 60 ? text[..60] + "…" : text;
            }
        }
    }

    /// <summary>
    /// 녹음 중 입력된 텍스트 범위(Start ~ End, 문자 인덱스)와
    /// 그 텍스트가 입력된 녹음 시간(StartTime ~ EndTime, 초)
    /// </summary>
    public class AudioMark
    {
        public int Start { get; set; }
        public int End { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
    }
}
