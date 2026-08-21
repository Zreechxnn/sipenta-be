using Microsoft.AspNetCore.Http;

namespace SIAP.Api.Services.Interfaces;

public interface IGoogleDriveService
{
    Task<string> UploadFileAsync(IFormFile file, string fileName);
    Task<string> UploadFileBytesAsync(byte[] fileBytes, string fileName, string contentType, string folderId = null);
    Task DeleteFileAsync(string fileId);
    Task<Stream> DownloadFileAsync(string fileId);
}
