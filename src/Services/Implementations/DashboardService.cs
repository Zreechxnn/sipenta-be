using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SIPENTA.Api.Data;
using SIPENTA.Api.DTOs.Dashboard;
using SIPENTA.Api.Services.Interfaces;

namespace SIPENTA.Api.Services.Implementations;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache? _cache;

    public DashboardService(AppDbContext context, IMemoryCache? cache = null)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(Guid? userId = null, int? userBidangId = null, bool isAdmin = false)
    {
        var cacheKey = $"dashboard_summary_{isAdmin}_{userId}_{userBidangId}";
        if (_cache != null && _cache.TryGetValue(cacheKey, out DashboardSummaryDto? cached) && cached != null)
        {
            return cached;
        }

        int totalUsers;
        int pendingUsers;

        if (isAdmin)
        {
            totalUsers = await _context.Users.AsNoTracking().CountAsync();
            pendingUsers = await _context.Users.AsNoTracking().CountAsync(u => !u.IsApproved);
        }
        else
        {
            // For Kepala Bidang (Admin Bidang) / Bidang-scoped roles:
            totalUsers = await _context.Users.AsNoTracking().CountAsync(u => u.BidangId == userBidangId);
            pendingUsers = await _context.Users.AsNoTracking().CountAsync(u => !u.IsApproved && (u.BidangId == null || u.BidangId == userBidangId));
        }
        
        var documentsQuery = _context.Documents.AsNoTracking().AsQueryable();

        // If not admin, restrict to owner documents, same Bidang documents, or explicitly shared documents
        if (!isAdmin && userId.HasValue)
        {
            var uId = userId.Value;
            documentsQuery = documentsQuery.Where(d => 
                d.UserId == uId || 
                (userBidangId.HasValue && (d.BidangId == userBidangId.Value || (d.BidangId == null && d.User != null && d.User.BidangId == userBidangId.Value))) || 
                d.Accesses.Any(a => a.UserId == uId));
        }

        var totalDocuments = await documentsQuery.CountAsync();
        var totalStorage = await documentsQuery.Select(d => (long?)d.Ukuran).SumAsync() ?? 0;

        var docsByBidang = await documentsQuery
            .GroupBy(d => d.Bidang != null ? d.Bidang.Nama : "Tanpa Bidang")
            .Select(g => new DocumentsByBidangDto
            {
                BidangName = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        var recentDocs = await documentsQuery
            .OrderByDescending(d => d.TanggalUpload)
            .Take(5)
            .Select(d => new RecentDocumentDto
            {
                Id = d.Id.ToString(),
                Nama = d.Nama ?? d.NamaFile,
                UploaderName = d.User != null ? (d.User.FullName ?? d.User.Username) : "Unknown",
                CreatedAt = d.TanggalUpload
            })
            .ToListAsync();

        var summary = new DashboardSummaryDto
        {
            TotalUsers = totalUsers,
            PendingUsers = pendingUsers,
            TotalDocuments = totalDocuments,
            TotalStorageBytes = totalStorage,
            DocumentsByBidang = docsByBidang,
            RecentDocuments = recentDocs
        };

        _cache?.Set(cacheKey, summary, TimeSpan.FromSeconds(20));

        return summary;
    }
}

