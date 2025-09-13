using Microsoft.OpenApi.Services;

using System.Text.Json;
using SearchResult = AgenticAIAPI.Models.SearchResult;

namespace AgenticAIAPI.Services
{
    public class QdrantService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "http://localhost:6333";

        public QdrantService()
        {
            _httpClient = new HttpClient();
        }

        public async Task CreateCollectionIfNotExistsAsync(string collectionName, int vectorSize)
        {
            // Check if collection exists
            var response = await _httpClient.GetAsync($"{_baseUrl}/collections/{collectionName}");
            if (response.IsSuccessStatusCode)
            {
                // Collection exists, no further action needed
                return;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Collection does not exist, create it
                var createCollectionPayload = new
                {
                    vectors = new
                    {
                        size = vectorSize,
                        distance = "Cosine" // or "Euclid", "Dot"
                    }
                };
                var createResponse = await _httpClient.PutAsJsonAsync($"{_baseUrl}/collections/{collectionName}", createCollectionPayload);
                createResponse.EnsureSuccessStatusCode();
            }
            else
            {
                // Unexpected status code - forward as exception
                response.EnsureSuccessStatusCode();
            }
        }

        public async Task UpsertPointsAsync(string collectionName, List<string> chunks, List<List<float>> embeddings)
    {
        if (chunks.Count != embeddings.Count)
            throw new ArgumentException("Chunks and embeddings count must match.");

        int expectedVectorSize = embeddings.First().Count;
        for (int i = 0; i < embeddings.Count; i++)
        {
            if (embeddings[i].Count != expectedVectorSize)
                throw new Exception($"Embedding length mismatch at index {i}");
        }

        // Create points array with correct structure
        var points = new List<object>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var id = Guid.NewGuid().ToString();

            points.Add(new
            {
                id = id,  // ID should be at the same level as vector and payload
                vector = embeddings[i],
                payload = new { text = chunks[i] }
            });
        }

        // Correct upsert payload structure
        var upsertPayload = new
        {
            points = points  // Remove the separate ids array
        };

        var jsonPayload = JsonSerializer.Serialize(upsertPayload);
        Console.WriteLine($"Upsert payload JSON (first 500 chars): {jsonPayload.Substring(0, Math.Min(500, jsonPayload.Length))}...");

