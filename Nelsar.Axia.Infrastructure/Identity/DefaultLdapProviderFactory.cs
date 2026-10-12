using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Infrastructure.Identity.Abstractions;
using Nelsar.Axia.Infrastructure.Identity.Providers;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Domain.ValueObjects;
using Novell.Directory.Ldap;

namespace Nelsar.Axia.Infrastructure.Identity;

/// <summary>
/// Turns the directory named by <c>X-LDAP-Provider-Id</c> into the provider that talks to it.
///
/// A request that names a directory gets that directory or an error — never a different one.
/// This used to fall back to the configured default whenever the named provider could not be
/// resolved: unknown id, deactivated row, every host unreachable, or any exception on the way.
/// The read cases were merely confusing; the write cases were not. A DELETE aimed at the branch
/// domain, sent while that domain was unreachable, was carried out against the default domain
/// instead, and answered 200 — a deletion in the wrong directory, reported as success, with a
/// single Warning in the log as the only trace.
///
/// So the fallback now applies to exactly one case: no header at all, meaning the caller
/// expressed no preference and the configured default is what they meant. Anything else throws,
/// and the two ways it can fail are kept apart because they are different problems for whoever
/// gets the response — a wrong id is the caller's to fix (400), an unreachable directory is not
/// (502).
/// </summary>
internal class DefaultLdapProviderFactory : ILdapProviderFactory
{
    private const string CacheKeyPrefix = "ldap-provider-resolve:";

    // Resolve() blocks synchronously on a DB lookup and probes TCP reachability for
    // every candidate host — both real per-request costs. Caching the outcome means that cost is
    // paid once per TTL window instead of on every request that carries X-LDAP-Provider-Id. A
    // provider config change via PUT /api/v1/providers/{id} can take up to this long to take
    // effect.
    private static readonly TimeSpan ResolveCacheTtl = TimeSpan.FromSeconds(30);

    // Failures are cached too, and deliberately for much less time. Not caching them at all
    // would let a repeated request for a dead domain re-probe every host on every call — a
    // two-second connect timeout each, which is a way to tie up the server by asking for a
    // directory that is down. Caching them for as long as a success would leave a domain
    // refusing work for half a minute after it came back.
    private static readonly TimeSpan FailureCacheTtl = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _sp;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DefaultLdapProviderFactory> _logger;
    private readonly ILdapProvider _defaultProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IProviderRepository _providerRepository;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly LdapProviderScope _providerScope;

    public DefaultLdapProviderFactory(
        IServiceProvider sp,
        IConfiguration configuration,
        ILogger<DefaultLdapProviderFactory> logger,
        ILdapProvider defaultProvider,
        IHttpContextAccessor httpContextAccessor,
        IProviderRepository providerRepository,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        LdapProviderScope providerScope)
    {
        _sp = sp;
        _configuration = configuration;
        _logger = logger;
        _defaultProvider = defaultProvider;
        _httpContextAccessor = httpContextAccessor;
        _providerRepository = providerRepository;
        _encryptionService = encryptionService;
        _cache = cache;
        _providerScope = providerScope;
    }

    public ILdapProvider Resolve(HttpContext? context = null)
    {
        context ??= _httpContextAccessor.HttpContext;

        // Nothing to read a header off: this is work dispatched away from the request thread, and
        // the directory it is for has to have been stated by whoever dispatched it. Reaching for
        // the default here regardless is what carried a queued bulk batch into the wrong domain —
        // see LdapProviderScope.
        if (context is null)
        {
            var scoped = _providerScope.ProviderId;
            return scoped is null ? _defaultProvider : Resolve(scoped.Value.ToString());
        }

        var id = context.Items["LdapProviderId"] as string;

        // The only fallback there is: no header, no preference, so the configured default is
        // what the caller meant.
        if (string.IsNullOrWhiteSpace(id))
            return _defaultProvider;

        // Named a directory: it is that one or nothing. Notably no try/catch — swallowing here
        // is what turned "your header is wrong" into "operation performed on another domain".
        return Resolve(id);
    }

