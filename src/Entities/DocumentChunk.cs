using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace SIAP.Api.Entities;

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    public int ChunkIndex { get; set; }
    public string ChunkType { get; set; } = string.Empty; // e.g. BAB, PASAL, PARAGRAPH, FIXED_SIZE
    public string Content { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int WordCount { get; set; }
    public int EstimatedToken { get; set; }
    
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }

    public DateTime CreatedAt { get; set; }

    public Pgvector.Vector? Embedding { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Metadata { get; set; } // JSON serialized metadata
}