        var response = await _httpClient.PutAsJsonAsync($"{_baseUrl}/collections/{collectionName}/points", upsertPayload);

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Qdrant upsert failed with status {response.StatusCode}: {content}");
            throw new Exception($"Qdrant upsert failed with status {response.StatusCode}: {content}");
        }
        else
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Qdrant upsert succeeded: {responseContent}");
        }
    }

        public async Task<List<string>> SearchPointsAsync(string collectionName, List<float> queryVector, int topK = 3)
        {
            var searchPayload = new
            {
                vector = queryVector,
                top = topK
            };

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/collections/{collectionName}/points/search", searchPayload);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var hits = doc.RootElement.GetProperty("result").EnumerateArray();

            var matchingTexts = new List<string>();

            foreach (var hit in hits)
            {
                var payload = hit.GetProperty("payload");
                if (payload.TryGetProperty("text", out JsonElement textElement))
                {
                    matchingTexts.Add(textElement.GetString()!);
                }
            }

            return matchingTexts;
        }

        public async Task<List<SearchResult>> SearchAcrossCollectionsAsync(string? collectionName, List<float> queryEmbedding, int topKPerCollection = 5)
        {
            if (!string.IsNullOrWhiteSpace(collectionName))
            {
                // Search single specific collection
                return await SearchPointsWithScoresAsync(collectionName.Trim().ToLower(), queryEmbedding, topKPerCollection);
            }
    
            // Search all collections
            var allCollections = await ListCollectionsAsync();
            var searchTasks = allCollections.Select(c => SearchPointsWithScoresAsync(c, queryEmbedding, topKPerCollection));
            var resultsPerCollection = await Task.WhenAll(searchTasks);
    
            // Flatten and combine all results
            var allResults = resultsPerCollection.SelectMany(r => r).ToList();

            // Apply stricter min score filtering here for extreme accuracy
            double minScoreThreshold = 0.4; // Adjust as needed for high precision
            var filteredResults = allResults.Where(r => r.Score >= minScoreThreshold).ToList();

            // Optionally take top N overall (e.g. 10) results for context construction
            var topResults = filteredResults.OrderByDescending(r => r.Score).Take(10).ToList();

            return topResults;
        }


        public async Task<List<string>> GetAllPayloadTextsAsync(string collectionName)
        {
            // This uses point scrolling; adjust limit if needed
            var url = $"{_baseUrl}/collections/{collectionName}/points/scroll";
            var scrollRequest = new { limit = 1000, with_payload = true, with_vector = false };
            var payloads = new List<string>();

            var response = await _httpClient.PostAsJsonAsync(url, scrollRequest);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var points = doc.RootElement.GetProperty("result").GetProperty("points").EnumerateArray();

            foreach (var point in points)
            {
                if (point.TryGetProperty("payload", out var payload) &&
                    payload.TryGetProperty("text", out var textElement))
                {
                    payloads.Add(textElement.GetString()!);
                }
            }
            return payloads;
        }

        public async Task<List<SearchResult>> SearchPointsWithScoresAsync(string collectionName, List<float> queryVector, int topK = 3)
    {
        var searchPayload = new
        {
            vector = queryVector,
            top = topK,
            with_payload = true,
            with_vector = false  // We don't need vectors back, just payload and scores
        };

        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/collections/{collectionName}/points/search", searchPayload);
    
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Qdrant search failed with status {response.StatusCode}: {errorContent}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var results = new List<AgenticAIAPI.Models.SearchResult>();

        if (!doc.RootElement.TryGetProperty("result", out var resultElement))
        {
            return results; // Empty results if no result property
        }

        foreach (var hit in resultElement.EnumerateArray())
        {
            try
            {
                // Extract similarity score
                double score = 0.0;
                if (hit.TryGetProperty("score", out var scoreElement))
                {
                    score = scoreElement.GetDouble();
                }

                // Extract text from payload
                string text = string.Empty;
                if (hit.TryGetProperty("payload", out var payload) &&
                    payload.TryGetProperty("text", out var textElement))
                {
                    text = textElement.GetString() ?? string.Empty;
                }

                // Only add results with valid text content
                if (!string.IsNullOrWhiteSpace(text))
                {
                    results.Add(new AgenticAIAPI.Models.SearchResult
                    {
                        Text = text,
                        Score = score
                    });
                }
            }
            catch (Exception ex)
            {
                // Log the error but continue processing other results
                Console.WriteLine($"Error processing search result: {ex.Message}");
            }
        }

        // Sort by score descending (highest relevance first)
        return results.OrderByDescending(r => r.Score).ToList();
    }

        public async Task DeleteCollectionAsync(string collectionName)
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/collections/{collectionName}");
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Failed to delete collection {collectionName}: {response.StatusCode} - {content}");
                throw new Exception($"Failed to delete collection {collectionName}: {response.StatusCode} - {content}");
            }
        }

        //List all collections in Qdrant
        public async Task<List<string>> ListCollectionsAsync()
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/collections");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var jsonDocument = JsonDocument.Parse(content);
            var collections = new List<string>();

            if (jsonDocument.RootElement.TryGetProperty("result", out var resultElement) &&
                resultElement.TryGetProperty("collections", out var collectionsElement))
            {
                foreach (var collection in collectionsElement.EnumerateArray())
                {
                    if (collection.TryGetProperty("name", out var nameElement))
                    {
                        collections.Add(nameElement.GetString()!);
                    }
                }
            }

            return collections;
        }

    }
}
