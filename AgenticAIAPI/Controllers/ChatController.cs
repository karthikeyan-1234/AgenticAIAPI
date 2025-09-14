using AgenticAIAPI.Infra;
using AgenticAIAPI.Models;
using AgenticAIAPI.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using System.Reflection;
using System.Text;

namespace AgenticAIAPI.Controllers
{
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly OllamaEmbeddingService _embeddingService;
    private readonly QdrantService _qdrantService;
    private readonly OllamaGenerationService _ollamaGenerationService;
    private readonly ILogger<ChatController> _logger;
    
    private string CollectionName = "documents";
    private const int OptimalTopK = 4; // Sweet spot for most questions
    private const double OptimalMinScore = 0.25; // Lower threshold for better recall

    public ChatController(

        ILogger<ChatController> logger)
    {
        _embeddingService = new OllamaEmbeddingService();
        _qdrantService = new QdrantService();
        _ollamaGenerationService = new OllamaGenerationService();
        _logger = logger;
    }

    [HttpPost("ask")]
    public async Task<IActionResult> AskQuestion([FromBody] ChatRequest request)
    {
        var correlationId = Guid.NewGuid().ToString()[..8];
        
        try
        {
            // Simple validation
            if (string.IsNullOrWhiteSpace(request?.Message))
                return BadRequest(new ChatResponse 
                { 
                    Reply = "Please provide a question.", 
                    Success = false 
                });

            if (request.Message.Length > 500)
                return BadRequest(new ChatResponse 
                { 
                    Reply = "Question is too long. Please keep it under 500 characters.", 
                    Success = false 
                });

            _logger.LogInformation("Chat question: '{Question}' - ID: {CorrelationId}", 
                request.Message.Substring(0, Math.Min(50, request.Message.Length)), correlationId);

                CollectionName = string.IsNullOrWhiteSpace(request.LookInFileName)
                    ? "documents"
                    : request.LookInFileName.Trim().ToLower();

                // Check if we have any documents
                var hasDocuments = await CheckIfDocumentsExistAsync();
            if (!hasDocuments)
            {
                return Ok(new ChatResponse
                {
                    Reply = "I don't have access to any documents yet. Please upload some documents first, then I'll be able to answer questions about them.",
                    Success = true,
                    HasSources = false
                });
            }

            // Generate embedding for the question
            var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Message);
            if (queryEmbedding == null || !queryEmbedding.Any())
            {
                return Ok(new ChatResponse
                {
                    Reply = "I'm having trouble processing your question right now. Please try again in a moment.",
                    Success = false
                });
            }

            // Search for relevant chunks with optimal settings
            var searchResults = await _qdrantService.SearchPointsWithScoresAsync(
                CollectionName, queryEmbedding, OptimalTopK);
            
            // Filter by relevance - use adaptive threshold
            var relevantResults = searchResults
                .Where(r => r.Score >= OptimalMinScore)
                .ToList();

            // Handle no relevant results
            if (!relevantResults.Any())
            {
                return Ok(new ChatResponse
                {
                    Reply = "I couldn't find information relevant to your question in the uploaded documents. Try asking about different topics or rephrasing your question.",
                    Success = true,
                    HasSources = false
                });
            }

            // Build context for LLM
            var context = BuildChatContext(relevantResults, request.Message);
            
            // Generate answer
            var answer = await _ollamaGenerationService.GenerateAnswerAsync(context);
            
            if (string.IsNullOrWhiteSpace(answer))
            {
                return Ok(new ChatResponse
                {
                    Reply = "I'm experiencing technical difficulties generating a response. Please try again.",
                    Success = false
                });
            }

            // Calculate simple confidence
            var avgScore = relevantResults.Average(r => r.Score);
            var confidence = CalculateSimpleConfidence(avgScore, relevantResults.Count);
            
            var response = new ChatResponse
            {
                Reply = answer.Trim(),
                Success = true,
                HasSources = true,
                Confidence = confidence,
                SourceCount = relevantResults.Count
            };

            _logger.LogInformation("Chat response generated - ID: {CorrelationId}, Sources: {SourceCount}, Confidence: {Confidence}", 
                correlationId, relevantResults.Count, confidence);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat error - ID: {CorrelationId}", correlationId);
            return Ok(new ChatResponse
            {
                Reply = "I encountered an error while processing your question. Please try again.",
                Success = false
            });
        }
    }


    [HttpPost("askEnhanced")]
    public async Task<IActionResult> AskQuestionEnhanced([FromBody] ChatRequest request)
    {
        var correlationId = Guid.NewGuid().ToString()[..8];

        try
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
                return BadRequest(new ChatResponse { Reply = "Please provide a question.", Success = false });

            if (request.Message.Length > 500)
                return BadRequest(new ChatResponse { Reply = "Question is too long. Please keep it under 500 characters.", Success = false });

            _logger.LogInformation("Enhanced chat question: '{Question}' - ID: {CorrelationId}", request.Message.Substring(0, Math.Min(50, request.Message.Length)), correlationId);

            // Generate embedding
            var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Message);
            if (queryEmbedding == null || !queryEmbedding.Any())
            {
                return Ok(new ChatResponse
                {
                    Reply = "I'm having trouble processing your question right now. Please try again in a moment.",
                    Success = false
                });
            }

            // Use the new multi-collection search method for relevant results
            var relevantResults = await _qdrantService.SearchAcrossCollectionsAsync(request.LookInFileName, queryEmbedding, 10);

            if (!relevantResults.Any())
            {
                return Ok(new ChatResponse
                {
                    Reply = "I couldn't find information relevant to your question in the uploaded documents.",
                    Success = true,
                    HasSources = false
                });
            }

            var context = BuildChatContext(relevantResults, request.Message);
            var answer = await _ollamaGenerationService.GenerateAnswerAsync(context);

            if (string.IsNullOrWhiteSpace(answer))
            {
                return Ok(new ChatResponse
                {
                    Reply = "I'm experiencing technical difficulties generating a response. Please try again.",
                    Success = false
                });
            }

            var avgScore = relevantResults.Average(r => r.Score);
            var confidence = CalculateSimpleConfidence(avgScore, relevantResults.Count);

            var response = new ChatResponse
            {
                Reply = answer.Trim(),
                Success = true,
                HasSources = true,
                Confidence = confidence,
                SourceCount = relevantResults.Count
            };

            _logger.LogInformation("Enhanced chat response generated - ID: {CorrelationId}, Sources: {SourceCount}, Confidence: {Confidence}",
                correlationId, relevantResults.Count, confidence);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enhanced chat error - ID: {CorrelationId}", correlationId);
            return Ok(new ChatResponse
            {
                Reply = "I encountered an error while processing your question. Please try again.",
                Success = false
            });
        }
    }

    [HttpPost("askMCP")]
    public async Task<IActionResult> AskMCP([FromBody] ChatRequest request)
    {
        var question = request?.Message;
        if (string.IsNullOrWhiteSpace(question))
            return BadRequest("Question is required.");

        // 1. Embed user question
        var queryEmbedding = await _embeddingService.GetEmbeddingAsync(question);
        if (queryEmbedding == null || !queryEmbedding.Any())
            return BadRequest("Failed to generate query embedding.");

        // 2. Discover all MCP methods and their intent descriptions
        var mcpMethods = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace?.StartsWith("AgenticAIAPI.Services.Business") == true)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(m =>
            {
                var attr = m.GetCustomAttribute<MCPAttribute>();
                return attr != null ? new { Method = m, Description = attr.Intent } : null;
            })
            .Where(x => x != null)
            .ToList();

        // 3. Compute embeddings for MCP method descriptions (cache these in prod)
        var methodDescriptions = mcpMethods
            .Select(m => m!.Description)
            .ToList();

        var descriptionsEmbeddings = new List<List<float>>();
        foreach(var desc in methodDescriptions)
        {
            var emb = await _embeddingService.GetEmbeddingAsync(desc);
            if (emb != null && emb.Any())
                descriptionsEmbeddings.Add(emb);
            else
                descriptionsEmbeddings.Add(new List<float>()); // fallback empty vector
        }

        // 4. Find best match by cosine similarity
        int bestMatchIdx = -1;
        double bestScore = -1;
        for (int i = 0; i < descriptionsEmbeddings.Count; i++)
        {
            var score = CosineSimilarity(queryEmbedding, descriptionsEmbeddings[i]);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatchIdx = i;
            }
        }

        if (bestMatchIdx < 0 || bestScore < 0.6) // threshold for match confidence
        {
            // fallback to regular RAG flow if no good semantic match
            return await AskQuestion(request!);
        }

        var matchedMethod = mcpMethods[bestMatchIdx]!.Method;
        var serviceInstance = Activator.CreateInstance(matchedMethod.DeclaringType!);

        // 5. Extract parameters from question with simple heuristic or defaults
        // Here, a simple example: you can extend with named entity recognition or another LLM call
        var parametersInfo = matchedMethod.GetParameters();
        var args = new object?[parametersInfo.Length];

        // For demo: fill parameters with default or null (You should implement better param extraction)
        for (int i = 0; i < parametersInfo.Length; i++)
        {
            args[i] = parametersInfo[i].HasDefaultValue ? parametersInfo[i].DefaultValue : GetDefault(parametersInfo[i].ParameterType);
        }

        // 6. Call the method via reflection
        try
        {
            var result = matchedMethod.Invoke(serviceInstance, args);
            return Ok(new ChatResponse
            {
                Reply = $"MCP Service response:\n{SerializeForResponse(result)}",
                Success = true,
                HasSources = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invoking MCP service method.");
            return Ok(new ChatResponse
            {
                Reply = "Failed to process your request using MCP services.",
                Success = false
            });
        }
    }


[HttpPost("askUnified")]
public async Task<IActionResult> AskUnified([FromBody] ChatRequest request)
{
    var correlationId = Guid.NewGuid().ToString()[..8];

    try
    {
        if (string.IsNullOrWhiteSpace(request?.Message))
            return BadRequest(new ChatResponse { Reply = "Please provide a question.", Success = false });

        _logger.LogInformation("Unified chat question: '{Question}' - ID: {CorrelationId}",
            request.Message.Substring(0, Math.Min(50, request.Message.Length)), correlationId);

        // STEP 1: Embed the query
        var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Message);
        if (queryEmbedding == null || !queryEmbedding.Any())
            return BadRequest(new ChatResponse { Reply = "Failed to process your query embedding.", Success = false });

        // STEP 2: Search across ALL uploaded collections
        var relevantResults = await _qdrantService.SearchAcrossCollectionsAsync(
            request.LookInFileName, // can be null/empty → search all
            queryEmbedding,
            topKPerCollection: 10
        );

        // Build context if docs found
        var contextText = relevantResults.Any()
            ? BuildChatContext(relevantResults, request.Message)
            : string.Empty;

        // STEP 3: Discover MCP methods
        var mcpMethods = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace?.StartsWith("AgenticAIAPI.Services.Business") == true)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(m =>
            {
                var attr = m.GetCustomAttribute<MCPAttribute>();
                return attr != null ? new { Method = m, Description = attr.Intent } : null;
            })
            .Where(x => x != null)
            .ToList();

        // STEP 4: Match MCP methods semantically
        var invokedResults = new List<object>();
        foreach (var method in mcpMethods)
        {
            var descEmbedding = await _embeddingService.GetEmbeddingAsync(method!.Description);
            if (descEmbedding == null || !descEmbedding.Any()) continue;

            var score = CosineSimilarity(queryEmbedding, descEmbedding);
            if (score >= 0.6) // threshold
            {
                var serviceInstance = Activator.CreateInstance(method.Method.DeclaringType!);
                var args = method.Method.GetParameters()
                    .Select(p => p.HasDefaultValue ? p.DefaultValue : GetDefault(p.ParameterType))
                    .ToArray();

                try
                {
                    var result = method.Method.Invoke(serviceInstance, args);
                    if (result != null) invokedResults.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error invoking MCP method {Method}", method.Method.Name);
                }
            }
        }

        // STEP 5: Combine docs + services
        if (relevantResults.Any() && invokedResults.Any())
        {
            var combinedContext = new StringBuilder();
            combinedContext.AppendLine("DOCUMENT CONTEXT:");
            foreach (var r in relevantResults)
                combinedContext.AppendLine(r.Text).AppendLine("---");

            combinedContext.AppendLine("SERVICE DATA:");
            foreach (var r in invokedResults)
                combinedContext.AppendLine(SerializeForResponse(r)).AppendLine("---");

            combinedContext.AppendLine($"QUESTION: {request.Message}");
            combinedContext.AppendLine("ANSWER:");

            var answer = await _ollamaGenerationService.GenerateAnswerAsync(combinedContext.ToString());
            return Ok(new ChatResponse
            {
                Reply = answer?.Trim() ?? "No suitable answer could be generated.",
                Success = true,
                HasSources = true
            });
        }

        // STEP 6: Only documents
        if (relevantResults.Any())
        {
            var answer = await _ollamaGenerationService.GenerateAnswerAsync(contextText);
            return Ok(new ChatResponse
            {
                Reply = answer?.Trim() ?? "Found relevant documents but couldn’t generate a clear answer.",
                Success = true,
                HasSources = true
            });
        }

        // STEP 7: Only MCP services
        if (invokedResults.Any())
        {
            var serviceContext = new StringBuilder();
            foreach (var r in invokedResults)
                serviceContext.AppendLine(SerializeForResponse(r)).AppendLine("---");

            var prompt = $"You are a helpful assistant. Use the following service data to answer:\n{serviceContext}\nQUESTION: {request.Message}\nANSWER:";
            var answer = await _ollamaGenerationService.GenerateAnswerAsync(prompt);

            return Ok(new ChatResponse
            {
                Reply = answer?.Trim() ?? "Found relevant services but couldn’t generate a clear answer.",
                Success = true,
                HasSources = true
            });
        }

        // STEP 8: Nothing found
        return Ok(new ChatResponse
        {
            Reply = "I couldn't find relevant documents or services to answer your query.",
            Success = false,
            HasSources = false
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unified chat error - ID: {CorrelationId}", correlationId);
        return Ok(new ChatResponse
        {
            Reply = "An error occurred while processing your question.",
            Success = false
        });
    }
}




    #region Helpers
        private static double CosineSimilarity(List<float> v1, List<float> v2)
    {
        if (v1.Count != v2.Count || v1.Count == 0) return -1;
        double dot = 0, mag1 = 0, mag2 = 0;
        for (int i = 0; i < v1.Count; i++)
        {
            dot += v1[i] * v2[i];
            mag1 += v1[i] * v1[i];
            mag2 += v2[i] * v2[i];
        }
        return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2));
    }

    private async Task<bool> CheckIfDocumentsExistAsync()
    {
        try
        {
            var allChunks = await _qdrantService.GetAllPayloadTextsAsync(CollectionName);
            return allChunks.Any();
        }
        catch
        {
            return false;
        }
    }

    private string BuildChatContext(List<SearchResult> relevantResults, string question)
    {
        var contextBuilder = new StringBuilder();
        
        contextBuilder.AppendLine("You are a helpful assistant. Answer the user's question using ONLY the information provided in the context below.");
        contextBuilder.AppendLine("If the context doesn't contain enough information to answer the question, say so politely.");
        contextBuilder.AppendLine("Be concise but complete in your response.");
        contextBuilder.AppendLine();
        
        contextBuilder.AppendLine("CONTEXT:");
        foreach (var result in relevantResults)
        {
            contextBuilder.AppendLine(result.Text);
            contextBuilder.AppendLine("---");
        }
        
        contextBuilder.AppendLine();
        contextBuilder.AppendLine($"QUESTION: {question}");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("ANSWER:");

        return contextBuilder.ToString();
    }

    private double CalculateSimpleConfidence(double avgScore, int sourceCount)
    {
        var baseConfidence = Math.Min(avgScore * 100, 85);
        var sourceBonus = Math.Min(sourceCount * 2, 10);
        return Math.Round(Math.Min(baseConfidence + sourceBonus, 90), 1);
    }

    private static string SerializeForResponse(object? obj)
    {
        if (obj == null)
            return "No data returned.";

        try
        {
            return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch
        {
            return obj.ToString() ?? "Result serialization failed.";
        }
    }

    private static object? GetDefault(Type t)
    {
        if (t.IsValueType)
            return Activator.CreateInstance(t);
        return null;
    }
        #endregion

    }

}
