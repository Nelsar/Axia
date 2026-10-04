using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Services;

internal class AuditService : IAuditService
{
    private readonly IAuditRepository _repo;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IAuditRepository repo, ILogger<AuditService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task LogAuthenticationAsync(string actor, bool success, string? dn = null, string? details = null)
    {
        Guid? objectId = null;
        if (!string.IsNullOrWhiteSpace(dn))
            objectId = BuildGuidFromString(dn);

        var evt = AuditEvent.Authentication(actor, success, objectId, dn, details, DateTimeOffset.UtcNow);

        try
        {
            await _repo.AddAsync(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save audit event");
        }
    }

    public async Task LogModificationAsync(Guid? objectId, string? objectDn, string actor, string action, bool success, string? details = null)
    {
        Guid? id = objectId;
        if (!id.HasValue && !string.IsNullOrWhiteSpace(objectDn))
            id = BuildGuidFromString(objectDn);

        var evt = AuditEvent.Modification(id, objectDn, actor, action, success, details, DateTimeOffset.UtcNow);

        try
        {
            await _repo.AddAsync(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save audit event");
        }
    }


    private static Guid BuildGuidFromString(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return new Guid(hash.AsSpan(0, 16));
    }
}
