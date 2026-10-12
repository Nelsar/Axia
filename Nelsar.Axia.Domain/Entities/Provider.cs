using Nelsar.Axia.Domain.Common;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Resources;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Domain.Entities;

/// <summary>
/// A directory this application authenticates and manages accounts against — the aggregate root
/// for everything about one LDAP domain's connection.
///
/// It used to be a bag of public setters, which meant every rule about a provider lived at the
/// call sites that built one: two command handlers, a seeder and a repository that copied field
/// by field. Nothing stopped a blank host, a page size of zero, or a search filter with no
/// placeholder from reaching <c>LdapConnection</c>, and the repository's copy loop meant a rule
/// added to one handler was silently absent from the other.
///
/// Now state changes only through the methods below, each named for the operation an
/// administrator is actually performing, and each responsible for the invariant it touches. The
/// setters stay private so that remains the only way in — including for a future caller nobody
/// has written yet.
/// </summary>
public class Provider
{
    public const int MaximumNameLength = 256;
    public const int MaximumHostLength = 256;
    public const int MaximumProviderTypeLength = 128;
    public const int MaximumFailoverHostsLength = 1024;
    public const int MaximumBaseDnLength = 512;
    public const int MaximumAdminUsernameLength = 256;

    /// <summary>
    /// A page of one is pointless but harmless; zero or negative used to reach the paging
    /// control, where it means "no paging" on some servers and is an error on others. The upper
    /// bound is a guard against a value that would have the directory build a response large
    /// enough to exhaust memory here before it ever finished sending.
    /// </summary>
    public const int MinimumSearchPageSize = 1;
    public const int MaximumSearchPageSize = 10_000;
    public const int DefaultSearchPageSize = 500;

    /// <summary>For EF materialisation only — see the remarks on the class.</summary>
    private Provider()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Host { get; private set; } = string.Empty;
    public LdapPort Port { get; private set; } = LdapPort.Default;

    /// <summary>
    /// How the connection is protected. Rows written before this existed were migrated to
    /// <see cref="LdapSecurity.None"/>, which is what they had always been; anything created since
    /// gets LDAPS unless it says otherwise.
    /// </summary>
    public LdapSecurity Security { get; private set; } = LdapSecurity.Ldaps;

