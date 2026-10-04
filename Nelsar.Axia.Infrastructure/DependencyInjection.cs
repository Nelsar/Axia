using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Infrastructure.Identity;
using Nelsar.Axia.Infrastructure.Identity.Abstractions;
using Nelsar.Axia.Infrastructure.Identity.Providers;
using Nelsar.Axia.Infrastructure.BackgroundServices;
using Nelsar.Axia.Infrastructure.Events;
using Nelsar.Axia.Infrastructure.Persistence;
using Nelsar.Axia.Infrastructure.Persistence.Repositories;
using Nelsar.Axia.Infrastructure.RateLimiting;

namespace Nelsar.Axia.Infrastructure;

public static class DependencyInjection
{
    private const string SmartAuthScheme = "SmartAuth";
    private const string LdapBasicScheme = "LdapBasic";

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddLdapIdentity(configuration);
        services.AddApplicationServices();
        services.AddBackgroundJobs();
        services.AddAuthenticationScheme(configuration);

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=nelsar-axia.db";
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        return services;
    }

    private static IServiceCollection AddLdapIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LdapSettings>(configuration.GetSection("Ldap"));

        // The provider is chosen by configuration: Ldap:ProviderType = "ActiveDirectory" | "StandardLdap"
        var ldapSettings = configuration.GetSection("Ldap").Get<LdapSettings>();
        if (ldapSettings?.ProviderType?.Equals("ActiveDirectory", StringComparison.OrdinalIgnoreCase) == true)
        {
            services.AddScoped<ILdapProvider, ActiveDirectoryProvider>();
        }
        else
        {
            services.AddScoped<LdapConnectionFactory>();
            services.AddScoped<ILdapProvider, StandardLdapProvider>();
        }

        services.AddScoped<IEncryptionService, AesGcmEncryptionService>();
        services.AddScoped<ILdapService, LdapService>();
        services.AddHttpContextAccessor();

        // Scoped, and the same instance the factory reads: how work dispatched off the request
        // thread states which directory it is for. Left unset during an HTTP request, where the
        // header is still the only source.
        services.AddScoped<LdapProviderScope>();
        services.AddScoped<ILdapProviderFactory, DefaultLdapProviderFactory>();

        return services;
    }

    /// <summary>
    /// The Application layer, plus the ports it declares and this layer implements.
    ///
    /// It used to also register that layer's own collaborators — UserDirectory, AuditService,
    /// PermissionResolver, the audit event subscriber. Those are internal to Application and
    /// registered by <c>AddApplicationLayer</c> now: naming them here meant Infrastructure knew
    /// how the layer above it is built, and kept every one of those types public for no reason
    /// but this file. What remains is what Infrastructure genuinely owns — the adapters.
    /// </summary>
    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddApplicationLayer();

        services.AddScoped<IUserLookupRepository, UserLookupRepository>();
        services.AddScoped<IProviderRepository, ProviderRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IRoleMappingRepository, RoleMappingRepository>();
        services.AddScoped<IRolePermissionRepository, RolePermissionRepository>();
        services.AddScoped<IRateLimitSettingsRepository, RateLimitSettingsRepository>();
        services.AddScoped<IRateLimitSettingsProvider, RateLimitSettingsProvider>();

        services.AddDomainEvents();

        return services;
    }

    /// <summary>
    /// Wires the mechanism that delivers domain events. Who listens for them is the Application
    /// layer's business and is registered there — this side only knows how a raised event gets
    /// to whoever subscribed.
    /// </summary>
    private static IServiceCollection AddDomainEvents(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();

        return services;
    }

    private static IServiceCollection AddBackgroundJobs(this IServiceCollection services)
    {
        services.AddSingleton<IBulkJobQueue, InMemoryBulkJobQueue>();
        services.AddSingleton<IBulkJobStore, InMemoryBulkJobStore>();
        services.AddHostedService<BulkUserBackgroundService>();

        return services;
    }

    private static IServiceCollection AddAuthenticationScheme(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = SmartAuthScheme;
            options.DefaultChallengeScheme = SmartAuthScheme;
        })
        .AddPolicyScheme(SmartAuthScheme, SmartAuthScheme, options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var authorization = context.Request.Headers.Authorization.ToString();

                if (!string.IsNullOrWhiteSpace(authorization) &&
                    authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                {
                    return LdapBasicScheme;
                }

                return JwtBearerDefaults.AuthenticationScheme;
            };
        })
        .AddJwtBearer(options => { })
        .AddScheme<AuthenticationSchemeOptions, LdapBasicAuthenticationHandler>(
            LdapBasicScheme,
            _ =>
            {
            }
        );

        // Bind JwtBearerOptions lazily from IOptions<JwtSettings> (resolved when the
        // scheme is first used, after the host is fully built) instead of capturing a
        // configuration snapshot at registration time — the latter froze the signing
        // key/issuer/audience before test hosts or config-reload providers could apply.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((jwtBearerOptions, jwtSettingsOptions) =>
            {
                var jwtSettings = jwtSettingsOptions.Value;
                jwtBearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings?.Issuer,
                    ValidAudience = jwtSettings?.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSettings?.Key ?? string.Empty))
                };
            });

        services.AddAuthorization();

        return services;
    }
}
