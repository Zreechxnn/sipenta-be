using Microsoft.AspNetCore.Http;
using SIAP.Api.Common;
using SIAP.Api.DTOs.Documents;

namespace SIAP.Api.Services.Interfaces;

public interface IDocumentService
{
    Task<List<DocumentResponseDto>> UploadAsync(DocumentCreateDto request);
    Task<DocumentResponseDto> UpdateAsync(Guid id, DocumentUpdateDto request);
    Task DeleteAsync(Guid id);
    Task<DocumentResponseDto> GetByIdAsync(Guid id, Guid? currentUserId = null, bool isAdmin = false);
    Task<PagedResponse<DocumentResponseDto>> GetAllAsync(DocumentSearchDto searchDto, Guid? currentUserId = null, int? userBidangId = null, bool isAdmin = false);
    Task<List<string>> GetCategoriesAsync();
    Task<DocumentStatusDto> GetStatusAsync(Guid id);
    Task<OcrStatusDto> GetOcrStatusAsync(Guid id);
    Task<(Stream FileStream, string ContentType, string FileName)> DownloadAsync(Guid id);
    Task<SIAP.Api.DTOs.Chunks.DocumentChunkListResponseDto> GetChunksAsync(Guid id);
    Task<SIAP.Api.DTOs.Chunks.DocumentChunkResponseDto> GetChunkByIdAsync(Guid id, Guid chunkId);
    Task<SIAP.Api.DTOs.Chunks.DocumentChunkResponseDto> UpdateChunkAsync(Guid documentId, Guid chunkId, SIAP.Api.DTOs.Chunks.DocumentChunkUpdateDto request);
    Task<bool> RechunkAsync(Guid id, string? strategy = null);

    // Document Sharing
    Task<List<DocumentAccessUserDto>> GetAccessesAsync(Guid documentId, Guid currentUserId, bool isAdmin);
    Task<DocumentAccessUserDto> ShareAsync(Guid documentId, string username, Guid currentUserId, bool isAdmin);
    Task RevokeAccessAsync(Guid documentId, Guid targetUserId, Guid currentUserId, bool isAdmin);

    Task<int> ReprocessImagesAsync(Guid documentId);
}
