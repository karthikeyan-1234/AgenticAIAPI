using AgenticAIAPI.Controllers;

namespace AgenticAIAPI.Models
{
    public class QueryResponse
    {
        public string Answer { get; set; } = string.Empty;
        public List<SourceInfo> Sources { get; set; } = new();
        public double Confidence { get; set; }
        public string Message { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public int ProcessingTimeMs { get; set; }
    }
}
