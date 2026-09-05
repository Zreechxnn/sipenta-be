using System.ComponentModel.DataAnnotations;

namespace SIAP.Api.DTOs.Chat;

public class ChatRequestDto
{
    [Required]
    public string Message { get; set; } = string.Empty;

    // Make History optional or ignore it since we will fetch from DB if SessionId is provided
    public List<ChatMessageDto> History { get; set; } = new List<ChatMessageDto>();
    
    public Guid? SessionId { get; set; }

    public int TopK { get; set; } = 5;

    public string? ModelMode { get; set; } = "auto";
}

public class ChatMessageDto
{
    [Required]
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    
    [Required]
    public string Content { get; set; } = string.Empty;
}
