using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SIAP.Api.Common;
using SIAP.Api.DTOs.Documents;
using SIAP.Api.Hubs;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _service;
    private readonly IDocumentRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IHubContext<AppHub> _hubContext;
    private readonly SIAP.Api.Data.AppDbContext _dbContext;

    public DocumentsController(
        IDocumentService service,
        IDocumentRepository repository,
        IUserRepository userRepository,
        IHubContext<AppHub> hubContext,
        SIAP.Api.Data.AppDbContext dbContext)
    {
        _service = service;
        _repository = repository;
        _userRepository = userRepository;
        _hubContext = hubContext;
        _dbContext = dbContext;
    }

    private async Task<(Guid? userId, int? userBidangId, string? userBidang, bool isAdmin, bool isApproved)> GetCurrentUserAsync()
    {
        var isAdmin = User.IsInRole("admin") || User.IsInRole("super-admin");
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return (null, null, null, isAdmin, false);
        }

        var dbUser = await _userRepository.GetByIdAsync(userId);
        var isApproved = dbUser?.IsApproved ?? false;
        var userBidangId = dbUser?.BidangId;
        var userBidang = dbUser?.Bidang?.Nama;

        return (userId, userBidangId, userBidang, isAdmin, isApproved);
    }

    [HttpPost]
    [Authorize(Roles = "super-admin,admin,user,kasubag")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000, ValueCountLimit = 5000)]
    public async Task<IActionResult> Upload([FromForm] DocumentCreateDto request)
    {
        try
        {
            var (userId, userBidangId, userBidang, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!userId.HasValue)
                return Unauthorized(ApiResponse<List<DocumentResponseDto>>.Gagal("Pengguna tidak terautentikasi."));

            if (!isAdmin && !isApproved)
                return StatusCode(403, ApiResponse<List<DocumentResponseDto>>.Gagal("Akun Anda sedang menunggu persetujuan dari Admin/Kasubag dan penentuan bidang."));

            if (request.Files == null || !request.Files.Any())
                return BadRequest(ApiResponse<List<DocumentResponseDto>>.Gagal("Tidak ada file yang diupload."));

            request.UserId = userId.Value;
            if (!request.BidangId.HasValue && string.IsNullOrWhiteSpace(request.Bidang))
            {
                request.BidangId = userBidangId;
            }

            var isKasubag = User.IsInRole("kasubag");
            if (isKasubag && request.BidangId != userBidangId)
            {
                return StatusCode(403, ApiResponse<List<DocumentResponseDto>>.Gagal("Kasubag hanya dapat menambah dokumen di bidangnya sendiri."));
            }

            var result = await _service.UploadAsync(request);
            
            // Broadcast SignalR event
            await _hubContext.Clients.All.SendAsync("DocumentCreated", result);

            return Ok(ApiResponse<List<DocumentResponseDto>>.Ok(result, "Dokumen berhasil diupload."));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<DocumentResponseDto>.Gagal(ex.Message));
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DocumentSearchDto request)
    {
        try
        {
            var (userId, userBidangId, userBidang, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!isAdmin && !isApproved)
            {
                return StatusCode(403, ApiResponse<PagedResponse<DocumentResponseDto>>.Gagal("Akun Anda sedang menunggu persetujuan dari Admin/Kasubag dan penentuan bidang."));
            }

            var result = await _service.GetAllAsync(request, userId, userBidangId, isAdmin);
            return Ok(ApiResponse<PagedResponse<DocumentResponseDto>>.Ok(result));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<PagedResponse<DocumentResponseDto>>.Gagal(ex.Message));
        }
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        try
        {
            var result = await _service.GetCategoriesAsync();
            return Ok(ApiResponse<List<string>>.Ok(result));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<List<string>>.Gagal(ex.Message));
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        try
        {
            if (!Guid.TryParse(id, out var guidId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }

            var (userId, userBidangId, userBidang, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!userId.HasValue) return Unauthorized();

            if (!isAdmin && !isApproved)
                return StatusCode(403, ApiResponse<DocumentResponseDto>.Gagal("Akun Anda sedang menunggu persetujuan dari Admin/Kasubag."));

            var hasAccess = await _repository.HasAccessAsync(guidId, userId.Value, userBidangId, isAdmin);
            if (!hasAccess)
            {
                return Forbid();
            }

            var result = await _service.GetByIdAsync(guidId, userId, isAdmin);
            if (result == null)
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }

            return Ok(ApiResponse<DocumentResponseDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<DocumentResponseDto>.Gagal(ex.Message));
        }
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(string id)
    {
        try
        {
            if (!Guid.TryParse(id, out var guidId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }

            var (userId, userBidangId, userBidang, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!userId.HasValue) return Unauthorized();

            if (!isAdmin && !isApproved)
                return StatusCode(403, ApiResponse<string>.Gagal("Akun Anda sedang menunggu persetujuan dari Admin/Kasubag."));

            var hasAccess = await _repository.HasAccessAsync(guidId, userId.Value, userBidangId, isAdmin);
            if (!hasAccess)
            {
                return Forbid();
            }

            var (fileStream, contentType, fileName) = await _service.DownloadAsync(guidId);
            return File(fileStream, contentType, fileName, enableRangeProcessing: true);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<string>.Gagal(ex.Message));
        }
    }

    // Document Sharing Endpoints
    [HttpGet("{id}/shares")]
    public async Task<IActionResult> GetShares(string id)
    {
        try
        {
            if (!Guid.TryParse(id, out var docId))
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "ID dokumen tidak valid." });

            var (userId, _, _, isAdmin, _) = await GetCurrentUserAsync();
            if (!userId.HasValue) return Unauthorized();

            var result = await _service.GetAccessesAsync(docId, userId.Value, isAdmin);
            return Ok(ApiResponse<List<DocumentAccessUserDto>>.Ok(result));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ApiResponse<object>.Gagal(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Gagal(ex.Message));
        }
    }

    [HttpPost("{id}/shares")]
    public async Task<IActionResult> ShareDocument(string id, [FromBody] ShareDocumentRequest request)
    {
        try
        {
            if (!Guid.TryParse(id, out var docId))
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "ID dokumen tidak valid." });

            var (userId, _, _, isAdmin, _) = await GetCurrentUserAsync();
            if (!userId.HasValue) return Unauthorized();

            var result = await _service.ShareAsync(docId, request.Username, userId.Value, isAdmin);

            // Broadcast SignalR event
            await _hubContext.Clients.All.SendAsync("DocumentShared", new { DocumentId = docId, SharedUser = result });

            return Ok(ApiResponse<DocumentAccessUserDto>.Ok(result, $"Akses baca dokumen berhasil diberikan ke @{result.Username}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ApiResponse<object>.Gagal(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Gagal(ex.Message));
        }
    }

    [HttpDelete("{id}/shares/{targetUserId}")]
    public async Task<IActionResult> RevokeShare(string id, string targetUserId)
    {
        try
        {
            if (!Guid.TryParse(id, out var docId) || !Guid.TryParse(targetUserId, out var targetId))
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "ID tidak valid." });

            var (userId, _, _, isAdmin, _) = await GetCurrentUserAsync();
            if (!userId.HasValue) return Unauthorized();

            await _service.RevokeAccessAsync(docId, targetId, userId.Value, isAdmin);

            // Broadcast SignalR event
            await _hubContext.Clients.All.SendAsync("DocumentAccessRevoked", new { DocumentId = docId, TargetUserId = targetId });

            return Ok(ApiResponse<bool>.Ok(true, "Hak akses berhasil dicabut."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ApiResponse<object>.Gagal(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Gagal(ex.Message));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "super-admin,admin,user,kasubag")]
    public async Task<IActionResult> Update(string id, [FromBody] DocumentUpdateDto request)
    {
        try
        {
            if (!Guid.TryParse(id, out var guidId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }

            var (userId, userBidangId, userBidang, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!userId.HasValue) return Unauthorized();

            if (!isAdmin && !isApproved)
                return StatusCode(403, ApiResponse<DocumentResponseDto>.Gagal("Akun Anda belum disetujui."));

            var doc = await _repository.GetByIdAsync(guidId);
            if (doc == null)
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });

            var isKasubag = User.IsInRole("kasubag");
            if (!isAdmin && !isKasubag && doc.UserId != userId.Value)
            {
                return StatusCode(403, ApiResponse<DocumentResponseDto>.Gagal("Hanya pemilik dokumen atau admin yang dapat mengubah dokumen ini."));
            }

            if (isKasubag && doc.BidangId != userBidangId)
            {
                return StatusCode(403, ApiResponse<DocumentResponseDto>.Gagal("Kasubag hanya dapat mengubah dokumen di bidangnya sendiri."));
            }

            if (isKasubag && ((request.BidangId.HasValue && request.BidangId.Value != userBidangId) || (!string.IsNullOrWhiteSpace(request.Bidang) && request.Bidang != userBidang)))
            {
                return StatusCode(403, ApiResponse<DocumentResponseDto>.Gagal("Kasubag tidak dapat memindahkan dokumen ke bidang lain."));
            }

            var result = await _service.UpdateAsync(guidId, request);

            // Broadcast SignalR event
            await _hubContext.Clients.All.SendAsync("DocumentUpdated", result);

            return Ok(ApiResponse<DocumentResponseDto>.Ok(result, "Dokumen berhasil diupdate."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<DocumentResponseDto>.Gagal(ex.Message));
        }
    }

    [HttpGet("{id}/status")]
    [Authorize(Roles = "super-admin,admin")]
    public async Task<IActionResult> GetStatus(string id)
    {
        try
        {
            if (!Guid.TryParse(id, out var guidId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }
            var result = await _service.GetStatusAsync(guidId);
            return Ok(ApiResponse<DocumentStatusDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<DocumentStatusDto>.Gagal(ex.Message));
        }
    }

    [HttpGet("{id}/ocr")]
    [Authorize(Roles = "super-admin,admin")]
    public async Task<IActionResult> GetOcrStatus(string id)
    {
        try
        {
            if (!Guid.TryParse(id, out var guidId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }
            var result = await _service.GetOcrStatusAsync(guidId);
            return Ok(ApiResponse<OcrStatusDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<OcrStatusDto>.Gagal(ex.Message));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "super-admin,admin,user,kasubag")]
    public async Task<IActionResult> Delete(string id)
    {
        try
        {
            if (!Guid.TryParse(id, out var guidId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }

            var (userId, userBidangId, userBidang, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!userId.HasValue) return Unauthorized();

            if (!isAdmin && !isApproved)
                return StatusCode(403, ApiResponse<bool>.Gagal("Akun Anda belum disetujui."));

            var doc = await _repository.GetByIdAsync(guidId);
            if (doc == null)
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });

            var isKasubag = User.IsInRole("kasubag");
            if (!isAdmin && !isKasubag && doc.UserId != userId.Value)
            {
                return StatusCode(403, ApiResponse<bool>.Gagal("Hanya pemilik dokumen, admin, atau kasubag yang dapat menghapus dokumen ini."));
            }

            if (isKasubag && doc.BidangId != userBidangId)
            {
                return StatusCode(403, ApiResponse<bool>.Gagal("Kasubag hanya dapat menghapus dokumen di bidangnya sendiri."));
            }

            await _service.DeleteAsync(guidId);

            // Broadcast SignalR event
            await _hubContext.Clients.All.SendAsync("DocumentDeleted", id);

            return Ok(ApiResponse<bool>.Ok(true, "Dokumen berhasil dihapus."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<bool>.Gagal(ex.Message));
        }
    }

    [HttpGet("{id}/chunks")]
    public async Task<IActionResult> GetChunks(string id)
    {
        try
        {
            if (!Guid.TryParse(id, out var guidId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }
            var result = await _service.GetChunksAsync(guidId);
            return Ok(ApiResponse<SIAP.Api.DTOs.Chunks.DocumentChunkListResponseDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<SIAP.Api.DTOs.Chunks.DocumentChunkListResponseDto>.Gagal(ex.Message));
        }
    }

    [HttpGet("{id}/images")]
    public async Task<IActionResult> GetImages(string id)
    {
        try
        {
            if (!Guid.TryParse(id, out var guidId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }

            var (userId, userBidangId, _, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!userId.HasValue) return Unauthorized();

            if (!isAdmin && !isApproved)
                return StatusCode(403, ApiResponse<object>.Gagal("Akun Anda belum disetujui."));

            var hasAccess = await _repository.HasAccessAsync(guidId, userId.Value, userBidangId, isAdmin);
            if (!hasAccess) return Forbid();

            var images = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                _dbContext.DocumentImages
                    .Where(di => di.DocumentId == guidId)
                    .OrderBy(di => di.PageNumber)
                    .Select(di => new
                    {
                        di.Id,
                        di.DocumentId,
                        di.PageNumber,
                        di.FileName,
                        di.FilePath,
                        di.MimeType,
                        di.Width,
                        di.Height,
                        di.FileSize,
                        di.Caption,
                        di.CreatedAt
                    })
            );

            return Ok(ApiResponse<object>.Ok(images));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Gagal(ex.Message));
        }
    }

    [HttpGet("chunks")]
    public async Task<IActionResult> GetAllChunks([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? keyword = null)
    {
        try
        {
            var (items, totalCount) = await _repository.GetAllChunksAsync(pageNumber, pageSize, keyword);
            var response = items.Select(c => new SIAP.Api.DTOs.Chunks.ChunkWithDocumentDto
            {
                Id = c.Id,
                DocumentId = c.DocumentId,
                ChunkIndex = c.ChunkIndex,
                ChunkType = c.ChunkType,
                Content = c.Content,
                CharacterCount = c.CharacterCount,
                WordCount = c.WordCount,
                EstimatedToken = c.EstimatedToken,
                CreatedAt = c.CreatedAt,
                Metadata = c.Metadata,
                NamaTenagaAhli = c.Document?.NamaTenagaAhli,
                Path = c.Document?.Path,
                Status = c.Document?.Status.ToString(),
                PeriodeLaporan = c.Document?.PeriodeLaporan,
                TanggalUpload = c.Document?.TanggalUpload,
                Ukuran = c.Document?.Ukuran,
                ProcessingStartedAt = c.Document?.ProcessingStartedAt,
                ProcessingFinishedAt = c.Document?.ProcessingFinishedAt,
                ProcessingDuration = c.Document?.ProcessingDuration
            }).ToList();

            var pagedResponse = new PagedResponse<SIAP.Api.DTOs.Chunks.ChunkWithDocumentDto>
            {
                Data = response,
                TotalRecords = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            return Ok(ApiResponse<PagedResponse<SIAP.Api.DTOs.Chunks.ChunkWithDocumentDto>>.Ok(pagedResponse));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<PagedResponse<SIAP.Api.DTOs.Chunks.ChunkWithDocumentDto>>.Gagal(ex.Message));
        }
    }

    [HttpGet("{id}/chunks/{chunkId}")]
    public async Task<IActionResult> GetChunkById(string id, string chunkId)
    {
        try
        {
            if (!Guid.TryParse(id, out var docId) || !Guid.TryParse(chunkId, out var chId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "ID Dokumen atau Chunk tidak valid." });
            }
            var result = await _service.GetChunkByIdAsync(docId, chId);
            return Ok(ApiResponse<SIAP.Api.DTOs.Chunks.DocumentChunkResponseDto>.Ok(result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<SIAP.Api.DTOs.Chunks.DocumentChunkResponseDto>.Gagal(ex.Message));
        }
    }

    [HttpPut("{id}/chunks/{chunkId}")]
    [Authorize(Roles = "super-admin,admin")]
    public async Task<IActionResult> UpdateChunk(string id, string chunkId, [FromBody] SIAP.Api.DTOs.Chunks.DocumentChunkUpdateDto request)
    {
        try
        {
            if (!Guid.TryParse(id, out var docId) || !Guid.TryParse(chunkId, out var chId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "ID Dokumen atau Chunk tidak valid." });
            }
            var result = await _service.UpdateChunkAsync(docId, chId, request);
            return Ok(ApiResponse<SIAP.Api.DTOs.Chunks.DocumentChunkResponseDto>.Ok(result, "Chunk berhasil diupdate."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<SIAP.Api.DTOs.Chunks.DocumentChunkResponseDto>.Gagal(ex.Message));
        }
    }

    [HttpPost("{id}/rechunk")]
    [Authorize(Roles = "super-admin,admin")]
    public async Task<IActionResult> Rechunk(string id, [FromQuery] string? strategy)
    {
        try
        {
            if (!Guid.TryParse(id, out var docId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }
            var result = await _service.RechunkAsync(docId, strategy);
            return Ok(ApiResponse<bool>.Ok(result, "Rechunk berhasil dijalankan."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<bool>.Gagal(ex.Message));
        }
    }

    [HttpPost("{id}/reprocess-images")]
    [Authorize(Roles = "super-admin,admin,user")]
    public async Task<IActionResult> ReprocessImages(string id)
    {
        try
        {
            if (!Guid.TryParse(id, out var docId))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Dokumen tidak ditemukan." });
            }

            var (userId, userBidangId, _, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!userId.HasValue) return Unauthorized();

            if (!isAdmin && !isApproved)
                return StatusCode(403, ApiResponse<object>.Gagal("Akun Anda belum disetujui."));

            var hasAccess = await _repository.HasAccessAsync(docId, userId.Value, userBidangId, isAdmin);
            if (!hasAccess) return Forbid();

            var count = await _service.ReprocessImagesAsync(docId);
            return Ok(ApiResponse<object>.Ok(new { DocumentId = docId, ExtractedImages = count }, $"Berhasil mengekstrak ulang {count} gambar dari dokumen."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Gagal(ex.Message));
        }
    }

    [HttpPost("search/vector")]
    public async Task<IActionResult> SemanticSearch([FromBody] VectorSearchDto request)
    {
        try
        {
            var (userId, userBidangId, userBidang, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!isAdmin && !isApproved)
                return StatusCode(403, ApiResponse<object>.Gagal("Akun Anda belum disetujui."));

            var results = await _repository.SearchKeywordAsync(request.Query, request.TopK, userId, userBidangId, isAdmin);

            var response = results.Select(c => new
            {
                c.Id,
                c.DocumentId,
                DocumentTitle = c.Document?.Nama,
                Bidang = c.Document?.Bidang?.Nama,
                c.ChunkType,
                c.Content,
                c.Metadata
            });

            return Ok(ApiResponse<object>.Ok(response, "Pencarian keyword berhasil. (Fallback dari Semantic)"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Gagal(ex.Message));
        }
    }

    [HttpPost("search/hybrid")]
    public async Task<IActionResult> HybridSearch([FromBody] VectorSearchDto request)
    {
        try
        {
            var (userId, userBidangId, userBidang, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!isAdmin && !isApproved)
                return StatusCode(403, ApiResponse<object>.Gagal("Akun Anda belum disetujui."));

            var results = await _repository.SearchKeywordAsync(request.Query, request.TopK, userId, userBidangId, isAdmin);

            var response = results.Select(x => new
            {
                Score = 1.0,
                ChunkId = x.Id,
                DocumentId = x.DocumentId,
                DocumentTitle = x.Document?.Nama,
                Bidang = x.Document?.Bidang?.Nama,
                ChunkType = x.ChunkType,
                Content = x.Content,
                Metadata = x.Metadata
            });

            return Ok(ApiResponse<object>.Ok(response, "Pencarian hybrid berhasil."));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Gagal(ex.Message));
        }
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] VectorSearchDto request, [FromServices] IGroqService groqService)
    {
        try
        {
            var (userId, userBidangId, userBidang, isAdmin, isApproved) = await GetCurrentUserAsync();
            if (!isAdmin && !isApproved)
                return StatusCode(403, ApiResponse<object>.Gagal("Akun Anda belum disetujui."));

            var results = await _repository.SearchKeywordAsync(request.Query, request.TopK, userId, userBidangId, isAdmin);

            var contextBuilder = new System.Text.StringBuilder();
            foreach (var chunk in results)
            {
                var docTitle = chunk.Document?.Nama ?? "Dokumen Tanpa Judul";
                var bidangNama = chunk.Document?.Bidang?.Nama;
                var bidangInfo = !string.IsNullOrEmpty(bidangNama) ? $" ({bidangNama})" : "";
                contextBuilder.AppendLine($"[Dokumen: {docTitle}{bidangInfo}]");
                contextBuilder.AppendLine(chunk.Content);
                contextBuilder.AppendLine("---");
            }

            var systemPrompt = @"Anda adalah asisten AI dari aplikasi SIPENTA (Sistem Pelaporan Tenaga Ahli) yang membantu Kasubag dan pegawai Diskominfo memahami serta mereview laporan kerja tenaga ahli.
PANDUAN:
1. Jawab berdasarkan informasi pada konteks dokumen laporan kerja yang diberikan secara ringkas dan objektif.
2. Gunakan bahasa profesional namun mudah dipahami, gunakan format Markdown yang rapi (bullet list, cetak tebal).
3. Jangan mengarang progres atau mengklaim pekerjaan di luar apa yang tertera di dokumen.
4. Jika rincian pertanyaan belum ditemukan dalam dokumen, sampaikan dengan jelas bagian apa saja yang ada pada laporan tersebut serta informasi yang kurang/tidak tersedia.";
            var userPrompt = $"Konteks:\n{contextBuilder}\n\nPertanyaan: {request.Query}";

            var answer = await groqService.GetChatCompletionAsync(systemPrompt, userPrompt);

            return Ok(ApiResponse<object>.Ok(new { 
                Answer = answer, 
                Sources = results.Select(chunk => new { 
                    DocumentTitle = chunk.Document?.Nama,
                    Bidang = chunk.Document?.Bidang?.Nama,
                    ChunkType = chunk.ChunkType,
                    Metadata = chunk.Metadata
                }) 
            }, "Sukses"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Gagal(ex.Message));
        }
    }

    [HttpGet("images/{fileId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetImage(string fileId, [FromServices] IGoogleDriveService driveService)
    {
        try
        {
            var stream = await driveService.DownloadFileAsync(fileId);
            return File(stream, "image/jpeg");
        }
        catch (Exception)
        {
            return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Image not found." });
        }
    }
}
