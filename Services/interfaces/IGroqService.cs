namespace SIAP.Api.Services.Interfaces;

public interface IGroqService
{
    Task<string> GetChatCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
    Task<string> GetChatCompletionWithHistoryAsync(string systemPrompt, IEnumerable<object> messages, CancellationToken cancellationToken = default);
}
