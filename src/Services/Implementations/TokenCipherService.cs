using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

/// <summary>
/// Implementasi AES-256 Cipher untuk enkripsi deterministik token refresh yang disimpan di database.
/// Menggunakan Synthetic IV (HMAC-SHA256) agar query pencarian index database tetap efisien (O(1))
/// sekaligus menyediakan verifikasi integritas data anti-tamper.
/// </summary>
public class TokenCipherService : ITokenCipherService
{
    private readonly byte[] _keyBytes;

    public TokenCipherService(IOptions<JwtOptions> jwtOptions)
    {
        var secret = !string.IsNullOrWhiteSpace(jwtOptions.Value.TokenCipherKey)
            ? jwtOptions.Value.TokenCipherKey
            : jwtOptions.Value.Key;

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Kunci cipher token (TokenCipherKey / Key) belum disetel pada konfigurasi!");
        }

        // Turunkan kunci 256-bit (32 bytes) menggunakan SHA-256
        _keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string HashToken(string plainToken)
    {
        if (string.IsNullOrWhiteSpace(plainToken)) return string.Empty;
        if (plainToken.StartsWith("HASH_")) return plainToken;

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainToken));
        return "HASH_" + Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public string Encrypt(string plainToken)
    {
        if (string.IsNullOrWhiteSpace(plainToken)) return plainToken;
        if (plainToken.StartsWith("ENC_")) return plainToken; // Mencegah enkripsi ganda

        var plainBytes = Encoding.UTF8.GetBytes(plainToken);

        // Turunkan IV 16-byte deterministik dari plaintext dan secret key (Synthetic IV)
        var hmac = HMACSHA256.HashData(_keyBytes, plainBytes);
        var iv = new byte[16];
        Buffer.BlockCopy(hmac, 0, iv, 0, 16);

        using var aes = Aes.Create();
        aes.Key = _keyBytes;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Gabungkan IV (16 byte) + Ciphertext
        var combined = new byte[iv.Length + cipherBytes.Length];
        Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, iv.Length, cipherBytes.Length);

        return "ENC_" + Convert.ToBase64String(combined)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    public string Decrypt(string cipherToken)
    {
        if (string.IsNullOrWhiteSpace(cipherToken)) return cipherToken;
        if (!cipherToken.StartsWith("ENC_")) return cipherToken; // Dukungan backward compatibility token lama

        var rawBase64Url = cipherToken.Substring(4);
        var paddedBase64 = rawBase64Url.Replace("-", "+").Replace("_", "/");
        switch (paddedBase64.Length % 4)
        {
            case 2: paddedBase64 += "=="; break;
            case 3: paddedBase64 += "="; break;
        }

        var combined = Convert.FromBase64String(paddedBase64);
        if (combined.Length < 17)
        {
            throw new CryptographicException("Ciphertext token tidak valid (ukuran data terlalu kecil).");
        }

        var iv = new byte[16];
        Buffer.BlockCopy(combined, 0, iv, 0, 16);

        var cipherLength = combined.Length - 16;
        var cipherBytes = new byte[cipherLength];
        Buffer.BlockCopy(combined, 16, cipherBytes, 0, cipherLength);

        using var aes = Aes.Create();
        aes.Key = _keyBytes;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        var plainToken = Encoding.UTF8.GetString(decryptedBytes);

        // Verifikasi integritas: pastikan IV cocok dengan HMAC dari token hasil dekripsi
        var expectedHmac = HMACSHA256.HashData(_keyBytes, Encoding.UTF8.GetBytes(plainToken));
        for (int i = 0; i < 16; i++)
        {
            if (iv[i] != expectedHmac[i])
            {
                throw new CryptographicException("Integritas data token tidak valid (HMAC verification failed).");
            }
        }

        return plainToken;
    }
}