    public string ProviderType { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    /// <summary>
    /// Comma-separated additional hosts tried in order if <see cref="Host"/> is unreachable.
    /// </summary>
    public string? FailoverHosts { get; private set; }

    public string BaseDn { get; private set; } = string.Empty;
    public string AdminUsername { get; private set; } = string.Empty;

    /// <summary>
    /// The bind password at rest. Typed rather than a string so it cannot be logged or returned
    /// by accident — see <see cref="EncryptedSecret"/>. The property name is unchanged because it
    /// is the column name.
    /// </summary>
    public EncryptedSecret EncryptedAdminPassword { get; private set; } = EncryptedSecret.None;

    public LdapSearchFilter UserSearchFilter { get; private set; } = LdapSearchFilter.Default;
    public int UserSearchPageSize { get; private set; } = DefaultSearchPageSize;

    /// <summary>
    /// A provider that satisfies every invariant from the moment it exists. The id is assigned
    /// here rather than by the repository so there is no window in which a provider is a real
    /// object without an identity.
    /// </summary>
    /// <exception cref="DomainValidationException">Any field breaks its rule.</exception>
    public static Provider Create(
        string? name,
        string? host,
        LdapPort port,
        LdapSecurity security,
        string? providerType,
        bool isActive,
        string? failoverHosts,
        string? baseDn,
        string? adminUsername,
        EncryptedSecret encryptedAdminPassword,
        LdapSearchFilter userSearchFilter,
        int userSearchPageSize)
    {
        var provider = new Provider { Id = Guid.NewGuid() };

        provider.Rename(name);
        provider.PointAt(host, port, security, failoverHosts);
        provider.ChangeType(providerType);
        provider.UseSearchRoot(baseDn);
        provider.UseAdminAccount(adminUsername);
        provider.RotateAdminPassword(encryptedAdminPassword);
        provider.ConfigureUserSearch(userSearchFilter, userSearchPageSize);

        if (isActive)
            provider.Activate();
        else
            provider.Deactivate();

        return provider;
    }

    public void Rename(string? name) =>
        Name = DomainGuard.RequiredText(name, nameof(Name), MaximumNameLength);

    /// <summary>
    /// Moves the provider to a different endpoint. Host, port, security and failover list change
    /// together because they describe one thing — where and how to connect — and a half-applied
    /// change points the provider at a host on a port that was meant for the other one.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// The security mode cannot work on that port — LDAPS on 389, say, or plain LDAP on 636.
    /// </exception>
    public void PointAt(string? host, LdapPort port, LdapSecurity security, string? failoverHosts)
    {
        ArgumentNullException.ThrowIfNull(port);

        if (!Enum.IsDefined(security))
            throw new ArgumentOutOfRangeException(nameof(security), security, null);

        // Refused here rather than discovered at connect, where it surfaces as a TLS handshake
        // that fails or a server that hangs up — neither of which mentions the setting.
        if (LdapSecurityPorts.Contradicts(security, port.Value))
        {
            throw new DomainValidationException(DomainMessages.LdapSecurityContradictsPort(
                security.ToString(), port.Value, LdapSecurityPorts.StandardPortFor(security)));
        }

        Host = DomainGuard.RequiredText(host, nameof(Host), MaximumHostLength);
        Port = port;
        Security = security;
        FailoverHosts = DomainGuard.OptionalText(failoverHosts, nameof(FailoverHosts), MaximumFailoverHostsLength);
    }

    /// <summary>
    /// Whether pointing the provider at these hosts and this port would have it connect somewhere
    /// it does not connect today: a host, primary or failover, it has not used before, or a port
    /// it has not used on a host it has. Dropping a failover host does not count; neither does
    /// re-ordering them.
    ///
    /// The stored bind password goes wherever the provider connects, in a simple bind, on the
    /// very next request. So an endpoint change that keeps the stored password is a way to make
    /// this server hand the service account's password to a host of the caller's choosing — the
    /// LDAP pass-back attack. Whoever moves a provider somewhere new has to supply the password
    /// for it, which they can only do if they already know it.
    /// </summary>
    public bool WouldReachNewEndpoint(string? host, LdapPort port, string? failoverHosts)
    {
        ArgumentNullException.ThrowIfNull(port);

        var current = Endpoints(Host, Port.Value, FailoverHosts);
        return Endpoints(host, port.Value, failoverHosts).Any(endpoint => !current.Contains(endpoint));
    }

    /// <summary>
    /// Whether <paramref name="security"/> would have the provider send its bind password in clear
    /// text where today it does not.
    ///
    /// The same pass-back as <see cref="WouldReachNewEndpoint"/>, to a different listener: the
    /// host stays, the encryption goes, and the stored password crosses the network readable by
    /// anyone on the path. Swapping one encrypted mode for the other does not count.
    /// </summary>
    public bool WouldDropEncryption(LdapSecurity security) =>
        security == LdapSecurity.None && Security != LdapSecurity.None;

    /// <summary>Split the way DefaultLdapProviderFactory splits them when it connects.</summary>
    private static HashSet<string> Endpoints(string? host, int port, string? failoverHosts) =>
        new[] { host }
            .Concat((failoverHosts ?? string.Empty).Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => $"{candidate!.Trim()}:{port}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Which dialect to speak. Not restricted to a known list on purpose: an unrecognised value
    /// already falls back to the standard LDAP provider, and rejecting one here would make rows
    /// written by an older version unreadable rather than merely unusual.
    /// </summary>
    public void ChangeType(string? providerType) =>
        ProviderType = DomainGuard.RequiredText(providerType, nameof(ProviderType), MaximumProviderTypeLength);

    /// <summary>
    /// The subtree every search for this provider starts from. Blank is allowed and means "take
    /// the base DN from configuration" — the behaviour the request contract has always had, and
    /// not something to tighten while moving the rules into the domain.
    /// </summary>
    public void UseSearchRoot(string? baseDn) =>
        BaseDn = DomainGuard.OptionalText(baseDn, nameof(BaseDn), MaximumBaseDnLength) ?? string.Empty;

    /// <summary>
    /// The account to bind as. Blank means an anonymous bind, which is a real configuration for
    /// a read-only directory rather than a missing value.
    /// </summary>
    public void UseAdminAccount(string? adminUsername) =>
        AdminUsername = DomainGuard.OptionalText(adminUsername, nameof(AdminUsername), MaximumAdminUsernameLength)
                        ?? string.Empty;

    /// <summary>
    /// Replaces the stored bind credential. Takes an already-encrypted value: the domain must
    /// never see a plaintext password, so encrypting is the caller's job and the type makes that
    /// impossible to forget.
    /// </summary>
    public void RotateAdminPassword(EncryptedSecret encryptedAdminPassword)
    {
        ArgumentNullException.ThrowIfNull(encryptedAdminPassword);
        EncryptedAdminPassword = encryptedAdminPassword;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <exception cref="DomainValidationException">The page size is outside the usable range.</exception>
    public void ConfigureUserSearch(LdapSearchFilter userSearchFilter, int userSearchPageSize)
    {
        ArgumentNullException.ThrowIfNull(userSearchFilter);

        if (userSearchPageSize is < MinimumSearchPageSize or > MaximumSearchPageSize)
        {
            throw new DomainValidationException(DomainMessages.SearchPageSizeOutOfRange(
                userSearchPageSize, MinimumSearchPageSize, MaximumSearchPageSize));
        }

        UserSearchFilter = userSearchFilter;
        UserSearchPageSize = userSearchPageSize;
    }
}
