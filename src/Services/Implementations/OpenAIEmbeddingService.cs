using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pgvector;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;

    public OpenAIEmbeddingService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OpenAI:ApiKey"] ?? string.Empty;
        _baseUrl = configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/embeddings";
        _model = configuration["OpenAI:Model"] ?? "text-embedding-3-small";
        
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
    }

    public async Task<Vector> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        // Mock fallback if API Key is empty (for development without key)
        if (string.IsNullOrEmpty(_apiKey))
        {
            var random = new Random();
            var mockVector = new float[1536];
            for (int i = 0; i < mockVector.Length; i++) mockVector[i] = (float)random.NextDouble();
            return new Vector(mockVector);
        }

        var requestBody = new
        {
            model = _model, 
            input = text
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_baseUrl, content, cancellationToken);
        
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseJson = JsonDocument.Parse(responseString);
        var embeddingArray = responseJson.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(x => x.GetSingle())
            .ToArray();

        return new Vector(embeddingArray);
    }
}
