using Microsoft.AspNetCore.Http;

namespace SIAP.Api.Services.Interfaces;

public interface IGoogleDriveService
{
    Task<string> UploadFileAsync(IFormFile file, string fileName);
    Task DeleteFileAsync(string fileId);
    Task<Stream> DownloadFileAsync(string fileId);
}
