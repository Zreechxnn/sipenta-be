using Microsoft.EntityFrameworkCore;
using SIPENTA.Api.Data;
using SIPENTA.Api.Entities;
using SIPENTA.Api.Repositories.Interfaces;

namespace SIPENTA.Api.Repositories.Implementations;

public class RoleRepository : IRoleRepository
{
    private readonly AppDbContext _context;

    public RoleRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Role?> GetByIdAsync(int id)
    {
        return await _context.Roles.FindAsync(id);
    }

    public async Task<Role?> GetByNameAsync(string name)
    {
        return await _context.Roles.FirstOrDefaultAsync(r => r.Name == name);
    }

    public async Task<IEnumerable<Role>> GetAllAsync()
    {
        return await _context.Roles.ToListAsync();
    }
}
