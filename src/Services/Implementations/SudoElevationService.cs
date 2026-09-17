using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

/// <summary>
/// Layanan Step-up Authentication ("Sudo Mode") untuk memverifikasi ulang identitas admin
/// sebelum membuka atau mengubah konfigurasi sistem SIAP.
/// Menghasilkan token elevated sementara (15 menit) yang diverifikasi stateless menggunakan HMAC-SHA256.
/// </summary>
public class SudoElevationService : ISudoElevationService
{
    private readonly IUserRepository _userRepository;
    private readonly IMemoryCache _cache;
    private readonly byte[] _hmacKey;
    private readonly ILogger<SudoElevationService> _logger;
    private const int ExpirationMinutes = 15;

    public SudoElevationService(
        IUserRepository userRepository,
        IMemoryCache cache,
        IOptions<JwtOptions> jwtOptions,
        ILogger<SudoElevationService> logger)
    {
        _userRepository = userRepository;
        _cache = cache;
        _logger = logger;

        var secret = (!string.IsNullOrWhiteSpace(jwtOptions.Value?.TokenCipherKey)
            ? jwtOptions.Value?.TokenCipherKey
            : jwtOptions.Value?.Key) ?? "siap_secure_sudo_elevation_secret_fallback_2026";

        _hmacKey = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public async Task<(bool Success, string SudoToken, DateTime ExpiresAt, string Message)> ElevateAsync(Guid userId, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, string.Empty, DateTime.MinValue, "Kata sandi wajib diisi.");
        }

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            return (false, string.Empty, DateTime.MinValue, "Pengguna tidak ditemukan.");
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("Percobaan sudo elevation gagal untuk user {UserId} ({Username}): Password salah.", user.Id, user.Username);
            return (false, string.Empty, DateTime.MinValue, "Kata sandi konfirmasi salah. Akses konfigurasi ditolak.");
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(ExpirationMinutes);
        var tokenPayload = $"{user.Id:D}:{expiresAt.Ticks}";
        var signatureBytes = HMACSHA256.HashData(_hmacKey, Encoding.UTF8.GetBytes(tokenPayload));
        var signature = Convert.ToHexString(signatureBytes).ToLowerInvariant();

        var sudoToken = $"sudo_{Convert.ToBase64String(Encoding.UTF8.GetBytes(tokenPayload))}.{signature}";
        
        _logger.LogInformation("User {Username} ({UserId}) berhasil mengaktifkan Sudo Mode konfigurasi hingga {ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC.", 
            user.Username, user.Id, expiresAt);

        return (true, sudoToken, expiresAt, "Otorisasi Sudo Mode berhasil. Akses konfigurasi sistem diberikan.");
    }

    public void RevokeSudoToken(string? sudoToken)
    {
        if (string.IsNullOrWhiteSpace(sudoToken)) return;
        var cacheKey = $"REVOKED_SUDO:{sudoToken}";
        _cache.Set(cacheKey, true, TimeSpan.FromMinutes(ExpirationMinutes));
        _logger.LogInformation("Sudo elevation token telah dicabut secara eksplisit di server.");
    }

    public bool ValidateSudoToken(Guid userId, string? sudoToken)
    {
        var (elevated, _, _) = CheckElevation(userId, sudoToken);
        return elevated;
    }

    public (bool Elevated, int SecondsRemaining, DateTime? ExpiresAt) CheckElevation(Guid userId, string? sudoToken)
    {
        if (string.IsNullOrWhiteSpace(sudoToken) || !sudoToken.StartsWith("sudo_"))
        {
            return (false, 0, null);
        }

        // Check if token was explicitly revoked
        var cacheKey = $"REVOKED_SUDO:{sudoToken}";
        if (_cache.TryGetValue(cacheKey, out _))
        {
            return (false, 0, null);
        }

        try
        {
            var raw = sudoToken.Substring(5);
            var dotIndex = raw.IndexOf('.');
            if (dotIndex <= 0) return (false, 0, null);

            var base64Payload = raw.Substring(0, dotIndex);
            var signature = raw.Substring(dotIndex + 1);

            var payloadBytes = Convert.FromBase64String(base64Payload);
            var expectedSigBytes = HMACSHA256.HashData(_hmacKey, payloadBytes);
            var expectedSig = Convert.ToHexString(expectedSigBytes).ToLowerInvariant();

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(signature), 
                    Encoding.UTF8.GetBytes(expectedSig)))
            {
                return (false, 0, null);
            }

            var payloadStr = Encoding.UTF8.GetString(payloadBytes);
            var parts = payloadStr.Split(':');
            if (parts.Length != 2) return (false, 0, null);

            if (!Guid.TryParse(parts[0], out var tokenUserId) || tokenUserId != userId)
            {
                return (false, 0, null);
            }

            if (!long.TryParse(parts[1], out var ticks))
            {
                return (false, 0, null);
            }

            var expiresAt = new DateTime(ticks, DateTimeKind.Utc);
            if (expiresAt <= DateTime.UtcNow)
            {
                return (false, 0, null);
            }

            var remaining = (int)Math.Max(0, (expiresAt - DateTime.UtcNow).TotalSeconds);
            return (true, remaining, expiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Validasi token sudo mengalami kesalahan format.");
            return (false, 0, null);
        }
    }
}
