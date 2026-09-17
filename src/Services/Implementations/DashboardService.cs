using Microsoft.EntityFrameworkCore;
using SIAP.Api.Data;
using SIAP.Api.DTOs.Dashboard;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(Guid? userId = null, int? userBidangId = null, bool isAdmin = false)
    {
        int totalUsers;
        int pendingUsers;

        if (isAdmin)
        {
            totalUsers = await _context.Users.CountAsync();
            pendingUsers = await _context.Users.CountAsync(u => !u.IsApproved);
        }
        else
        {
            // For Kasubag (Admin Bidang) / Bidang-scoped roles:
            totalUsers = await _context.Users.CountAsync(u => u.BidangId == userBidangId);
            pendingUsers = await _context.Users.CountAsync(u => !u.IsApproved && (u.BidangId == null || u.BidangId == userBidangId));
        }
        
        var documentsQuery = _context.Documents.AsQueryable();

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
        var totalStorage = totalDocuments > 0 ? (await documentsQuery.Select(d => (long?)d.Ukuran).SumAsync() ?? 0) : 0;

        var docsByBidang = await documentsQuery
            .Include(d => d.Bidang)
            .GroupBy(d => d.Bidang != null ? d.Bidang.Nama : "Tanpa Bidang")
            .Select(g => new DocumentsByBidangDto
            {
                BidangName = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        var recentDocs = await documentsQuery
            .Include(d => d.User)
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

        return new DashboardSummaryDto
        {
            TotalUsers = totalUsers,
            PendingUsers = pendingUsers,
            TotalDocuments = totalDocuments,
            TotalStorageBytes = totalStorage,
            DocumentsByBidang = docsByBidang,
            RecentDocuments = recentDocs
        };
    }
}
