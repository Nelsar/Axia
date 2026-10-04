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
            // SQLite's EF Core provider can't translate ORDER BY over a DateTimeOffset
            // column server-side ("Convert the values to a supported type, or use LINQ
            // to Objects" — this is Microsoft's own guidance for this exact limitation),
            // so order/page client-side after loading. Audit volume here is modest
            // (login + modification events only); revisit if this table grows large.
            var all = await _db.AuditEvents.AsNoTracking().ToListAsync();
            var ordered = all.OrderByDescending(e => e.Timestamp).ToList();
            var items = ordered.Skip(offset).Take(pageSize).ToList();
            var hasMore = ordered.Count > offset + pageSize;
            return (items, hasMore);
        }
    }
}
