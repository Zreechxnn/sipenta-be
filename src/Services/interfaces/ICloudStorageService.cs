using Microsoft.AspNetCore.Http;

namespace SIPENTA.Api.Services.Interfaces;

public interface ICloudStorageService
{
    string ProviderName { get; }
    Task<string> UploadFileAsync(IFormFile file, string fileName);
    Task<string> UploadFileBytesAsync(byte[] fileBytes, string fileName, string contentType, string? folderOrPrefix = null);
    Task<string> EnsureSubfolderAsync(string subfolderName);
    Task DeleteFileAsync(string fileIdOrPath);
    Task<Stream> DownloadFileAsync(string fileIdOrPath);
    Task<bool> TestConnectionAsync();
}
