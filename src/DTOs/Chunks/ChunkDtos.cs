namespace SIAP.Api.DTOs.Chunks;

public class DocumentChunkResponseDto
{
    public Guid Id { get; set; }
    public int ChunkIndex { get; set; }
    public string ChunkType { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int WordCount { get; set; }
    public int EstimatedToken { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Metadata { get; set; }
}

public class DocumentChunkPreviewDto
{
    public Guid Id { get; set; }
    public int ChunkIndex { get; set; }
    public string ChunkType { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int WordCount { get; set; }
    public int EstimatedToken { get; set; }
    public string? Metadata { get; set; }
    public string Preview { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class DocumentChunkListResponseDto
{
    public int TotalChunks { get; set; }
    public List<DocumentChunkPreviewDto> Chunks { get; set; } = new();
}
