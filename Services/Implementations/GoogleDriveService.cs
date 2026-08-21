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

namespace SIAP.Api.Services.Implementations;

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
    private readonly DriveService _driveService;
    private readonly string _folderId;
    private readonly ILogger<GoogleDriveService> _logger;

    public GoogleDriveService(IConfiguration config, ILogger<GoogleDriveService> logger)
    {
        _logger = logger;
        
        var tokenJson = config["GoogleDrive:TokenJson"] ?? config["GoogleDrive__TokenJson"];
        _folderId = config["GoogleDrive:FolderId"] ?? config["GoogleDrive__FolderId"] 
                    ?? throw new ArgumentNullException("GoogleDrive__FolderId configuration is missing.");

        if (string.IsNullOrEmpty(tokenJson))
        {
            throw new ArgumentNullException("GoogleDrive__TokenJson configuration is missing.");
        }

        tokenJson = tokenJson.Trim('\'');
        var tokenData = JsonSerializer.Deserialize<GoogleDriveToken>(tokenJson);
        if (tokenData == null)
        {
            throw new Exception("Invalid GoogleDrive Token JSON");
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
            // Set so the Google client knows exactly when it expires and can preemptively refresh
            // without waiting for a 401 Unauthorized error
            tokenResponse.IssuedUtc = DateTime.UtcNow;
            var expiresIn = (long)(tokenData.Expiry.Value.ToUniversalTime() - DateTime.UtcNow).TotalSeconds;
            // If already expired, set to 0 or negative so it refreshes immediately on first use
            tokenResponse.ExpiresInSeconds = expiresIn > 0 ? expiresIn : 0;
        }

        var credential = new UserCredential(flow, "user", tokenResponse);

        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "SIAP API"
        });
    }

    public async Task<string> UploadFileAsync(IFormFile file, string fileName)
    {
        var fileMetadata = new Google.Apis.Drive.v3.Data.File()
        {
            Name = fileName,
            Parents = new List<string> { _folderId }
        };

        using var stream = file.OpenReadStream();
        var request = _driveService.Files.Create(fileMetadata, stream, file.ContentType);
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

    public async Task DeleteFileAsync(string fileId)
    {
        try
        {
            await _driveService.Files.Delete(fileId).ExecuteAsync();
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
        var request = _driveService.Files.Get(fileId);
        var progress = await request.DownloadAsync(stream);
        
        if (progress.Status == Google.Apis.Download.DownloadStatus.Failed)
        {
            throw new Exception($"Failed to download file {fileId}", progress.Exception);
        }

        stream.Position = 0; // Reset position for reading
        return stream;
    }
}
