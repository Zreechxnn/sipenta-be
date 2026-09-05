namespace SIAP.Api.Services.Interfaces;

public interface IDocumentDetectionService
{
    Task<DocumentDetectionResult> DetectAsync(Stream stream, string extension, string mimeType);
}
