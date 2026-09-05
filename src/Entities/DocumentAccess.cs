namespace SIAP.Api.Entities;

public class DocumentAccess
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    public Guid? SharedByUserId { get; set; }
    public User? SharedByUser { get; set; }
    
    public string AccessLevel { get; set; } = "read"; // "read"
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
