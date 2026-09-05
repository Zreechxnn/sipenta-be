using SIAP.Api.Entities;

namespace SIAP.Api.DTOs.Documents;

public class DocumentStatusDto
{
    public DocumentStatus DocumentStatus { get; set; }
    public ParseStatus ParseStatus { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingFinishedAt { get; set; }
    public TimeSpan? ProcessingDuration { get; set; }
    public string? ErrorMessage { get; set; }
}
