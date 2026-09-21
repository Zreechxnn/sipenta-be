using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SIAP.Api.DTOs;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class CloudStorageManager : IGoogleDriveService, ICloudStorageService
{
    private readonly ISystemConfigService _configService;
    private readonly GoogleDriveService _googleDriveService;
    private readonly HttpClient _httpClient;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CloudStorageManager> _logger;

    public string ProviderName => "CloudStorageManager";

    public CloudStorageManager(
        ISystemConfigService configService,
        GoogleDriveService googleDriveService,
        HttpClient httpClient,
        ILoggerFactory loggerFactory,
        ILogger<CloudStorageManager> logger)
    {
        _configService = configService;
        _googleDriveService = googleDriveService;
        _httpClient = httpClient;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    private async Task<ICloudStorageService> GetActiveProviderAsync()
    {
        var config = await _configService.GetStorageConfigAsync();
        var provider = config.ActiveProvider?.Trim().ToLowerInvariant() ?? "googledrive";

        switch (provider)
        {
            case "localstorage":
            case "local":
                return new LocalStorageProvider(
                    _loggerFactory.CreateLogger<LocalStorageProvider>(),
                    config.LocalStorage?.BasePath ?? "Uploads/Storage",
                    config.LocalStorage?.DocumentFolder ?? "Documents",
                    config.LocalStorage?.ImageFolder ?? "Images"
                );

            case "supabase":
            case "supabasestorage":
                return ResolveSupabaseProvider(config);

            case "s3compatible":
            case "s3":
            case "openstack":
            case "openstackswift":
            case "minio":
            case "pdn_objectstorage":
            case "pdn_s3":
                if (config.S3Compatible?.ProviderType?.Equals("Supabase", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return ResolveSupabaseProvider(config);
                }
                return new S3CompatibleStorageProvider(
                    _httpClient,
                    _loggerFactory.CreateLogger<S3CompatibleStorageProvider>(),
                    config.S3Compatible?.Endpoint ?? string.Empty,
                    config.S3Compatible?.BucketName ?? string.Empty,
                    config.S3Compatible?.AccessKey ?? string.Empty,
                    config.S3Compatible?.SecretKey ?? string.Empty,
                    config.S3Compatible?.DocumentPrefix ?? "documents",
                    config.S3Compatible?.ImagePrefix ?? "images",
                    config.S3Compatible?.Region ?? "us-east-1"
                );

            case "webdav":
            case "nextcloud":
            case "owncloud":
            case "pdn":
            case "pdns":
            case "pdn_webdav":
                return new WebDavStorageProvider(
                    _httpClient,
                    _loggerFactory.CreateLogger<WebDavStorageProvider>(),
                    config.WebDav?.ServerUrl ?? string.Empty,
                    config.WebDav?.Username ?? string.Empty,
                    config.WebDav?.Password ?? string.Empty,
                    config.WebDav?.RemotePath ?? "sipenta",
                    config.WebDav?.DocumentPath ?? "documents",
                    config.WebDav?.ImagePath ?? "images"
                );

            case "googledrive":
            default:
                return _googleDriveService;
        }
    }

    private async Task<ICloudStorageService> ResolveProviderForResourceAsync(string fileIdOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileIdOrPath))
        {
            return _googleDriveService;
        }

        if (fileIdOrPath.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            var config = await _configService.GetStorageConfigAsync();
            return new LocalStorageProvider(
                _loggerFactory.CreateLogger<LocalStorageProvider>(),
                config.LocalStorage?.BasePath ?? "Uploads/Storage",
                config.LocalStorage?.DocumentFolder ?? "Documents",
                config.LocalStorage?.ImageFolder ?? "Images"
            );
        }

        if (fileIdOrPath.StartsWith("supabase:", StringComparison.OrdinalIgnoreCase))
        {
            var config = await _configService.GetStorageConfigAsync();
            return ResolveSupabaseProvider(config);
        }

        if (fileIdOrPath.StartsWith("s3:", StringComparison.OrdinalIgnoreCase) || 
            fileIdOrPath.StartsWith("openstack:", StringComparison.OrdinalIgnoreCase) ||
            fileIdOrPath.StartsWith("minio:", StringComparison.OrdinalIgnoreCase) ||
            fileIdOrPath.StartsWith("pdn_s3:", StringComparison.OrdinalIgnoreCase))
        {
            var config = await _configService.GetStorageConfigAsync();
            if (config.S3Compatible?.ProviderType?.Equals("Supabase", StringComparison.OrdinalIgnoreCase) == true)
            {
                return ResolveSupabaseProvider(config);
            }
            return new S3CompatibleStorageProvider(
                _httpClient,
                _loggerFactory.CreateLogger<S3CompatibleStorageProvider>(),
                config.S3Compatible?.Endpoint ?? string.Empty,
                config.S3Compatible?.BucketName ?? string.Empty,
                config.S3Compatible?.AccessKey ?? string.Empty,
                config.S3Compatible?.SecretKey ?? string.Empty,
                config.S3Compatible?.DocumentPrefix ?? "documents",
                config.S3Compatible?.ImagePrefix ?? "images",
                config.S3Compatible?.Region ?? "us-east-1"
            );
        }

        if (fileIdOrPath.StartsWith("webdav:", StringComparison.OrdinalIgnoreCase) ||
            fileIdOrPath.StartsWith("nextcloud:", StringComparison.OrdinalIgnoreCase) ||
            fileIdOrPath.StartsWith("owncloud:", StringComparison.OrdinalIgnoreCase) ||
            fileIdOrPath.StartsWith("pdn:", StringComparison.OrdinalIgnoreCase) ||
            fileIdOrPath.StartsWith("pdns:", StringComparison.OrdinalIgnoreCase))
        {
            var config = await _configService.GetStorageConfigAsync();
            return new WebDavStorageProvider(
                _httpClient,
                _loggerFactory.CreateLogger<WebDavStorageProvider>(),
                config.WebDav?.ServerUrl ?? string.Empty,
                config.WebDav?.Username ?? string.Empty,
                config.WebDav?.Password ?? string.Empty,
                config.WebDav?.RemotePath ?? "sipenta",
                config.WebDav?.DocumentPath ?? "documents",
                config.WebDav?.ImagePath ?? "images"
            );
        }

        // Default to Google Drive for existing alphanumeric file IDs
        return _googleDriveService;
    }

    public async Task<string> UploadFileAsync(IFormFile file, string fileName)
    {
        var provider = await GetActiveProviderAsync();
        _logger.LogInformation("Uploading file '{FileName}' via active provider '{ProviderName}'", fileName, provider.ProviderName);
        return await provider.UploadFileAsync(file, fileName);
    }

    public async Task<string> UploadFileBytesAsync(byte[] fileBytes, string fileName, string contentType, string? folderOrPrefix = null)
    {
        var provider = await GetActiveProviderAsync();
        _logger.LogInformation("Uploading bytes '{FileName}' via active provider '{ProviderName}'", fileName, provider.ProviderName);
        return await provider.UploadFileBytesAsync(fileBytes, fileName, contentType, folderOrPrefix);
    }

    public async Task DeleteFileAsync(string fileIdOrPath)
    {
        var provider = await ResolveProviderForResourceAsync(fileIdOrPath);
        _logger.LogInformation("Deleting resource '{FileIdOrPath}' via provider '{ProviderName}'", fileIdOrPath, provider.ProviderName);
        await provider.DeleteFileAsync(fileIdOrPath);
    }

    public async Task<Stream> DownloadFileAsync(string fileIdOrPath)
    {
        var provider = await ResolveProviderForResourceAsync(fileIdOrPath);
        _logger.LogInformation("Downloading resource '{FileIdOrPath}' via provider '{ProviderName}'", fileIdOrPath, provider.ProviderName);
        return await provider.DownloadFileAsync(fileIdOrPath);
    }

    public async Task<bool> TestConnectionAsync()
    {
        var provider = await GetActiveProviderAsync();
        return await provider.TestConnectionAsync();
    }

    private ICloudStorageService ResolveSupabaseProvider(StorageConfigDto config)
    {
        var projectUrl = !string.IsNullOrWhiteSpace(config.Supabase?.ProjectUrl)
            ? config.Supabase.ProjectUrl
            : config.S3Compatible?.Endpoint ?? string.Empty;

        var apiKey = !string.IsNullOrWhiteSpace(config.Supabase?.ApiKey)
            ? config.Supabase.ApiKey
            : (!string.IsNullOrWhiteSpace(config.S3Compatible?.SecretKey)
                ? config.S3Compatible.SecretKey
                : config.S3Compatible?.AccessKey ?? string.Empty);

        var bucket = !string.IsNullOrWhiteSpace(config.Supabase?.BucketName)
            ? config.Supabase.BucketName
            : (!string.IsNullOrWhiteSpace(config.S3Compatible?.BucketName)
                ? config.S3Compatible.BucketName
                : "documents");

        var docFolder = !string.IsNullOrWhiteSpace(config.Supabase?.DocumentFolder)
            ? config.Supabase.DocumentFolder
            : (!string.IsNullOrWhiteSpace(config.S3Compatible?.DocumentPrefix)
                ? config.S3Compatible.DocumentPrefix
                : "documents");

        var imgFolder = !string.IsNullOrWhiteSpace(config.Supabase?.ImageFolder)
            ? config.Supabase.ImageFolder
            : (!string.IsNullOrWhiteSpace(config.S3Compatible?.ImagePrefix)
                ? config.S3Compatible.ImagePrefix
                : "images");

        return new SupabaseStorageProvider(
            _httpClient,
            _loggerFactory.CreateLogger<SupabaseStorageProvider>(),
            projectUrl,
            apiKey,
            bucket,
            docFolder,
            imgFolder
        );
    }
}
