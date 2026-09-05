using System.Text;
using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.Entities;

namespace SIAP.Api.Services.Chunking.Implementations;

public class ParagraphChunkStrategy : BaseChunkStrategy
{
    public override string StrategyName => "Paragraph";

    public ParagraphChunkStrategy(IOptions<ChunkOptions> options) : base(options)
    {
    }

    public override Task<IReadOnlyList<DocumentChunk>> ChunkAsync(Document document, string rawText, CancellationToken cancellationToken)
    {
        var chunks = new List<DocumentChunk>();

        if (string.IsNullOrWhiteSpace(rawText))
            return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);

        ExtractGlobalMetadata(document, rawText);

        var paragraphs = rawText.Split(new[] { "\r\n\r\n", "\n\n", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        int maxChars = _options.MaxCharacters > 0 ? _options.MaxCharacters : 1000;
        int overlap = _options.OverlapCharacters;

        var currentChunkParagraphs = new List<string>();
        int currentLength = 0;
        int chunkIndex = 1;
        int startOffset = 0;

        for (int i = 0; i < paragraphs.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var p = paragraphs[i].Trim();
            if (string.IsNullOrWhiteSpace(p)) continue;

            // If a single paragraph is larger than maxChars, we need to split it by sentences or fixed length.
            // But to keep it simple, we'll let it be its own chunk, unless it's insanely large.
            if (p.Length > maxChars)
            {
                // Flush existing
                if (currentChunkParagraphs.Count > 0)
                {
                    var content = string.Join("\n", currentChunkParagraphs).TrimEnd();
                    int endOffset = startOffset + content.Length;
                    chunks.Add(CreateChunk(document, content, chunkIndex++, startOffset, endOffset, "PARAGRAPH"));
                    startOffset = endOffset;
                    currentChunkParagraphs.Clear();
                    currentLength = 0;
                }

                // Chunk the huge paragraph
                int pStart = 0;
                while (pStart < p.Length)
                {
                    int pLen = Math.Min(maxChars, p.Length - pStart);
                    string slice = p.Substring(pStart, pLen);
                    int endOffset = startOffset + slice.Length;
                    chunks.Add(CreateChunk(document, slice, chunkIndex++, startOffset, endOffset, "PARAGRAPH"));
                    startOffset = endOffset;
                    
                    pStart += pLen - (overlap > 0 ? overlap : 0);
                    if (pStart >= p.Length || pLen < maxChars) break;
                }
                continue;
            }

            if (currentLength + p.Length + 1 > maxChars && currentChunkParagraphs.Count > 0)
            {
                var content = string.Join("\n", currentChunkParagraphs).TrimEnd();
                int endOffset = startOffset + content.Length;

                chunks.Add(CreateChunk(document, content, chunkIndex++, startOffset, endOffset, "PARAGRAPH"));
                startOffset = endOffset; // Approximate

                // Overlap: keep last paragraphs that fit into overlap size
                int overlapLength = 0;
                var overlapParagraphs = new List<string>();
                for (int j = currentChunkParagraphs.Count - 1; j >= 0; j--)
                {
                    if (overlapLength + currentChunkParagraphs[j].Length < overlap)
                    {
                        overlapParagraphs.Insert(0, currentChunkParagraphs[j]);
                        overlapLength += currentChunkParagraphs[j].Length + 1;
                    }
                    else
                    {
                        break;
                    }
                }

                currentChunkParagraphs.Clear();
                currentChunkParagraphs.AddRange(overlapParagraphs);
                currentLength = overlapLength;
            }

            currentChunkParagraphs.Add(p);
            currentLength += p.Length + 1; // +1 for newline
        }

        if (currentChunkParagraphs.Count > 0)
        {
            var content = string.Join("\n", currentChunkParagraphs).TrimEnd();
            int endOffset = startOffset + content.Length;
            chunks.Add(CreateChunk(document, content, chunkIndex, startOffset, endOffset, "PARAGRAPH"));
        }

        return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
    }
}
