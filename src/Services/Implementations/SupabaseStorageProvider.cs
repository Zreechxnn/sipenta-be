using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class SupabaseStorageProvider : ICloudStorageService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SupabaseStorageProvider> _logger;
    private readonly string _baseStorageUrl;
    private readonly string _apiKey;
    private readonly string _bucket;
    private readonly string _documentPrefix;
    private readonly string _imagePrefix;

    public string ProviderName => "Supabase Storage";

    public SupabaseStorageProvider(
        HttpClient httpClient,
        ILogger<SupabaseStorageProvider> logger,
        string projectUrl,
        string apiKey,
        string bucketName,
        string? documentFolder = "documents",
        string? imageFolder = "images")
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = apiKey.Trim();
        _bucket = string.IsNullOrWhiteSpace(bucketName) ? "documents" : bucketName.Trim();
        _documentPrefix = string.IsNullOrWhiteSpace(documentFolder) ? "documents" : documentFolder.Trim().Trim('/');
        _imagePrefix = string.IsNullOrWhiteSpace(imageFolder) ? "images" : imageFolder.Trim().Trim('/');
        _baseStorageUrl = NormalizeBaseStorageUrl(projectUrl);
    }

    private static string NormalizeBaseStorageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var clean = url.Trim().TrimEnd('/');
        if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // If user only passed project reference (e.g. nqjcxemixxpjvnbdybmp)
            if (!clean.Contains('.'))
            {
                clean = $"https://{clean}.supabase.co";
            }
            else
            {
                clean = $"https://{clean}";
            }
        }

        // Strip /storage/v1/s3 or /storage/v1 if user pasted full storage API URL
        if (clean.EndsWith("/storage/v1/s3", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(0, clean.Length - "/storage/v1/s3".Length);
        }
        else if (clean.EndsWith("/storage/v1", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(0, clean.Length - "/storage/v1".Length);
        }

        return $"{clean}/storage/v1";
    }

    private void ApplyAuthHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.TryAddWithoutValidation("apikey", _apiKey);
        }
    }

    private async Task EnsureBucketExistsAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_baseStorageUrl) || string.IsNullOrWhiteSpace(_bucket)) return;

            var checkUrl = $"{_baseStorageUrl}/bucket/{_bucket}";
            using var checkReq = new HttpRequestMessage(HttpMethod.Get, checkUrl);
            ApplyAuthHeaders(checkReq);
            var checkRes = await _httpClient.SendAsync(checkReq);

            if (checkRes.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Try creating bucket automatically if permitted
                var createUrl = $"{_baseStorageUrl}/bucket";
                using var createReq = new HttpRequestMessage(HttpMethod.Post, createUrl);
                ApplyAuthHeaders(createReq);
                var payload = JsonSerializer.Serialize(new { id = _bucket, name = _bucket, @public = false });
                createReq.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                await _httpClient.SendAsync(createReq);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "EnsureBucketExists notice for bucket '{Bucket}'", _bucket);
        }
    }

    public async Task<string> UploadFileAsync(IFormFile file, string fileName)
    {
        await EnsureBucketExistsAsync();

        var sanitized = Path.GetFileName(fileName);
        var objectKey = $"{_documentPrefix}/{Guid.NewGuid():N}_{sanitized}".TrimStart('/');
        var uploadUrl = $"{_baseStorageUrl}/object/{_bucket}/{objectKey}";

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.TryAddWithoutValidation("x-upsert", "true");
        ApplyAuthHeaders(request);

        using var stream = file.OpenReadStream();
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType
        );

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Supabase Storage upload failed with status {StatusCode}: {Error}", response.StatusCode, err);
            throw new InvalidOperationException($"Upload ke Supabase Storage gagal: {response.StatusCode} - {err}");
        }

        _logger.LogInformation("File uploaded to Supabase Storage: {ObjectKey}", objectKey);
        return $"supabase:{_bucket}/{objectKey}";
    }

    public async Task<string> UploadFileBytesAsync(byte[] fileBytes, string fileName, string contentType, string? folderOrPrefix = null)
    {
        await EnsureBucketExistsAsync();

        var sanitized = Path.GetFileName(fileName);
        var prefix = !string.IsNullOrWhiteSpace(folderOrPrefix) ? folderOrPrefix.Trim('/') : _imagePrefix;
        var objectKey = $"{prefix}/{Guid.NewGuid():N}_{sanitized}".TrimStart('/');
        var uploadUrl = $"{_baseStorageUrl}/object/{_bucket}/{objectKey}";

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.TryAddWithoutValidation("x-upsert", "true");
        ApplyAuthHeaders(request);

        request.Content = new ByteArrayContent(fileBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        );

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Supabase Storage upload bytes failed with status {StatusCode}: {Error}", response.StatusCode, err);
            throw new InvalidOperationException($"Upload bytes ke Supabase Storage gagal: {response.StatusCode} - {err}");
        }

        _logger.LogInformation("Bytes uploaded to Supabase Storage: {ObjectKey}", objectKey);
        return $"supabase:{_bucket}/{objectKey}";
    }

    public async Task DeleteFileAsync(string fileIdOrPath)
    {
        var objectKey = ExtractObjectKey(fileIdOrPath);
        var deleteUrl = $"{_baseStorageUrl}/object/{_bucket}/{objectKey}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, deleteUrl);
        ApplyAuthHeaders(request);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Supabase Storage delete failed with status {StatusCode}: {Error}", response.StatusCode, err);
        }
    }

    public async Task<Stream> DownloadFileAsync(string fileIdOrPath)
    {
        var objectKey = ExtractObjectKey(fileIdOrPath);

        // Try authenticated download endpoint first
        var authUrl = $"{_baseStorageUrl}/object/authenticated/{_bucket}/{objectKey}";
        using var authReq = new HttpRequestMessage(HttpMethod.Get, authUrl);
        ApplyAuthHeaders(authReq);

        var response = await _httpClient.SendAsync(authReq, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            // Fallback to standard object endpoint
            var stdUrl = $"{_baseStorageUrl}/object/{_bucket}/{objectKey}";
            using var stdReq = new HttpRequestMessage(HttpMethod.Get, stdUrl);
            ApplyAuthHeaders(stdReq);
            response = await _httpClient.SendAsync(stdReq, HttpCompletionOption.ResponseHeadersRead);
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Gagal mengunduh berkas dari Supabase Storage: {response.StatusCode} - {err}");
        }

        var memory = new MemoryStream();
        using var stream = await response.Content.ReadAsStreamAsync();
        await stream.CopyToAsync(memory);
        memory.Position = 0;
        return memory;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_baseStorageUrl) || string.IsNullOrWhiteSpace(_apiKey))
            {
                return false;
            }

            // 1. Try checking specific bucket
            var bucketUrl = $"{_baseStorageUrl}/bucket/{_bucket}";
            using var bucketReq = new HttpRequestMessage(HttpMethod.Get, bucketUrl);
            ApplyAuthHeaders(bucketReq);
            var bucketRes = await _httpClient.SendAsync(bucketReq);

            if (bucketRes.IsSuccessStatusCode)
            {
                return true;
            }

            // 2. Try listing all buckets (validates API key and project connectivity)
            var listUrl = $"{_baseStorageUrl}/bucket";
            using var listReq = new HttpRequestMessage(HttpMethod.Get, listUrl);
            ApplyAuthHeaders(listReq);
            var listRes = await _httpClient.SendAsync(listReq);

            if (listRes.IsSuccessStatusCode)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Supabase Storage connection test failed.");
            return false;
        }
    }

    private string ExtractObjectKey(string fileIdOrPath)
    {
        var clean = fileIdOrPath;
        if (clean.StartsWith("supabase:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("supabase:".Length);
        }
        else if (clean.StartsWith("s3:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("s3:".Length);
        }

        if (!string.IsNullOrWhiteSpace(_bucket) && clean.StartsWith($"{_bucket}/", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(_bucket.Length + 1);
        }

        return clean.TrimStart('/');
    }
}
