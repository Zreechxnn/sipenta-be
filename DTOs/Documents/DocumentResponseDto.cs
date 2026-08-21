using SIAP.Api.Entities;

namespace SIAP.Api.DTOs.Documents;

public class DocumentResponseDto
{
    public Guid Id { get; set; }
    public string? Nama { get; set; }
    public string NamaFile { get; set; } = string.Empty;
    public long Ukuran { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string? NamaTenagaAhli { get; set; }
    public string? JenisDokumen { get; set; }
    public string? PeriodeLaporan { get; set; }
    public DateTime TanggalUpload { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    // DocumentContent info
    public ParseStatus? ParseStatus { get; set; }
    public int? PageCount { get; set; }
    public string? Language { get; set; }
    public DateTime? ParsedAt { get; set; }
    public string? PreviewText { get; set; }
    public Guid? UserId { get; set; }
    public int? BidangId { get; set; }
    public string? Bidang { get; set; }
    public string? BidangKode { get; set; }
    public string? UploaderUsername { get; set; }
    public string? UploaderFullName { get; set; }
    public bool IsOwner { get; set; }
    public bool IsSharedWithMe { get; set; }
    public List<DocumentAccessUserDto>? SharedWith { get; set; }
}
