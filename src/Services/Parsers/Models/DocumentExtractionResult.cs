namespace SIAP.Api.Services.Parsers.Models;

public class DocumentExtractionResult
{
    public string RawText { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public string Language { get; set; } = string.Empty;
}
