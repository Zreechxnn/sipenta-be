namespace SIAP.Api.Services.Interfaces;

public record LlmImageInput(byte[] Bytes, string MimeType, string? Caption = null, string? ImageUrl = null);

public interface IGroqService
{
    Task<string> GetChatCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
    Task<string> GetChatCompletionWithHistoryAsync(string systemPrompt, IEnumerable<object> messages, CancellationToken cancellationToken = default);
    Task<string> GetChatCompletionWithVisionAsync(string systemPrompt, IEnumerable<object> historyMessages, string userMessage, IEnumerable<LlmImageInput>? images, CancellationToken cancellationToken = default);
}
