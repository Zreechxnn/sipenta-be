using SIAP.Api.Entities;

namespace SIAP.Api.Repositories.Interfaces;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(int id);
    Task<Role?> GetByNameAsync(string name);
    Task<IEnumerable<Role>> GetAllAsync();
}
