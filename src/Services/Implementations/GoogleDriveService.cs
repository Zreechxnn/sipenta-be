using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SIPENTA.Api.Services.Implementations;

public class GoogleDriveToken
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
    
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;
    
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;
    
    [JsonPropertyName("client_secret")]
    public string ClientSecret { get; set; } = string.Empty;
    
    [JsonPropertyName("expiry")]
    public DateTime? Expiry { get; set; }
}

public class GoogleDriveService : Interfaces.IGoogleDriveService
{
    public string ProviderName => "GoogleDrive";

    private DriveService? _driveService;
    private readonly string _folderId;
    private readonly string? _imageFolderId;
    private readonly ILogger<GoogleDriveService> _logger;
    private readonly ConcurrentDictionary<string, string> _subfolderCache = new();

    public GoogleDriveService(IConfiguration config, ILogger<GoogleDriveService> logger)
    {
        _logger = logger;
        
        var tokenJson = config["GoogleDrive:TokenJson"] ?? config["GoogleDrive__TokenJson"];
        _folderId = config["GoogleDrive:FolderId"] ?? config["GoogleDrive__FolderId"] ?? "";
        _imageFolderId = config["GoogleDrive:Folder_image"] ?? config["GoogleDrive__Folder_image"];

        if (string.IsNullOrEmpty(tokenJson) || string.IsNullOrEmpty(_folderId))
        {
            _logger.LogWarning("Kredensial GoogleDrive (TokenJson atau FolderId) belum disetel di environment.");
            return;
        }

        try
        {
            tokenJson = tokenJson.Trim('\'');
            var tokenData = JsonSerializer.Deserialize<GoogleDriveToken>(tokenJson);
            if (tokenData == null)
            {
                _logger.LogWarning("Format GoogleDrive Token JSON tidak valid.");
                return;
            }

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = tokenData.ClientId,
                    ClientSecret = tokenData.ClientSecret
                },
                Scopes = new[] { DriveService.Scope.Drive }
            });

            var tokenResponse = new TokenResponse
            {
                AccessToken = tokenData.Token,
                RefreshToken = tokenData.RefreshToken,
            };

            if (tokenData.Expiry.HasValue)
            {
                tokenResponse.IssuedUtc = DateTime.UtcNow;
                var expiresIn = (long)(tokenData.Expiry.Value.ToUniversalTime() - DateTime.UtcNow).TotalSeconds;
                tokenResponse.ExpiresInSeconds = expiresIn > 0 ? expiresIn : 0;
            }

            var credential = new UserCredential(flow, "user", tokenResponse);

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "SIPENTA API"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal menginisialisasi GoogleDriveService.");
        }
    }

    private DriveService GetClient()
    {
        if (_driveService == null)
        {
            throw new InvalidOperationException("Kredensial Google Drive belum dikonfigurasi di environment server (GoogleDrive__TokenJson / GoogleDrive__FolderId).");
        }
        return _driveService;
    }

    public async Task<string> UploadFileAsync(IFormFile file, string fileName)
    {
        var fileMetadata = new Google.Apis.Drive.v3.Data.File()
        {
            Name = fileName,
            Parents = new List<string> { _folderId }
        };

        using var stream = file.OpenReadStream();
        var request = GetClient().Files.Create(fileMetadata, stream, file.ContentType);
        request.Fields = "id";
        
        var progress = await request.UploadAsync();
        if (progress.Status == Google.Apis.Upload.UploadStatus.Failed)
        {
            _logger.LogError(progress.Exception, "Upload to Google Drive failed.");
            throw progress.Exception;
        }

        var fileResult = request.ResponseBody;
        return fileResult.Id;
    }

    public async Task<string> GetOrCreateSubfolderAsync(string parentFolderId, string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return parentFolderId;
        
        var safeFolderName = folderName.Trim().Trim('/', '\\');
        if (string.IsNullOrEmpty(safeFolderName)) return parentFolderId;

        var cacheKey = $"{parentFolderId}:{safeFolderName}";
        if (_subfolderCache.TryGetValue(cacheKey, out var cachedId))
        {
            return cachedId;
        }

        try
        {
            var client = GetClient();
            var listRequest = client.Files.List();
            var escapedName = safeFolderName.Replace("'", "\\'");
            listRequest.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{escapedName}' and '{parentFolderId}' in parents and trashed = false";
            listRequest.Fields = "files(id, name)";
            listRequest.PageSize = 1;

            var listResult = await listRequest.ExecuteAsync();
            var existingFolder = listResult.Files?.FirstOrDefault();
            if (existingFolder != null && !string.IsNullOrEmpty(existingFolder.Id))
            {
                _subfolderCache[cacheKey] = existingFolder.Id;
                return existingFolder.Id;
            }

            var folderMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = safeFolderName,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentFolderId }
            };

            var createRequest = client.Files.Create(folderMetadata);
            createRequest.Fields = "id";
            var createdFolder = await createRequest.ExecuteAsync();

            _subfolderCache[cacheKey] = createdFolder.Id;
            _logger.LogInformation("Berhasil membuat subfolder '{FolderName}' ({FolderId}) di Google Drive dalam parent {ParentId}", safeFolderName, createdFolder.Id, parentFolderId);
            return createdFolder.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal mendapatkan atau membuat subfolder '{FolderName}' di Google Drive. Menggunakan parent folder default.", safeFolderName);
            return parentFolderId;
        }
    }

    public async Task<string> UploadFileBytesAsync(byte[] fileBytes, string fileName, string contentType, string? folderOrPrefix = null)
    {
        var baseFolder = !string.IsNullOrEmpty(_imageFolderId) ? _imageFolderId : _folderId;
        var targetFolder = baseFolder;

        if (!string.IsNullOrWhiteSpace(folderOrPrefix))
        {
            targetFolder = await GetOrCreateSubfolderAsync(baseFolder, folderOrPrefix);
        }
                          
        var fileMetadata = new Google.Apis.Drive.v3.Data.File()
        {
            Name = fileName,
            Parents = new List<string> { targetFolder }
        };

        using var stream = new MemoryStream(fileBytes);
        var request = GetClient().Files.Create(fileMetadata, stream, contentType);
        request.Fields = "id";
        
        var progress = await request.UploadAsync();
        if (progress.Status == Google.Apis.Upload.UploadStatus.Failed)
        {
            _logger.LogError(progress.Exception, "Upload bytes to Google Drive failed.");
            throw progress.Exception;
        }

        var fileResult = request.ResponseBody;
        return fileResult.Id;
    }

    public async Task DeleteFileAsync(string fileId)
    {
        try
        {
            await GetClient().Files.Delete(fileId).ExecuteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file {FileId} from Google Drive", fileId);
            throw;
        }
    }

    public async Task<Stream> DownloadFileAsync(string fileId)
    {
        var stream = new MemoryStream();
        var request = GetClient().Files.Get(fileId);
        var progress = await request.DownloadAsync(stream);
        
        if (progress.Status == Google.Apis.Download.DownloadStatus.Failed)
        {
            throw new Exception($"Failed to download file {fileId}", progress.Exception);
        }

        stream.Position = 0; // Reset position for reading
        return stream;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            if (_driveService == null) return false;
            var request = GetClient().About.Get();
            request.Fields = "user";
            var result = await request.ExecuteAsync();
            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Drive test connection failed.");
            return false;
        }
    }
}
