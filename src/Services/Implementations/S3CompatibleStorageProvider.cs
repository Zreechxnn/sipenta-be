using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class S3CompatibleStorageProvider : ICloudStorageService, IDisposable
{
    private readonly ILogger<S3CompatibleStorageProvider> _logger;
    private readonly string _endpoint;
    private readonly string _bucket;
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _region;
    private readonly string _documentPrefix;
    private readonly string _imagePrefix;
    private readonly IAmazonS3 _s3Client;
    private bool _disposed;

    public string ProviderName => "S3Compatible (MinIO / AWS S3 / Cloudflare R2 / PDN)";

    public S3CompatibleStorageProvider(
        HttpClient? httpClient,
        ILogger<S3CompatibleStorageProvider> logger,
        string endpoint = "",
        string bucket = "",
        string accessKey = "",
        string secretKey = "",
        string documentPrefix = "documents",
        string imagePrefix = "images",
        string region = "us-east-1")
    {
        _logger = logger;
        _endpoint = (endpoint ?? string.Empty).Trim().TrimEnd('/');
        _bucket = (bucket ?? string.Empty).Trim();
        _accessKey = (accessKey ?? string.Empty).Trim();
        _secretKey = (secretKey ?? string.Empty).Trim();
        _region = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region.Trim();
        _documentPrefix = string.IsNullOrWhiteSpace(documentPrefix) ? "documents" : documentPrefix.Trim().Trim('/');
        _imagePrefix = string.IsNullOrWhiteSpace(imagePrefix) ? "images" : imagePrefix.Trim().Trim('/');

        _s3Client = CreateS3Client();
    }

    private IAmazonS3 CreateS3Client()
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = true, // Wajib untuk MinIO, Ceph, dan custom S3 storage
            UseHttp = _endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        };

        if (!string.IsNullOrWhiteSpace(_endpoint))
        {
            config.ServiceURL = _endpoint;
        }

        if (!string.IsNullOrWhiteSpace(_region))
        {
            config.AuthenticationRegion = _region;
        }

        AWSCredentials credentials;
        if (!string.IsNullOrWhiteSpace(_accessKey) && !string.IsNullOrWhiteSpace(_secretKey))
        {
            credentials = new BasicAWSCredentials(_accessKey, _secretKey);
        }
        else
        {
            credentials = new AnonymousAWSCredentials();
        }

        return new AmazonS3Client(credentials, config);
    }

    public async Task<string> UploadFileAsync(IFormFile file, string fileName)
    {
        if (string.IsNullOrWhiteSpace(_bucket))
        {
            throw new InvalidOperationException("Nama bucket S3 belum dikonfigurasi.");
        }

        var sanitized = Path.GetFileName(fileName);
        var objectKey = $"{_documentPrefix}/{Guid.NewGuid():N}_{sanitized}";

        using var stream = file.OpenReadStream();
        var putRequest = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            InputStream = stream,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType
        };

        var response = await _s3Client.PutObjectAsync(putRequest);
        if (response.HttpStatusCode != System.Net.HttpStatusCode.OK && 
            response.HttpStatusCode != System.Net.HttpStatusCode.Created && 
            response.HttpStatusCode != System.Net.HttpStatusCode.NoContent)
        {
            _logger.LogError("S3 PutObject gagal dengan HTTP status {StatusCode}", response.HttpStatusCode);
            throw new InvalidOperationException($"Upload ke S3 Storage gagal dengan status: {response.HttpStatusCode}");
        }

        _logger.LogInformation("File berhasil diupload ke S3 Compatible: {Bucket}/{ObjectKey}", _bucket, objectKey);
        return $"s3:{_bucket}/{objectKey}";
    }

    public async Task<string> UploadFileBytesAsync(byte[] fileBytes, string fileName, string contentType, string? folderOrPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(_bucket))
        {
            throw new InvalidOperationException("Nama bucket S3 belum dikonfigurasi.");
        }

        var sanitized = Path.GetFileName(fileName);
        var prefix = !string.IsNullOrWhiteSpace(folderOrPrefix) ? folderOrPrefix.Trim('/') : _imagePrefix;
        var objectKey = $"{prefix}/{Guid.NewGuid():N}_{sanitized}";

        using var stream = new MemoryStream(fileBytes);
        var putRequest = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            InputStream = stream,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        };

        var response = await _s3Client.PutObjectAsync(putRequest);
        if (response.HttpStatusCode != System.Net.HttpStatusCode.OK && 
            response.HttpStatusCode != System.Net.HttpStatusCode.Created && 
            response.HttpStatusCode != System.Net.HttpStatusCode.NoContent)
        {
            _logger.LogError("S3 PutObject bytes gagal dengan HTTP status {StatusCode}", response.HttpStatusCode);
            throw new InvalidOperationException($"Upload bytes ke S3 Storage gagal dengan status: {response.HttpStatusCode}");
        }

        _logger.LogInformation("Bytes berhasil diupload ke S3 Compatible: {Bucket}/{ObjectKey}", _bucket, objectKey);
        return $"s3:{_bucket}/{objectKey}";
    }

    public async Task DeleteFileAsync(string fileIdOrPath)
    {
        var objectKey = ExtractObjectKey(fileIdOrPath);
        var deleteRequest = new DeleteObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey
        };

        try
        {
            await _s3Client.DeleteObjectAsync(deleteRequest);
            _logger.LogInformation("File S3 terhapus: {Bucket}/{ObjectKey}", _bucket, objectKey);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Berkas S3 tidak ditemukan saat hendak dihapus: {Key}", objectKey);
        }
    }

    public async Task<Stream> DownloadFileAsync(string fileIdOrPath)
    {
        var objectKey = ExtractObjectKey(fileIdOrPath);
        var getRequest = new GetObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey
        };

        try
        {
            var response = await _s3Client.GetObjectAsync(getRequest);
            var memory = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memory);
            memory.Position = 0;
            return memory;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "Gagal mengunduh berkas S3 {Bucket}/{Key}: {Message}", _bucket, objectKey, ex.Message);
            throw new InvalidOperationException($"Gagal mengunduh berkas dari S3 ({ex.StatusCode}): {ex.Message}", ex);
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_endpoint) || string.IsNullOrWhiteSpace(_bucket))
            {
                return false;
            }

            var listReq = new ListObjectsV2Request
            {
                BucketName = _bucket,
                MaxKeys = 1
            };

            var response = await _s3Client.ListObjectsV2Async(listReq);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Uji koneksi S3 Compatible gagal: {Message}", ex.Message);
            return false;
        }
    }

    private string ExtractObjectKey(string fileIdOrPath)
    {
        var clean = fileIdOrPath;
        if (clean.StartsWith("s3:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("s3:".Length);
        }
        else if (clean.StartsWith("openstack:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("openstack:".Length);
        }
        else if (clean.StartsWith("minio:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("minio:".Length);
        }
        else if (clean.StartsWith("pdn:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("pdn:".Length);
        }
        else if (clean.StartsWith("pdn_s3:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring("pdn_s3:".Length);
        }

        // Jika diawali nama bucket, potong
        if (!string.IsNullOrWhiteSpace(_bucket) && clean.StartsWith($"{_bucket}/", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(_bucket.Length + 1);
        }

        return clean;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _s3Client.Dispose();
            _disposed = true;
        }
    }
}
