using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Runtime.InteropServices;
using System.Text;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Services;
using Nelsar.Axia.Contracts.Identity;
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
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddLdapIdentity(configuration);
        services.AddApplicationServices(configuration);
        services.AddBackgroundJobs();
        services.AddAuthenticationScheme(configuration);

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // Read now rather than when the context is first resolved, so that a missing or unknown
        // provider stops startup with the setting named. See AxiaDatabase.FromConfiguration.
        var (provider, connectionString) = AxiaDatabase.FromConfiguration(configuration);
        services.AddDbContext<AppDbContext>(options => options.UseAxiaDatabase(provider, connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        return services;
    }

    private static IServiceCollection AddLdapIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        // Checked when the host starts: a security mode that cannot work on the configured port
        // — LDAPS on 389, the shape of every configuration written before LDAPS was the default —
        // stops startup with what to change, instead of failing every sign-in. See LdapSettingsRules.
        services.AddOptions<LdapSettings>()
            .Bind(configuration.GetSection("Ldap"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LdapSettings>, LdapSettingsValidation>();

        // The provider is chosen by configuration: Ldap:ProviderType = "ActiveDirectory" | "StandardLdap"
        var ldapSettings = configuration.GetSection("Ldap").Get<LdapSettings>();
        if (ldapSettings?.ProviderType?.Equals("ActiveDirectory", StringComparison.OrdinalIgnoreCase) == true)
        {
            // ActiveDirectoryProvider is built on System.DirectoryServices, which ships only on
            // Windows. Registering it elsewhere starts the application and then fails every
            // directory call with a PlatformNotSupportedException from inside the provider —
            // nowhere near the setting that caused it. Refusing at startup puts the message where
            // whoever edited the configuration is looking.
            if (!OperatingSystem.IsWindows())
            {
                throw new InvalidOperationException(
                    "Ldap:ProviderType is 'ActiveDirectory', which needs System.DirectoryServices and "
                    + $"therefore Windows; this host is {RuntimeInformation.OSDescription}. Use "
                    + "'StandardLdap' — it speaks to Active Directory as well as to OpenLDAP, over "
                    + "plain LDAP, on any platform.");
            }

            services.AddScoped<ILdapProvider, ActiveDirectoryProvider>();
        }
        else
        {
            services.AddScoped<LdapConnectionFactory>();
            services.AddScoped<ILdapProvider, StandardLdapProvider>();
        }

        // Checked when the host starts, and again by DbInitializer before it seeds anything: a
        // missing or malformed key used to mean bind passwords stored in plaintext, with only a
        // warning in the log to say so. See SecretRequirements.
        services.AddOptions<EncryptionSettings>()
            .Bind(configuration.GetSection(EncryptionSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EncryptionSettings>, EncryptionSettingsValidation>();

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
    private static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApplicationLayer();

        services.AddScoped<IUserLookupRepository, UserLookupRepository>();
        services.AddScoped<IProviderRepository, ProviderRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IRoleMappingRepository, RoleMappingRepository>();

        // How role mappings start out and whether mappings by group name count; checked at start,
        // see RoleMappingOptionsValidation. And which groups only an administrator may touch.
        services.AddOptions<RoleMappingOptions>()
            .Bind(configuration.GetSection(RoleMappingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RoleMappingOptions>, RoleMappingOptionsValidation>();
        services.AddOptions<DirectoryProtectionOptions>()
            .Bind(configuration.GetSection(DirectoryProtectionOptions.SectionName));
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
        // A placeholder, missing or short signing key stops the host from starting. The example
        // configuration's placeholder is public, and a token signed with it names whoever its
        // author chooses. See SecretRequirements.
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection("Jwt"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidation>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        // Bearer tokens are the only way in. There used to be a second scheme, LDAP credentials
        // in a Basic header on any request, and it was worse than redundant: it checked the
        // password against the default directory while the request was then served from
        // whichever directory X-LDAP-Provider-Id named, and it sat outside the sign-in rate limit,
        // so any endpoint could be used to guess passwords at full speed. The console never used
        // it.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => { });

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

                // A token has to say which directory entry it is for. Ones minted before the
                // claim existed say only which login, and resolving a login is the search that
                // two entries can answer — the hole the claim was added to close. They are
                // refused, rather than authorised the old way for the rest of their lifetime;
                // the console treats the 401 as an expired session and its user signs in again.
                jwtBearerOptions.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst(AxiaClaims.DistinguishedName)?.Value))
                            context.Fail("The token does not name a directory entry.");

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
