using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using SIAP.Api.Data;
using SIAP.Api.Entities;
using SIAP.Api.Repositories.Interfaces;

namespace SIAP.Api.Repositories.Implementations;

public class DocumentRepository : IDocumentRepository
{
    private readonly AppDbContext _context;

    public DocumentRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Document?> GetByIdAsync(Guid id)
    {
        return await _context.Set<Document>()
            .Include(x => x.Content)
            .Include(x => x.User)
            .Include(x => x.Bidang)
            .Include(x => x.Accesses)
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.Bidang)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<(IEnumerable<Document>, int)> GetPagedAsync(
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
        string? filterBidang = null)
    {
        var query = _context.Set<Document>()
            .Include(x => x.Content)
            .Include(x => x.User)
            .Include(x => x.Bidang)
            .Include(x => x.Accesses)
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.Bidang)
            .AsQueryable();

        // If not admin, restrict to owner documents, same Bidang documents, or explicitly shared documents
        if (!isAdmin && userId.HasValue)
        {
            var uId = userId.Value;
            query = query.Where(x => 
                x.UserId == uId || 
                (userBidangId.HasValue && (x.BidangId == userBidangId.Value || (x.BidangId == null && x.User != null && x.User.BidangId == userBidangId.Value))) || 
                x.Accesses.Any(a => a.UserId == uId));
        }

        // Filter by Bidang if specified
        if (filterBidangId.HasValue && filterBidangId.Value > 0)
        {
            query = query.Where(x => x.BidangId == filterBidangId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(filterBidang))
        {
            query = query.Where(x => x.Bidang != null && (x.Bidang.Nama == filterBidang || x.Bidang.Kode == filterBidang));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var searchPattern = $"%{keyword}%";
            query = query.Where(x => 
                EF.Functions.ILike(x.Nama!, searchPattern) || 
                EF.Functions.ILike(x.NamaTenagaAhli!, searchPattern) ||
                (x.Content != null && EF.Functions.ILike(x.Content.RawText, searchPattern)));
        }

        if (!string.IsNullOrWhiteSpace(namaTenagaAhli))
        {
            var namaPattern = $"%{namaTenagaAhli}%";
            query = query.Where(x => x.NamaTenagaAhli != null && EF.Functions.ILike(x.NamaTenagaAhli, namaPattern));
        }

        if (!string.IsNullOrWhiteSpace(jenisDokumen))
        {
            var jenisPattern = $"%{jenisDokumen}%";
            query = query.Where(x => x.JenisDokumen != null && EF.Functions.ILike(x.JenisDokumen, jenisPattern));
        }

        if (!string.IsNullOrWhiteSpace(periodeLaporan))
        {
            var periodePattern = $"%{periodeLaporan}%";
            query = query.Where(x => x.PeriodeLaporan != null && EF.Functions.ILike(x.PeriodeLaporan, periodePattern));
        }

        var total = await query.CountAsync();
        var data = await query.OrderByDescending(x => x.TanggalUpload)
                              .Skip((pageNumber - 1) * pageSize)
                              .Take(pageSize)
                              .ToListAsync();

        return (data, total);
    }

    public async Task<bool> HasAccessAsync(Guid documentId, Guid userId, int? userBidangId, bool isAdmin)
    {
        if (isAdmin) return true;

        return await _context.Documents
            .Include(d => d.User)
            .Include(d => d.Accesses)
            .AnyAsync(d => 
                d.Id == documentId && (
                    d.UserId == userId || 
                    (userBidangId.HasValue && (d.BidangId == userBidangId.Value || (d.BidangId == null && d.User != null && d.User.BidangId == userBidangId.Value))) || 
                    d.Accesses.Any(a => a.UserId == userId)
                ));
    }

    public async Task<List<DocumentAccess>> GetDocumentAccessesAsync(Guid documentId)
    {
        return await _context.DocumentAccesses
            .Include(da => da.User)
                .ThenInclude(u => u.Bidang)
            .Include(da => da.SharedByUser)
            .Where(da => da.DocumentId == documentId)
            .OrderByDescending(da => da.CreatedAt)
            .ToListAsync();
    }

