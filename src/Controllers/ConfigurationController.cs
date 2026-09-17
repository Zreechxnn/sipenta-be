using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIAP.Api.DTOs;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "admin")]
public class ConfigurationController : ControllerBase
{
    private readonly ISystemConfigService _configService;
    private readonly ISudoElevationService _sudoService;
    private readonly IConfigCipherService _cipherService;
    private readonly ILogger<ConfigurationController> _logger;

    public ConfigurationController(
        ISystemConfigService configService, 
        ISudoElevationService sudoService,
        IConfigCipherService cipherService,
        ILogger<ConfigurationController> logger)
    {
        _configService = configService;
        _sudoService = sudoService;
        _cipherService = cipherService;
        _logger = logger;
    }

    private Guid? GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(idStr, out var id) ? id : null;
    }

    private IActionResult? CheckSudoElevation()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "Sesi pengguna tidak valid." });
        }

        var sudoToken = Request.Headers["X-Sudo-Token"].FirstOrDefault();
        if (!_sudoService.ValidateSudoToken(userId.Value, sudoToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "REQUIRE_SUDO_ELEVATION",
                message = "Area pengaturan sistem dilindungi. Harap masukkan kata sandi akun Anda (Sudo Mode) untuk melanjutkan."
            });
        }

        return null;
    }

    [HttpPost("sudo-elevate")]
    public async Task<IActionResult> SudoElevate([FromBody] SudoElevateRequestDto request)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "Identitas pengguna tidak valid." });
        }

        var (success, sudoToken, expiresAt, message) = await _sudoService.ElevateAsync(userId.Value, request.Password);
        if (!success)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new { message });
        }

        return Ok(new SudoElevateResponseDto
        {
            Success = true,
            SudoToken = sudoToken,
            ExpiresInSeconds = (int)Math.Max(0, (expiresAt - DateTime.UtcNow).TotalSeconds),
            ElevatedUntil = expiresAt,
            Message = message
        });
    }

    [HttpGet("sudo-status")]
    public IActionResult GetSudoStatus()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return Ok(new { elevated = false, expiresInSeconds = 0, elevatedUntil = (DateTime?)null });
        }

        var sudoToken = Request.Headers["X-Sudo-Token"].FirstOrDefault();
        var (isElevated, remaining, expiresAt) = _sudoService.CheckElevation(userId.Value, sudoToken);
        return Ok(new
        {
            elevated = isElevated,
            expiresInSeconds = remaining,
            elevatedUntil = expiresAt
        });
    }

    [HttpPost("sudo-lock")]
    public IActionResult SudoLock()
    {
        var sudoToken = Request.Headers["X-Sudo-Token"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sudoToken))
        {
            _sudoService.RevokeSudoToken(sudoToken);
        }
        return Ok(new { success = true, message = "Sesi Sudo Mode berhasil dikunci dan dicabut." });
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        var result = await _configService.GetOverviewAsync();
        return Ok(result);
    }

    [HttpGet("llm")]
    public async Task<IActionResult> GetLlmConfigs()
    {
        var sudo = CheckSudoElevation();
        if (sudo != null) return sudo;

        var result = await _configService.GetLlmConfigsAsync();

        // Ensure all API keys sent to the frontend are strictly AES-256-GCM ciphertext
        var secureList = new LlmConfigListDto
        {
            Endpoints = result.Endpoints.Select(e => new LlmEndpointConfigDto
            {
                Id = e.Id,
                Name = e.Name,
                Provider = e.Provider,
                ApiKey = _cipherService.IsEncrypted(e.ApiKey) ? e.ApiKey : _cipherService.Encrypt(e.ApiKey),
                BaseUrl = e.BaseUrl,
                Model = e.Model,
                ImageModel = e.ImageModel,
                IsActive = e.IsActive,
                Priority = e.Priority
            }).ToList()
        };

        return Ok(secureList);
    }

    [HttpPost("llm")]
    public async Task<IActionResult> SaveLlmConfigs([FromBody] LlmConfigListDto config)
    {
        var sudo = CheckSudoElevation();
        if (sudo != null) return sudo;

        if (config == null || config.Endpoints == null)
        {
            return BadRequest(new { message = "Data konfigurasi LLM tidak valid." });
        }

        // If client sends back encrypted keys (unaltered), decrypt them for internal store to re-encrypt cleanly
        foreach (var endpoint in config.Endpoints)
        {
            if (_cipherService.IsEncrypted(endpoint.ApiKey))
            {
                endpoint.ApiKey = _cipherService.Decrypt(endpoint.ApiKey);
            }
        }

        var username = User.Identity?.Name ?? "admin";
        await _configService.SaveLlmConfigsAsync(config, username);
        return Ok(new { message = "Konfigurasi LLM API Key berhasil disimpan." });
    }

    [HttpPost("llm/test")]
    public async Task<IActionResult> TestLlmEndpoint([FromBody] LlmTestRequestDto request)
    {
        var sudo = CheckSudoElevation();
        if (sudo != null) return sudo;

        if (request == null || string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequest(new { message = "API Key diperlukan untuk melakukan pengujian." });
        }

        // If client passes encrypted key, decrypt it before executing external HTTP test
        if (_cipherService.IsEncrypted(request.ApiKey))
        {
            request.ApiKey = _cipherService.Decrypt(request.ApiKey);
        }

        var result = await _configService.TestLlmEndpointAsync(request);
        return Ok(result);
    }

    [HttpGet("storage")]
    public async Task<IActionResult> GetStorageConfig()
    {
        var sudo = CheckSudoElevation();
        if (sudo != null) return sudo;

        var result = await _configService.GetStorageConfigAsync();

        // Always encrypt secrets before transmitting over HTTP to frontend
        var secureStorage = new StorageConfigDto
        {
            ActiveProvider = result.ActiveProvider,
            GoogleDrive = new GoogleDriveSettingsDto
            {
                TokenJson = !string.IsNullOrWhiteSpace(result.GoogleDrive?.TokenJson)
                    ? (_cipherService.IsEncrypted(result.GoogleDrive.TokenJson) ? result.GoogleDrive.TokenJson : _cipherService.Encrypt(result.GoogleDrive.TokenJson))
                    : "",
                FolderId = result.GoogleDrive?.FolderId ?? "",
                FolderImageId = result.GoogleDrive?.FolderImageId ?? "",
                ClientId = result.GoogleDrive?.ClientId ?? "",
                ClientSecret = !string.IsNullOrWhiteSpace(result.GoogleDrive?.ClientSecret)
                    ? (_cipherService.IsEncrypted(result.GoogleDrive.ClientSecret) ? result.GoogleDrive.ClientSecret : _cipherService.Encrypt(result.GoogleDrive.ClientSecret))
                    : ""
            },
            LocalStorage = result.LocalStorage,
            Supabase = new SupabaseSettingsDto
            {
                ProjectUrl = result.Supabase?.ProjectUrl ?? "",
                ApiKey = !string.IsNullOrWhiteSpace(result.Supabase?.ApiKey)
                    ? (_cipherService.IsEncrypted(result.Supabase.ApiKey) ? result.Supabase.ApiKey : _cipherService.Encrypt(result.Supabase.ApiKey))
                    : "",
                BucketName = result.Supabase?.BucketName ?? "documents",
                DocumentFolder = result.Supabase?.DocumentFolder ?? "documents",
                ImageFolder = result.Supabase?.ImageFolder ?? "images"
            },
            S3Compatible = new S3SettingsDto
            {
                Endpoint = result.S3Compatible?.Endpoint ?? "",
                BucketName = result.S3Compatible?.BucketName ?? "",
                AccessKey = result.S3Compatible?.AccessKey ?? "",
                SecretKey = !string.IsNullOrWhiteSpace(result.S3Compatible?.SecretKey)
                    ? (_cipherService.IsEncrypted(result.S3Compatible.SecretKey) ? result.S3Compatible.SecretKey : _cipherService.Encrypt(result.S3Compatible.SecretKey))
                    : "",
                Region = result.S3Compatible?.Region ?? "",
                ProviderType = result.S3Compatible?.ProviderType ?? "",
                DocumentPrefix = result.S3Compatible?.DocumentPrefix ?? "documents",
                ImagePrefix = result.S3Compatible?.ImagePrefix ?? "images"
            },
            WebDav = new WebDavSettingsDto
            {
                ServerUrl = result.WebDav?.ServerUrl ?? "",
                Username = result.WebDav?.Username ?? "",
                Password = !string.IsNullOrWhiteSpace(result.WebDav?.Password)
                    ? (_cipherService.IsEncrypted(result.WebDav.Password) ? result.WebDav.Password : _cipherService.Encrypt(result.WebDav.Password))
                    : "",
                RemotePath = result.WebDav?.RemotePath ?? "",
                Preset = result.WebDav?.Preset ?? "Nextcloud",
                DocumentPath = result.WebDav?.DocumentPath ?? "documents",
                ImagePath = result.WebDav?.ImagePath ?? "images"
            }
        };

        return Ok(secureStorage);
    }

    [HttpPost("storage")]
    public async Task<IActionResult> SaveStorageConfig([FromBody] StorageConfigDto config)
    {
        var sudo = CheckSudoElevation();
        if (sudo != null) return sudo;

        if (config == null)
        {
            return BadRequest(new { message = "Data konfigurasi storage tidak valid." });
        }

        // Decrypt encrypted fields from client before saving
        if (config.GoogleDrive != null)
        {
            if (_cipherService.IsEncrypted(config.GoogleDrive.TokenJson))
                config.GoogleDrive.TokenJson = _cipherService.Decrypt(config.GoogleDrive.TokenJson);
            if (_cipherService.IsEncrypted(config.GoogleDrive.ClientSecret))
                config.GoogleDrive.ClientSecret = _cipherService.Decrypt(config.GoogleDrive.ClientSecret);
        }
        if (config.Supabase != null && _cipherService.IsEncrypted(config.Supabase.ApiKey))
        {
            config.Supabase.ApiKey = _cipherService.Decrypt(config.Supabase.ApiKey);
        }
        if (config.S3Compatible != null && _cipherService.IsEncrypted(config.S3Compatible.SecretKey))
        {
            config.S3Compatible.SecretKey = _cipherService.Decrypt(config.S3Compatible.SecretKey);
        }
        if (config.WebDav != null && _cipherService.IsEncrypted(config.WebDav.Password))
        {
            config.WebDav.Password = _cipherService.Decrypt(config.WebDav.Password);
        }

        var username = User.Identity?.Name ?? "admin";
        await _configService.SaveStorageConfigAsync(config, username);
        return Ok(new { message = "Konfigurasi Cloud Storage berhasil disimpan." });
    }

    [HttpPost("storage/test")]
    public async Task<IActionResult> TestStorage([FromBody] StorageTestRequestDto? request)
    {
        var sudo = CheckSudoElevation();
        if (sudo != null) return sudo;

        if (request?.Config != null)
        {
            if (request.Config.GoogleDrive != null)
            {
                if (_cipherService.IsEncrypted(request.Config.GoogleDrive.TokenJson))
                    request.Config.GoogleDrive.TokenJson = _cipherService.Decrypt(request.Config.GoogleDrive.TokenJson);
                if (_cipherService.IsEncrypted(request.Config.GoogleDrive.ClientSecret))
                    request.Config.GoogleDrive.ClientSecret = _cipherService.Decrypt(request.Config.GoogleDrive.ClientSecret);
            }
            if (request.Config.Supabase != null && _cipherService.IsEncrypted(request.Config.Supabase.ApiKey))
            {
                request.Config.Supabase.ApiKey = _cipherService.Decrypt(request.Config.Supabase.ApiKey);
            }
            if (request.Config.S3Compatible != null && _cipherService.IsEncrypted(request.Config.S3Compatible.SecretKey))
            {
                request.Config.S3Compatible.SecretKey = _cipherService.Decrypt(request.Config.S3Compatible.SecretKey);
            }
            if (request.Config.WebDav != null && _cipherService.IsEncrypted(request.Config.WebDav.Password))
            {
                request.Config.WebDav.Password = _cipherService.Decrypt(request.Config.WebDav.Password);
            }
        }

        var result = await _configService.TestStorageAsync(request?.Config);
        return Ok(result);
    }

    [HttpGet("database")]
    public async Task<IActionResult> GetDatabaseConfig()
    {
        var sudo = CheckSudoElevation();
        if (sudo != null) return sudo;

        var result = await _configService.GetDatabaseConfigAsync();

        if (!string.IsNullOrWhiteSpace(result.ConnectionString))
        {
            result.ConnectionString = _cipherService.IsEncrypted(result.ConnectionString)
                ? result.ConnectionString
                : _cipherService.Encrypt(result.ConnectionString);
        }
        result.Password = "•••••••••••• (Terenkripsi AES-256-GCM)";

        return Ok(result);
    }

    [HttpPost("database/test")]
    public async Task<IActionResult> TestDatabase([FromBody] DatabaseTestRequestDto request)
    {
        var sudo = CheckSudoElevation();
        if (sudo != null) return sudo;

        if (request == null)
        {
            return BadRequest(new { message = "Data pengujian database tidak valid." });
        }

        if (!string.IsNullOrWhiteSpace(request.ConnectionString) && _cipherService.IsEncrypted(request.ConnectionString))
        {
            request.ConnectionString = _cipherService.Decrypt(request.ConnectionString);
        }

        var result = await _configService.TestDatabaseAsync(request);
        return Ok(result);
    }

    [HttpPost("database")]
    public async Task<IActionResult> SaveDatabaseConfig([FromBody] DatabaseConfigDto request)
    {
        var sudo = CheckSudoElevation();
        if (sudo != null) return sudo;

        if (request == null)
        {
            return BadRequest(new { message = "Data konfigurasi database tidak valid." });
        }

        if (!string.IsNullOrWhiteSpace(request.ConnectionString) && _cipherService.IsEncrypted(request.ConnectionString))
        {
            request.ConnectionString = _cipherService.Decrypt(request.ConnectionString);
        }

        var username = User.Identity?.Name ?? "admin";
        await _configService.SaveDatabaseConfigAsync(request, username);
        return Ok(new
        {
            message = "Konfigurasi URL Database berhasil disimpan. Harap restart backend jika ingin menggunakan koneksi database baru.",
            requiresRestart = true
        });
    }

    [HttpPost("encrypt-all")]
    public async Task<IActionResult> EncryptAllConfigurations()
    {
        var sudo = CheckSudoElevation();
        if (sudo != null) return sudo;

        await _configService.EnsureAllConfigurationsEncryptedAsync();
        return Ok(new
        {
            success = true,
            message = "Seluruh konfigurasi sistem dan kredensial sensitif di tabel SystemSettings database telah diverifikasi dan dienkripsi dengan standar AES-256-GCM."
        });
    }
}
