using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SIAP.Api.Data;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Services.Chunking.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SIAP.Api.Services.Chunking.Implementations;

public class ChunkService : IChunkService
{
    private readonly IChunkStrategyFactory _strategyFactory;
    private readonly AppDbContext _dbContext;
    private readonly IDocumentRepository _repository;
    private readonly ILogger<ChunkService> _logger;

    public ChunkService(
        IChunkStrategyFactory strategyFactory,
        AppDbContext dbContext,
        IDocumentRepository repository,
        ILogger<ChunkService> logger)
    {
        _strategyFactory = strategyFactory;
        _dbContext = dbContext;
        _repository = repository;
        _logger = logger;
    }

    public async Task ProcessChunksAsync(Guid documentId, string? strategyName = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Chunking Started for document {DocumentId}", documentId);
        var stopWatch = Stopwatch.StartNew();

        try
        {
            var document = await _repository.GetByIdAsync(documentId);
            if (document == null)
            {
                throw new Exception("Document not found");
            }

            if (document.Content == null || string.IsNullOrWhiteSpace(document.Content.RawText))
            {
                throw new Exception("RawText is empty, skipping chunking");
            }

            if (string.IsNullOrWhiteSpace(strategyName))
            {
                // Auto-detect legal document type
                string context = $"{document.Nama} {document.NamaFile}".ToLowerInvariant();
                if (context.Contains("perda") || context.Contains("perbup") || context.Contains("pergub") ||
                    context.Contains("uu") || context.Contains("perpres") || context.Contains("undang") || 
                    context.Contains("peraturan") || context.Contains("ketetapan") || context.Contains("tap") ||
                    context.Contains("putusan") || context.Contains("keputusan") || context.Contains("hukum") ||
                    document.Content.RawText.Contains("Pasal ", StringComparison.OrdinalIgnoreCase) ||
                    document.Content.RawText.Contains("BAB ", StringComparison.OrdinalIgnoreCase))
                {
                    strategyName = "Legal";
                }
            }

            var strategy = _strategyFactory.GetStrategy(strategyName);

            // Delete existing chunks
            var existingChunks = await _dbContext.DocumentChunks.Where(c => c.DocumentId == documentId).ToListAsync(cancellationToken);
            if (existingChunks.Any())
            {
                _dbContext.DocumentChunks.RemoveRange(existingChunks);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // Create new chunks
            var chunks = await strategy.ChunkAsync(document, document.Content.RawText, cancellationToken);
            
            if (chunks.Count == 0)
            {
                throw new Exception("Jumlah chunk adalah 0 (nol). Document requires > 0 chunks.");
            }

            await _dbContext.DocumentChunks.AddRangeAsync(chunks, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Chunk Saved. {Count} chunks saved to database.", chunks.Count);

            stopWatch.Stop();
            _logger.LogInformation("Chunking Finished");
            _logger.LogInformation("Chunk Count: {Count}", chunks.Count);
        }
        catch (Exception ex)
        {
            stopWatch.Stop();
            _logger.LogError(ex, "Chunking Failed for document {DocumentId}", documentId);
            throw; // Let it bubble up to the BackgroundService or Controller
        }
    }

    public Task ProcessEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
