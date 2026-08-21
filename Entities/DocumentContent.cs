namespace SIAP.Api.Entities;

public class DocumentContent
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string RawText { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public string Language { get; set; } = string.Empty;
    public ParseStatus ParseStatus { get; set; } = ParseStatus.Pending;
    public DateTime? ParsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public OcrStatus OcrStatus { get; set; } = OcrStatus.Pending;
    public string? OcrEngine { get; set; }
    public DateTime? OcrStartedAt { get; set; }
    public DateTime? OcrFinishedAt { get; set; }
    public TimeSpan? OcrDuration { get; set; }
    public double? OcrConfidence { get; set; }
    public bool IsScannedDocument { get; set; }
    public bool HasTextLayer { get; set; }

    public Document Document { get; set; } = null!;
}