    public async Task<DocumentAccess?> GetDocumentAccessAsync(Guid documentId, Guid userId)
    {
        return await _context.DocumentAccesses
            .Include(da => da.User)
                .ThenInclude(u => u.Bidang)
            .FirstOrDefaultAsync(da => da.DocumentId == documentId && da.UserId == userId);
    }

    public async Task AddDocumentAccessAsync(DocumentAccess access)
    {
        await _context.DocumentAccesses.AddAsync(access);
        await _context.SaveChangesAsync();
    }

    public async Task RemoveDocumentAccessAsync(DocumentAccess access)
    {
        _context.DocumentAccesses.Remove(access);
        await _context.SaveChangesAsync();
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        return await Task.FromResult(new List<string>());
    }

    public async Task<Document> AddAsync(Document document)
    {
        await _context.Set<Document>().AddAsync(document);
        await _context.SaveChangesAsync();
        return document;
    }

    public async Task UpdateAsync(Document document)
    {
        _context.Set<Document>().Update(document);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Document document)
    {
        _context.Set<Document>().Remove(document);
        await _context.SaveChangesAsync();
    }

    public async Task ExecuteInTransactionAsync(Func<Task> action)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await action();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    public async Task<List<DocumentChunk>> GetChunksByDocumentIdAsync(Guid documentId)
    {
        return await _context.Set<DocumentChunk>()
            .Where(x => x.DocumentId == documentId)
            .OrderBy(x => x.ChunkIndex)
            .ToListAsync();
    }

    public async Task<(List<DocumentChunk> Items, int TotalCount)> GetAllChunksAsync(int pageNumber, int pageSize, string? keyword)
    {
        var query = _context.DocumentChunks
            .Include(c => c.Document)
                .ThenInclude(d => d!.Bidang)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(c => EF.Functions.ToTsVector("indonesian", c.Content)
                        .Matches(EF.Functions.WebSearchToTsQuery("indonesian", keyword)));
        }

        var totalCount = await query.CountAsync();
        
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<DocumentChunk?> GetChunkByIdAsync(Guid documentId, Guid chunkId)
    {
        return await _context.Set<DocumentChunk>()
            .FirstOrDefaultAsync(x => x.DocumentId == documentId && x.Id == chunkId);
    }

    public async Task UpdateChunkAsync(DocumentChunk chunk)
    {
        _context.Set<DocumentChunk>().Update(chunk);
        await _context.SaveChangesAsync();
    }

    private static List<string> ExtractSearchTerms(string keyword)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        { 
            "apa", "itu", "yang", "dan", "di", "ke", "dari", "ini", "untuk", "dengan", "adalah", 
            "pada", "dalam", "sebagai", "bahwa", "oleh", "atau", "kepada", "karena", "saat", 
            "jika", "tidak", "bisa", "bagaimana", "cara", "siapa", "dimana", "kapan", "mengapa", 
            "kenapa", "apakah", "tolong", "jelaskan", "sebutkan", "berikan", "maksud", "arti", "makna",
            "tentang", "soal", "mengenai", "saja", "ada", "sudah", "belum", "lagi", "pun"
        };

        var normalized = keyword
            .Replace("ditanggal", " tanggal ", StringComparison.OrdinalIgnoreCase)
            .Replace("padatanggal", " tanggal ", StringComparison.OrdinalIgnoreCase)
            .Replace("di tanggal", " tanggal ", StringComparison.OrdinalIgnoreCase)
            .Replace("pada tanggal", " tanggal ", StringComparison.OrdinalIgnoreCase)
            .Replace("tgl", " tanggal ", StringComparison.OrdinalIgnoreCase);

