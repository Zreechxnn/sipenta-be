using SIAP.Api.Entities;

namespace SIAP.Api.Services.Chunking.Interfaces;

public interface IChunkService
{
    Task ProcessChunksAsync(Guid documentId, string? strategyName = null, CancellationToken cancellationToken = default);
    Task ProcessEmbeddingsAsync(CancellationToken cancellationToken = default);
}