    /// <exception cref="DomainValidationException">
    /// No provider answers to that id, or the one that does has been deactivated.
    /// </exception>
    /// <exception cref="DirectoryUnavailableException">
    /// The provider exists and is active, but none of its hosts could be reached.
    /// </exception>
    public ILdapProvider Resolve(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return _defaultProvider;

        var resolution = _cache.GetOrCreate(CacheKeyPrefix + providerId, entry =>
        {
            var result = ResolveUncached(providerId);
            entry.AbsoluteExpirationRelativeToNow =
                result.Outcome == ResolutionOutcome.Resolved ? ResolveCacheTtl : FailureCacheTtl;
            return result;
        })!;

        // Thrown fresh rather than cached and rethrown, so the stack belongs to the request that
        // is actually failing rather than to whichever one first found the directory missing.
        return resolution.Outcome switch
        {
            ResolutionOutcome.Unknown => throw new DomainValidationException(
                ApplicationMessages.DirectoryProviderUnknown(providerId)),
            ResolutionOutcome.Unreachable => throw new DirectoryUnavailableException(
                ApplicationMessages.DirectoryProviderUnreachable(providerId)),
            // 502 alongside "unreachable" rather than a 500, and deliberately: from the caller's
            // side both mean this directory cannot serve them, and neither is theirs to fix. A
            // 500 would file it next to genuine bugs, where an outage that wants somebody paged
            // is exactly what it is.
            ResolutionOutcome.Misconfigured => throw new DirectoryUnavailableException(
                ApplicationMessages.DirectoryProviderMisconfigured(providerId)),
            _ => resolution.Provider!
        };
    }

    private Resolution ResolveUncached(string providerId)
    {
        // /api/v1/providers CRUD persists connection profiles as DB rows keyed by Guid;
        // prefer that source of truth so the API actually controls what X-LDAP-Provider-Id resolves to.
        if (Guid.TryParse(providerId, out var providerGuid))
        {
            var dbProvider = _providerRepository.GetByIdAsync(providerGuid).GetAwaiter().GetResult();
            if (dbProvider is { IsActive: true })
                return ResolveFromDbProvider(dbProvider, providerGuid);

            // Absent or deactivated — fall through, because a config-declared provider may still
            // be keyed by this id.
        }

        // Fall back to config-based providers: Ldap:Providers:{id} in appsettings.
        var section = _configuration.GetSection($"Ldap:Providers:{providerId}");
        if (!section.Exists())
        {
            _logger.LogWarning("Request named provider {ProviderId}, which is neither an active row nor a configured section.",
                providerId);
            return Resolution.Unknown;
        }

        var settings = section.Get<LdapSettings>() ?? new LdapSettings();
        settings.TrustedCaCertificatePath ??= HostTrustedCaCertificatePath;

        // The host's timeouts unless this section sets its own. Bound as it is, an unset key would
        // otherwise read as the class default and quietly ignore what the Ldap section says.
        if (section[nameof(LdapSettings.ConnectTimeoutSeconds)] is null)
            settings.ConnectTimeoutSeconds = HostConnectTimeoutSeconds;
        if (section[nameof(LdapSettings.OperationTimeoutSeconds)] is null)
            settings.OperationTimeoutSeconds = HostOperationTimeoutSeconds;
        var servers = section.GetValue<string>("Servers") ?? settings.Server;

        // Checked here rather than at startup because these sections are only read when a
        // request names one. Misconfigured, like a database row that cannot work: retrying will
        // not change the port.
        if (LdapSettingsRules.Problem(settings, $"Ldap:Providers:{providerId}") is { } problem)
        {
            _logger.LogError("Configured provider {ProviderId} cannot be used: {Problem}", providerId, problem);
            return Resolution.Misconfigured;
        }

        var resolvedFromConfig = TryResolveFromHosts(SplitHosts(servers), settings.ResolvedPort, settings, isActiveDirectory: false);
        if (resolvedFromConfig != null)
            return Resolution.Resolved(resolvedFromConfig);

        _logger.LogError("No available LDAP endpoints found for configured provider {ProviderId}.", providerId);
        return Resolution.Unreachable;
    }

    /// <summary>Why a provider is or is not usable — the thing worth caching, rather than an
    /// exception carrying one request's stack into another's.</summary>
    private enum ResolutionOutcome
    {
        Resolved,
        Unknown,
        Unreachable,
        Misconfigured
    }

