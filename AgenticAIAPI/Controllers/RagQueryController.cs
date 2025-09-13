using AgenticAIAPI.Models;
using AgenticAIAPI.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using System.Text;

namespace AgenticAIAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RagQueryController : ControllerBase
    {
        private readonly OllamaEmbeddingService _embeddingService;
        private readonly QdrantService _qdrantService;
        private readonly OllamaGenerationService _ollamaGenerationService;
        private readonly ILogger<RagQueryController> _logger;
    
        private const string CollectionName = "documents";
        private const int DefaultTopK = 3;
        private const int MaxTopK = 10;
        private const int MaxQuestionLength = 1000;

        public RagQueryController(ILogger<RagQueryController> logger)
        {
            _embeddingService = new OllamaEmbeddingService();
            _qdrantService = new QdrantService();
            _ollamaGenerationService = new OllamaGenerationService();
            _logger = logger;
        }

        [HttpPost("query")]
        public async Task<IActionResult> QueryRagAsync([FromBody] QueryRequest request)
        {
            var correlationId = Guid.NewGuid().ToString()[..8];
            _logger.LogInformation("RAG Query started - ID: {CorrelationId}", correlationId);

            try
            {
                // Input validation
                var validationResult = ValidateQueryRequest(request);
                if (validationResult != null)
                    return validationResult;

                var topK = Math.Min(request.TopK ?? DefaultTopK, MaxTopK);
            
                _logger.LogInformation("Processing query: '{Question}' (TopK: {TopK}) - ID: {CorrelationId}", 
                    request.Question?.Substring(0, Math.Min(50, request.Question.Length)), topK, correlationId);

                // Check if collection exists and has data
                var collectionStatus = await CheckCollectionStatusAsync();
                if (!collectionStatus.IsValid)
                {
                    return Ok(new QueryResponse
                    {
                        Answer = "I don't have any documents to reference yet. Please upload some documents first.",
                        Sources = new List<SourceInfo>(),
                        Confidence = 0.0,
                        Message = collectionStatus.Message,
                        CorrelationId = correlationId
                    });
                }

                // 1. Generate embedding for user question
                var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Question!);
                if (queryEmbedding == null || !queryEmbedding.Any())
                {
                    throw new InvalidOperationException("Failed to generate embedding for the question");
                }

                // 2. Search Qdrant for top-k similar chunks
                var searchResults = await _qdrantService.SearchPointsWithScoresAsync(CollectionName, queryEmbedding, topK);
            
                if (!searchResults.Any())
                {
                    return Ok(new QueryResponse
                    {
                        Answer = "I couldn't find any relevant information to answer your question. Try rephrasing or asking about different topics.",
                        Sources = new List<SourceInfo>(),
                        Confidence = 0.0,
                        Message = "No relevant chunks found",
                        CorrelationId = correlationId
                    });
                }

                // 3. Filter by relevance threshold and prepare context
                var relevantResults = searchResults
                    .Where(r => r.Score >= (request.MinimumScore ?? 0.3)) // Default minimum relevance
                    .ToList();

                if (!relevantResults.Any())
                {
                    return Ok(new QueryResponse
                    {
                        Answer = "The available information doesn't seem relevant to your question. Could you try rephrasing or asking about something more specific?",
                        Sources = searchResults.Select(r => new SourceInfo 
                        { 
                            Text = TruncateText(r.Text, 200), 
                            Score = r.Score 
                        }).ToList(),
                        Confidence = 0.0,
                        Message = "No chunks met minimum relevance threshold",
                        CorrelationId = correlationId
                    });
                }

                // 4. Build enhanced context with metadata
                var context = BuildEnhancedContext(relevantResults, request.Question!);
            
                // 5. Generate answer using LLM
                var answer = await _ollamaGenerationService.GenerateAnswerAsync(context.Prompt);

                if (string.IsNullOrWhiteSpace(answer))
                {
                    throw new InvalidOperationException("LLM returned empty response");
                }

                // 6. Calculate confidence based on relevance scores
                var avgScore = relevantResults.Average(r => r.Score);
                var confidence = CalculateConfidence(avgScore, relevantResults.Count, relevantResults, request.Question!);


                var response = new QueryResponse
                {
                    Answer = answer.Trim(),
                    Sources = relevantResults.Select(r => new SourceInfo
                    {
                        Text = TruncateText(r.Text, 300),
                        Score = r.Score,
                        Rank = relevantResults.IndexOf(r) + 1
                    }).ToList(),
                    Confidence = confidence,
                    Message = $"Found {relevantResults.Count} relevant sources",
                    CorrelationId = correlationId,
                    ProcessingTimeMs = 0 // You could add timing if needed
                };

                _logger.LogInformation("RAG Query completed successfully - ID: {CorrelationId}, Sources: {SourceCount}, Confidence: {Confidence:F2}", 
                    correlationId, relevantResults.Count, confidence);

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RAG Query failed - ID: {CorrelationId}", correlationId);
                return StatusCode(500, new { 
                    error = "An error occurred while processing your question", 
                    correlationId,
                    details = ex.Message 
                });
            }
        }

        private IActionResult? ValidateQueryRequest(QueryRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required");

            if (string.IsNullOrWhiteSpace(request.Question))
                return BadRequest("Question text is required");

            if (request.Question.Length > MaxQuestionLength)
                return BadRequest($"Question exceeds maximum length of {MaxQuestionLength} characters");

            if (request.TopK.HasValue && (request.TopK <= 0 || request.TopK > MaxTopK))
                return BadRequest($"TopK must be between 1 and {MaxTopK}");

            if (request.MinimumScore.HasValue && (request.MinimumScore < 0 || request.MinimumScore > 1))
                return BadRequest("MinimumScore must be between 0 and 1");

            return null;
        }

        private async Task<(bool IsValid, string Message)> CheckCollectionStatusAsync()
        {
            try
            {
                var allChunks = await _qdrantService.GetAllPayloadTextsAsync(CollectionName);
                if (!allChunks.Any())
                {
                    return (false, "No documents available in the knowledge base");
                }
                return (true, $"{allChunks.Count} documents available");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check collection status");
                return (false, "Knowledge base is not available");
            }
        }

        private ContextInfo BuildEnhancedContext(List<SearchResult> relevantResults, string question)
        {
            var promptBuilder = new StringBuilder();
        
            promptBuilder.AppendLine("You are a helpful assistant that answers questions based on the provided context.");
            promptBuilder.AppendLine("Use only the information from the context below to answer the question.");
            promptBuilder.AppendLine("If the context doesn't contain enough information, say so clearly.");
            promptBuilder.AppendLine();
        
            promptBuilder.AppendLine("CONTEXT:");
            for (int i = 0; i < relevantResults.Count; i++)
            {
                promptBuilder.AppendLine($"[Source {i + 1}] (Relevance: {relevantResults[i].Score:F2})");
                promptBuilder.AppendLine(relevantResults[i].Text);
                promptBuilder.AppendLine("---");
            }
        
            promptBuilder.AppendLine();
            promptBuilder.AppendLine($"QUESTION: {question}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Please provide a clear and concise answer based on the context above:");

            return new ContextInfo
            {
                Prompt = promptBuilder.ToString(),
                SourceCount = relevantResults.Count
            };
        }

        private double CalculateConfidence(double avgScore, int sourceCount, List<SearchResult> results = null!, string question = null!)
        {
            // Base confidence from average similarity score (0-100 scale)
            var baseConfidence = Math.Min(avgScore * 100, 90); // Cap at 90% for base score
    
            // Source diversity bonus - diminishing returns
            var sourceBonus = sourceCount switch
            {
                1 => 0,           // Single source - no bonus
                2 => 3,           // Two sources - small bonus
                3 => 5,           // Three sources - good bonus
                >= 4 => 7         // Multiple sources - maximum bonus
,
                _ => throw new NotImplementedException()
            };
    
            // Score consistency bonus - reward consistent high scores across sources
            var consistencyBonus = 0.0;
            if (results != null && results.Count > 1)
            {
                var scores = results.Select(r => r.Score).ToList();
                var scoreStdDev = CalculateStandardDeviation(scores);
                var avgScoreForConsistency = scores.Average();
        
                // Lower standard deviation with high average = more consistent = higher confidence
                if (scoreStdDev < 0.1 && avgScoreForConsistency > 0.7)
                    consistencyBonus = 3;
                else if (scoreStdDev < 0.15 && avgScoreForConsistency > 0.6)
                    consistencyBonus = 2;
                else if (scoreStdDev < 0.2 && avgScoreForConsistency > 0.5)
                    consistencyBonus = 1;
            }
    
            // Question length penalty - very short questions might be ambiguous
            var questionPenalty = 0.0;
            if (!string.IsNullOrEmpty(question))
            {
                var wordCount = question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordCount < 3)
                    questionPenalty = -5; // Very short questions are harder to answer accurately
                else if (wordCount < 5)
                    questionPenalty = -2; // Short questions might be ambiguous
            }
    
            // Score gap penalty - large gap between top results suggests uncertainty
            var scoreGapPenalty = 0.0;
            if (results != null && results.Count > 1)
            {
                var topScore = results.First().Score;
                var secondScore = results.Skip(1).First().Score;
                var scoreGap = topScore - secondScore;
        
                if (scoreGap > 0.3)
                    scoreGapPenalty = -3; // Large gap suggests one dominant but potentially isolated result
                else if (scoreGap > 0.2)
                    scoreGapPenalty = -1; // Moderate gap
            }
    
            // Low score threshold penalty - if even the best match is poor
            var lowScorePenalty = 0.0;
            if (results != null && results.Any())
            {
                var topScore = results.First().Score;
                if (topScore < 0.4)
                    lowScorePenalty = -10; // Very poor match
                else if (topScore < 0.6)
                    lowScorePenalty = -5;  // Poor match
            }
    
            // Calculate final confidence
            var confidence = baseConfidence + sourceBonus + consistencyBonus + questionPenalty + scoreGapPenalty + lowScorePenalty;
    
            // Apply confidence bands for clearer interpretation
            confidence = confidence switch
            {
                >= 85 => Math.Min(confidence, 95), // Very High - cap at 95%
                >= 70 => Math.Min(confidence, 85), // High
                >= 50 => Math.Min(confidence, 70), // Medium  
                >= 30 => Math.Min(confidence, 50), // Low
                _ => Math.Max(confidence, 5)       // Very Low - minimum 5%
            };
    
            return Math.Round(Math.Max(confidence, 0), 1);
        }

        private double CalculateStandardDeviation(List<double> values)
        {
            if (values.Count <= 1) return 0;
    
            var average = values.Average();
            var squaredDifferences = values.Select(value => Math.Pow(value - average, 2));
            var variance = squaredDifferences.Average();
            return Math.Sqrt(variance);
        }

        // Optional: Add confidence level interpretation
        private string GetConfidenceLevel(double confidence)
        {
            return confidence switch
            {
                >= 85 => "Very High",
                >= 70 => "High", 
                >= 50 => "Medium",
                >= 30 => "Low",
                _ => "Very Low"
            };
        }

        // Update your controller call to:
        // var confidence = CalculateConfidence(avgScore, relevantResults.Count, relevantResults, request.Question);

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength - 3) + "...";
        }


    }
}