        var clean = new string(normalized.Select(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ').ToArray());
        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Where(w => !stopWords.Contains(w) && w.Length >= 1)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToList();

        return words;
    }

    public async Task<List<DocumentChunk>> SearchKeywordAsync(string keyword, int topK, Guid? userId = null, int? userBidangId = null, bool isAdmin = false)
    {
        var words = ExtractSearchTerms(keyword);
        if (!words.Any())
        {
            return new List<DocumentChunk>();
        }

        var tsQueryStringAnd = string.Join(" & ", words);
        var tsQueryStringOr = string.Join(" | ", words);

        var query = _context.DocumentChunks
            .Include(c => c.Document)
                .ThenInclude(d => d!.Bidang)
            .Include(c => c.Document)
                .ThenInclude(d => d!.User)
            .Include(c => c.Document)
                .ThenInclude(d => d!.Accesses)
            .AsQueryable();

        if (!isAdmin && userId.HasValue)
        {
            var uId = userId.Value;
            query = query.Where(c => 
                c.Document != null && (
                    c.Document.UserId == uId || 
                    (userBidangId.HasValue && (c.Document.BidangId == userBidangId.Value || (c.Document.BidangId == null && c.Document.User != null && c.Document.User.BidangId == userBidangId.Value))) || 
                    c.Document.Accesses.Any(a => a.UserId == uId)
                ));
        }

        // 1. Try AND logic first
        var andResults = await query
            .Where(c => c.Document != null && EF.Functions.ToTsVector("indonesian", (c.Document.Nama ?? "") + " " + (c.Document.NamaTenagaAhli ?? "") + " " + (c.Document.PeriodeLaporan ?? "") + " " + c.Content)
                        .Matches(EF.Functions.ToTsQuery("indonesian", tsQueryStringAnd)))
            .OrderByDescending(c => EF.Functions.ToTsVector("indonesian", (c.Document!.Nama ?? "") + " " + (c.Document!.NamaTenagaAhli ?? "") + " " + (c.Document!.PeriodeLaporan ?? "") + " " + c.Content)
                        .Rank(EF.Functions.ToTsQuery("indonesian", tsQueryStringAnd)))
            .Take(topK)
            .ToListAsync();

        if (andResults.Any())
        {
            return andResults;
        }

        // 2. Fallback to OR logic
        return await query
            .Where(c => c.Document != null && EF.Functions.ToTsVector("indonesian", (c.Document.Nama ?? "") + " " + (c.Document.NamaTenagaAhli ?? "") + " " + (c.Document.PeriodeLaporan ?? "") + " " + c.Content)
                        .Matches(EF.Functions.ToTsQuery("indonesian", tsQueryStringOr)))
            .OrderByDescending(c => EF.Functions.ToTsVector("indonesian", (c.Document!.Nama ?? "") + " " + (c.Document!.NamaTenagaAhli ?? "") + " " + (c.Document!.PeriodeLaporan ?? "") + " " + c.Content)
                        .Rank(EF.Functions.ToTsQuery("indonesian", tsQueryStringOr)))
            .Take(topK)
            .ToListAsync();
    }

    public async Task<List<DocumentChunk>> SearchHybridAsync(string keyword, Pgvector.Vector embedding, int topK, Guid? userId = null, int? userBidangId = null, bool isAdmin = false)
    {
        int fetchCount = topK * 3;

        var query = _context.DocumentChunks
            .Include(c => c.Document)
                .ThenInclude(d => d!.Bidang)
            .Include(c => c.Document)
                .ThenInclude(d => d!.User)
            .Include(c => c.Document)
                .ThenInclude(d => d!.Accesses)
            .AsQueryable();

        if (!isAdmin && userId.HasValue)
        {
            var uId = userId.Value;
            query = query.Where(c => 
                c.Document != null && (
                    c.Document.UserId == uId || 
                    (userBidangId.HasValue && (c.Document.BidangId == userBidangId.Value || (c.Document.BidangId == null && c.Document.User != null && c.Document.User.BidangId == userBidangId.Value))) || 
                    c.Document.Accesses.Any(a => a.UserId == uId)
                ));
        }

        // 1. Vector Search
        var vectorResults = await query
            .Where(c => c.Embedding != null)
            .OrderBy(c => c.Embedding!.CosineDistance(embedding))
            .Take(fetchCount)
            .ToListAsync();

        // 2. Keyword Search
        var words = ExtractSearchTerms(keyword);
        var tsQueryStringAnd = string.Join(" & ", words);
        var tsQueryStringOr = string.Join(" | ", words);

        List<DocumentChunk> ftsResults = new List<DocumentChunk>();
        if (words.Any())
        {
            ftsResults = await query
                .Where(c => c.Document != null && EF.Functions.ToTsVector("indonesian", (c.Document.Nama ?? "") + " " + (c.Document.NamaTenagaAhli ?? "") + " " + (c.Document.PeriodeLaporan ?? "") + " " + c.Content)
                            .Matches(EF.Functions.ToTsQuery("indonesian", tsQueryStringAnd)))
                .OrderByDescending(c => EF.Functions.ToTsVector("indonesian", (c.Document!.Nama ?? "") + " " + (c.Document!.NamaTenagaAhli ?? "") + " " + (c.Document!.PeriodeLaporan ?? "") + " " + c.Content)
                            .Rank(EF.Functions.ToTsQuery("indonesian", tsQueryStringAnd)))
                .Take(fetchCount)
                .ToListAsync();

            if (!ftsResults.Any())
            {
                ftsResults = await query
                    .Where(c => c.Document != null && EF.Functions.ToTsVector("indonesian", (c.Document.Nama ?? "") + " " + (c.Document.NamaTenagaAhli ?? "") + " " + (c.Document.PeriodeLaporan ?? "") + " " + c.Content)
                                .Matches(EF.Functions.ToTsQuery("indonesian", tsQueryStringOr)))
                    .OrderByDescending(c => EF.Functions.ToTsVector("indonesian", (c.Document!.Nama ?? "") + " " + (c.Document!.NamaTenagaAhli ?? "") + " " + (c.Document!.PeriodeLaporan ?? "") + " " + c.Content)
                                .Rank(EF.Functions.ToTsQuery("indonesian", tsQueryStringOr)))
                    .Take(fetchCount)
                    .ToListAsync();
            }
        }

        // 3. Exact date / keyword substring boost search
        var exactMatches = new List<DocumentChunk>();
        foreach (var word in words.Where(w => w.Length >= 2))
        {
            var pattern = $"%{word}%";
            var match = await query
                .Where(c => EF.Functions.ILike(c.Content, pattern) || 
                            (c.Document != null && c.Document.Nama != null && EF.Functions.ILike(c.Document.Nama, pattern)) ||
                            (c.Document != null && c.Document.NamaTenagaAhli != null && EF.Functions.ILike(c.Document.NamaTenagaAhli, pattern)))
                .Take(topK)
                .ToListAsync();
            exactMatches.AddRange(match);
        }

        // 4. Reciprocal Rank Fusion (RRF)
        var rrfScores = new Dictionary<Guid, double>();
        int k = 60;

        for (int i = 0; i < vectorResults.Count; i++)
        {
            var id = vectorResults[i].Id;
            rrfScores[id] = rrfScores.GetValueOrDefault(id) + 1.0 / (k + i + 1);
        }

        for (int i = 0; i < ftsResults.Count; i++)
        {
            var id = ftsResults[i].Id;
            rrfScores[id] = rrfScores.GetValueOrDefault(id) + 1.2 / (k + i + 1);
        }

        for (int i = 0; i < exactMatches.Count; i++)
        {
            var id = exactMatches[i].Id;
            rrfScores[id] = rrfScores.GetValueOrDefault(id) + 1.5 / (k + i + 1);
        }

        var combinedMap = vectorResults.Concat(ftsResults).Concat(exactMatches).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());

        var finalResults = rrfScores
            .OrderByDescending(kvp => kvp.Value)
            .Take(topK)
            .Select(kvp => combinedMap[kvp.Key])
            .ToList();

        return finalResults;
    }
}
