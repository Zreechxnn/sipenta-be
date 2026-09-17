using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

/// <summary>
/// Layanan enkripsi data at rest untuk nilai sensitif konfigurasi sistem di database
/// menggunakan standar industri AES-256-GCM (Authenticated Encryption with Associated Data).
/// Melindungi kredensial DB, Secret Keys Cloud Storage, dan LLM API Keys dari dump database fisik.
/// </summary>
public class ConfigCipherService : IConfigCipherService
{
    private const string Prefix = "enc:v1:";
    private readonly byte[] _keyBytes;
    private readonly List<byte[]> _candidateKeyBytes = new();
    private readonly ILogger<ConfigCipherService> _logger;

    public ConfigCipherService(
        IConfiguration config, 
        IOptions<JwtOptions> jwtOptions,
        ILogger<ConfigCipherService> logger)
    {
        _logger = logger;

        var candidateSecrets = new List<string?>
        {
            config["CONFIG_MASTER_KEY"],
            config["JwtOptions__TokenCipherKey"],
            config["JwtOptions:TokenCipherKey"],
            jwtOptions.Value?.TokenCipherKey,
            config["JwtOptions__Key"],
            config["JwtOptions:Key"],
            config["Jwt__Key"],
            config["Jwt:Key"],
            config["JWT_SECRET"],
            config["JWT_KEY"],
            jwtOptions.Value?.Key,
            "siap_aes256_super_secure_token_cipher_secret_key_rechan_2026",
            "this_is_a_very_long_secret_key_for_jwt_authentication_in_siap_app",
            "siap_secure_default_config_master_key_fallback_2026"
        };

        var uniqueSecrets = candidateSecrets
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Distinct()
            .ToList();

        var primarySecret = uniqueSecrets.FirstOrDefault() ?? "siap_secure_default_config_master_key_fallback_2026";
        _keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(primarySecret));

        foreach (var sec in uniqueSecrets)
        {
            _candidateKeyBytes.Add(SHA256.HashData(Encoding.UTF8.GetBytes(sec)));
        }
    }

    public bool IsEncrypted(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.StartsWith(Prefix);
    }

    public string Encrypt(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        if (IsEncrypted(plainText))
        {
            return plainText; // Mencegah enkripsi ganda
        }

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var nonce = new byte[12]; // 96-bit nonce untuk AES-GCM
            RandomNumberGenerator.Fill(nonce);

            var cipherBytes = new byte[plainBytes.Length];
            var tag = new byte[16]; // 128-bit authentication tag

            using var aesGcm = new AesGcm(_keyBytes, 16);
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

            return $"{Prefix}{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(cipherBytes)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal mengenkripsi nilai konfigurasi sistem.");
            throw new InvalidOperationException("Gagal mengenkripsi data konfigurasi.", ex);
        }
    }

    public string Decrypt(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return string.Empty;
        }

        // Backward compatibility: jika belum terenkripsi (misal string plain lama di DB / .env), kembalikan langsung
        if (!IsEncrypted(cipherText))
        {
            return cipherText;
        }

        try
        {
            var raw = cipherText.Substring(Prefix.Length);
            var parts = raw.Split(':');
            if (parts.Length != 3)
            {
                _logger.LogWarning("Format ciphertext konfigurasi tidak valid: {CipherText}", cipherText);
                return cipherText;
            }

            var nonce = Convert.FromBase64String(parts[0]);
            var tag = Convert.FromBase64String(parts[1]);
            var cipherBytes = Convert.FromBase64String(parts[2]);

            var plainBytes = new byte[cipherBytes.Length];

            // 1. Coba dengan primary active key
            try
            {
                using var aesGcm = new AesGcm(_keyBytes, 16);
                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException)
            {
                // 2. Coba fallback kandidat kunci yang lain jika kunci utama berbeda environment
                foreach (var candidateKey in _candidateKeyBytes)
                {
                    if (candidateKey.SequenceEqual(_keyBytes)) continue;

                    try
                    {
                        using var candidateGcm = new AesGcm(candidateKey, 16);
                        candidateGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
                        _logger.LogInformation("Ciphertext konfigurasi berhasil didekripsi menggunakan candidate fallback key.");
                        return Encoding.UTF8.GetString(plainBytes);
                    }
                    catch (CryptographicException)
                    {
                        // Lanjut ke kandidat berikutnya
                    }
                }

                _logger.LogError("Integritas atau kunci dekripsi konfigurasi tidak valid untuk semua kandidat kunci.");
                throw new InvalidOperationException("Gagal mendekripsi nilai konfigurasi (Authentication tag mismatch).");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Format data konfigurasi terenkripsi rusak.");
            return cipherText;
        }
    }
}
