namespace SIAP.Api.DTOs.Documents;

public class ShareDocumentRequest
{
    public string Username { get; set; } = null!;
}

public class DocumentAccessUserDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public int? BidangId { get; set; }
    public string? Bidang { get; set; }
    public string AccessLevel { get; set; } = "read";
    public DateTime CreatedAt { get; set; }
}
