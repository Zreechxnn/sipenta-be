using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class LocalStorageProvider : ICloudStorageService
{
    private readonly string _basePath;
    private readonly string _documentFolder;
    private readonly string _imageFolder;
    private readonly ILogger<LocalStorageProvider> _logger;

    public string ProviderName => "LocalStorage";

    public LocalStorageProvider(
        ILogger<LocalStorageProvider> logger, 
        string basePath = "Uploads/Storage",
        string documentFolder = "Documents",
        string imageFolder = "Images")
    {
        _logger = logger;
        _basePath = string.IsNullOrWhiteSpace(basePath) 
            ? Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "Storage")
            : (Path.IsPathRooted(basePath) ? basePath : Path.Combine(Directory.GetCurrentDirectory(), basePath));

        _documentFolder = string.IsNullOrWhiteSpace(documentFolder) ? "Documents" : documentFolder.Trim();
        _imageFolder = string.IsNullOrWhiteSpace(imageFolder) ? "Images" : imageFolder.Trim();

        EnsureDirectoryExists(Path.Combine(_basePath, _documentFolder));
        EnsureDirectoryExists(Path.Combine(_basePath, _imageFolder));
    }

    private void EnsureDirectoryExists(string dirPath)
    {
        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }
    }

    public async Task<string> UploadFileAsync(IFormFile file, string fileName)
    {
        var sanitized = Path.GetFileName(fileName);
        var uniqueName = $"{Guid.NewGuid():N}_{sanitized}";
        var targetDir = Path.Combine(_basePath, _documentFolder);
        EnsureDirectoryExists(targetDir);
        var targetPath = Path.Combine(targetDir, uniqueName);

        using (var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream);
        }

        _logger.LogInformation("File saved to LocalStorage: {DocumentFolder}/{UniqueName}", _documentFolder, uniqueName);
        return $"local:{_documentFolder}/{uniqueName}";
    }

    public async Task<string> UploadFileBytesAsync(byte[] fileBytes, string fileName, string contentType, string? folderOrPrefix = null)
    {
        var sanitized = Path.GetFileName(fileName);
        var uniqueName = $"{Guid.NewGuid():N}_{sanitized}";
        var subFolder = !string.IsNullOrWhiteSpace(folderOrPrefix) ? folderOrPrefix : _imageFolder;
        var targetDir = Path.Combine(_basePath, subFolder);
        EnsureDirectoryExists(targetDir);
        var targetPath = Path.Combine(targetDir, uniqueName);

        await File.WriteAllBytesAsync(targetPath, fileBytes);

        _logger.LogInformation("Bytes saved to LocalStorage: {SubFolder}/{UniqueName}", subFolder, uniqueName);
        return $"local:{subFolder}/{uniqueName}";
    }

    public Task DeleteFileAsync(string fileIdOrPath)
    {
        var relative = fileIdOrPath.StartsWith("local:", StringComparison.OrdinalIgnoreCase) 
            ? fileIdOrPath.Substring("local:".Length) 
            : fileIdOrPath;

        var fullPath = Path.Combine(_basePath, relative.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Deleted file from LocalStorage: {Path}", fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<Stream> DownloadFileAsync(string fileIdOrPath)
    {
        var relative = fileIdOrPath.StartsWith("local:", StringComparison.OrdinalIgnoreCase) 
            ? fileIdOrPath.Substring("local:".Length) 
            : fileIdOrPath;

        var fullPath = Path.Combine(_basePath, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Berkas lokal tidak ditemukan: {fileIdOrPath}");
        }

        var memory = new MemoryStream();
        using (var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fileStream.CopyTo(memory);
        }
        memory.Position = 0;
        return Task.FromResult<Stream>(memory);
    }

    public Task<bool> TestConnectionAsync()
    {
        try
        {
            var testDir = Path.Combine(_basePath, "Test");
            EnsureDirectoryExists(testDir);
            var testFile = Path.Combine(testDir, $"test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "SIAP Storage Test Ping");
            var readBack = File.ReadAllText(testFile);
            File.Delete(testFile);
            return Task.FromResult(readBack == "SIAP Storage Test Ping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LocalStorage test failed.");
            return Task.FromResult(false);
        }
    }
}
