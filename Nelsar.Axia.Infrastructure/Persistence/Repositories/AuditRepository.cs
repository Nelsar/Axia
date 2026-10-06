using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Infrastructure.Persistence.Repositories
{
    internal class AuditRepository : IAuditRepository
    {
        private readonly IAppDbContext _db;

        public AuditRepository(IAppDbContext db)
        {
            _db = db;
        }

        public async Task AddAsync(AuditEvent evt)
        {
            // Id and timestamp arrive with the event: an audit entry states when the thing it
            // records happened, and stamping it on the way to the table dated it to when the
            // write got round to running instead.
            ArgumentNullException.ThrowIfNull(evt);
            await _db.AuditEvents.AddAsync(evt);
            await _db.SaveChangesAsync();
        }

        public async Task<(IReadOnlyList<AuditEvent> Items, bool HasMore)> GetRecentAsync(int offset, int pageSize)
        {
            // Ordered and paged by the database. This used to load every row and sort in memory,
            // because EF Core cannot ORDER BY a DateTimeOffset on SQLite; the timestamp is now
            // stored as UTC text that sorts correctly (see AuditEventConfiguration). One row past
            // the page answers whether there is another page, without counting the table.
            // Id breaks ties, so two entries in the same tick cannot swap between pages.
            var rows = await _db.AuditEvents.AsNoTracking()
                .OrderByDescending(e => e.Timestamp)
                .ThenByDescending(e => e.Id)
                .Skip(offset)
                .Take(pageSize + 1)
                .ToListAsync();

            var hasMore = rows.Count > pageSize;
            return (hasMore ? rows.GetRange(0, pageSize) : rows, hasMore);
        }
    }
}
