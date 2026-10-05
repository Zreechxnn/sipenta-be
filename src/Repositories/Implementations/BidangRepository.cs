using Microsoft.EntityFrameworkCore;
using SIPENTA.Api.Data;
using SIPENTA.Api.DTOs;
using SIPENTA.Api.Entities;
using SIPENTA.Api.Repositories.Interfaces;


namespace SIPENTA.Api.Repositories.Implementations;

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
            .AsNoTracking()
            .OrderBy(b => b.Id)
            .ToListAsync();
    }

    public async Task<List<BidangDto>> GetAllDtosAsync()
    {
        return await _context.Bidangs
            .AsNoTracking()
            .OrderBy(b => b.Id)
            .Select(b => new BidangDto
            {
                Id = b.Id,
                Nama = b.Nama,
                Kode = b.Kode,
                Deskripsi = b.Deskripsi,
                UserCount = b.Users.Count(),
                DocumentCount = b.Documents.Count(),
                CreatedAt = b.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<Bidang?> GetByIdAsync(int id)
    {
        return await _context.Bidangs
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<BidangDto?> GetByIdDtoAsync(int id)
    {
        return await _context.Bidangs
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BidangDto
            {
                Id = b.Id,
                Nama = b.Nama,
                Kode = b.Kode,
                Deskripsi = b.Deskripsi,
                UserCount = b.Users.Count(),
                DocumentCount = b.Documents.Count(),
                CreatedAt = b.CreatedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<Bidang?> GetByNamaAsync(string nama)
    {
        return await _context.Bidangs
            .AsNoTracking()
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
