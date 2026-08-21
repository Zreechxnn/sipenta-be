namespace SIAP.Api.Configurations;

public class OcrOptions
{
    public string Provider { get; set; } = "Tesseract";
    public string ExecutablePath { get; set; } = string.Empty;
    public string DataPath { get; set; } = string.Empty;
    public string Language { get; set; } = "ind+eng";
}
