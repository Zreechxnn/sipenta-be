using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.Entities;

namespace SIAP.Api.Services.Chunking.Implementations;

public class FixedLengthChunkStrategy : BaseChunkStrategy
{
    public override string StrategyName => "FixedLength";

    public FixedLengthChunkStrategy(IOptions<ChunkOptions> options) : base(options)
    {
    }

    public override Task<IReadOnlyList<DocumentChunk>> ChunkAsync(Document document, string rawText, CancellationToken cancellationToken)
    {
        var chunks = new List<DocumentChunk>();

        if (string.IsNullOrWhiteSpace(rawText))
            return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);

        ExtractGlobalMetadata(document, rawText);

        int maxChars = _options.MaxCharacters;
        int overlap = _options.OverlapCharacters;

        if (maxChars <= 0) maxChars = 1000;
        if (overlap < 0 || overlap >= maxChars) overlap = 100;

        int step = maxChars - overlap;
        int currentIndex = 0;
        int chunkIndex = 1;

        while (currentIndex < rawText.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int length = Math.Min(maxChars, rawText.Length - currentIndex);
            string content = rawText.Substring(currentIndex, length);

            chunks.Add(CreateChunk(document, content, chunkIndex, currentIndex, currentIndex + length, "FIXED_SIZE"));

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
