using SIPENTA.Api.DTOs;
using SIPENTA.Api.Entities;

namespace SIPENTA.Api.Repositories.Interfaces;

public interface IBidangRepository
{
    Task<IEnumerable<Bidang>> GetAllAsync();
    Task<List<BidangDto>> GetAllDtosAsync();
    Task<Bidang?> GetByIdAsync(int id);
    Task<BidangDto?> GetByIdDtoAsync(int id);
    Task<Bidang?> GetByNamaAsync(string nama);
    Task<Bidang> AddAsync(Bidang bidang);
    Task UpdateAsync(Bidang bidang);
    Task DeleteAsync(Bidang bidang);
}

