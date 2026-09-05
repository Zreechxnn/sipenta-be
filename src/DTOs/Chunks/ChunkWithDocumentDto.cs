namespace SIAP.Api.DTOs.Chunks;

public class ChunkWithDocumentDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string ChunkType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int WordCount { get; set; }
    public int EstimatedToken { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Metadata { get; set; }
    
    // Document Fields
    public string? NamaTenagaAhli { get; set; }
    public string? Path { get; set; }
    public string? Status { get; set; }
    public string? PeriodeLaporan { get; set; }
    public DateTime? TanggalUpload { get; set; }
    public long? Ukuran { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingFinishedAt { get; set; }
    public TimeSpan? ProcessingDuration { get; set; }
}
