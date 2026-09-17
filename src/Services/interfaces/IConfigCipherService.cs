namespace SIAP.Api.Services.Interfaces;

public interface IConfigCipherService
{
    string Encrypt(string? plainText);
    string Decrypt(string? cipherText);
    bool IsEncrypted(string? value);
}
