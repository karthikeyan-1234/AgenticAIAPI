namespace AgenticAIAPI.Models
{
    public class QueryRequest
    {
        public string Question { get; set; } = string.Empty;
        public int? TopK { get; set; }
        public double? MinimumScore { get; set; }
    }
}