    private sealed record Resolution(ResolutionOutcome Outcome, ILdapProvider? Provider)
    {
        public static Resolution Resolved(ILdapProvider provider) => new(ResolutionOutcome.Resolved, provider);

        public static readonly Resolution Unknown = new(ResolutionOutcome.Unknown, null);

        public static readonly Resolution Unreachable = new(ResolutionOutcome.Unreachable, null);

        public static readonly Resolution Misconfigured = new(ResolutionOutcome.Misconfigured, null);
    }

    private Resolution ResolveFromDbProvider(Provider dbProvider, Guid providerId)
    {
        if (!TryRevealBindPassword(dbProvider, providerId, out var bindPassword))
            return Resolution.Misconfigured;

        var hosts = new[] { dbProvider.Host }
            .Concat(SplitHosts(dbProvider.FailoverHosts))
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .ToArray();

        var settings = new LdapSettings
        {
            Server = dbProvider.Host,
            Port = dbProvider.Port.Value,
            Security = dbProvider.Security,
            // A row has no CA of its own; the host's is the one there is. Nor timeouts.
            TrustedCaCertificatePath = HostTrustedCaCertificatePath,
            ConnectTimeoutSeconds = HostConnectTimeoutSeconds,
            OperationTimeoutSeconds = HostOperationTimeoutSeconds,
            BaseDn = dbProvider.BaseDn,
            AdminUsername = dbProvider.AdminUsername,
            AdminPassword = bindPassword,
            UserSearchFilter = dbProvider.UserSearchFilter.Template,
            ProviderType = dbProvider.ProviderType,
            UserSearchPageSize = dbProvider.UserSearchPageSize
        };

        var isActiveDirectory = dbProvider.ProviderType.Equals("ActiveDirectory", StringComparison.OrdinalIgnoreCase);

        // Same platform limit as the configured default directory, except that this one arrives
        // from a database row rather than from appsettings, so it cannot be caught at startup.
        // Misconfigured rather than Unreachable: the host may well be up, and no amount of
        // retrying will make System.DirectoryServices exist here.
        if (isActiveDirectory && !OperatingSystem.IsWindows())
        {
            _logger.LogError(
                "DB provider {ProviderId} ({Name}) asks for ProviderType=ActiveDirectory, which needs "
                + "Windows; this host is {OperatingSystem}. StandardLdap reaches Active Directory too.",
                providerId, dbProvider.Name, RuntimeInformation.OSDescription);
            return Resolution.Misconfigured;
        }

        if (isActiveDirectory && dbProvider.Security == LdapSecurity.StartTls)
        {
            _logger.LogError(
                "DB provider {ProviderId} ({Name}) asks for ProviderType=ActiveDirectory with StartTls, which "
                + "System.DirectoryServices cannot do. Use Ldaps, or ProviderType StandardLdap.",
                providerId, dbProvider.Name);
            return Resolution.Misconfigured;
        }

        var resolved = TryResolveFromHosts(hosts, dbProvider.Port.Value, settings, isActiveDirectory);

        if (resolved != null)
            return Resolution.Resolved(resolved);

        // Error, not Warning: the directory an administrator asked for is down, and every
        // request naming it is failing until somebody looks.
        _logger.LogError("No reachable LDAP endpoint for DB provider {ProviderId} ({Name}).",
            providerId, dbProvider.Name);
        return Resolution.Unreachable;
    }

