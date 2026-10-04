using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Infrastructure.Persistence.Repositories;

internal class UserLookupRepository : IUserLookupRepository
{
    private readonly IAppDbContext _db;

    public UserLookupRepository(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<string?> FindEmailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await _db.UserLookups
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return entry?.Email;
    }

    public async Task RecordAsync(Guid id, string email, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.UserLookups.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (existing == null)
            _db.UserLookups.Add(UserLookupEntry.Remember(id, email, now));
        else
            existing.SeenAgain(email, now);

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _db.UserLookups.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing == null)
            return;

        _db.UserLookups.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
