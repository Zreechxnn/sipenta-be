namespace SIAP.Api.Services.Interfaces;

public class ExtractedPdfImage
{
    public int PageNumber { get; set; }
    public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
    public string Extension { get; set; } = "png";
    public string MimeType { get; set; } = "image/png";
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }
    public string? PageText { get; set; }
    public string? Caption { get; set; }
}

public interface IPdfImageExtractor
{
    Task<List<ExtractedPdfImage>> ExtractImagesAsync(Stream pdfStream);
}
