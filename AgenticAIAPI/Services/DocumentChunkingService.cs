namespace AgenticAIAPI.Services
{

public class TextChunkingService
{
    private readonly int _maxChunkSize;
    private readonly int _overlapSize;
    
    public TextChunkingService(int maxChunkSize = 500, int overlapSize = 50)
    {
        _maxChunkSize = maxChunkSize;
        _overlapSize = Math.Min(overlapSize, maxChunkSize / 4); // Overlap shouldn't exceed 25% of chunk size
    }
    
    public List<string> ChunkText(string input)
    {
        var chunks = new List<string>();
        
        if (string.IsNullOrWhiteSpace(input))
            return chunks;
        
        // Normalize line endings and clean up whitespace
        input = input.Replace("\r\n", "\n").Replace("\r", "\n");
        
        // First try to split by paragraphs (double newlines)
        var paragraphs = input.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        string currentChunk = "";
        
        foreach (var paragraph in paragraphs)
        {
            var cleanParagraph = paragraph.Trim();
            if (string.IsNullOrEmpty(cleanParagraph))
                continue;
            
            // If current chunk + new paragraph exceeds max size, finalize current chunk
            if (!string.IsNullOrEmpty(currentChunk) && 
                (currentChunk.Length + cleanParagraph.Length + 2) > _maxChunkSize)
            {
                chunks.Add(currentChunk.Trim());
                
                // Start new chunk with overlap from previous chunk if enabled
                currentChunk = GetOverlapText(currentChunk) + cleanParagraph;
            }
            else
            {
                // Add paragraph to current chunk
                currentChunk = string.IsNullOrEmpty(currentChunk) 
                    ? cleanParagraph 
                    : currentChunk + "\n\n" + cleanParagraph;
            }
            
            // If single paragraph exceeds max size, split it by sentences
            if (currentChunk.Length > _maxChunkSize)
            {
                var sentenceChunks = ChunkBySentences(currentChunk);
                chunks.AddRange(sentenceChunks.Take(sentenceChunks.Count - 1));
                currentChunk = sentenceChunks.Last();
            }
        }
        
        // Add final chunk if it has content
        if (!string.IsNullOrWhiteSpace(currentChunk))
        {
            chunks.Add(currentChunk.Trim());
        }
        
        return chunks.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
    }
    
    private List<string> ChunkBySentences(string text)
    {
        var chunks = new List<string>();
        var sentences = SplitIntoSentences(text);
        
        string currentChunk = "";
        
        foreach (var sentence in sentences)
        {
            if (!string.IsNullOrEmpty(currentChunk) && 
                (currentChunk.Length + sentence.Length + 1) > _maxChunkSize)
            {
                chunks.Add(currentChunk.Trim());
                currentChunk = GetOverlapText(currentChunk) + sentence;
            }
            else
            {
                currentChunk = string.IsNullOrEmpty(currentChunk) 
                    ? sentence 
                    : currentChunk + " " + sentence;
            }
            
            // If single sentence exceeds max size, split by words as last resort
            if (currentChunk.Length > _maxChunkSize)
            {
                var wordChunks = ChunkByWords(currentChunk);
                chunks.AddRange(wordChunks.Take(wordChunks.Count - 1));
                currentChunk = wordChunks.Last();
            }
        }
        
        if (!string.IsNullOrWhiteSpace(currentChunk))
        {
            chunks.Add(currentChunk.Trim());
        }
        
        return chunks;
    }
    
    private List<string> ChunkByWords(string text)
    {
        var chunks = new List<string>();
        var words = text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        string currentChunk = "";
        
        foreach (var word in words)
        {
            if (!string.IsNullOrEmpty(currentChunk) && 
                (currentChunk.Length + word.Length + 1) > _maxChunkSize)
            {
                chunks.Add(currentChunk.Trim());
                currentChunk = GetOverlapText(currentChunk) + word;
            }
            else
            {
                currentChunk = string.IsNullOrEmpty(currentChunk) 
                    ? word 
                    : currentChunk + " " + word;
            }
        }
        
        if (!string.IsNullOrWhiteSpace(currentChunk))
        {
            chunks.Add(currentChunk.Trim());
        }
        
        return chunks;
    }
    
    private List<string> SplitIntoSentences(string text)
    {
        // Simple sentence splitting - can be enhanced with more sophisticated logic
        var sentences = new List<string>();
        var sentenceEnders = new[] { '.', '!', '?' };
        
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (sentenceEnders.Contains(text[i]))
            {
                // Look ahead to avoid splitting on abbreviations like "Dr." or "Mr."
                if (i < text.Length - 1 && char.IsWhiteSpace(text[i + 1]) &&
                    (i == text.Length - 2 || char.IsUpper(text[i + 2])))
                {
                    var sentence = text.Substring(start, i - start + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(sentence))
                    {
                        sentences.Add(sentence);
                    }
                    start = i + 1;
                }
            }
        }
        
        // Add remaining text as last sentence
        if (start < text.Length)
        {
            var lastSentence = text.Substring(start).Trim();
            if (!string.IsNullOrWhiteSpace(lastSentence))
            {
                sentences.Add(lastSentence);
            }
        }
        
        return sentences;
    }
    
    private string GetOverlapText(string chunk)
    {
        if (_overlapSize <= 0 || string.IsNullOrEmpty(chunk))
            return "";
        
        // Try to get overlap at word boundary
        if (chunk.Length <= _overlapSize)
            return chunk + "\n\n";
        
        var overlapText = chunk.Substring(Math.Max(0, chunk.Length - _overlapSize));
        var lastSpaceIndex = overlapText.LastIndexOf(' ');
        
        if (lastSpaceIndex > 0)
        {
            overlapText = overlapText.Substring(lastSpaceIndex + 1);
        }
        
        return overlapText + "\n\n";
    }
}
}
