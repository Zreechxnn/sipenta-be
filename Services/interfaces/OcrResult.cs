namespace SIAP.Api.Services.Interfaces;

public class OcrResult
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string RawText { get; set; } = string.Empty;
    public double? Confidence { get; set; }
}
