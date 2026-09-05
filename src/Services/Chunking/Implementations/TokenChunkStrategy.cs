using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.Entities;

namespace SIAP.Api.Services.Chunking.Implementations;

public class TokenChunkStrategy : BaseChunkStrategy
{
    public override string StrategyName => "Token";

    public TokenChunkStrategy(IOptions<ChunkOptions> options) : base(options)
    {
    }

    public override Task<IReadOnlyList<DocumentChunk>> ChunkAsync(Document document, string rawText, CancellationToken cancellationToken)
    {
        var chunks = new List<DocumentChunk>();

        if (string.IsNullOrWhiteSpace(rawText))
            return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);

        ExtractGlobalMetadata(document, rawText);

        // Approximation: 1 token = 4 characters
        int maxTokens = _options.MaxTokens;
        if (maxTokens <= 0) maxTokens = 250;
        
        int maxChars = maxTokens * 4;
        int overlap = _options.OverlapCharacters;

        if (overlap < 0 || overlap >= maxChars) overlap = 100;

        int step = maxChars - overlap;
        int currentIndex = 0;
        int chunkIndex = 1;

        while (currentIndex < rawText.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int length = Math.Min(maxChars, rawText.Length - currentIndex);
            string content = rawText.Substring(currentIndex, length);

            chunks.Add(CreateChunk(document, content, chunkIndex, currentIndex, currentIndex + length, "TOKEN"));

            if (currentIndex + length >= rawText.Length)
            {
                break;
            }

            currentIndex += step;
            chunkIndex++;
        }

        return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
    }
}
