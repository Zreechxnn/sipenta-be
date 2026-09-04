namespace SIAP.Api.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? FullName { get; set; }
    public string PasswordHash { get; set; } = null!;
    
    // Foreign key
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    
    public int? BidangId { get; set; }
    public Bidang? Bidang { get; set; }

    public bool IsApproved { get; set; } = false;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<DocumentAccess> DocumentAccesses { get; set; } = new List<DocumentAccess>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
