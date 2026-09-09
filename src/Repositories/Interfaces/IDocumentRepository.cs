using SIAP.Api.Entities;

namespace SIAP.Api.Repositories.Interfaces;

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id);
    Task<(IEnumerable<Document>, int)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? keyword = null,
        string? namaTenagaAhli = null,
        string? jenisDokumen = null,
        string? periodeLaporan = null,
        Guid? userId = null,
        int? userBidangId = null,
        bool isAdmin = false,
        int? filterBidangId = null,
        string? filterBidang = null);
    Task<List<string>> GetCategoriesAsync();
    Task<Document> AddAsync(Document document);
    Task UpdateAsync(Document document);
    Task DeleteAsync(Document document);
    Task ExecuteInTransactionAsync(Func<Task> action);
    Task<List<DocumentChunk>> GetChunksByDocumentIdAsync(Guid documentId);
    Task<(List<DocumentChunk> Items, int TotalCount)> GetAllChunksAsync(int pageNumber, int pageSize, string? keyword, Guid? userId = null, int? userBidangId = null, bool isAdmin = false);
    Task<DocumentChunk?> GetChunkByIdAsync(Guid documentId, Guid chunkId);
    Task UpdateChunkAsync(DocumentChunk chunk);
    Task<List<DocumentChunk>> SearchKeywordAsync(string keyword, int topK, Guid? userId = null, int? userBidangId = null, bool isAdmin = false);
    Task<List<DocumentChunk>> SearchHybridAsync(string keyword, Pgvector.Vector embedding, int topK, Guid? userId = null, int? userBidangId = null, bool isAdmin = false);

    Task<bool> HasAccessAsync(Guid documentId, Guid userId, int? userBidangId, bool isAdmin);
    Task<List<DocumentAccess>> GetDocumentAccessesAsync(Guid documentId);
    Task<DocumentAccess?> GetDocumentAccessAsync(Guid documentId, Guid userId);
    Task AddDocumentAccessAsync(DocumentAccess access);
    Task RemoveDocumentAccessAsync(DocumentAccess access);
}
