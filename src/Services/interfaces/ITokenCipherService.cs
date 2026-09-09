namespace SIAP.Api.Services.Interfaces;

/// <summary>
/// Layanan cipher kriptografi untuk mengenkripsi dan mendekripsi token refresh/sesi
/// menggunakan algoritma AES-256 sebelum disimpan ke database, sehingga token asli tidak terekspos.
/// </summary>
public interface ITokenCipherService
{
    /// <summary>
    /// Menghasilkan one-way hash SHA-256 dari token refresh untuk penyimpanan aman di database (CWE-916).
    /// </summary>
    /// <param name="plainToken">Token mentah asli.</param>
    /// <returns>String hash dengan prefiks HASH_.</returns>
    string HashToken(string plainToken);

    /// <summary>
    /// Enkripsi token mentah (plaintext) menggunakan algoritma AES-256 menjadi ciphertext yang aman disimpan di database.
    /// </summary>
    /// <param name="plainToken">Token mentah asli.</param>
    /// <returns>String ciphertext dengan prefiks ENC_.</returns>
    string Encrypt(string plainToken);

    /// <summary>
    /// Dekripsi ciphertext dari database kembali menjadi token mentah menggunakan algoritma AES-256.
    /// </summary>
    /// <param name="cipherToken">Ciphertext token yang tersimpan di DB.</param>
    /// <returns>Token mentah asli.</returns>
    string Decrypt(string cipherToken);
}
