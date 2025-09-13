using System.Text.Json;

namespace AgenticAIAPI.Services
{
public class OllamaGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly string _ollamaUrl = "http://localhost:11434/v1/chat/completions";
    
    public OllamaGenerationService()
    {
        _httpClient = new HttpClient();
    }

    public async Task<string> GenerateAnswerAsync(string prompt)
    {
        try
        {
            var requestBody = new
            {
                model = "llama3",  // Changed from moondream to llama3
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 500,  // Increased from 200 for better responses
                temperature = 0.7,
                stream = false
            };

            Console.WriteLine($"Sending request to Ollama with model: llama3");
            
            var response = await _httpClient.PostAsJsonAsync(_ollamaUrl, requestBody);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Ollama API error: {response.StatusCode} - {errorContent}");
                throw new Exception($"Ollama API error: {response.StatusCode} - {errorContent}");
            }
            
            var json = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Ollama response received, length: {json.Length}");
            
            using var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("choices", out var choicesElement) || 
                choicesElement.GetArrayLength() == 0)
            {
                Console.WriteLine("No choices found in Ollama response");
                return "I apologize, but I couldn't generate a response. Please try again.";
            }
            
            var firstChoice = choicesElement[0];
            if (!firstChoice.TryGetProperty("message", out var messageElement) ||
                !messageElement.TryGetProperty("content", out var contentElement))
            {
                Console.WriteLine("No message content found in Ollama response");
                return "I apologize, but I couldn't generate a response. Please try again.";
            }
            
            var message = contentElement.GetString();
            
            if (string.IsNullOrWhiteSpace(message))
            {
                Console.WriteLine("Empty message content from Ollama");
                return "I apologize, but I couldn't generate a response. Please try again.";
            }
            
            Console.WriteLine($"Successfully extracted answer, length: {message.Length}");
            return message.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GenerateAnswerAsync: {ex.Message}");
            throw;
        }
    }
}

}
