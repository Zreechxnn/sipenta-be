namespace SIAP.Api.Entities;

public class DocumentImage
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public int PageNumber { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/png";
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }
    public string? Caption { get; set; }
    public string? ContextText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
