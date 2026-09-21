namespace SIAP.Api.DTOs;

public class LlmEndpointConfigDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = "groq"; // groq, openai, custom
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
    public string Model { get; set; } = "openai/gpt-oss-120b";
    public string ImageModel { get; set; } = "qwen/qwen3.8-27b";
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 1;
}

public class LlmConfigListDto
{
    public List<LlmEndpointConfigDto> Endpoints { get; set; } = new();
}

public class LlmTestRequestDto
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
    public string Model { get; set; } = "openai/gpt-oss-120b";
    public string? TestPrompt { get; set; } = "Halo, uji koneksi sistem SIPENTA.";
}

public class LlmTestResponseDto
{
    public bool Success { get; set; }
    public long LatencyMs { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ResponseText { get; set; }
}

public class GoogleDriveSettingsDto
{
    public string TokenJson { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public string FolderImageId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public class LocalStorageSettingsDto
{
    public string BasePath { get; set; } = "Uploads/Storage";
    public string DocumentFolder { get; set; } = "Documents";
    public string ImageFolder { get; set; } = "Images";
}

public class S3SettingsDto
{
    public string Endpoint { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string? PublicUrlBase { get; set; }
    public string ProviderType { get; set; } = "S3Compatible"; // "S3Compatible", "OpenStackSwift", "PDN_ObjectStorage", "MinIO", "Supabase"
    public string? ProjectId { get; set; }
    public string DocumentPrefix { get; set; } = "documents";
    public string ImagePrefix { get; set; } = "images";
}

public class WebDavSettingsDto
{
    public string ServerUrl { get; set; } = string.Empty; // e.g. https://cloud.instansi.go.id/remote.php/dav/files/user/
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RemotePath { get; set; } = "sipenta";
    public string Preset { get; set; } = "Nextcloud"; // "Nextcloud", "ownCloud", "PDN", "Custom"
    public string DocumentPath { get; set; } = "documents";
    public string ImagePath { get; set; } = "images";
}

public class SudoElevateRequestDto
{
    public string Password { get; set; } = string.Empty;
}

public class SudoElevateResponseDto
{
    public bool Success { get; set; }
    public string SudoToken { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; } = 900;
    public DateTime ElevatedUntil { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SupabaseSettingsDto
{
    public string ProjectUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "documents";
    public string DocumentFolder { get; set; } = "documents";
    public string ImageFolder { get; set; } = "images";
}

public class StorageConfigDto
{
    public string ActiveProvider { get; set; } = "GoogleDrive"; // "GoogleDrive", "LocalStorage", "Supabase", "S3Compatible", "WebDav"
    public GoogleDriveSettingsDto GoogleDrive { get; set; } = new();
    public LocalStorageSettingsDto LocalStorage { get; set; } = new();
    public SupabaseSettingsDto Supabase { get; set; } = new();
    public S3SettingsDto S3Compatible { get; set; } = new();
    public WebDavSettingsDto WebDav { get; set; } = new();
}

public class StorageTestRequestDto
{
    public string? Provider { get; set; }
    public StorageConfigDto? Config { get; set; }
}

public class StorageTestResponseDto
{
    public bool Success { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public class DatabaseConfigDto
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? SslMode { get; set; }
    public bool? Pooling { get; set; }
}

public class DatabaseTestRequestDto
{
    public string? ConnectionString { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Database { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public class DatabaseTestResponseDto
{
    public bool Success { get; set; }
    public long LatencyMs { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PostgresVersion { get; set; }
    public int? TableCount { get; set; }
}

public class ConfigurationOverviewDto
{
    public int TotalLlmKeys { get; set; }
    public int ActiveLlmKeys { get; set; }
    public string ActiveStorageProvider { get; set; } = string.Empty;
    public string DatabaseHost { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public bool IsDatabaseConnected { get; set; }
}
