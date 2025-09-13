using System.Text.Json.Serialization;

namespace AgenticAIAPI.Models
{
    // Model for Ollama API request
    public class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }
    }
}
