using AgenticAIAPI.Models;
using AgenticAIAPI.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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
}

}
