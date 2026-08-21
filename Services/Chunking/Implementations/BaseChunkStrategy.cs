using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.Entities;
using SIAP.Api.Services.Chunking.Interfaces;

namespace SIAP.Api.Services.Chunking.Implementations;

public abstract class BaseChunkStrategy : IChunkStrategy
{
    protected readonly ChunkOptions _options;

    public abstract string StrategyName { get; }

    protected BaseChunkStrategy(IOptions<ChunkOptions> options)
    {
        _options = options.Value;
    }

    public abstract Task<IReadOnlyList<DocumentChunk>> ChunkAsync(Document document, string rawText, CancellationToken cancellationToken);

    protected void ExtractGlobalMetadata(Document document, string rawText)
    {
        SIAP.Api.Common.DocumentHelper.EnrichDocumentMetadata(document, rawText);
    }

    protected DocumentChunk CreateChunk(Document document, string content, int chunkIndex, int startOffset, int endOffset, string chunkType, ChunkMetadata? localMetadata = null)
    {
        int wordCount = content.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        int estimatedToken = content.Length / 4; 

        ChunkMetadata metadata = localMetadata != null ? localMetadata.Clone() : new ChunkMetadata();

        // Always try to extract Bab and Pasal strictly from the chunk's content if not already set
        if (string.IsNullOrWhiteSpace(metadata.Bab))
        {
            metadata.Bab = ExtractBab(content);
        }
        if (string.IsNullOrWhiteSpace(metadata.Pasal))
        {
            metadata.Pasal = ExtractPasal(content);
        }
        if (string.IsNullOrWhiteSpace(metadata.Ayat))
        {
            metadata.Ayat = ExtractAyat(content);
        }

        // Validate ChunkType
        var validTypes = new[] { "PARAGRAPH", "SENTENCE", "PAGE", "SEMANTIC", "BAB", "PASAL", "AYAT", "HEADING", "FIXED_SIZE", "TOKEN" };
        if (string.IsNullOrWhiteSpace(chunkType) || !validTypes.Contains(chunkType.ToUpperInvariant()))
        {
            chunkType = "PARAGRAPH";
        }

        return new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            ChunkIndex = chunkIndex,
            ChunkType = chunkType.ToUpperInvariant(),
            Content = content,
            CharacterCount = content.Length,
            WordCount = wordCount,
            EstimatedToken = estimatedToken,
            StartOffset = startOffset,
            EndOffset = endOffset,
            CreatedAt = DateTime.UtcNow,
            Metadata = JsonSerializer.Serialize(metadata)
        };
    }

    private string? ExtractBab(string content)
    {
        var match = Regex.Match(content, @"\bBAB\s+[IVXLCDM]+\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }

    private string? ExtractPasal(string content)
    {
        var match = Regex.Match(content, @"\bPasal\s+\d+\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }

    private string? ExtractAyat(string content)
    {
        var match = Regex.Match(content, @"\(\d+\)", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }
}
