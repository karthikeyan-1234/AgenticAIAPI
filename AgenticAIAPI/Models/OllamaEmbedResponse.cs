using System.Text.Json.Serialization;

namespace AgenticAIAPI.Models
{
    // Model for Ollama API response
    public class OllamaEmbedResponse
    {
        [JsonPropertyName("embedding")]
        public List<float>? Embedding { get; set; }
    }
}
