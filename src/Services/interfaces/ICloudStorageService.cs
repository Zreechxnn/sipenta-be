using Microsoft.AspNetCore.Http;

namespace SIAP.Api.Services.Interfaces;

public interface ICloudStorageService
{
    string ProviderName { get; }
    Task<string> UploadFileAsync(IFormFile file, string fileName);
    Task<string> UploadFileBytesAsync(byte[] fileBytes, string fileName, string contentType, string? folderOrPrefix = null);
    Task DeleteFileAsync(string fileIdOrPath);
    Task<Stream> DownloadFileAsync(string fileIdOrPath);
    Task<bool> TestConnectionAsync();
}
