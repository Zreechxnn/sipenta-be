using SIAP.Api.Entities;

namespace SIAP.Api.Repositories.Interfaces;

public interface IBidangRepository
{
    Task<IEnumerable<Bidang>> GetAllAsync();
    Task<Bidang?> GetByIdAsync(int id);
    Task<Bidang?> GetByNamaAsync(string nama);
    Task<Bidang> AddAsync(Bidang bidang);
    Task UpdateAsync(Bidang bidang);
    Task DeleteAsync(Bidang bidang);
}
