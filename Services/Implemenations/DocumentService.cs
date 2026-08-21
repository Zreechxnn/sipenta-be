using MapsterMapper;
using Microsoft.AspNetCore.Http;
using SIAP.Api.Common;
using SIAP.Api.DTOs.Documents;
using SIAP.Api.DTOs.Chunks;
using SIAP.Api.Entities;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Services.Interfaces;
using SIAP.Api.Services.Chunking.Interfaces;
using SIAP.Api.Services.Parsers;
using Microsoft.Extensions.Logging;

namespace SIAP.Api.Services.Implemenations;

public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IBidangRepository _bidangRepository;
    private readonly IMapper _mapper;
    private readonly IDocumentProcessingQueue _queue;
    private readonly ILogger<DocumentService> _logger;
    private readonly IGoogleDriveService _driveService;
    private readonly IChunkService _chunkService;
    private readonly string _uploadPath;
    private readonly IDocumentParserFactory _parserFactory;
    private readonly IGroqService _groqService;
    private readonly IPdfImageExtractor _pdfImageExtractor;
    private readonly SIAP.Api.Data.AppDbContext _dbContext;

    public DocumentService(
        IDocumentRepository repository,
        IUserRepository userRepository,
        IBidangRepository bidangRepository,
        IMapper mapper, 
        IDocumentProcessingQueue queue,
        ILogger<DocumentService> logger,
        IChunkService chunkService,
        IDocumentParserFactory parserFactory,
        IGroqService groqService,
        IGoogleDriveService driveService,
        IPdfImageExtractor pdfImageExtractor,
        SIAP.Api.Data.AppDbContext dbContext)
    {
        _repository = repository;
        _userRepository = userRepository;
        _bidangRepository = bidangRepository;
        _mapper = mapper;
        _queue = queue;
        _logger = logger;
        _chunkService = chunkService;
        _parserFactory = parserFactory;
        _groqService = groqService;
        _driveService = driveService;
        _pdfImageExtractor = pdfImageExtractor;
        _dbContext = dbContext;
        
        _uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "Documents");
        if (!Directory.Exists(_uploadPath))
        {
            Directory.CreateDirectory(_uploadPath);
        }
    }

    public async Task<List<DocumentResponseDto>> UploadAsync(DocumentCreateDto request)
    {
        var responses = new List<DocumentResponseDto>();

        // Resolve BidangId
        int? resolvedBidangId = null;
        if (request.BidangId.HasValue && request.BidangId.Value > 0)
        {
            var b = await _bidangRepository.GetByIdAsync(request.BidangId.Value);
            if (b != null) resolvedBidangId = b.Id;
        }
        else if (!string.IsNullOrWhiteSpace(request.Bidang))
        {
            var b = await _bidangRepository.GetByNamaAsync(request.Bidang);
            if (b != null) resolvedBidangId = b.Id;
        }
        else if (request.UserId.HasValue)
        {
            var uploader = await _userRepository.GetByIdAsync(request.UserId.Value);
            resolvedBidangId = uploader?.BidangId;
        }

        foreach (var file in request.Files)
        {
            if (file == null || file.Length == 0)
                continue;
                
            var originalFileName = Path.GetFileName(file.FileName);
            var extension = Path.GetExtension(originalFileName);
            var fileName = $"{Guid.NewGuid()}_{originalFileName}";
            
            string? driveFileId = null;
            try
            {
                driveFileId = await _driveService.UploadFileAsync(file, fileName);

                var document = new Document
                {
                    Id = Guid.NewGuid(),
                    NamaFile = originalFileName,
                    Nama = string.IsNullOrWhiteSpace(request.Nama) ? originalFileName : request.Nama,
                    UserId = request.UserId,
                    BidangId = resolvedBidangId,
                    NamaTenagaAhli = request.NamaTenagaAhli,
                    JenisDokumen = !string.IsNullOrWhiteSpace(request.JenisDokumen) ? request.JenisDokumen : DetectJenisDokumen(originalFileName),
                    PeriodeLaporan = request.PeriodeLaporan,
                    Path = driveFileId,
                    Ukuran = file.Length,
                    MimeType = file.ContentType,
                    TanggalUpload = DateTime.UtcNow,
                    Status = DocumentStatus.Uploaded,
                    Content = new DocumentContent
                    {
                        Id = Guid.NewGuid(),
                        CreatedAt = DateTime.UtcNow,
                        ParseStatus = ParseStatus.Pending
                    }
                };
                document.Content.DocumentId = document.Id;

                var result = await _repository.AddAsync(document);
                _logger.LogInformation("Document uploaded with ID: {DocumentId}", result.Id);
                
                await _queue.EnqueueAsync(result.Id);
                _logger.LogInformation("Document queued for processing: {DocumentId}", result.Id);

                var fresh = await _repository.GetByIdAsync(result.Id);
                var response = MapToResponseDto(fresh ?? result, request.UserId);
                responses.Add(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyelesaikan upload dokumen untuk berkas {FileName}. Membatalkan file di Google Drive.", originalFileName);
                if (!string.IsNullOrEmpty(driveFileId))
                {
                    try
                    {
                        await _driveService.DeleteFileAsync(driveFileId);
                        _logger.LogInformation("Berhasil membersihkan file Google Drive {DriveFileId} setelah kegagalan upload.", driveFileId);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Gagal membersihkan file Google Drive {DriveFileId}", driveFileId);
                    }
                }
                throw;
            }
        }
        
        return responses;
    }

    public async Task<DocumentResponseDto> UpdateAsync(Guid id, DocumentUpdateDto request)
    {
        var document = await _repository.GetByIdAsync(id);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        if (request.Nama != null) document.Nama = request.Nama;
        if (request.NamaTenagaAhli != null) document.NamaTenagaAhli = request.NamaTenagaAhli;
        if (request.JenisDokumen != null) document.JenisDokumen = request.JenisDokumen;
        if (request.PeriodeLaporan != null) document.PeriodeLaporan = request.PeriodeLaporan;

        if (request.BidangId.HasValue && request.BidangId.Value > 0)
        {
            var b = await _bidangRepository.GetByIdAsync(request.BidangId.Value);
            if (b != null) document.BidangId = b.Id;
        }
        else if (request.Bidang != null)
        {
            if (string.IsNullOrWhiteSpace(request.Bidang))
            {
                document.BidangId = null;
            }
            else
            {
                var b = await _bidangRepository.GetByNamaAsync(request.Bidang);
                if (b != null) document.BidangId = b.Id;
            }
        }
        
        await _repository.UpdateAsync(document);
        var fresh = await _repository.GetByIdAsync(id);
        return MapToResponseDto(fresh ?? document, document.UserId);
    }

    public async Task DeleteAsync(Guid id)
    {
        var document = await _repository.GetByIdAsync(id);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        await _repository.ExecuteInTransactionAsync(async () =>
        {
            await _repository.DeleteAsync(document);
            try {
                await _driveService.DeleteFileAsync(document.Path);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "Failed to delete file from Google Drive. It might have been deleted already.");
            }
        });
    }

    public async Task<DocumentResponseDto> GetByIdAsync(Guid id, Guid? currentUserId = null, bool isAdmin = false)
    {
        var document = await _repository.GetByIdAsync(id);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        return MapToResponseDto(document, currentUserId);
    }

    public async Task<DocumentStatusDto> GetStatusAsync(Guid id)
    {
        var document = await _repository.GetByIdAsync(id);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        return new DocumentStatusDto
        {
            DocumentStatus = document.Status,
            ParseStatus = document.Content?.ParseStatus ?? ParseStatus.Pending,
            ProcessingStartedAt = document.ProcessingStartedAt,
            ProcessingFinishedAt = document.ProcessingFinishedAt,
            ProcessingDuration = document.ProcessingDuration,
            ErrorMessage = document.ErrorMessage
        };
    }

    public async Task<OcrStatusDto> GetOcrStatusAsync(Guid id)
    {
        var document = await _repository.GetByIdAsync(id);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        return new OcrStatusDto
        {
            OcrStatus = document.Content?.OcrStatus ?? OcrStatus.Pending,
            OcrEngine = document.Content?.OcrEngine,
            OcrDuration = document.Content?.OcrDuration,
            OcrConfidence = document.Content?.OcrConfidence,
            IsScannedDocument = document.Content?.IsScannedDocument ?? false,
            HasTextLayer = document.Content?.HasTextLayer ?? false
        };
    }

    public async Task<PagedResponse<DocumentResponseDto>> GetAllAsync(DocumentSearchDto searchDto, Guid? currentUserId = null, int? userBidangId = null, bool isAdmin = false)
    {
        var (data, total) = await _repository.GetPagedAsync(
            searchDto.PageNumber,
            searchDto.PageSize,
            searchDto.Keyword,
            searchDto.NamaTenagaAhli,
            searchDto.JenisDokumen,
            searchDto.PeriodeLaporan,
            currentUserId,
            userBidangId,
            isAdmin,
            searchDto.BidangId,
            searchDto.Bidang);

        var dtoList = data.Select(x => MapToResponseDto(x, currentUserId)).ToList();

        return new PagedResponse<DocumentResponseDto>
        {
            Data = dtoList,
            TotalRecords = total,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize
        };
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        return await _repository.GetCategoriesAsync();
    }

    public async Task<(Stream FileStream, string ContentType, string FileName)> DownloadAsync(Guid id)
    {
        var document = await _repository.GetByIdAsync(id);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        var stream = await _driveService.DownloadFileAsync(document.Path);

        return (stream, document.MimeType, document.NamaFile);
    }

    public async Task<DocumentChunkListResponseDto> GetChunksAsync(Guid id)
    {
        var document = await _repository.GetByIdAsync(id);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        var chunks = await _repository.GetChunksByDocumentIdAsync(id);

        return new DocumentChunkListResponseDto
        {
            TotalChunks = chunks.Count,
            Chunks = chunks.Select(c => new DocumentChunkPreviewDto
            {
                Id = c.Id,
                ChunkIndex = c.ChunkIndex,
                ChunkType = c.ChunkType,
                CharacterCount = c.CharacterCount,
                WordCount = c.WordCount,
                EstimatedToken = c.EstimatedToken,
                Metadata = c.Metadata,
                Preview = CleanPreview(c.Content),
                Content = c.Content
            }).ToList()
        };
    }

    private string CleanPreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var clean = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\s+", " ");
        return clean.Length > 200 ? clean.Substring(0, 200) + "..." : clean;
    }

    public async Task<DocumentChunkResponseDto> GetChunkByIdAsync(Guid id, Guid chunkId)
    {
        var document = await _repository.GetByIdAsync(id);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        var chunk = await _repository.GetChunkByIdAsync(id, chunkId);
        if (chunk == null)
            throw new KeyNotFoundException("Chunk tidak ditemukan.");

        return new DocumentChunkResponseDto
        {
            Id = chunk.Id,
            ChunkIndex = chunk.ChunkIndex,
            ChunkType = chunk.ChunkType,
            CharacterCount = chunk.CharacterCount,
            WordCount = chunk.WordCount,
            EstimatedToken = chunk.EstimatedToken,
            Content = chunk.Content,
            Metadata = chunk.Metadata
        };
    }

    public async Task<DocumentChunkResponseDto> UpdateChunkAsync(Guid documentId, Guid chunkId, SIAP.Api.DTOs.Chunks.DocumentChunkUpdateDto request)
    {
        var document = await _repository.GetByIdAsync(documentId);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        var chunk = await _repository.GetChunkByIdAsync(documentId, chunkId);
        if (chunk == null)
            throw new KeyNotFoundException("Chunk tidak ditemukan.");

        chunk.Content = request.Content;
        chunk.CharacterCount = request.Content.Length;
        chunk.WordCount = request.Content.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        chunk.EstimatedToken = request.Content.Length / 4;

        await _repository.UpdateChunkAsync(chunk);

        return new DocumentChunkResponseDto
        {
            Id = chunk.Id,
            ChunkIndex = chunk.ChunkIndex,
            ChunkType = chunk.ChunkType,
            CharacterCount = chunk.CharacterCount,
            WordCount = chunk.WordCount,
            EstimatedToken = chunk.EstimatedToken,
            Content = chunk.Content,
            Metadata = chunk.Metadata
        };
    }

    public async Task<bool> RechunkAsync(Guid id, string? strategy = null)
    {
        var document = await _repository.GetByIdAsync(id);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        if (document.Status != DocumentStatus.Parsed && document.Status != DocumentStatus.Failed)
            throw new Exception("Dokumen masih dalam proses, tidak dapat direchunk saat ini.");

        try 
        {
            await _chunkService.ProcessChunksAsync(id, strategy);
            
            document.Status = DocumentStatus.Parsed;
            document.ErrorMessage = null;
            await _repository.UpdateAsync(document);
            
            return true;
        }
        catch(Exception ex)
        {
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = $"Rechunk failed: {ex.Message}";
            await _repository.UpdateAsync(document);
            throw;
        }
    }

    // Document Sharing Methods
    public async Task<List<DocumentAccessUserDto>> GetAccessesAsync(Guid documentId, Guid currentUserId, bool isAdmin)
    {
        var document = await _repository.GetByIdAsync(documentId);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        if (!isAdmin && document.UserId != currentUserId)
            throw new UnauthorizedAccessException("Hanya pemilik dokumen atau admin yang dapat melihat daftar hak akses.");

        var accesses = await _repository.GetDocumentAccessesAsync(documentId);
        return accesses.Select(a => MapAccess(a)).ToList();
    }

    public async Task<DocumentAccessUserDto> ShareAsync(Guid documentId, string username, Guid currentUserId, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username wajib diisi.");

        var document = await _repository.GetByIdAsync(documentId);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        if (!isAdmin && document.UserId != currentUserId)
            throw new UnauthorizedAccessException("Hanya pemilik dokumen atau admin yang dapat membagikan dokumen.");

        var cleanUsername = username.Trim().TrimStart('@');
        var targetUser = await _userRepository.GetByUsernameAsync(cleanUsername);
        if (targetUser == null)
            throw new KeyNotFoundException($"Pengguna dengan username '@{cleanUsername}' tidak ditemukan.");

        if (document.UserId == targetUser.Id)
            throw new InvalidOperationException("Pengguna ini adalah pemilik dokumen (sudah memiliki akses penuh).");

        var existing = await _repository.GetDocumentAccessAsync(documentId, targetUser.Id);
        if (existing != null)
            throw new InvalidOperationException($"Dokumen sudah dibagikan kepada @{targetUser.Username}.");

        var access = new DocumentAccess
        {
            DocumentId = documentId,
            UserId = targetUser.Id,
            SharedByUserId = currentUserId,
            AccessLevel = "read",
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddDocumentAccessAsync(access);

        return new DocumentAccessUserDto
        {
            Id = access.Id,
            UserId = targetUser.Id,
            Username = targetUser.Username,
            FullName = targetUser.FullName,
            Email = targetUser.Email,
            BidangId = targetUser.BidangId,
            Bidang = targetUser.Bidang?.Nama,
            AccessLevel = access.AccessLevel,
            CreatedAt = access.CreatedAt
        };
    }

    public async Task RevokeAccessAsync(Guid documentId, Guid targetUserId, Guid currentUserId, bool isAdmin)
    {
        var document = await _repository.GetByIdAsync(documentId);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        if (!isAdmin && document.UserId != currentUserId)
            throw new UnauthorizedAccessException("Hanya pemilik dokumen atau admin yang dapat mencabut akses.");

        var existing = await _repository.GetDocumentAccessAsync(documentId, targetUserId);
        if (existing == null)
            throw new KeyNotFoundException("Hak akses untuk pengguna tersebut tidak ditemukan.");

        await _repository.RemoveDocumentAccessAsync(existing);
    }

    private DocumentResponseDto MapToResponseDto(Document doc, Guid? currentUserId)
    {
        var dto = _mapper.Map<DocumentResponseDto>(doc);
        dto.PreviewText = SnippetHelper.GetSnippet(doc.Content?.RawText, null);
        dto.BidangId = doc.BidangId;
        dto.Bidang = doc.Bidang?.Nama;
        dto.BidangKode = doc.Bidang?.Kode;
        dto.UploaderUsername = doc.User?.Username;
        dto.UploaderFullName = doc.User?.FullName;
        dto.IsOwner = currentUserId.HasValue && doc.UserId == currentUserId.Value;
        dto.IsSharedWithMe = currentUserId.HasValue && doc.Accesses != null && doc.Accesses.Any(a => a.UserId == currentUserId.Value);
        dto.SharedWith = doc.Accesses?.Select(a => MapAccess(a)).ToList();
        return dto;
    }

    private static DocumentAccessUserDto MapAccess(DocumentAccess a)
    {
        return new DocumentAccessUserDto
        {
            Id = a.Id,
            UserId = a.UserId,
            Username = a.User?.Username ?? "",
            FullName = a.User?.FullName,
            Email = a.User?.Email,
            BidangId = a.User?.BidangId,
            Bidang = a.User?.Bidang?.Nama,
            AccessLevel = a.AccessLevel,
            CreatedAt = a.CreatedAt
        };
    }

    private static string DetectJenisDokumen(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("bulanan")) return "Laporan Bulanan";
        if (lower.Contains("akhir") || lower.Contains("final")) return "Laporan Akhir";
        if (lower.Contains("antara") || lower.Contains("progres") || lower.Contains("kemajuan")) return "Laporan Antara";
        if (lower.Contains("harian")) return "Laporan Harian";
        if (lower.Contains("kak") || lower.Contains("tor")) return "Kerangka Acuan Kerja (KAK)";
        if (lower.Contains("bast") || lower.Contains("serah terima")) return "Berita Acara (BAST)";
        if (lower.Contains("teknis") || lower.Contains("spesifikasi")) return "Dokumen Teknis";
        return "Laporan Kerja";
    }

    public async Task<int> ReprocessImagesAsync(Guid documentId)
    {
        var document = await _repository.GetByIdAsync(documentId);
        if (document == null)
            throw new KeyNotFoundException("Dokumen tidak ditemukan.");

        var ext = Path.GetExtension(document.NamaFile ?? document.Nama ?? "");
        if (!ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (string.IsNullOrEmpty(document.Path))
            throw new InvalidOperationException("Dokumen tidak memiliki Google Drive File ID.");

        var fileStream = await _driveService.DownloadFileAsync(document.Path);
        if (fileStream == null || fileStream.Length == 0)
            throw new InvalidOperationException("Gagal mengunduh file dari Google Drive.");

        fileStream.Position = 0;
        var extractedImages = await _pdfImageExtractor.ExtractImagesAsync(fileStream);

        if (extractedImages.Any())
        {
            var imagesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "Images", document.Id.ToString());
            if (!Directory.Exists(imagesDirectory))
            {
                Directory.CreateDirectory(imagesDirectory);
            }

            var oldImages = _dbContext.DocumentImages.Where(di => di.DocumentId == document.Id);
            _dbContext.DocumentImages.RemoveRange(oldImages);

            int imgIndex = 1;
            foreach (var img in extractedImages)
            {
                var imgFileName = $"p{img.PageNumber}_{imgIndex}_{Guid.NewGuid():N}.{img.Extension}";
                var imgFullPath = Path.Combine(imagesDirectory, imgFileName);
                await File.WriteAllBytesAsync(imgFullPath, img.ImageBytes);

                var relativeUrl = $"/uploads/images/{document.Id}/{imgFileName}";

                var docImage = new DocumentImage
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    PageNumber = img.PageNumber,
                    FileName = imgFileName,
                    FilePath = relativeUrl,
                    MimeType = img.MimeType,
                    Width = img.Width,
                    Height = img.Height,
                    FileSize = img.FileSize,
                    Caption = img.Caption,
                    ContextText = img.PageText,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.DocumentImages.Add(docImage);
                imgIndex++;
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Reprocessed {Count} images for document {DocumentId}", extractedImages.Count, documentId);
        }

        return extractedImages.Count;
    }
}
