using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.Entities;

namespace SIAP.Api.Services.Chunking.Implementations;

public class LegalDocumentChunkStrategy : BaseChunkStrategy
{
    private readonly ParagraphChunkStrategy _fallbackStrategy;

    public override string StrategyName => "Legal";

    public LegalDocumentChunkStrategy(IOptions<ChunkOptions> options) : base(options)
    {
        _fallbackStrategy = new ParagraphChunkStrategy(options);
    }

    public override async Task<IReadOnlyList<DocumentChunk>> ChunkAsync(Document document, string rawText, CancellationToken cancellationToken)
    {
        var chunks = new List<DocumentChunk>();

        if (string.IsNullOrWhiteSpace(rawText))
            return chunks;

        ExtractGlobalMetadata(document, rawText);
        var cleaned = SIAP.Api.Common.DocumentHelper.CleanOcrNoise(rawText);

        // Determine if it looks like a legal document by finding BAB or Pasal
        bool hasBabOrPasal = Regex.IsMatch(cleaned, @"\bBAB\b\s+[IVXLCDM]+", RegexOptions.IgnoreCase) || 
                             Regex.IsMatch(cleaned, @"\bPasal\b\s+\d+", RegexOptions.IgnoreCase);

        if (!hasBabOrPasal)
        {
            // Fallback
            return await _fallbackStrategy.ChunkAsync(document, cleaned, cancellationToken);
        }

        var lines = cleaned.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        
        var sb = new StringBuilder();
        int chunkIndex = 1;
        int startOffset = 0;
        string currentChunkType = "PARAGRAPH"; // Default
        string? currentBab = null;
        string? currentPasal = null;
        string? currentAyat = null;

        for (int i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            // Check for heading boundaries
            bool isBab = Regex.IsMatch(line, @"^BAB\s+[IVXLCDM]+", RegexOptions.IgnoreCase);
            bool isPasal = Regex.IsMatch(line, @"^Pasal\s+\d+", RegexOptions.IgnoreCase);
            bool isAyat = currentPasal != null && Regex.IsMatch(line, @"^\(\d+\)", RegexOptions.IgnoreCase); 

            bool shouldSplit = isBab || isPasal || isAyat;

            if (shouldSplit && sb.Length > 0)
            {
                var content = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    int endOffset = startOffset + content.Length;
                    var meta = new ChunkMetadata
                    {
                        Bab = currentBab,
                        Pasal = currentPasal,
                        Ayat = currentAyat
                    };
                    chunks.Add(CreateChunk(document, content, chunkIndex++, startOffset, endOffset, currentChunkType, meta));
                    startOffset = endOffset;
                }
                sb.Clear();
            }

            if (isBab)
            {
                currentBab = Regex.Match(line, @"^BAB\s+[IVXLCDM]+", RegexOptions.IgnoreCase).Value;
                currentPasal = null;
                currentAyat = null;
                currentChunkType = "BAB";
            }
            else if (isPasal)
            {
                currentPasal = Regex.Match(line, @"^Pasal\s+\d+", RegexOptions.IgnoreCase).Value;
                currentAyat = null;
                currentChunkType = "PASAL";
            }
            else if (isAyat)
            {
                currentAyat = Regex.Match(line, @"^\(\d+\)", RegexOptions.IgnoreCase).Value;
                currentChunkType = "AYAT";
            }

            sb.AppendLine(line);
            
            // Fallback for extremely long chunks without structural breaks
            if (sb.Length > (_options.MaxCharacters > 0 ? _options.MaxCharacters : 2000) && !shouldSplit)
            {
                var content = sb.ToString().Trim();
                int endOffset = startOffset + content.Length;
                var meta = new ChunkMetadata
                {
                    Bab = currentBab,
                    Pasal = currentPasal,
                    Ayat = currentAyat
                };
                chunks.Add(CreateChunk(document, content, chunkIndex++, startOffset, endOffset, currentChunkType, meta));
                startOffset = endOffset;
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            var content = sb.ToString().Trim();
            int endOffset = startOffset + content.Length;
            var meta = new ChunkMetadata
            {
                Bab = currentBab,
                Pasal = currentPasal,
                Ayat = currentAyat
            };
            chunks.Add(CreateChunk(document, content, chunkIndex++, startOffset, endOffset, currentChunkType, meta));
        }

        return chunks;
    }
}
