using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.Entities;

namespace SIAP.Api.Services.Chunking.Implementations;

public class HeadingChunkStrategy : BaseChunkStrategy
{
    public override string StrategyName => "Heading";

    public HeadingChunkStrategy(IOptions<ChunkOptions> options) : base(options)
    {
    }

    public override Task<IReadOnlyList<DocumentChunk>> ChunkAsync(Document document, string rawText, CancellationToken cancellationToken)
    {
        var chunks = new List<DocumentChunk>();

        if (string.IsNullOrWhiteSpace(rawText))
            return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);

        ExtractGlobalMetadata(document, rawText);

        var lines = rawText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        
        int maxChars = _options.MaxCharacters > 0 ? _options.MaxCharacters : 2000;

        var sb = new StringBuilder();
        int chunkIndex = 1;
        int startOffset = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = lines[i].Trim();
            
            // Check for headings
            bool isHeading = Regex.IsMatch(line, @"^(BAB\s+[IVXLCDM]+|Bagian\s+Keempat|Pasal\s+\d+)", RegexOptions.IgnoreCase);

            if (isHeading && sb.Length > 0)
            {
                var content = sb.ToString().TrimEnd();
                int endOffset = startOffset + content.Length;

                chunks.Add(CreateChunk(document, content, chunkIndex, startOffset, endOffset, "HEADING"));
                
                chunkIndex++;
                sb.Clear();
                startOffset = endOffset;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine(line);
            }
            
            // Fallback if the chunk gets too big
            if (sb.Length > maxChars)
            {
                var content = sb.ToString().TrimEnd();
                int endOffset = startOffset + content.Length;

                chunks.Add(CreateChunk(document, content, chunkIndex, startOffset, endOffset, "HEADING"));
                
                chunkIndex++;
                sb.Clear();
                startOffset = endOffset;
            }
        }

        if (sb.Length > 0)
        {
            var content = sb.ToString().TrimEnd();
            int endOffset = startOffset + content.Length;
            chunks.Add(CreateChunk(document, content, chunkIndex, startOffset, endOffset, "HEADING"));
        }

        return Task.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
    }
}
