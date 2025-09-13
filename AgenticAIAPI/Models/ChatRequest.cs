namespace AgenticAIAPI.Models
{
    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string LookInFileName { get; set; } = string.Empty; // Optional filter by file name
    }
}
