using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SIAP.Api.Services.Parsers.Models;
using System.Text;

namespace SIAP.Api.Services.Parsers.Implementations;

public class DocxDocumentParser : IDocumentParser
{
    public bool CanParse(string extension, string mimeType)
    {
        return extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) || 
               mimeType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase);
    }

    public Task<DocumentExtractionResult> ParseAsync(Stream fileStream)
    {
        var result = new DocumentExtractionResult();
        var sb = new StringBuilder();

        using var ms = new MemoryStream();
        fileStream.CopyTo(ms);
        ms.Position = 0;

        using (var wordDoc = WordprocessingDocument.Open(ms, false))
        {
            var body = wordDoc.MainDocumentPart?.Document?.Body;
            if (body != null)
            {
                foreach (var paragraph in body.Elements<Paragraph>())
                {
                    sb.AppendLine(paragraph.InnerText);
                }
            }
            
            // DOCX doesn't have a reliable page count property without rendering, 
            // but we can try to check extended properties
            var extendedProps = wordDoc.ExtendedFilePropertiesPart?.Properties;
            if (extendedProps?.Pages != null && int.TryParse(extendedProps.Pages.Text, out var pages))
            {
                result.PageCount = pages;
            }
            else
            {
                result.PageCount = 1; // Default fallback
            }

            // Default language fallback as DOCX doesn't easily expose this at root
            result.Language = "Unknown";
        }

        result.RawText = sb.ToString();
        return Task.FromResult(result);
    }
}
