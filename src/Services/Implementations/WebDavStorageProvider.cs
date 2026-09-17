using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class WebDavStorageProvider : ICloudStorageService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebDavStorageProvider> _logger;
    private readonly string _serverUrl;
    private readonly string _username;
    private readonly string _password;
    private readonly string _remotePath;
    private readonly string _documentPath;
    private readonly string _imagePath;

    public string ProviderName => "WebDav (Nextcloud/ownCloud/PDN)";

    public WebDavStorageProvider(
        HttpClient httpClient,
        ILogger<WebDavStorageProvider> logger,
        string serverUrl = "",
        string username = "",
        string password = "",
        string remotePath = "siap",
        string documentPath = "documents",
        string imagePath = "images")
    {
        _httpClient = httpClient;
        _logger = logger;
        _username = (username ?? string.Empty).Trim();
        _password = (password ?? string.Empty).Trim();
        _serverUrl = NormalizeWebDavUrl(serverUrl, _username);
        _remotePath = (remotePath ?? "siap").Trim().Trim('/');
        _documentPath = string.IsNullOrWhiteSpace(documentPath) ? "documents" : documentPath.Trim().Trim('/');
        _imagePath = string.IsNullOrWhiteSpace(imagePath) ? "images" : imagePath.Trim().Trim('/');
    }

    private static string NormalizeWebDavUrl(string? rawUrl, string username)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return string.Empty;
        var trimmed = rawUrl.Trim().TrimEnd('/');
        
        // If URL already includes WebDAV paths, keep it
        if (trimmed.Contains("/remote.php/") || trimmed.EndsWith("/remote.php") || 
            trimmed.Contains("/webdav") || trimmed.Contains("/dav/"))
        {
            return trimmed;
        }

        // For Nextcloud/ownCloud/PDN, if only hostname provided, default to standard user WebDAV path
        if (!string.IsNullOrWhiteSpace(username))
        {
            return $"{trimmed}/remote.php/dav/files/{Uri.EscapeDataString(username)}";
        }
        return $"{trimmed}/remote.php/webdav";
    }

    private void ApplyAuthentication(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_username) && string.IsNullOrWhiteSpace(_password))
        {
            return;
        }

        if (_password.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _password.Substring(7).Trim());
        }
        else
        {
            var authBytes = Encoding.UTF8.GetBytes($"{_username}:{_password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        }
    }

    private string BuildFullUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(_serverUrl))
        {
            throw new InvalidOperationException("URL Server WebDAV (Nextcloud/ownCloud/PDN) belum dikonfigurasi.");
        }

        var clean = relativePath.TrimStart('/');
        return $"{_serverUrl}/{clean}";
    }

    private async Task EnsureRemoteDirectoryExistsAsync(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return;

        var parts = directoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";

        foreach (var part in parts)
        {
            current = string.IsNullOrEmpty(current) ? part : $"{current}/{part}";
            var url = BuildFullUrl(current);

            try
            {
                using var mkcolReq = new HttpRequestMessage(new HttpMethod("MKCOL"), url);
                ApplyAuthentication(mkcolReq);
                var res = await _httpClient.SendAsync(mkcolReq);
                // 201 Created or 405 Method Not Allowed (already exists) is fine
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Directory check MKCOL on {Url}", url);
            }
        }
    }

    public async Task<string> UploadFileAsync(IFormFile file, string fileName)
    {
        var sanitized = Path.GetFileName(fileName);
        var uniqueName = $"{Guid.NewGuid():N}_{sanitized}";
        var subDir = string.IsNullOrWhiteSpace(_remotePath) ? _documentPath : $"{_remotePath}/{_documentPath}";

        await EnsureRemoteDirectoryExistsAsync(subDir);

        var remoteObject = $"{subDir}/{uniqueName}";
        var url = BuildFullUrl(remoteObject);

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        ApplyAuthentication(request);

        using var stream = file.OpenReadStream();
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType
        );

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Created)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("WebDAV Upload failed with status {StatusCode}: {Error}", response.StatusCode, err);
            throw new InvalidOperationException($"Upload ke WebDAV (Nextcloud/ownCloud/PDN) gagal: {response.StatusCode} - {err}");
        }

        _logger.LogInformation("File uploaded to WebDAV: {RemoteObject}", remoteObject);
        return $"webdav:{remoteObject}";
    }

    public async Task<string> UploadFileBytesAsync(byte[] fileBytes, string fileName, string contentType, string? folderOrPrefix = null)
    {
        var sanitized = Path.GetFileName(fileName);
        var uniqueName = $"{Guid.NewGuid():N}_{sanitized}";
        var prefix = !string.IsNullOrWhiteSpace(folderOrPrefix) ? folderOrPrefix.Trim('/') : _imagePath;
        var subDir = string.IsNullOrWhiteSpace(_remotePath) ? prefix : $"{_remotePath}/{prefix}";

        await EnsureRemoteDirectoryExistsAsync(subDir);

        var remoteObject = $"{subDir}/{uniqueName}";
        var url = BuildFullUrl(remoteObject);

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        ApplyAuthentication(request);

        request.Content = new ByteArrayContent(fileBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        );

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Created)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("WebDAV Upload bytes failed with status {StatusCode}: {Error}", response.StatusCode, err);
            throw new InvalidOperationException($"Upload bytes ke WebDAV gagal: {response.StatusCode} - {err}");
        }

        _logger.LogInformation("Bytes uploaded to WebDAV: {RemoteObject}", remoteObject);
        return $"webdav:{remoteObject}";
    }

    private static string ExtractRemotePath(string fileIdOrPath)
    {
        var clean = fileIdOrPath;
        if (clean.StartsWith("webdav:", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(7);
        else if (clean.StartsWith("nextcloud:", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(10);
        else if (clean.StartsWith("owncloud:", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(9);
        else if (clean.StartsWith("pdn:", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(4);

        return clean;
    }

    public async Task DeleteFileAsync(string fileIdOrPath)
    {
        var cleanPath = ExtractRemotePath(fileIdOrPath);
        var url = BuildFullUrl(cleanPath);
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        ApplyAuthentication(request);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("WebDAV Delete failed with status {StatusCode}: {Error}", response.StatusCode, err);
        }
    }

    public async Task<Stream> DownloadFileAsync(string fileIdOrPath)
    {
        var cleanPath = ExtractRemotePath(fileIdOrPath);
        var url = BuildFullUrl(cleanPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuthentication(request);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Gagal mengunduh berkas dari WebDAV: {response.StatusCode} - {err}");
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
            if (string.IsNullOrWhiteSpace(_serverUrl)) return false;

            // Try PROPFIND or OPTIONS/HEAD on serverUrl
            using var propfindReq = new HttpRequestMessage(new HttpMethod("PROPFIND"), _serverUrl);
            ApplyAuthentication(propfindReq);
            propfindReq.Headers.Add("Depth", "0");

            var response = await _httpClient.SendAsync(propfindReq);
            if (response.IsSuccessStatusCode || 
                response.StatusCode == (HttpStatusCode)207 || // Multi-Status (Standard WebDAV OK)
                response.StatusCode == HttpStatusCode.NoContent)
            {
                return true;
            }

            // Fallback try HEAD / OPTIONS
            using var headReq = new HttpRequestMessage(HttpMethod.Head, _serverUrl);
            ApplyAuthentication(headReq);
            var headRes = await _httpClient.SendAsync(headReq);
            if (headRes.IsSuccessStatusCode || headRes.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebDAV Test connection failed.");
            return false;
        }
    }
}
