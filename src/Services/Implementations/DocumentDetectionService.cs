using System.Text;
using UglyToad.PdfPig;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class DocumentDetectionService : IDocumentDetectionService
{
    public async Task<DocumentDetectionResult> DetectAsync(Stream stream, string extension, string mimeType)
    {
        var result = new DocumentDetectionResult();
        extension = extension.ToLowerInvariant();

        if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || mimeType.StartsWith("image/"))
        {
            result.IsImage = true;
            result.HasTextLayer = false;
            result.IsScannedDocument = true; // Technically, images don't have text layers and require OCR.
            return result;
        }

        if (extension == ".pdf" || mimeType == "application/pdf")
        {
            result.IsPdf = true;
            
            // Check if PDF has text layer
            try
            {
                var startPosition = stream.Position;
                
                using (var document = PdfDocument.Open(stream))
                {
                    bool hasText = false;
                    
                    // Sample first few pages to detect text
                    int pagesToCheck = Math.Min(3, document.NumberOfPages);
                    for (int i = 1; i <= pagesToCheck; i++)
                    {
                        var page = document.GetPage(i);
                        var text = page.Text;
                        
                        // If we find meaningful text, it has a text layer
                        if (!string.IsNullOrWhiteSpace(text) && text.Trim().Length > 20)
                        {
                            hasText = true;
                            break;
                        }
                    }

                    result.HasTextLayer = hasText;
                    result.IsScannedDocument = !hasText;
                }
                
                // Reset stream position
                stream.Position = startPosition;
            }
            catch
            {
                // If parsing fails, default to assuming it might need OCR or is broken.
                result.HasTextLayer = false;
                result.IsScannedDocument = true;
            }

            return result;
        }

        // For Docx, Txt, etc.
        result.HasTextLayer = true;
        result.IsScannedDocument = false;
        
        return result;
    }
}
