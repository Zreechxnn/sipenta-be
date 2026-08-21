namespace SIAP.Api.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ChatSessionId { get; set; }
    public ChatSession ChatSession { get; set; } = null!;
    
    public string Role { get; set; } = null!; // "user" or "assistant"
    public string Content { get; set; } = null!;
    
    public string? Sources { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
