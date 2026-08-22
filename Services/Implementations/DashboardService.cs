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

    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var totalUsers = await _context.Users.CountAsync();
        var pendingUsers = await _context.Users.CountAsync(u => !u.IsApproved);
        
        var documentsQuery = _context.Documents.AsQueryable();
        var totalDocuments = await documentsQuery.CountAsync();
        
        var totalStorage = await documentsQuery.SumAsync(d => (long)d.Ukuran);

        var docsByBidang = await _context.Documents
            .Include(d => d.Bidang)
            .GroupBy(d => d.Bidang != null ? d.Bidang.Nama : "Tanpa Bidang")
            .Select(g => new DocumentsByBidangDto
            {
                BidangName = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        var recentDocs = await _context.Documents
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
