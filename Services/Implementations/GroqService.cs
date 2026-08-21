using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class GroqService : IGroqService
{
    private record LlmEndpointConfig(string ApiKey, string BaseUrl, string Model, string Name);

    private readonly HttpClient _httpClient;
    private readonly ILogger<GroqService>? _logger;
    private readonly List<LlmEndpointConfig> _configs = new();

    public GroqService(HttpClient httpClient, IConfiguration configuration, ILogger<GroqService>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;

        LoadConfigurations(configuration);
    }

    private void LoadConfigurations(IConfiguration configuration)
    {
        var defaultBaseUrl = "https://api.groq.com/openai/v1/chat/completions";
        var defaultModel = "qwen/qwen3.6-27b";

        // 1. Primary config (Llm:ApiKey, Llm:BaseUrl, Llm:Model)
        var primaryKey = configuration["Llm:ApiKey"] ?? string.Empty;
        var primaryUrl = configuration["Llm:BaseUrl"] ?? defaultBaseUrl;
        var primaryModel = configuration["Llm:Model"] ?? defaultModel;

        if (!string.IsNullOrWhiteSpace(primaryKey))
        {
            _configs.Add(new LlmEndpointConfig(primaryKey.Trim(), primaryUrl.Trim(), primaryModel.Trim(), "Primary (Llm)"));
        }

        // 2. Secondary & subsequent configs (Llm:ApiKey2, Llm:ApiKey3, etc.)
        for (int i = 2; i <= 10; i++)
        {
            var key = configuration[$"Llm:ApiKey{i}"];
            if (!string.IsNullOrWhiteSpace(key))
            {
                var url = configuration[$"Llm:BaseUrl{i}"] ?? primaryUrl;
                var model = configuration[$"Llm:Model{i}"] ?? primaryModel;
                _configs.Add(new LlmEndpointConfig(key.Trim(), url.Trim(), model.Trim(), $"Fallback {i} (Llm{i})"));
            }
        }
    }

    public async Task<string> GetChatCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var messages = new[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };

        return await SendWithFallbackAsync(messages, cancellationToken);
    }

    public async Task<string> GetChatCompletionWithHistoryAsync(string systemPrompt, IEnumerable<object> messages, CancellationToken cancellationToken = default)
    {
        var allMessages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        allMessages.AddRange(messages);

        return await SendWithFallbackAsync(allMessages, cancellationToken);
    }

    private async Task<string> SendWithFallbackAsync(IEnumerable<object> messages, CancellationToken cancellationToken)
    {
        if (_configs.Count == 0)
        {
            return "Llm API Key is not configured.";
        }

        var errors = new List<string>();

        for (int i = 0; i < _configs.Count; i++)
        {
            var config = _configs[i];
            try
            {
                var requestBody = new
                {
                    model = config.Model,
                    messages = messages,
                    temperature = 0.1,
                    max_tokens = 3000
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorDetail = await response.Content.ReadAsStringAsync(cancellationToken);
                    var statusCode = (int)response.StatusCode;
                    var errorMsg = $"[{config.Name}] Status {statusCode}: {errorDetail}";
                    
                    _logger?.LogWarning("LLM Request to {Name} ({Url} - {Model}) failed with status {StatusCode}. Details: {ErrorDetail}. Attempting next config...", 
                        config.Name, config.BaseUrl, config.Model, statusCode, errorDetail);
                    
                    errors.Add(errorMsg);
                    continue; // Coba endpoint / API key berikutnya
                }

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                var responseJson = JsonDocument.Parse(responseString);
                var answer = responseJson.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return answer ?? string.Empty;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var errorMsg = $"[{config.Name}] Exception: {ex.Message}";
                _logger?.LogError(ex, "Error while calling LLM on {Name} ({Url}). Attempting fallback...", config.Name, config.BaseUrl);
                errors.Add(errorMsg);
            }
        }

        // Jika semua API Key / Endpoint gagal
        if (errors.Any(e => e.Contains("429") || e.Contains("Rate limit") || e.Contains("rate_limit")))
        {
            return "Mohon maaf, seluruh kuota layanan AI sedang sibuk atau mencapai batas limit (Rate Limit). Silakan coba beberapa saat lagi.";
        }

        return $"Terjadi kesalahan saat memproses jawaban AI dari semua endpoint: {string.Join(" | ", errors)}";
    }
}

