using Microsoft.EntityFrameworkCore;
using SIAP.Api.Data;
using SIAP.Api.Entities;
using SIAP.Api.Repositories.Interfaces;

namespace SIAP.Api.Repositories.Implemenations;

public class BidangRepository : IBidangRepository
{
    private readonly AppDbContext _context;

    public BidangRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Bidang>> GetAllAsync()
    {
        return await _context.Bidangs
            .Include(b => b.Users)
            .Include(b => b.Documents)
            .OrderBy(b => b.Id)
            .ToListAsync();
    }

    public async Task<Bidang?> GetByIdAsync(int id)
    {
        return await _context.Bidangs
            .Include(b => b.Users)
            .Include(b => b.Documents)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Bidang?> GetByNamaAsync(string nama)
    {
        return await _context.Bidangs
            .Include(b => b.Users)
            .Include(b => b.Documents)
            .FirstOrDefaultAsync(b => EF.Functions.ILike(b.Nama, nama) || (b.Kode != null && EF.Functions.ILike(b.Kode, nama)));
    }

    public async Task<Bidang> AddAsync(Bidang bidang)
    {
        await _context.Bidangs.AddAsync(bidang);
        await _context.SaveChangesAsync();
        return bidang;
    }

    public async Task UpdateAsync(Bidang bidang)
    {
        bidang.UpdatedAt = DateTime.UtcNow;
        _context.Bidangs.Update(bidang);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Bidang bidang)
    {
        _context.Bidangs.Remove(bidang);
        await _context.SaveChangesAsync();
    }
}
