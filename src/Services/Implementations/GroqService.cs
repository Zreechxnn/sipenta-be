using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class GroqService : IGroqService
{
    private record LlmEndpointConfig(string ApiKey, string BaseUrl, string Model, string ImageModel, string Name);

    private readonly HttpClient _httpClient;
    private readonly ILogger<GroqService>? _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly List<LlmEndpointConfig> _fallbackConfigs = new();

    public GroqService(HttpClient httpClient, IConfiguration configuration, IServiceProvider serviceProvider, ILogger<GroqService>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _serviceProvider = serviceProvider;

        LoadConfigurations(configuration);
    }

    private void LoadConfigurations(IConfiguration configuration)
    {
        var defaultBaseUrl = "https://api.groq.com/openai/v1/chat/completions";
        var defaultModel = "openai/gpt-oss-120b";
        var defaultImageModel = "qwen/qwen3.8-27b";

        var primaryKey = configuration["Llm:ApiKey"] ?? string.Empty;
        var primaryUrl = configuration["Llm:BaseUrl"] ?? defaultBaseUrl;
        var primaryModel = configuration["Llm:Model"] ?? defaultModel;
        var primaryImage = configuration["Llm:Image"] ?? configuration["Llm:image"] ?? defaultImageModel;

        if (!string.IsNullOrWhiteSpace(primaryKey))
        {
            _fallbackConfigs.Add(new LlmEndpointConfig(primaryKey.Trim(), primaryUrl.Trim(), primaryModel.Trim(), primaryImage.Trim(), "Primary (Llm)"));
        }

        for (int i = 2; i <= 10; i++)
        {
            var key = configuration[$"Llm:ApiKey{i}"] ?? configuration[$"Llm:apikey{i}"];
            if (!string.IsNullOrWhiteSpace(key))
            {
                var url = configuration[$"Llm:BaseUrl{i}"] ?? configuration[$"Llm:baseurl{i}"] ?? primaryUrl;
                var model = configuration[$"Llm:Model{i}"] ?? configuration[$"Llm:model{i}"] ?? primaryModel;
                var image = configuration[$"Llm:Image{i}"] ?? configuration[$"Llm:image{i}"] ?? primaryImage;
                _fallbackConfigs.Add(new LlmEndpointConfig(key.Trim(), url.Trim(), model.Trim(), image.Trim(), $"Fallback {i} (Llm{i})"));
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
            var contentParts = new List<object>
            {
                new { type = "text", text = userMessage }
            };

            for (int i = 0; i < imageList.Count; i++)
            {
                var img = imageList[i];
                var base64 = Convert.ToBase64String(img.Bytes);
                var mime = string.IsNullOrWhiteSpace(img.MimeType) ? "image/jpeg" : img.MimeType;
                var captionText = !string.IsNullOrWhiteSpace(img.Caption) ? $" - {img.Caption}" : "";
                contentParts.Add(new
                {
                    type = "text",
                    text = $"[GAMBAR #{i + 1}{captionText}]:"
                });
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

            if (!string.IsNullOrWhiteSpace(visionResult) && 
                !visionResult.StartsWith("Terjadi kesalahan") && 
                !visionResult.StartsWith("Llm API Key is not") &&
                !visionResult.StartsWith("Mohon maaf, seluruh kuota"))
            {
                return visionResult;
            }

            _logger?.LogWarning("Vision LLM completion failed across endpoints ({VisionResult}). Falling back to text-only model...", visionResult);
        }

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
        var configs = new List<LlmEndpointConfig>();
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var configService = scope.ServiceProvider.GetService<ISystemConfigService>();
            if (configService != null)
            {
                var dynamicConfigs = await configService.GetLlmConfigsAsync();
                var active = dynamicConfigs.Endpoints
                    .Where(e => e.IsActive && !string.IsNullOrWhiteSpace(e.ApiKey))
                    .OrderBy(e => e.Priority)
                    .ToList();

                foreach (var ep in active)
                {
                    configs.Add(new LlmEndpointConfig(
                        ep.ApiKey.Trim(),
                        string.IsNullOrWhiteSpace(ep.BaseUrl) ? "https://api.groq.com/openai/v1/chat/completions" : ep.BaseUrl.Trim(),
                        string.IsNullOrWhiteSpace(ep.Model) ? "openai/gpt-oss-120b" : ep.Model.Trim(),
                        string.IsNullOrWhiteSpace(ep.ImageModel) ? "qwen/qwen3.8-27b" : ep.ImageModel.Trim(),
                        string.IsNullOrWhiteSpace(ep.Name) ? "Configured LLM" : ep.Name.Trim()
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not load dynamic LLM configs from database. Falling back to static configs.");
        }

        if (!configs.Any())
        {
            configs = _fallbackConfigs;
        }

        if (!configs.Any())
        {
            return "Llm API Key is not configured in environment variables, appsettings.json, or database. Please configure LLM API Keys in Admin Configuration.";
        }

        var errors = new List<string>();

        foreach (var config in configs)
        {
            var selectedModel = isVision ? config.ImageModel : config.Model;
            try
            {
                var requestBody = new
                {
                    model = selectedModel,
                    messages = messages,
                    temperature = 0.3,
                    max_completion_tokens = 2048
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody), 
                    Encoding.UTF8, 
                    "application/json"
                );

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorDetail = await response.Content.ReadAsStringAsync(cancellationToken);
                    var errorMsg = $"[{config.Name}] Status {statusCode}: {errorDetail}";
                    
                    _logger?.LogWarning("LLM Request to {Name} ({Url} - {Model}) failed with status {StatusCode}. Details: {ErrorDetail}. Attempting next config...", 
                        config.Name, config.BaseUrl, selectedModel, statusCode, errorDetail);
                    
                    errors.Add(errorMsg);
                    continue;
                }

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                var responseJson = JsonDocument.Parse(responseString);
                var answer = responseJson.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return CleanLlmResponse(answer);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var errorMsg = $"[{config.Name}] Exception: {ex.Message}";
                _logger?.LogError(ex, "Error while calling LLM on {Name} ({Url} - {Model}). Attempting fallback...", config.Name, config.BaseUrl, selectedModel);
                errors.Add(errorMsg);
            }
        }

        if (errors.Any(e => e.Contains("429") || e.Contains("Rate limit") || e.Contains("rate_limit")))
        {
            return "Mohon maaf, seluruh kuota layanan AI sedang sibuk atau mencapai batas limit (Rate Limit). Silakan coba beberapa saat lagi.";
        }

        return $"Terjadi kesalahan saat memproses jawaban AI dari semua endpoint: {string.Join(" | ", errors)}";
    }

    private string CleanLlmResponse(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return string.Empty;

        // 1. Remove closed <think>...</think> tags
        answer = System.Text.RegularExpressions.Regex.Replace(answer, @"<think>[\s\S]*?</think>", "").Trim();

        // 2. If <think> tag is still present (unclosed or truncated before </think>)
        if (answer.Contains("<think>"))
        {
            // Try to extract the drafted final answer inside the thinking block if available
            var finalSection = System.Text.RegularExpressions.Regex.Match(
                answer, 
                @"(?:(?:\*{1,2}(?:Final Polish|Final Response|Jawaban Akhir|Jawaban|Direct Answer)\*{1,2}|Headline:)\s*)[^:\n]*:?\s*([\s\S]+)$", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (finalSection.Success && finalSection.Groups[1].Value.Trim().Length > 20)
            {
                answer = finalSection.Groups[1].Value.Trim();
            }
            else
            {
                // Fallback: strip <think> tag and introductory thinking lines
                answer = System.Text.RegularExpressions.Regex.Replace(
                    answer, 
                    @"^<think>[\s\S]*?(?:Here's a thinking process.*?\n\n|Analyze the User.*?\n\n)?", 
                    "", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                ).Trim();
            }
        }

        // 3. If model outputted an internal step-by-step monologue without <think> tags:
        // (e.g. starting with "1. **Analyze the User's Request:** ... 6. **Final Polish**:")
        if (System.Text.RegularExpressions.Regex.IsMatch(answer, @"^\s*1\.\s+\*\*Analyze", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var finalSection = System.Text.RegularExpressions.Regex.Match(
                answer, 
                @"(?:(?:\*{1,2}(?:Final Polish|Final Response|Jawaban Akhir|Jawaban|Direct Answer)\*{1,2}|Headline:)\s*)[^:\n]*:?\s*([\s\S]+)$", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (finalSection.Success && finalSection.Groups[1].Value.Trim().Length > 20)
            {
                answer = finalSection.Groups[1].Value.Trim();
            }
        }

        return answer.Trim();
    }
}

