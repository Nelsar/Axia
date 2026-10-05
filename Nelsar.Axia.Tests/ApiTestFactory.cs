using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Contracts.Abstractions;

namespace Nelsar.Axia.Tests;

public class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection> _configureServices;
    private readonly IReadOnlyDictionary<string, string?> _configuration;

    // Computed once per factory, not inside ConfigureServices: WebApplicationFactory runs that
    // delegate more than once per instance, so generating the name there gave each host its own
    // database. Anything seeded through factory.Services then landed somewhere the requests
    // never looked.
    private readonly string _databaseName = $"TestDb-{Guid.NewGuid():N}";

    /// <param name="configuration">
    /// Extra configuration for this host, layered over the defaults below. It is how a test turns
    /// on a deployment switch that ships off — self-registration, say — without every other test
    /// inheriting it.
    /// </param>
    public ApiTestFactory(
        Action<IServiceCollection> configureServices,
        IReadOnlyDictionary<string, string?>? configuration = null)
    {
        _configureServices = configureServices;
        _configuration = configuration ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Fixed Jwt config used by both the auth pipeline (JwtBearer validation, bound at
        // startup from configuration) and TestAuth's token issuance, so tokens minted by
        // tests always validate regardless of what appsettings.json contains.
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestAuth.SigningKey,
                ["Jwt:Issuer"] = TestAuth.Issuer,
                ["Jwt:Audience"] = TestAuth.Audience,
                ["Jwt:ExpiresInMinutes"] = "60",

                // The host refuses to start without a usable key (see SecretRequirements), so
                // every test host gets one. Zero bytes are as good as any for a test.
                ["Encryption:Key"] = TestAuth.EncryptionKey,

                // No sinks under test. The file sink is the one that matters: xUnit runs these
                // classes in parallel, each with its own host and its own logger, and they would
                // all reach for the same day's log file. The console is off for the reader's
                // sake — a failing assertion should not have to be found among a few hundred
                // JSON events. What the log *contains* is asserted directly, in
                // ObservabilityTests, rather than as a side effect of these.
                ["Observability:Console:Enabled"] = "false",
                ["Observability:File:Enabled"] = "false"
            });
        });

        // UseSetting rather than another AddInMemoryCollection, because some settings are read
        // eagerly: Program.cs calls GetSection(...).Get<T>() while the app is being built, which
        // happens before ConfigureAppConfiguration's callbacks run. Host settings are in the
        // configuration from the start, so they reach both kinds of reader.
        foreach (var (key, value) in _configuration)
            builder.UseSetting(key, value);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IOptions<JwtSettings>));
            services.RemoveAll(typeof(AppDbContext));
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(DbContextOptions));

            // EF Core 9+ carries the provider selection (UseSqlite, set by
            // AddInfrastructureServices) in this registration rather than in DbContextOptions.
            // Leaving it behind registers Sqlite *and* InMemory in one container, and EF then
            // throws "Only a single database provider can be registered" the first time
            // anything resolves a DbSet.
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));

            // Per-test database name: the in-memory store is keyed by name and outlives an
            // individual host, so a fixed name would let one test's rows leak into the next.
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            // Authorisation now resolves permissions per request from LDAP group membership and
            // the RolePermissions table. Making every test that touches an endpoint stand up a
            // directory and a grant table would bury what each of them is actually about, so the
            // default here grants everything. Authorisation itself is covered by
            // PermissionAuthorizationTests, which substitutes the real resolver.
            services.AddScoped<IPermissionResolver, GrantEverythingPermissionResolver>();

            _configureServices?.Invoke(services);
        });
    }
}

/// <summary>Grants the whole catalogue, so a test can reach the endpoint it came for.</summary>
internal sealed class GrantEverythingPermissionResolver : IPermissionResolver
{
    public Task<IReadOnlySet<string>> ResolveAsync(
        string subject,
        Guid? providerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(
            Permissions.All.ToHashSet(StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// A signed-in caller, for tests that drive the MediatR pipeline directly rather than over HTTP.
/// Passing no subject stands in for an unauthenticated request.
/// </summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    public TestCurrentUser(string? subject = null, Guid? providerId = null, bool isSystem = false)
    {
        Subject = subject;
        ProviderId = providerId;
        IsSystem = isSystem;
    }

    public string? Subject { get; }

    public Guid? ProviderId { get; }

    public bool IsSystem { get; }
}

/// <summary>Grants exactly the listed permissions, for tests about refusal.</summary>
internal sealed class FixedPermissionResolver : IPermissionResolver
{
    private readonly IReadOnlySet<string> _permissions;

    public FixedPermissionResolver(params string[] permissions) =>
        _permissions = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlySet<string>> ResolveAsync(
        string subject,
        Guid? providerId,
        CancellationToken cancellationToken = default) => Task.FromResult(_permissions);
}

