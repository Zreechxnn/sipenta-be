using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Pgvector;
using SIPENTA.Api.Services.Interfaces;

namespace SIPENTA.Api.Services.Implementations;

public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache? _cache;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;

    public OpenAIEmbeddingService(HttpClient httpClient, IConfiguration configuration, IMemoryCache? cache = null)
    {
        _httpClient = httpClient;
        _cache = cache;
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
        // 1. Cache hit for fast repeated search queries
        var cacheKey = $"embedding_{_model}_{text.Trim()}";
        if (_cache != null && text.Length <= 1000 && _cache.TryGetValue(cacheKey, out Vector? cached) && cached != null)
        {
            return cached;
        }

        // Mock fallback if API Key is empty (for development without key)
        if (string.IsNullOrEmpty(_apiKey))
        {
            var random = new Random();
            var mockVector = new float[1536];
            for (int i = 0; i < mockVector.Length; i++) mockVector[i] = (float)random.NextDouble();
            var result = new Vector(mockVector);
            _cache?.Set(cacheKey, result, TimeSpan.FromMinutes(30));
            return result;
        }

        var requestBody = new
        {
            model = _model, 
            input = text
        };

        using var requestContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(_baseUrl, requestContent, cancellationToken);
        
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseJson = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var embeddingArray = responseJson.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(x => x.GetSingle())
            .ToArray();

        var vector = new Vector(embeddingArray);
        if (_cache != null && text.Length <= 1000)
        {
            _cache.Set(cacheKey, vector, TimeSpan.FromMinutes(30));
        }

        return vector;
    }

    public async Task<List<Vector>> GenerateEmbeddingsBatchAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts == null || texts.Count == 0)
        {
            return new List<Vector>();
        }

        // Mock fallback if API Key is empty
        if (string.IsNullOrEmpty(_apiKey))
        {
            var random = new Random();
            var list = new List<Vector>(texts.Count);
            for (int t = 0; t < texts.Count; t++)
            {
                var mockVector = new float[1536];
                for (int i = 0; i < mockVector.Length; i++) mockVector[i] = (float)random.NextDouble();
                list.Add(new Vector(mockVector));
            }
            return list;
        }

        try
        {
            var requestBody = new
            {
                model = _model,
                input = texts
            };

            using var requestContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(_baseUrl, requestContent, cancellationToken);

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var responseJson = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var dataElement = responseJson.RootElement.GetProperty("data");
            var results = new (int Index, Vector Vector)[dataElement.GetArrayLength()];

            int counter = 0;
            foreach (var item in dataElement.EnumerateArray())
            {
                var index = item.TryGetProperty("index", out var idxElem) ? idxElem.GetInt32() : counter;
                var embArray = item.GetProperty("embedding")
                    .EnumerateArray()
                    .Select(x => x.GetSingle())
                    .ToArray();

                results[counter] = (index, new Vector(embArray));
                counter++;
            }

            return results.OrderBy(r => r.Index).Select(r => r.Vector).ToList();
        }
        catch
        {
            // Resilient fallback to single generation if batch fails or provider restricts batching
            var fallbackList = new List<Vector>(texts.Count);
            foreach (var t in texts)
            {
                fallbackList.Add(await GenerateEmbeddingAsync(t, cancellationToken));
            }
            return fallbackList;
        }
    }
}