    /// <summary>
    /// Unwraps the stored bind credential, or reports that it cannot be.
    ///
    /// This is the one place the ciphertext is deliberately unwrapped: it is on its way to the
    /// decryptor, and everywhere else <see cref="EncryptedSecret"/> keeps it out of strings.
    ///
    /// It is also a third way a provider can fail, and until now the only one that escaped as an
    /// unhandled exception: a 500 with a stack trace, logged as "unhandled exception processing
    /// POST /api/v1/auth/login" and naming no provider at all. An operator then had to read the
    /// stack to learn which of their directories was broken — for a failure that is neither rare
    /// nor mysterious. Rotating <c>Encryption:Key</c> without re-encrypting the stored passwords
    /// does it to every provider at once, and a hand-edited row does it to one.
    ///
    /// The caller is told the directory is unusable rather than that decryption failed, since
    /// what they can do about it is the same either way; which of the causes below it was belongs
    /// in the log, with the exception, where the person who can fix it is looking.
    /// </summary>
    private bool TryRevealBindPassword(Provider dbProvider, Guid providerId, out string bindPassword)
    {
        if (dbProvider.EncryptedAdminPassword.IsEmpty)
        {
            // An anonymous bind — a real configuration for a read-only directory, not a failure.
            bindPassword = string.Empty;
            return true;
        }

        try
        {
            bindPassword = _encryptionService.Decrypt(dbProvider.EncryptedAdminPassword.Reveal());
            return true;
        }
        catch (Exception ex) when (ex is FormatException          // the stored payload is not enc:<nonce>:<cipher>:<tag>
                                      or CryptographicException   // wrong key, or the row was tampered with
                                      or ArgumentException        // nonce or tag of the wrong length
                                      or InvalidOperationException) // Encryption:Key is not configured at all
        {
            // The exception travels to the log and no further. None of these carry plaintext —
            // they describe the shape of the payload, not its contents — so logging it costs
            // nothing and is the only thing that names the actual cause.
            _logger.LogError(ex, "Bind credential for DB provider {ProviderId} ({Name}) could not be read.",
                providerId, dbProvider.Name);

            bindPassword = string.Empty;
            return false;
        }
    }

    private ILdapProvider? TryResolveFromHosts(IEnumerable<string> hosts, int port, LdapSettings baseSettings, bool isActiveDirectory)
    {
        foreach (var host in hosts)
        {
            if (!TryConnect(host, port))
                continue;

            var options = Options.Create(baseSettings.ForEndpoint(host, port));

            if (isActiveDirectory)
            {
                // Written as a guard rather than left to the caller's check so that the platform
                // analyser can see it. Both callers have already refused this combination, so
                // reaching here off Windows would be a bug in this file, not a bad configuration.
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException(
                        "ActiveDirectoryProvider was reached on a non-Windows host; callers are "
                        + "expected to resolve this to Misconfigured first.");

                var adLogger = _sp.GetService<ILogger<ActiveDirectoryProvider>>();
                return new ActiveDirectoryProvider(options, adLogger!);
            }

            var logger = _sp.GetService<ILogger<StandardLdapProvider>>();
            return new StandardLdapProvider(
                options, new LdapConnectionFactory(options, _sp.GetService<ILogger<LdapConnectionFactory>>()), logger!);
        }

        return null;
    }

    /// <summary>The host-wide CA for directories that do not name one of their own.</summary>
    private string? HostTrustedCaCertificatePath => _configuration["Ldap:TrustedCaCertificatePath"];

    // The host-wide timeouts, for the same directories. The Ldap section is validated at startup,
    // so whatever these read has already been held to its bounds.
    private int HostConnectTimeoutSeconds =>
        _configuration.GetValue("Ldap:ConnectTimeoutSeconds", LdapSettings.DefaultConnectTimeoutSeconds);

    private int HostOperationTimeoutSeconds =>
        _configuration.GetValue("Ldap:OperationTimeoutSeconds", LdapSettings.DefaultOperationTimeoutSeconds);

    private static string[] SplitHosts(string? hosts) =>
        (hosts ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Whether anything accepts a TCP connection at the endpoint — nothing more, and nothing is
    /// sent. This used to be an LDAP connect and disconnect, which ends with an unbind written in
    /// plain text: on an LDAPS port, LDAP bytes where a TLS handshake belongs. Whether TLS works is
    /// the real connection's question, where a failure is reported as what it is. A directory may
    /// still log the probe as a connection closed before its handshake; that is the probe, once
    /// per resolution, not a fault.
    /// </summary>
    private bool TryConnect(string host, int port, int timeoutMs = 2000)
    {
        try
        {
            using var timeout = new CancellationTokenSource(timeoutMs);
            using var client = new TcpClient();
            client.ConnectAsync(host, port, timeout.Token).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to connect to LDAP server {Host}:{Port}", host, port);
            return false;
        }
    }
}
