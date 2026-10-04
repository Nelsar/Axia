using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Infrastructure.Persistence.Repositories;

internal class RoleMappingRepository : IRoleMappingRepository
{
    private readonly IAppDbContext _db;

    public RoleMappingRepository(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<RoleMapping> AddAsync(RoleMapping mapping)
    {
        // Id comes from RoleMapping.Create — see the note on ProviderRepository.AddAsync.
        _db.RoleMappings.Add(mapping);
        await _db.SaveChangesAsync();
        return mapping;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var m = await _db.RoleMappings.FindAsync(id);
        if (m == null) return false;
        _db.RoleMappings.Remove(m);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<RoleMapping>> GetAllAsync()
    {
        return await _db.RoleMappings.AsNoTracking().OrderBy(x => x.Priority).ToListAsync();
    }

    public async Task<RoleMapping?> GetByIdAsync(Guid id)
    {
        return await _db.RoleMappings.FindAsync(id);
    }

    public async Task<IEnumerable<RoleMapping>> GetActiveForProviderAsync(Guid? providerId)
    {
        return await _db.RoleMappings
            .AsNoTracking()
            .Where(x => x.ProviderId == null || x.ProviderId == providerId)
            .OrderBy(x => x.Priority)
            .ToListAsync();
    }

    /// <summary>
    /// Commits changes already applied to the tracked mapping — see the note on
    /// ProviderRepository.UpdateAsync for why the field-by-field copy is gone.
    /// </summary>
    public async Task<RoleMapping?> UpdateAsync(RoleMapping mapping)
    {
        var exists = await _db.RoleMappings.FindAsync(mapping.Id);
        if (exists == null) return null;

        await _db.SaveChangesAsync();
        return exists;
    }
}
