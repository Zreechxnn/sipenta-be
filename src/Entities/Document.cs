namespace SIAP.Api.Entities;

public class Document
{
    public Guid Id { get; set; }
    public string? Nama { get; set; }
    public string NamaFile { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Ukuran { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string? NamaTenagaAhli { get; set; }
    public string? JenisDokumen { get; set; }
    public string? PeriodeLaporan { get; set; }
    public DateTime TanggalUpload { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingFinishedAt { get; set; }
    public TimeSpan? ProcessingDuration { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public int? BidangId { get; set; }
    public Bidang? Bidang { get; set; }

    public DocumentContent Content { get; set; } = null!;
    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
    public ICollection<DocumentAccess> Accesses { get; set; } = new List<DocumentAccess>();
    public ICollection<DocumentImage> Images { get; set; } = new List<DocumentImage>();
}
