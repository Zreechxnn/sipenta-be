using SIAP.Api.Entities;

namespace SIAP.Api.Services.Chunking.Interfaces;

public interface IChunkStrategy
{
    string StrategyName { get; }
    Task<IReadOnlyList<DocumentChunk>> ChunkAsync(Document document, string rawText, CancellationToken cancellationToken);
}
