using static AgenticAIAPI.Controllers.FileUploadController;

namespace AgenticAIAPI.Models
{
        public class ValidationResponse
        {
            public int ChunkCount { get; set; }
            public int FoundInQdrant { get; set; }
            public bool ExistsInQdrant { get; set; }
            public List<ChunkValidationResult> ValidationResults { get; set; } = new();
            public string Message { get; set; } = string.Empty;
            public CollectionInfo? CollectionInfo { get; set; }
        }
}
