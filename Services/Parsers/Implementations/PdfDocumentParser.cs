using SIAP.Api.Services.Parsers.Models;
using UglyToad.PdfPig;
using System.Text;
using System.Linq;

namespace SIAP.Api.Services.Parsers.Implementations;

public class PdfDocumentParser : IDocumentParser
{
    public bool CanParse(string extension, string mimeType)
    {
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || 
               mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    public Task<DocumentExtractionResult> ParseAsync(Stream fileStream)
    {
        var result = new DocumentExtractionResult();
        var sb = new StringBuilder();

        using var ms = new MemoryStream();
        fileStream.CopyTo(ms);
        ms.Position = 0;

        using (var pdf = PdfDocument.Open(ms))
        {
            result.PageCount = pdf.NumberOfPages;
            
            // Default language fallback as PdfPig might not easily expose this
            result.Language = "Unknown";

            foreach (var page in pdf.GetPages())
            {
                string text = page.Text ?? string.Empty;
                sb.AppendLine(text);
            }
        }

        result.RawText = sb.ToString();
        return Task.FromResult(result);
    }
}
