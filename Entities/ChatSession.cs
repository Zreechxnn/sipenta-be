namespace SIAP.Api.Entities;

public class ChatSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Title { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
