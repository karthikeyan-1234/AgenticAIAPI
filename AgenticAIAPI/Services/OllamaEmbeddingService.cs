using AgenticAIAPI.Models;

namespace AgenticAIAPI.Services
{
// Service to call Ollama embedding API
public class OllamaEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _ollamaUrl;
    private readonly string _modelName;

    public OllamaEmbeddingService(string ollamaUrl = "http://localhost:11434/api/embeddings", string modelName = "mxbai-embed-large")
    {
        _httpClient = new HttpClient();
        _ollamaUrl = ollamaUrl;
        _modelName = modelName;
    }

    public async Task<List<float>> GetEmbeddingAsync(string text)
    {
        var request = new OllamaEmbedRequest
        {
            Model = _modelName,
            Prompt = text
        };

        var response = await _httpClient.PostAsJsonAsync(_ollamaUrl, request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>();
        return result!.Embedding!;
    }

    // Bulk version for multiple chunks
    public async Task<List<List<float>>> GetEmbeddingsAsync(IEnumerable<string> chunks)
    {
        var output = new List<List<float>>();
        foreach (var chunk in chunks)
        {
            output.Add(await GetEmbeddingAsync(chunk));
        }
        return output;
    }
}
}
