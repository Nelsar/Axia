using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Infrastructure.Persistence.Repositories;

internal class ProviderRepository : IProviderRepository
{
    private readonly IAppDbContext _db;

    public ProviderRepository(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Provider> AddAsync(Provider provider)
    {
        // The id arrives with the aggregate — Provider.Create assigns it, so there is no moment
        // at which a provider exists without an identity, and no second place that decides one.
        _db.Providers.Add(provider);
        await _db.SaveChangesAsync();
        return provider;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var p = await _db.Providers.FindAsync(id);
        if (p == null) return false;
        _db.Providers.Remove(p);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<Provider>> GetAllAsync()
    {
        return await _db.Providers.AsNoTracking().ToListAsync();
    }

    public async Task<Provider?> GetByIdAsync(Guid id)
    {
        return await _db.Providers.FindAsync(id);
    }

    /// <summary>
    /// Persists changes already made to the aggregate returned by <see cref="GetByIdAsync"/>.
    ///
    /// It used to copy twelve fields from a detached <see cref="Provider"/> onto the tracked one.
    /// That copy was a second way to write a provider — one that answered to no invariant, so a
    /// rule enforced by the aggregate was undone here by assignment. Now the use case mutates the
    /// tracked instance through the aggregate's own methods and this only decides whether the row
    /// still exists and commits.
    /// </summary>
    public async Task<Provider?> UpdateAsync(Provider provider)
    {
        var exists = await _db.Providers.FindAsync(provider.Id);
        if (exists == null) return null;

        await _db.SaveChangesAsync();
        return exists;
    }
}
