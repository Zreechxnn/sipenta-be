using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class GroqService : IGroqService
{
    private record LlmEndpointConfig(string ApiKey, string BaseUrl, string Model, string ImageModel, string Name);

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
        var defaultModel = "openai/gpt-oss-120b";
        var defaultImageModel = "llama-3.2-11b-vision-preview";

        // 1. Primary config (Llm:ApiKey, Llm:BaseUrl, Llm:Model, Llm:Image)
        var primaryKey = configuration["Llm:ApiKey"] ?? string.Empty;
        var primaryUrl = configuration["Llm:BaseUrl"] ?? defaultBaseUrl;
        var primaryModel = configuration["Llm:Model"] ?? defaultModel;
        var primaryImage = configuration["Llm:Image"] ?? configuration["Llm:image"] ?? defaultImageModel;

        if (!string.IsNullOrWhiteSpace(primaryKey))
        {
            _configs.Add(new LlmEndpointConfig(primaryKey.Trim(), primaryUrl.Trim(), primaryModel.Trim(), primaryImage.Trim(), "Primary (Llm)"));
        }

        // 2. Secondary & subsequent configs (Llm:ApiKey2, Llm:ApiKey3, etc.)
        for (int i = 2; i <= 10; i++)
        {
            var key = configuration[$"Llm:ApiKey{i}"] ?? configuration[$"Llm:apikey{i}"];
            if (!string.IsNullOrWhiteSpace(key))
            {
                var url = configuration[$"Llm:BaseUrl{i}"] ?? configuration[$"Llm:baseurl{i}"] ?? primaryUrl;
                var model = configuration[$"Llm:Model{i}"] ?? configuration[$"Llm:model{i}"] ?? primaryModel;
                var image = configuration[$"Llm:Image{i}"] ?? configuration[$"Llm:image{i}"] ?? primaryImage;
                _configs.Add(new LlmEndpointConfig(key.Trim(), url.Trim(), model.Trim(), image.Trim(), $"Fallback {i} (Llm{i})"));
            }
        }
    }

    public async Task<string> GetChatCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var messages = new[]
        {
            new { role = "system", content = (object)systemPrompt },
            new { role = "user", content = (object)userPrompt }
        };

        return await SendWithFallbackAsync(messages, isVision: false, cancellationToken);
    }

    public async Task<string> GetChatCompletionWithHistoryAsync(string systemPrompt, IEnumerable<object> messages, CancellationToken cancellationToken = default)
    {
        var allMessages = new List<object>
        {
            new { role = "system", content = (object)systemPrompt }
        };
        allMessages.AddRange(messages);

        return await SendWithFallbackAsync(allMessages, isVision: false, cancellationToken);
    }

    public async Task<string> GetChatCompletionWithVisionAsync(
        string systemPrompt, 
        IEnumerable<object> historyMessages, 
        string userMessage, 
        IEnumerable<LlmImageInput>? images, 
        CancellationToken cancellationToken = default)
    {
        var imageList = images?.Where(img => img.Bytes != null && img.Bytes.Length > 0).ToList();
        if (imageList != null && imageList.Any())
        {
            // Build Vision Content parts for user message
            var contentParts = new List<object>
            {
                new { type = "text", text = userMessage }
            };

            foreach (var img in imageList)
            {
                var base64 = Convert.ToBase64String(img.Bytes);
                var mime = string.IsNullOrWhiteSpace(img.MimeType) ? "image/jpeg" : img.MimeType;
                contentParts.Add(new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = $"data:{mime};base64,{base64}"
                    }
                });
            }

            var visionMessages = new List<object>
            {
                new { role = "system", content = (object)systemPrompt }
            };
            if (historyMessages != null)
            {
                visionMessages.AddRange(historyMessages);
            }
            visionMessages.Add(new { role = "user", content = (object)contentParts });

            var visionResult = await SendWithFallbackAsync(visionMessages, isVision: true, cancellationToken);

            // If vision completion succeeded without errors, return it
            if (!string.IsNullOrWhiteSpace(visionResult) && 
                !visionResult.StartsWith("Terjadi kesalahan") && 
                !visionResult.StartsWith("Llm API Key is not") &&
                !visionResult.StartsWith("Mohon maaf, seluruh kuota"))
            {
                return visionResult;
            }

            _logger?.LogWarning("Vision LLM completion failed across endpoints ({VisionResult}). Falling back to text-only model...", visionResult);
        }

        // Fallback or text-only completion
        var textMessages = new List<object>
        {
            new { role = "system", content = (object)systemPrompt }
        };
        if (historyMessages != null)
        {
            textMessages.AddRange(historyMessages);
        }
        textMessages.Add(new { role = "user", content = (object)userMessage });

        return await SendWithFallbackAsync(textMessages, isVision: false, cancellationToken);
    }

    private async Task<string> SendWithFallbackAsync(IEnumerable<object> messages, bool isVision, CancellationToken cancellationToken)
    {
        if (_configs.Count == 0)
        {
            return "Llm API Key is not configured.";
        }

        var errors = new List<string>();

        for (int i = 0; i < _configs.Count; i++)
        {
            var config = _configs[i];
            var selectedModel = isVision ? config.ImageModel : config.Model;
            try
            {
                var requestBody = new
                {
                    model = selectedModel,
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
                        config.Name, config.BaseUrl, selectedModel, statusCode, errorDetail);
                    
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

                if (!string.IsNullOrEmpty(answer))
                {
                    // Clean up <think>...</think> tags if model produces reasoning output
                    answer = System.Text.RegularExpressions.Regex.Replace(answer, @"<think>[\s\S]*?</think>", "").Trim();
                }

                return answer ?? string.Empty;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var errorMsg = $"[{config.Name}] Exception: {ex.Message}";
                _logger?.LogError(ex, "Error while calling LLM on {Name} ({Url} - {Model}). Attempting fallback...", config.Name, config.BaseUrl, selectedModel);
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

