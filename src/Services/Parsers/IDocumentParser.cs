using SIPENTA.Api.Services.Parsers.Models;

namespace SIPENTA.Api.Services.Parsers;

public interface IDocumentParser
{
    bool CanParse(string extension, string mimeType);
    Task<DocumentExtractionResult> ParseAsync(Stream fileStream);
}
