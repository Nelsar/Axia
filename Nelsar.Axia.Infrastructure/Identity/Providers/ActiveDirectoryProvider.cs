using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;
using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Contracts.Diagnostics;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Infrastructure.Identity.Abstractions;

namespace Nelsar.Axia.Infrastructure.Identity.Providers;

/// <summary>
/// The LDAP provider for Windows Active Directory.
/// Built on System.DirectoryServices.AccountManagement (PrincipalContext, UserPrincipal).
/// Windows-only, selected with ProviderType = "ActiveDirectory" in configuration.
/// </summary>
[SupportedOSPlatform("windows")]
internal class ActiveDirectoryProvider : ILdapProvider
{
    private readonly LdapSettings _settings;
    private readonly ILogger<ActiveDirectoryProvider> _logger;

    public ActiveDirectoryProvider(IOptions<LdapSettings> options, ILogger<ActiveDirectoryProvider> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Ldaps adds SSL to the Negotiate bind, on the port in the settings. None keeps what this
    /// provider always did: no TLS, but a Negotiate bind with signing and sealing, so unlike plain
    /// LDAP on the standard provider the traffic is still encrypted by Kerberos or NTLM.
    ///
    /// StartTls has no counterpart in System.DirectoryServices.AccountManagement. The settings
    /// check refuses it for this provider before a context is ever built, so arriving here with
    /// it is a bug rather than a configuration to be handled.
    /// </summary>
    private PrincipalContext CreateContext() => _settings.Security == LdapSecurity.Ldaps
        ? new(ContextType.Domain, $"{_settings.Server}:{_settings.ResolvedPort}", _settings.BaseDn,
            BindOptions, _settings.AdminUsername, _settings.AdminPassword)
        : new(ContextType.Domain, _settings.Server, _settings.BaseDn,
            BindOptions, _settings.AdminUsername, _settings.AdminPassword);

    /// <summary>
    /// The options every bind uses, the user's own included. Passed to ValidateCredentials
    /// explicitly as well as to the context: the overload without them is not the place to find
    /// out which options a password was sent with.
    /// </summary>
    private ContextOptions BindOptions => _settings.Security switch
    {
        LdapSecurity.Ldaps => ContextOptions.Negotiate | ContextOptions.SecureSocketLayer,
        LdapSecurity.None => ContextOptions.Negotiate | ContextOptions.Signing | ContextOptions.Sealing,
        _ => throw new NotSupportedException(
            $"ActiveDirectoryProvider cannot use Security={_settings.Security}; the settings check should have refused it.")
    };

    public bool Authenticate(string username, string password)
    {
        try
        {
            // ValidateCredentials returns false for a rejected login and throws only when it
            // could not ask — so, unlike the Novell client, the two cases arrive separately
            // here and the catch below is unambiguously an outage.
            using var context = CreateContext();
            return context.ValidateCredentials(username, password, BindOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AD authentication could not be completed for {Login}.", LogMask.Login(username));
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectoryCredentialCheckFailed(username), ex);
        }
    }

    public LdapUserInfo? FindUser(string username)
    {
        try
        {
            using var context = CreateContext();
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username);
            return user == null ? null : Map(user);
        }
        catch (Exception ex)
        {
            // See StandardLdapProvider.FindUser: null has to mean "searched, found nobody",
            // otherwise a directory outage reads as "this account is free to create".
            _logger.LogError(ex, "Error finding AD user {Login}.", LogMask.Login(username));
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectorySearchFailed(username), ex);
        }
    }

    public DirectoryUserListing GetAllUsers()
    {
        var users = new List<LdapUserInfo>();
        try
        {
            using var context = CreateContext();
            using var searcher = new PrincipalSearcher(new UserPrincipal(context));
            foreach (var result in searcher.FindAll().OfType<UserPrincipal>())
                users.Add(Map(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating AD users after {EntryCount} entries.", users.Count);
            return DirectoryUserListing.Truncated(users);
        }

        return DirectoryUserListing.Complete(users);
    }

    public void CreateUser(string name, string email, string password)
    {
        using var context = CreateContext();
        var samAccount = Email.Create(email).LocalPart;
        var user = new UserPrincipal(context, samAccount, password, enabled: true)
        {
            DisplayName = name,
            EmailAddress = email,
            Name = name
        };
        user.Save();
        _logger.LogInformation("AD user created: {Login}", LogMask.Login(samAccount));
    }

    public void UpdateUser(string username, string? newName, string? newEmail, string? newPassword)
    {
        using var context = CreateContext();
        using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username)
            ?? throw new DomainNotFoundException(ApplicationMessages.UserNotFoundInAd(username));

        if (!string.IsNullOrWhiteSpace(newName))
            user.DisplayName = newName;

        // sAMAccountName is deliberately left alone: it is the login, and rewriting it on an
        // address change would lock the user out of their own account.
        if (!string.IsNullOrWhiteSpace(newEmail))
            user.EmailAddress = newEmail;

        if (!string.IsNullOrWhiteSpace(newPassword))
            user.SetPassword(newPassword);

        user.Save();
        _logger.LogInformation("AD user updated: {Login}", LogMask.Login(username));
    }

    public bool DeleteUser(string username)
    {
        try
        {
            using var context = CreateContext();
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username);
            if (user == null) return false;
            user.Delete();
            _logger.LogInformation("AD user deleted: {Login}", LogMask.Login(username));
            return true;
        }
        catch (Exception ex)
        {
            // Matches StandardLdapProvider.DeleteUser: false means the account genuinely was
            // not there, so a failure must not borrow that meaning and be reported as 404.
            _logger.LogError(ex, "Error deleting AD user {Login}.", LogMask.Login(username));
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedDelete(username), ex);
        }
    }

    public string[] GetUserGroups(string username)
    {
        try
        {
            using var context = CreateContext();
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, username);
            if (user == null)
            {
                // See StandardLdapProvider.GetUserGroups: "membership unknown" must not be
                // answered with "no groups", or an outage quietly strips an administrator's role.
                throw new DirectoryUnavailableException(
                    ApplicationMessages.AdPrincipalMissingForMembership(username));
            }

            return user.GetGroups()
                .Select(g => g.Name)
                .ToArray();
        }
        catch (DirectoryUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting AD groups for {Login}.", LogMask.Login(username));
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectoryGroupMembershipFailed(username), ex);
        }
    }

    // Organizational units are not modelled by System.DirectoryServices.AccountManagement at
    // all — it deals in principals — so implementing them here would mean a second, untestable
    // Windows-only path via DirectoryEntry. StandardLdapProvider already manages OUs in Active
    // Directory over plain LDAP, so the honest answer is to say this provider does not, and
    // point at the one that does. Nothing here returns an empty list pretending the directory
    // has no units.

    public DirectoryOrganizationalUnitListing GetAllOrganizationalUnits() =>
        throw OrganizationalUnitsUnsupported();

    public string CreateOrganizationalUnit(string name, string parentDn, string description) =>
        throw OrganizationalUnitsUnsupported();

    public string UpdateOrganizationalUnit(string dn, string? newName, string? newParentDn, string? newDescription) =>
        throw OrganizationalUnitsUnsupported();

    public bool DeleteOrganizationalUnit(string dn) =>
        throw OrganizationalUnitsUnsupported();

    private static FeatureNotImplementedException OrganizationalUnitsUnsupported() =>
        new(ApplicationMessages.OrganizationalUnitsUnsupportedByAdProvider());

    // Groups take the same line as organizational units above. GroupPrincipal would cover part
    // of this — listing, creating, membership — but not renaming or moving, which are moddn
    // operations it does not model, so half the contract would still have to drop through to
    // DirectoryEntry. That is a second Windows-only write path, and one no test in this repo can
    // reach. StandardLdapProvider already manages Active Directory groups over plain LDAP, so
    // the honest answer is to say this provider does not and point at the one that does.

    public DirectoryGroupListing GetAllGroups() => throw GroupsUnsupported();

    public string CreateGroup(string name, string parentDn, string description, IReadOnlyList<string> memberDns) =>
        throw GroupsUnsupported();

    public string UpdateGroup(string dn, string? newName, string? newParentDn, string? newDescription, IReadOnlyList<string>? memberDns) =>
        throw GroupsUnsupported();

    public bool DeleteGroup(string dn) => throw GroupsUnsupported();

    private static FeatureNotImplementedException GroupsUnsupported() =>
        new(ApplicationMessages.GroupsUnsupportedByAdProvider());

    // Computer accounts take the same line as groups and organizational units above.
    // System.DirectoryServices.AccountManagement has ComputerPrincipal, but it models neither
    // rename/move nor userAccountControl, so most of the contract would drop through to
    // DirectoryEntry — a third Windows-only write path no test here can reach.
    // StandardLdapProvider manages Active Directory computer accounts over plain LDAP.

    public DirectoryComputerListing GetAllComputers() => throw ComputersUnsupported();

    public string CreateComputer(string name, string parentDn, string description, string dnsHostName) =>
        throw ComputersUnsupported();

    public string UpdateComputer(string dn, string? newName, string? newParentDn, string? newDescription, string? newDnsHostName, bool? enabled) =>
        throw ComputersUnsupported();

    public bool DeleteComputer(string dn) => throw ComputersUnsupported();

    private static FeatureNotImplementedException ComputersUnsupported() =>
        new(ApplicationMessages.ComputersUnsupportedByAdProvider());

    // Raw attributes take the same line again. UserPrincipal exposes the handful of properties
    // Map below reads and nothing else, so answering this would mean dropping to the underlying
    // DirectoryEntry and walking its PropertyCollection — a Windows-only read path no test in
    // this repo can reach, sitting directly on top of credential attributes that must never be
    // read out. StandardLdapProvider does this against Active Directory over plain LDAP, with
    // the redaction rules in DirectoryAttributeReader applied and covered by tests.
    public DirectoryEntryAttributes? GetUserAttributes(string username) =>
        throw new FeatureNotImplementedException(
            ApplicationMessages.UserAttributesUnsupportedByAdProvider());

    private static LdapUserInfo Map(UserPrincipal user) => new()
    {
        Dn = user.DistinguishedName ?? string.Empty,
        Username = user.SamAccountName ?? string.Empty,
        DisplayName = user.DisplayName ?? string.Empty,
        Email = user.EmailAddress ?? string.Empty,
        Groups = []
    };
}
