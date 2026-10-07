using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Tests;

internal static class AuthorizedPipeline
{
    /// <summary>
    /// Puts a caller holding every permission behind the MediatR pipeline.
    ///
    /// Use cases declare what they require (see <c>IRequirePermission</c>) and
    /// AuthorizationBehaviour enforces it, so a pipeline built with no principal refuses before
    /// any handler runs. A test about what a use case <em>does</em> should not have to be a test
    /// of who may call it as well; refusal is covered on its own by
    /// <see cref="UseCaseAuthorizationTests"/>.
    ///
    /// This mirrors what ApiTestFactory does for tests that go over HTTP, and for the same reason.
    /// </summary>
    public static IServiceCollection WithAuthorizedCaller(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentUser>(new TestCurrentUser("admin@example.com"));
        services.AddSingleton<IPermissionResolver>(new FixedPermissionResolver([.. Permissions.All]));
        return services.WithAuditTrail();
    }

    /// <summary>
    /// Somewhere for AuditBehaviour to write. Every command is audited, so a pipeline with no
    /// audit repository cannot run one; tests that are not about the trail get this in-memory one,
    /// and a test that registers its own keeps it.
    /// </summary>
    public static IServiceCollection WithAuditTrail(this IServiceCollection services)
    {
        services.TryAddSingleton<IAuditRepository>(new RecordingAuditRepository());
        return services.WithNoRoleMappings();
    }

    /// <summary>
    /// Empty role tables, for PrivilegeGuard to read when a use case checks its target. No group
    /// is mapped to anything, so it never refuses on a group's account; whether it refuses on a
    /// user's is the permission resolver's answer. TryAdd, so a test that registers its own wins.
    /// </summary>
    public static IServiceCollection WithNoRoleMappings(this IServiceCollection services)
    {
        var mappings = new Mock<IRoleMappingRepository>();
        mappings.Setup(x => x.GetActiveForProviderAsync(It.IsAny<Guid?>())).ReturnsAsync([]);
        services.TryAddSingleton(mappings.Object);

        var grants = new Mock<IRolePermissionRepository>();
        grants.Setup(x => x.GetPermissionsForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
        services.TryAddSingleton(grants.Object);

        return services;
    }
}

/// <summary>Keeps what was written to the audit trail, in order, for a test to read back.</summary>
internal sealed class RecordingAuditRepository : IAuditRepository
{
    private readonly ConcurrentQueue<AuditEvent> _events = new();

    public IReadOnlyList<AuditEvent> Events => [.. _events];

    public Task AddAsync(AuditEvent evt)
    {
        _events.Enqueue(evt);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<AuditEvent> Items, bool HasMore)> GetRecentAsync(int offset, int pageSize) =>
        Task.FromResult<(IReadOnlyList<AuditEvent>, bool)>(
            ([.. _events.Reverse().Skip(offset).Take(pageSize)], _events.Count > offset + pageSize));
}
