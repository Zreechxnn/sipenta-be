using Pgvector;

namespace SIAP.Api.Services.Interfaces;

public interface IEmbeddingService
{
    Task<Vector> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
