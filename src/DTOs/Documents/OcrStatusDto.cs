using SIAP.Api.Entities;

namespace SIAP.Api.DTOs.Documents;

public class OcrStatusDto
{
    public OcrStatus OcrStatus { get; set; }
    public string? OcrEngine { get; set; }
    public TimeSpan? OcrDuration { get; set; }
    public double? OcrConfidence { get; set; }
    public bool IsScannedDocument { get; set; }
    public bool HasTextLayer { get; set; }
}
