using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Infrastructure.Persistence.Repositories;

internal class RateLimitSettingsRepository : IRateLimitSettingsRepository
{
    private readonly IAppDbContext _db;

    public RateLimitSettingsRepository(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<RateLimitSettings>> GetAllAsync()
    {
        return await _db.RateLimitSettings.AsNoTracking().OrderBy(x => x.PolicyName).ToListAsync();
    }

    public async Task<RateLimitSettings?> GetByPolicyNameAsync(string policyName)
    {
        return await _db.RateLimitSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PolicyName == policyName);
    }

    /// <summary>
    /// Creates the row for a policy name, or applies the new limits to the existing one.
    ///
    /// The caller identifies the row by policy name rather than by id, so an update genuinely has
    /// to carry values across from one instance to another. It does so through
    /// <see cref="RateLimitSettings.Adjust"/> and Enable/Disable rather than by assigning fields,
    /// which means the row that ends up in the table has passed the same checks as one built
    /// from scratch — the point the old copy loop quietly missed.
    /// </summary>
    public async Task<RateLimitSettings> UpsertAsync(RateLimitSettings settings)
    {
        var existing = await _db.RateLimitSettings.FirstOrDefaultAsync(x => x.PolicyName == settings.PolicyName);
        if (existing == null)
        {
            _db.RateLimitSettings.Add(settings);
            await _db.SaveChangesAsync();
            return settings;
        }

        existing.Adjust(settings.PermitLimit, settings.WindowSeconds, settings.QueueLimit);

        if (settings.Enabled)
            existing.Enable();
        else
            existing.Disable();

        await _db.SaveChangesAsync();
        return existing;
    }
}
