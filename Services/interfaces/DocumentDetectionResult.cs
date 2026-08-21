namespace SIAP.Api.Services.Interfaces;

public class DocumentDetectionResult
{
    public bool IsImage { get; set; }
    public bool IsPdf { get; set; }
    public bool HasTextLayer { get; set; }
    public bool IsScannedDocument { get; set; }
}
