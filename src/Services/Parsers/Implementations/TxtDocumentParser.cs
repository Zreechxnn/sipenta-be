using SIAP.Api.Services.Parsers.Models;

namespace SIAP.Api.Services.Parsers.Implementations;

public class TxtDocumentParser : IDocumentParser
{
    public bool CanParse(string extension, string mimeType)
    {
        return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) || 
               mimeType.Equals("text/plain", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<DocumentExtractionResult> ParseAsync(Stream fileStream)
    {
        using var reader = new StreamReader(fileStream, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        
        return new DocumentExtractionResult
        {
            RawText = text,
            PageCount = 1, // TXT typically considered 1 logical page
            Language = "Unknown"
        };
    }
}
