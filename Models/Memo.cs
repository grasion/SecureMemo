using System;

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
    }
}
