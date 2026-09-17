using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using SIAP.Api.Data;
using SIAP.Api.DTOs;
using SIAP.Api.Entities;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class SystemConfigService : ISystemConfigService
{
    private const string LlmCacheKey = "SYSTEM_CONFIG_LLM";
    private const string StorageCacheKey = "SYSTEM_CONFIG_STORAGE";
    private const string DatabaseCacheKey = "SYSTEM_CONFIG_DB";

    private const string LlmSettingKey = "System:LlmConfigs";
    private const string StorageSettingKey = "System:StorageConfig";
    private const string DatabaseSettingKey = "System:DatabaseConfig";

    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly HttpClient _httpClient;
    private readonly IConfigCipherService _cipherService;
    private readonly ILogger<SystemConfigService> _logger;

    public SystemConfigService(
        AppDbContext dbContext,
        IConfiguration configuration,
        IMemoryCache cache,
        HttpClient httpClient,
        IConfigCipherService cipherService,
        ILogger<SystemConfigService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _cache = cache;
        _httpClient = httpClient;
        _cipherService = cipherService;
        _logger = logger;

        EnsureTableCreated();
    }

    private void EnsureTableCreated()
    {
        try
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS ""SystemSettings"" (
                    ""Key"" text NOT NULL,
                    ""Value"" text NOT NULL,
                    ""Description"" text,
                    ""UpdatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedBy"" text,
                    CONSTRAINT ""PK_SystemSettings"" PRIMARY KEY (""Key"")
                );";
            _dbContext.Database.ExecuteSqlRaw(sql);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify/create SystemSettings table automatically.");
        }
    }

    public async Task<ConfigurationOverviewDto> GetOverviewAsync()
    {
        var llm = await GetLlmConfigsAsync();
        var storage = await GetStorageConfigAsync();
        var db = await GetDatabaseConfigAsync();

        bool isDbOk = false;
        try
        {
            isDbOk = await _dbContext.Database.CanConnectAsync();
        }
        catch
        {
            isDbOk = false;
        }

        return new ConfigurationOverviewDto
        {
            TotalLlmKeys = llm.Endpoints.Count,
            ActiveLlmKeys = llm.Endpoints.Count(e => e.IsActive),
            ActiveStorageProvider = storage.ActiveProvider,
            DatabaseHost = db.Host,
            DatabaseName = db.Database,
            IsDatabaseConnected = isDbOk
        };
    }

    #region LLM Configurations

    public async Task<LlmConfigListDto> GetLlmConfigsAsync()
    {
        if (_cache.TryGetValue(LlmCacheKey, out LlmConfigListDto? cached) && cached != null)
        {
            return cached;
        }

        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == LlmSettingKey);
        if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
        {
            try
            {
                if (!_cipherService.IsEncrypted(setting.Value))
                {
                    setting.Value = _cipherService.Encrypt(setting.Value);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("LlmConfigs setting otomatis dienkripsi dengan AES-256-GCM.");
                }

                var decrypted = _cipherService.Decrypt(setting.Value);
                var parsed = JsonSerializer.Deserialize<LlmConfigListDto>(decrypted);
                if (parsed != null && parsed.Endpoints.Any())
                {
                    _cache.Set(LlmCacheKey, parsed, TimeSpan.FromMinutes(5));
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse LlmConfigs from database");
            }
        }

        // Fallback: seed from IConfiguration (.env)
        var seeded = SeedLlmConfigsFromEnvironment();
        if (seeded.Endpoints.Any())
        {
            await SaveLlmConfigsAsync(seeded, "System (Initial Seed)");
            _cache.Set(LlmCacheKey, seeded, TimeSpan.FromMinutes(5));
            return seeded;
        }

        var emptyList = new LlmConfigListDto();
        _cache.Set(LlmCacheKey, emptyList, TimeSpan.FromMinutes(1));
        return emptyList;
    }

    private LlmConfigListDto SeedLlmConfigsFromEnvironment()
    {
        var result = new LlmConfigListDto();
        var defaultBaseUrl = "https://api.groq.com/openai/v1/chat/completions";
        var defaultModel = "openai/gpt-oss-120b";
        var defaultImageModel = "qwen/qwen3.8-27b";

        var primaryKey = _configuration["Llm:ApiKey"] ?? _configuration["Llm__ApiKey"] ?? _configuration["Llm:ApiKey1"] ?? _configuration["Llm__ApiKey1"];
        if (!string.IsNullOrWhiteSpace(primaryKey))
        {
            result.Endpoints.Add(new LlmEndpointConfigDto
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Groq Primary (Env)",
                Provider = "groq",
                ApiKey = primaryKey.Trim(),
                BaseUrl = _configuration["Llm:BaseUrl"] ?? _configuration["Llm__BaseUrl"] ?? defaultBaseUrl,
                Model = _configuration["Llm:Model"] ?? _configuration["Llm__Model"] ?? defaultModel,
                ImageModel = _configuration["Llm:Image"] ?? _configuration["Llm:image"] ?? defaultImageModel,
                IsActive = true,
                Priority = 1
            });
        }

        for (int i = 2; i <= 20; i++)
        {
            var key = _configuration[$"Llm:ApiKey{i}"] ?? _configuration[$"Llm__ApiKey{i}"] ?? _configuration[$"Llm:apikey{i}"];
            if (!string.IsNullOrWhiteSpace(key))
            {
                var url = _configuration[$"Llm:BaseUrl{i}"] ?? _configuration[$"Llm__BaseUrl{i}"] ?? defaultBaseUrl;
                var model = _configuration[$"Llm:Model{i}"] ?? _configuration[$"Llm__Model{i}"] ?? defaultModel;
                var image = _configuration[$"Llm:Image{i}"] ?? _configuration[$"Llm__Image{i}"] ?? defaultImageModel;

                result.Endpoints.Add(new LlmEndpointConfigDto
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"Groq Backup {i} (Env)",
                    Provider = "groq",
                    ApiKey = key.Trim(),
                    BaseUrl = url.Trim(),
                    Model = model.Trim(),
                    ImageModel = image.Trim(),
                    IsActive = true,
                    Priority = i
                });
            }
        }

        return result;
    }

    public async Task SaveLlmConfigsAsync(LlmConfigListDto config, string? updatedBy = null)
    {
        // Clean and sort by priority
        config.Endpoints = config.Endpoints.OrderBy(e => e.Priority).ToList();
        int p = 1;
        foreach (var endpoint in config.Endpoints)
        {
            endpoint.Priority = p++;
        }

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var encryptedValue = _cipherService.Encrypt(json);
        var existing = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == LlmSettingKey);
        if (existing == null)
        {
            _dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = LlmSettingKey,
                Value = encryptedValue,
                Description = "Daftar konfigurasi LLM dan API Keys (Terenkripsi AES-256)",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = updatedBy
            });
        }
        else
        {
            existing.Value = encryptedValue;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = updatedBy;
        }

        await _dbContext.SaveChangesAsync();
        _cache.Remove(LlmCacheKey);
    }

    public async Task<LlmTestResponseDto> TestLlmEndpointAsync(LlmTestRequestDto request)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return new LlmTestResponseDto
                {
                    Success = false,
                    Message = "API Key tidak boleh kosong."
                };
            }

            var prompt = string.IsNullOrWhiteSpace(request.TestPrompt) ? "Halo, tes koneksi." : request.TestPrompt;
            var body = new
            {
                model = string.IsNullOrWhiteSpace(request.Model) ? "openai/gpt-oss-120b" : request.Model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_completion_tokens = 50
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, request.BaseUrl);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(req);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errDetail = await response.Content.ReadAsStringAsync();
                return new LlmTestResponseDto
                {
                    Success = false,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Message = $"Gagal memanggil endpoint (Status {(int)response.StatusCode}): {errDetail}"
                };
            }

            var resJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resJson);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return new LlmTestResponseDto
            {
                Success = true,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = "Koneksi ke LLM API berhasil!",
                ResponseText = content
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new LlmTestResponseDto
            {
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"Terjadi error: {ex.Message}"
            };
        }
    }

    #endregion

    #region Storage Configurations

    public async Task<StorageConfigDto> GetStorageConfigAsync()
    {
        if (_cache.TryGetValue(StorageCacheKey, out StorageConfigDto? cached) && cached != null)
        {
            return cached;
        }

        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == StorageSettingKey);
        if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
        {
            try
            {
                if (!_cipherService.IsEncrypted(setting.Value))
                {
                    setting.Value = _cipherService.Encrypt(setting.Value);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("StorageConfig setting otomatis dienkripsi dengan AES-256-GCM.");
                }

                var decrypted = _cipherService.Decrypt(setting.Value);
                var parsed = JsonSerializer.Deserialize<StorageConfigDto>(decrypted);
                if (parsed != null)
                {
                    _cache.Set(StorageCacheKey, parsed, TimeSpan.FromMinutes(5));
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse StorageConfig from database");
            }
        }

        // Fallback: seed from IConfiguration (.env)
        var seeded = SeedStorageConfigFromEnvironment();
        await SaveStorageConfigAsync(seeded, "System (Initial Seed)");
        _cache.Set(StorageCacheKey, seeded, TimeSpan.FromMinutes(5));
        return seeded;
    }

    private StorageConfigDto SeedStorageConfigFromEnvironment()
    {
        var config = new StorageConfigDto
        {
            ActiveProvider = "GoogleDrive",
            GoogleDrive = new GoogleDriveSettingsDto
            {
                TokenJson = _configuration["GoogleDrive:TokenJson"] ?? _configuration["GoogleDrive__TokenJson"] ?? string.Empty,
                FolderId = _configuration["GoogleDrive:FolderId"] ?? _configuration["GoogleDrive__FolderId"] ?? string.Empty,
                FolderImageId = _configuration["GoogleDrive:Folder_image"] ?? _configuration["GoogleDrive__Folder_image"] ?? string.Empty,
                ClientId = _configuration["Google:ClientId"] ?? _configuration["Google__ClientId"] ?? string.Empty
            },
            LocalStorage = new LocalStorageSettingsDto
            {
                BasePath = "Uploads/Storage",
                DocumentFolder = "Documents",
                ImageFolder = "Images"
            },
            Supabase = new SupabaseSettingsDto
            {
                ProjectUrl = _configuration["Supabase:Url"] ?? _configuration["Supabase__Url"] ?? string.Empty,
                ApiKey = _configuration["Supabase:ApiKey"] ?? _configuration["Supabase__ApiKey"] ?? string.Empty,
                BucketName = _configuration["Supabase:BucketName"] ?? _configuration["Supabase__BucketName"] ?? "documents",
                DocumentFolder = "documents",
                ImageFolder = "images"
            },
            S3Compatible = new S3SettingsDto
            {
                Endpoint = "",
                BucketName = "documents",
                Region = "ap-southeast-1",
                DocumentPrefix = "documents",
                ImagePrefix = "images"
            },
            WebDav = new WebDavSettingsDto
            {
                ServerUrl = "",
                Username = "",
                Password = "",
                RemotePath = "siap",
                Preset = "Nextcloud",
                DocumentPath = "documents",
                ImagePath = "images"
            }
        };

        return config;
    }

    public async Task SaveStorageConfigAsync(StorageConfigDto config, string? updatedBy = null)
    {
        StorageConfigDto? existingConfig = null;
        if (_cache.TryGetValue(StorageCacheKey, out StorageConfigDto? cached) && cached != null)
        {
            existingConfig = cached;
        }

        var existing = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == StorageSettingKey);
        if (existingConfig == null && existing != null && !string.IsNullOrWhiteSpace(existing.Value))
        {
            try
            {
                var decrypted = _cipherService.Decrypt(existing.Value);
                existingConfig = JsonSerializer.Deserialize<StorageConfigDto>(decrypted);
            }
            catch
            {
                // ignore
            }
        }

        existingConfig ??= SeedStorageConfigFromEnvironment();

        // Merge to preserve all provider settings across switches
        var merged = new StorageConfigDto
        {
            ActiveProvider = !string.IsNullOrWhiteSpace(config.ActiveProvider) ? config.ActiveProvider : (existingConfig.ActiveProvider ?? "GoogleDrive"),
            GoogleDrive = new GoogleDriveSettingsDto
            {
                TokenJson = !string.IsNullOrWhiteSpace(config.GoogleDrive?.TokenJson) ? config.GoogleDrive.TokenJson : (existingConfig.GoogleDrive?.TokenJson ?? string.Empty),
                FolderId = !string.IsNullOrWhiteSpace(config.GoogleDrive?.FolderId) ? config.GoogleDrive.FolderId : (existingConfig.GoogleDrive?.FolderId ?? string.Empty),
                FolderImageId = !string.IsNullOrWhiteSpace(config.GoogleDrive?.FolderImageId) ? config.GoogleDrive.FolderImageId : (existingConfig.GoogleDrive?.FolderImageId ?? string.Empty),
                ClientId = !string.IsNullOrWhiteSpace(config.GoogleDrive?.ClientId) ? config.GoogleDrive.ClientId : (existingConfig.GoogleDrive?.ClientId ?? string.Empty),
                ClientSecret = !string.IsNullOrWhiteSpace(config.GoogleDrive?.ClientSecret) ? config.GoogleDrive.ClientSecret : (existingConfig.GoogleDrive?.ClientSecret ?? string.Empty)
            },
            LocalStorage = new LocalStorageSettingsDto
            {
                BasePath = !string.IsNullOrWhiteSpace(config.LocalStorage?.BasePath) ? config.LocalStorage.BasePath : (existingConfig.LocalStorage?.BasePath ?? "Uploads/Storage"),
                DocumentFolder = !string.IsNullOrWhiteSpace(config.LocalStorage?.DocumentFolder) ? config.LocalStorage.DocumentFolder : (existingConfig.LocalStorage?.DocumentFolder ?? "Documents"),
                ImageFolder = !string.IsNullOrWhiteSpace(config.LocalStorage?.ImageFolder) ? config.LocalStorage.ImageFolder : (existingConfig.LocalStorage?.ImageFolder ?? "Images")
            },
            Supabase = new SupabaseSettingsDto
            {
                ProjectUrl = !string.IsNullOrWhiteSpace(config.Supabase?.ProjectUrl) ? config.Supabase.ProjectUrl : (existingConfig.Supabase?.ProjectUrl ?? string.Empty),
                ApiKey = !string.IsNullOrWhiteSpace(config.Supabase?.ApiKey) ? config.Supabase.ApiKey : (existingConfig.Supabase?.ApiKey ?? string.Empty),
                BucketName = !string.IsNullOrWhiteSpace(config.Supabase?.BucketName) ? config.Supabase.BucketName : (existingConfig.Supabase?.BucketName ?? "documents"),
                DocumentFolder = !string.IsNullOrWhiteSpace(config.Supabase?.DocumentFolder) ? config.Supabase.DocumentFolder : (existingConfig.Supabase?.DocumentFolder ?? "documents"),
                ImageFolder = !string.IsNullOrWhiteSpace(config.Supabase?.ImageFolder) ? config.Supabase.ImageFolder : (existingConfig.Supabase?.ImageFolder ?? "images")
            },
            S3Compatible = new S3SettingsDto
            {
                Endpoint = !string.IsNullOrWhiteSpace(config.S3Compatible?.Endpoint) ? config.S3Compatible.Endpoint : (existingConfig.S3Compatible?.Endpoint ?? string.Empty),
                BucketName = !string.IsNullOrWhiteSpace(config.S3Compatible?.BucketName) ? config.S3Compatible.BucketName : (existingConfig.S3Compatible?.BucketName ?? "documents"),
                AccessKey = !string.IsNullOrWhiteSpace(config.S3Compatible?.AccessKey) ? config.S3Compatible.AccessKey : (existingConfig.S3Compatible?.AccessKey ?? string.Empty),
                SecretKey = !string.IsNullOrWhiteSpace(config.S3Compatible?.SecretKey) ? config.S3Compatible.SecretKey : (existingConfig.S3Compatible?.SecretKey ?? string.Empty),
                Region = !string.IsNullOrWhiteSpace(config.S3Compatible?.Region) ? config.S3Compatible.Region : (existingConfig.S3Compatible?.Region ?? "us-east-1"),
                ProviderType = !string.IsNullOrWhiteSpace(config.S3Compatible?.ProviderType) ? config.S3Compatible.ProviderType : (existingConfig.S3Compatible?.ProviderType ?? "S3Compatible"),
                DocumentPrefix = !string.IsNullOrWhiteSpace(config.S3Compatible?.DocumentPrefix) ? config.S3Compatible.DocumentPrefix : (existingConfig.S3Compatible?.DocumentPrefix ?? "documents"),
                ImagePrefix = !string.IsNullOrWhiteSpace(config.S3Compatible?.ImagePrefix) ? config.S3Compatible.ImagePrefix : (existingConfig.S3Compatible?.ImagePrefix ?? "images")
            },
            WebDav = new WebDavSettingsDto
            {
                ServerUrl = !string.IsNullOrWhiteSpace(config.WebDav?.ServerUrl) ? config.WebDav.ServerUrl : (existingConfig.WebDav?.ServerUrl ?? string.Empty),
                Username = !string.IsNullOrWhiteSpace(config.WebDav?.Username) ? config.WebDav.Username : (existingConfig.WebDav?.Username ?? string.Empty),
                Password = !string.IsNullOrWhiteSpace(config.WebDav?.Password) ? config.WebDav.Password : (existingConfig.WebDav?.Password ?? string.Empty),
                RemotePath = !string.IsNullOrWhiteSpace(config.WebDav?.RemotePath) ? config.WebDav.RemotePath : (existingConfig.WebDav?.RemotePath ?? "siap"),
                Preset = !string.IsNullOrWhiteSpace(config.WebDav?.Preset) ? config.WebDav.Preset : (existingConfig.WebDav?.Preset ?? "Nextcloud"),
                DocumentPath = !string.IsNullOrWhiteSpace(config.WebDav?.DocumentPath) ? config.WebDav.DocumentPath : (existingConfig.WebDav?.DocumentPath ?? "documents"),
                ImagePath = !string.IsNullOrWhiteSpace(config.WebDav?.ImagePath) ? config.WebDav.ImagePath : (existingConfig.WebDav?.ImagePath ?? "images")
            }
        };

        var json = JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true });
        var encryptedValue = _cipherService.Encrypt(json);
        if (existing == null)
        {
            _dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = StorageSettingKey,
                Value = encryptedValue,
                Description = "Konfigurasi Cloud Storage (Terenkripsi AES-256)",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = updatedBy
            });
        }
        else
        {
            existing.Value = encryptedValue;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = updatedBy;
        }

        await _dbContext.SaveChangesAsync();
        _cache.Remove(StorageCacheKey);
    }

    public async Task<StorageTestResponseDto> TestStorageAsync(StorageConfigDto? testConfig = null)
    {
        var config = testConfig ?? await GetStorageConfigAsync();
        var provider = config.ActiveProvider?.Trim().ToLowerInvariant() ?? "googledrive";

        try
        {
            if (provider == "localstorage" || provider == "local")
            {
                var local = new LocalStorageProvider(
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<LocalStorageProvider>(),
                    config.LocalStorage?.BasePath ?? "Uploads/Storage",
                    config.LocalStorage?.DocumentFolder ?? "Documents",
                    config.LocalStorage?.ImageFolder ?? "Images"
                );
                var ok = await local.TestConnectionAsync();
                return new StorageTestResponseDto
                {
                    Success = ok,
                    Provider = "LocalStorage",
                    Message = ok ? "Penyimpanan lokal berfungsi dengan baik (Write & Read sukses)!" : "Gagal menguji direktori penyimpanan lokal.",
                    Details = $"Path: {config.LocalStorage?.BasePath ?? "Uploads/Storage"} (Dokumen: {config.LocalStorage?.DocumentFolder ?? "Documents"}, Gambar: {config.LocalStorage?.ImageFolder ?? "Images"})"
                };
            }
            else if (provider == "supabase" || provider == "supabasestorage" || 
                     (config.S3Compatible?.ProviderType?.Equals("Supabase", StringComparison.OrdinalIgnoreCase) == true && (provider == "s3compatible" || provider == "s3")))
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

                var supabase = new SupabaseStorageProvider(
                    _httpClient,
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SupabaseStorageProvider>(),
                    projectUrl,
                    apiKey,
                    bucket,
                    docFolder,
                    imgFolder
                );
                var ok = await supabase.TestConnectionAsync();
                return new StorageTestResponseDto
                {
                    Success = ok,
                    Provider = "Supabase Storage",
                    Message = ok ? "Koneksi ke Supabase Storage REST API berhasil!" : "Gagal menghubungi Supabase Storage. Pastikan Project URL, API Key, dan nama Bucket valid.",
                    Details = $"Project URL: {projectUrl}, Bucket: {bucket} (Folder Dokumen: {docFolder}, Folder Gambar: {imgFolder})"
                };
            }
            else if (provider == "s3compatible" || provider == "s3" || provider == "supabase" || 
                     provider == "openstack" || provider == "openstackswift" || provider == "minio" || 
                     provider == "pdn_objectstorage" || provider == "pdn_s3")
            {
                var s3 = new S3CompatibleStorageProvider(
                    _httpClient,
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<S3CompatibleStorageProvider>(),
                    config.S3Compatible?.Endpoint ?? "",
                    config.S3Compatible?.BucketName ?? "",
                    config.S3Compatible?.AccessKey ?? "",
                    config.S3Compatible?.SecretKey ?? "",
                    config.S3Compatible?.DocumentPrefix ?? "documents",
                    config.S3Compatible?.ImagePrefix ?? "images",
                    config.S3Compatible?.Region ?? "us-east-1"
                );
                var ok = await s3.TestConnectionAsync();
                return new StorageTestResponseDto
                {
                    Success = ok,
                    Provider = $"Cloud Storage ({config.S3Compatible?.ProviderType ?? "S3 / OpenStack / PDN"})",
                    Message = ok ? "Koneksi ke S3 / OpenStack / PDN Object Storage berhasil!" : "Gagal menghubungi endpoint S3 / Cloud Storage.",
                    Details = $"Endpoint: {config.S3Compatible?.Endpoint}, Bucket: {config.S3Compatible?.BucketName} (Dokumen: {config.S3Compatible?.DocumentPrefix ?? "documents"}, Gambar: {config.S3Compatible?.ImagePrefix ?? "images"})"
                };
            }
            else if (provider == "webdav" || provider == "nextcloud" || provider == "owncloud" || provider == "pdn" || provider == "pdns")
            {
                var webdav = new WebDavStorageProvider(
                    _httpClient,
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<WebDavStorageProvider>(),
                    config.WebDav?.ServerUrl ?? "",
                    config.WebDav?.Username ?? "",
                    config.WebDav?.Password ?? "",
                    config.WebDav?.RemotePath ?? "siap",
                    config.WebDav?.DocumentPath ?? "documents",
                    config.WebDav?.ImagePath ?? "images"
                );
                var ok = await webdav.TestConnectionAsync();
                return new StorageTestResponseDto
                {
                    Success = ok,
                    Provider = $"WebDAV ({config.WebDav?.Preset ?? "Nextcloud/ownCloud/PDN"})",
                    Message = ok ? "Koneksi ke WebDAV server (Nextcloud/ownCloud/PDN) berhasil!" : "Gagal menghubungi server WebDAV. Periksa URL, Username, dan Password/Token.",
                    Details = $"Server: {config.WebDav?.ServerUrl}, User: {config.WebDav?.Username}, Path: {config.WebDav?.RemotePath} (Dokumen: {config.WebDav?.DocumentPath ?? "documents"}, Gambar: {config.WebDav?.ImagePath ?? "images"})"
                };
            }
            else
            {
                // Google Drive
                var token = config.GoogleDrive?.TokenJson ?? _configuration["GoogleDrive:TokenJson"] ?? _configuration["GoogleDrive__TokenJson"];
                if (string.IsNullOrWhiteSpace(token))
                {
                    return new StorageTestResponseDto
                    {
                        Success = false,
                        Provider = "GoogleDrive",
                        Message = "Token JSON Google Drive belum disetel."
                    };
                }

                // Check JSON format
                try
                {
                    using var doc = JsonDocument.Parse(token.Trim('\''));
                    var hasRefreshToken = doc.RootElement.TryGetProperty("refresh_token", out _);
                    var hasClientId = doc.RootElement.TryGetProperty("client_id", out _);
                    if (!hasRefreshToken && !hasClientId)
                    {
                        return new StorageTestResponseDto
                        {
                            Success = false,
                            Provider = "GoogleDrive",
                            Message = "Token JSON Google Drive tidak memiliki field 'refresh_token' atau 'client_id'."
                        };
                    }
                }
                catch (Exception jsonEx)
                {
                    return new StorageTestResponseDto
                    {
                        Success = false,
                        Provider = "GoogleDrive",
                        Message = $"Token JSON Google Drive tidak valid: {jsonEx.Message}"
                    };
                }

                return new StorageTestResponseDto
                {
                    Success = true,
                    Provider = "GoogleDrive",
                    Message = "Konfigurasi Google Drive valid dan siap digunakan!",
                    Details = $"Folder ID Dokumen: {config.GoogleDrive?.FolderId}, Folder ID Gambar: {config.GoogleDrive?.FolderImageId}"
                };
            }
        }
        catch (Exception ex)
        {
            return new StorageTestResponseDto
            {
                Success = false,
                Provider = config.ActiveProvider ?? "Unknown",
                Message = $"Pengujian penyimpanan gagal: {ex.Message}"
            };
        }
    }

    #endregion

    #region Database Configurations

    public async Task<DatabaseConfigDto> GetDatabaseConfigAsync()
    {
        var raw = _configuration.GetConnectionString("DefaultConnection") 
                  ?? _configuration["ConnectionStrings:DefaultConnection"] 
                  ?? _configuration["ConnectionStrings__DefaultConnection"] 
                  ?? string.Empty;

        // Check if override in DB
        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == DatabaseSettingKey);
        if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
        {
            if (!_cipherService.IsEncrypted(setting.Value))
            {
                setting.Value = _cipherService.Encrypt(setting.Value);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("DatabaseConfig setting otomatis dienkripsi dengan AES-256-GCM.");
            }

            raw = _cipherService.Decrypt(setting.Value);
        }
        else if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                _dbContext.SystemSettings.Add(new SystemSetting
                {
                    Key = DatabaseSettingKey,
                    Value = _cipherService.Encrypt(raw),
                    Description = "Connection String Database Default (Terenkripsi AES-256)",
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = "System (Initial Seed)"
                });
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("DatabaseConfig initial seed berhasil disimpan terenkripsi di SystemSettings.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist initial database connection string to SystemSettings.");
            }
        }

        var dto = ParseNpgsqlConnectionString(raw);
        return dto;
    }

    public async Task<DatabaseTestResponseDto> TestDatabaseAsync(DatabaseTestRequestDto request)
    {
        var sw = Stopwatch.StartNew();
        var connStr = BuildConnectionString(request);

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connStr)
            {
                Timeout = 15,
                CommandTimeout = 15
            };

            if (builder.Host.Contains("supabase.com", StringComparison.OrdinalIgnoreCase) || builder.Port == 6543)
            {
                builder.Pooling = false;
                builder.SslMode = SslMode.Require;
                builder.TrustServerCertificate = true;
            }

            await using var conn = new NpgsqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT version();";
            var version = (string?)await cmd.ExecuteScalarAsync();

            cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';";
            var countObj = await cmd.ExecuteScalarAsync();
            var tableCount = Convert.ToInt32(countObj);

            sw.Stop();
            return new DatabaseTestResponseDto
            {
                Success = true,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = "Koneksi ke database PostgreSQL berhasil!",
                PostgresVersion = version,
                TableCount = tableCount
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DatabaseTestResponseDto
            {
                Success = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Message = $"Gagal terhubung ke database: {ex.Message}"
            };
        }
    }

    public async Task SaveDatabaseConfigAsync(DatabaseConfigDto request, string? updatedBy = null)
    {
        var connStr = !string.IsNullOrWhiteSpace(request.ConnectionString)
            ? request.ConnectionString.Trim()
            : BuildConnectionString(new DatabaseTestRequestDto
            {
                Host = request.Host,
                Port = request.Port,
                Database = request.Database,
                Username = request.Username,
                Password = request.Password,
                ConnectionString = request.ConnectionString
            });

        var encryptedConnStr = _cipherService.Encrypt(connStr);

        // 1. Save in SystemSettings
        var existing = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == DatabaseSettingKey);
        if (existing == null)
        {
            _dbContext.SystemSettings.Add(new SystemSetting
            {
                Key = DatabaseSettingKey,
                Value = encryptedConnStr,
                Description = "Connection String Database Default (Terenkripsi AES-256)",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = updatedBy
            });
        }
        else
        {
            existing.Value = encryptedConnStr;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = updatedBy;
        }

        await _dbContext.SaveChangesAsync();

        // 2. Update .env file
        UpdateEnvFileDatabaseConnection(connStr);
    }

    private void UpdateEnvFileDatabaseConnection(string newConnectionString)
    {
        try
        {
            var potentialPaths = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), ".env"),
                Path.Combine(Directory.GetCurrentDirectory(), "siap-be", ".env"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".env"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "siap-be", ".env")
            };

            string? envPath = potentialPaths.FirstOrDefault(File.Exists);
            if (envPath == null)
            {
                // Fallback: create in current directory
                envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            }

            var lines = File.Exists(envPath) ? File.ReadAllLines(envPath).ToList() : new List<string>();
            bool replaced = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("ConnectionStrings__DefaultConnection=", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("ConnectionStrings:DefaultConnection=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"ConnectionStrings__DefaultConnection={newConnectionString}";
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                lines.Insert(0, $"ConnectionStrings__DefaultConnection={newConnectionString}");
            }

            File.WriteAllLines(envPath, lines, Encoding.UTF8);
            _logger.LogInformation("Database connection updated in .env at {Path}", envPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update database connection in .env file.");
        }
    }

    private string BuildConnectionString(DatabaseTestRequestDto req)
    {
        if (!string.IsNullOrWhiteSpace(req.ConnectionString))
        {
            return req.ConnectionString.Trim();
        }

        var raw = _configuration.GetConnectionString("DefaultConnection") 
                  ?? _configuration["ConnectionStrings:DefaultConnection"] 
                  ?? _configuration["ConnectionStrings__DefaultConnection"] 
                  ?? string.Empty;

        NpgsqlConnectionStringBuilder? currentBuilder = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                currentBuilder = new NpgsqlConnectionStringBuilder(raw);
            }
        }
        catch {}

        var pwd = req.Password;
        if (string.IsNullOrEmpty(pwd) && currentBuilder != null)
        {
            if (string.Equals(req.Host, currentBuilder.Host, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(req.Host))
            {
                pwd = currentBuilder.Password;
            }
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = string.IsNullOrWhiteSpace(req.Host) ? (currentBuilder?.Host ?? "localhost") : req.Host.Trim(),
            Port = req.Port > 0 ? req.Port.Value : (currentBuilder?.Port ?? 5432),
            Database = string.IsNullOrWhiteSpace(req.Database) ? (currentBuilder?.Database ?? "postgres") : req.Database.Trim(),
            Username = string.IsNullOrWhiteSpace(req.Username) ? (currentBuilder?.Username ?? "postgres") : req.Username.Trim(),
            Password = pwd ?? string.Empty
        };

        if (currentBuilder != null && (string.Equals(builder.Host, currentBuilder.Host, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(req.Host)))
        {
            builder.Pooling = currentBuilder.Pooling;
            builder.SslMode = currentBuilder.SslMode;
            builder.TrustServerCertificate = currentBuilder.TrustServerCertificate;
            builder.KeepAlive = currentBuilder.KeepAlive;
        }

        if (builder.Host.Contains("supabase.com", StringComparison.OrdinalIgnoreCase) || builder.Port == 6543)
        {
            builder.Pooling = false;
            builder.SslMode = SslMode.Require;
            builder.TrustServerCertificate = true;
        }

        return builder.ConnectionString;
    }

    private DatabaseConfigDto ParseNpgsqlConnectionString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new DatabaseConfigDto();
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(raw);
            return new DatabaseConfigDto
            {
                ConnectionString = MaskPasswordInConnectionString(raw),
                Host = builder.Host ?? string.Empty,
                Port = builder.Port,
                Database = builder.Database ?? string.Empty,
                Username = builder.Username ?? string.Empty,
                Password = string.IsNullOrEmpty(builder.Password) ? string.Empty : "••••••••••••",
                SslMode = builder.SslMode.ToString(),
                Pooling = builder.Pooling
            };
        }
        catch
        {
            return new DatabaseConfigDto
            {
                ConnectionString = MaskPasswordInConnectionString(raw)
            };
        }
    }

    private string MaskPasswordInConnectionString(string connStr)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(
            connStr,
            @"(Password=)[^;]+",
            "$1••••••••••••",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
    }

    #endregion
}
