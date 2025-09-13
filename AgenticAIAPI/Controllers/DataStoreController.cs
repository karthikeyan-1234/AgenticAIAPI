using AgenticAIAPI.Models;
using AgenticAIAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace AgenticAIAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DataStoreController : ControllerBase
    {
        private readonly TextChunkingService _chunkingService;
        private readonly OllamaEmbeddingService _embeddingService;
        private readonly QdrantService _qdrantService;

        public DataStoreController()
        {
            _chunkingService = new TextChunkingService(500);
            _embeddingService = new OllamaEmbeddingService();
            _qdrantService = new QdrantService();
        }

        [HttpPost("chunk")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadAndChunkPolicy(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded or file is empty.");

            var extension = Path.GetExtension(file.FileName);
            if (!extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Only .txt files are allowed.");

            var collectionName = file.FileName;

            string fileText;
            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            {
                fileText = await reader.ReadToEndAsync();
            }

            var chunks = _chunkingService.ChunkText(fileText);
            var embeddings = await _embeddingService.GetEmbeddingsAsync(chunks);

            if (embeddings == null || embeddings.Count == 0)
                return BadRequest("Failed to generate embeddings.");

            await _qdrantService.CreateCollectionIfNotExistsAsync(collectionName, embeddings.First().Count);
            await _qdrantService.UpsertPointsAsync(collectionName, chunks, embeddings);

            return Ok(new
            {
                chunkCount = chunks.Count,
                chunks,
                embeddingsCount = embeddings.Count
            });
        }

        [HttpPost("validate")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ValidateFileInQdrant(IFormFile file)
        {
            try
            {
                // Input validation
                var validationResult = ValidateFileInput(file);
                if (validationResult != null)
                    return validationResult;

                // Extract and chunk file text
                var fileText = await ExtractFileTextAsync(file);
                var chunks = _chunkingService.ChunkText(fileText);
        
                if (!chunks.Any())
                {
                    return BadRequest("File contains no processable text content.");
                }

                // Normalize chunks for comparison
                var normalizedChunks = chunks.Select(NormalizeText).ToHashSet();
        
                string collectionName = file.FileName;
        
                // Check if collection exists
                var collectionExists = await CheckCollectionExistsAsync(collectionName);
                if (!collectionExists)
                {
                    return Ok(new ValidationResponse
                    {
                        ChunkCount = chunks.Count,
                        FoundInQdrant = 0,
                        ExistsInQdrant = false,
                        ValidationResults = chunks.Select(chunk => new ChunkValidationResult
                        {
                            Chunk = chunk,
                            ExistsInQdrant = false,
                            SimilarityScore = null
                        }).ToList(),
                        Message = "Collection 'documents' does not exist in Qdrant."
                    });
                }

                // Get all stored chunks from Qdrant
                var storedChunks = await _qdrantService.GetAllPayloadTextsAsync(collectionName);
                var normalizedStoredChunks = storedChunks.Select(NormalizeText).ToHashSet();

                // Perform validation with detailed results
                var validationResults = chunks.Select((chunk, index) => new ChunkValidationResult
                {
                    Chunk = chunk,
                    ChunkIndex = index,
                    ExistsInQdrant = normalizedStoredChunks.Contains(NormalizeText(chunk)),
                    SimilarityScore = null // Could be enhanced with fuzzy matching
                }).ToList();

                var foundCount = validationResults.Count(r => r.ExistsInQdrant);
                var allChunksExist = foundCount == chunks.Count;

                // Log detailed results
                LogValidationResults(file.FileName, validationResults);

                return Ok(new ValidationResponse
                {
                    ChunkCount = chunks.Count,
                    FoundInQdrant = foundCount,
                    ExistsInQdrant = allChunksExist,
                    ValidationResults = validationResults,
                    Message = GenerateValidationMessage(foundCount, chunks.Count),
                    CollectionInfo = new CollectionInfo
                    {
                        Name = collectionName,
                        TotalStoredChunks = storedChunks.Count
                    }
                });
            }
            catch (Exception ex)
            {
                // Log exception details
                Console.WriteLine($"Validation error for file '{file?.FileName}': {ex.Message}");
                return StatusCode(500, new { error = "An error occurred during validation", details = ex.Message });
            }
        }

        [HttpDelete("deleteCollection/{collectionName}")]
        public async Task<IActionResult> DeleteCollection(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                return BadRequest("Collection name is required.");
            try
            {
                await _qdrantService.DeleteCollectionAsync(collectionName);
                return Ok(new { message = $"Collection '{collectionName}' deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to delete collection", details = ex.Message });
            }
        }

        [HttpGet("listCollections")]
        public async Task<IActionResult> ListCollections()
        {
            try
            {
                var collections = await _qdrantService.ListCollectionsAsync();
                return Ok(collections);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to list collections", details = ex.Message });
            }
        }


        private IActionResult? ValidateFileInput(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded or file is empty.");
        
            var extension = Path.GetExtension(file.FileName);
            if (!extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Only .txt files are allowed.");
        
            if (file.Length > 10 * 1024 * 1024) // 10MB limit
                return BadRequest("File size exceeds 10MB limit.");
        
            return null;
        }

        private async Task<string> ExtractFileTextAsync(IFormFile file)
        {
            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
        
            return text
                .Trim()
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\t", " ")
                .Trim();
        }

        private async Task<bool> CheckCollectionExistsAsync(string collectionName)
        {
            try
            {
                // This assumes you add a method to check collection existence
                // You can implement this in your QdrantService
                await _qdrantService.GetAllPayloadTextsAsync(collectionName);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void LogValidationResults(string fileName, List<ChunkValidationResult> results)
        {
            var missingChunks = results.Where(r => !r.ExistsInQdrant).ToList();
    
            if (missingChunks.Any())
            {
                Console.WriteLine($"Validation Results for '{fileName}':");
                Console.WriteLine($"Missing {missingChunks.Count} out of {results.Count} chunks:");
        
                foreach (var missing in missingChunks.Take(5)) // Log only first 5 for brevity
                {
                    var preview = missing.Chunk.Length > 100 
                        ? missing.Chunk.Substring(0, 100) + "..."
                        : missing.Chunk;
                    Console.WriteLine($"[{missing.ChunkIndex}] {preview}");
                }
        
                if (missingChunks.Count > 5)
                {
                    Console.WriteLine($"... and {missingChunks.Count - 5} more missing chunks");
                }
            }
            else
            {
                Console.WriteLine($"All chunks from '{fileName}' exist in Qdrant.");
            }
        }

        private string GenerateValidationMessage(int foundCount, int totalCount)
        {
            if (foundCount == 0)
                return "No chunks found in Qdrant. This appears to be a new document.";
            if (foundCount == totalCount)
                return "All chunks already exist in Qdrant. Document appears to be fully uploaded.";
            return $"Partial match: {foundCount}/{totalCount} chunks found in Qdrant.";
        }

    }
}
