namespace AgenticAIAPI.Models
{
        public class ChunkValidationResult
        {
            public string Chunk { get; set; } = string.Empty;
            public int ChunkIndex { get; set; }
            public bool ExistsInQdrant { get; set; }
            public double? SimilarityScore { get; set; }
        }

}
