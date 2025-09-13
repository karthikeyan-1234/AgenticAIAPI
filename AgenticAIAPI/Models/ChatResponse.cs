namespace AgenticAIAPI.Models
{
    public class ChatResponse
    {
        public string Reply { get; set; } = string.Empty;
        public bool Success { get; set; }
        public bool HasSources { get; set; }
        public double? Confidence { get; set; }
        public int? SourceCount { get; set; }
    }
}
