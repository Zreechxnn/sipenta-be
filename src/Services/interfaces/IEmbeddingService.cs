using Pgvector;

namespace SIPENTA.Api.Services.Interfaces;

public interface IEmbeddingService
{
    Task<Vector> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
