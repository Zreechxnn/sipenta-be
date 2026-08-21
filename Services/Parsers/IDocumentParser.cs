using SIAP.Api.Services.Parsers.Models;

namespace SIAP.Api.Services.Parsers;

public interface IDocumentParser
{
    bool CanParse(string extension, string mimeType);
    Task<DocumentExtractionResult> ParseAsync(Stream fileStream);
}
