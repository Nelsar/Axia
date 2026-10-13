using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.ValueObjects;
using Novell.Directory.Ldap;
using Novell.Directory.Ldap.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Infrastructure.Identity.Abstractions;
using Nelsar.Axia.Contracts.Diagnostics;

namespace Nelsar.Axia.Infrastructure.Identity.Providers;

/// <summary>
/// The cross-platform LDAP provider, speaking the standard protocol (RFC 4511).
/// Built on Novell.Directory.Ldap.NETStandard. Selected with ProviderType = "StandardLdap".
/// Read/auth operations (Authenticate, FindUser, FindUserByDn, GetAllUsers) work against
/// any RFC-compliant directory — Active Directory, OpenLDAP, or Novell eDirectory — since
/// filters and attribute lookups try both AD-style (sAMAccountName/userPrincipalName) and
/// generic-style (uid) names. CreateUser is the exception: it writes an inetOrgPerson entry
/// with a uid RDN, which is correct for OpenLDAP/eDirectory but not for creating a true AD
/// user object (AD needs objectClass=user, sAMAccountName, userAccountControl, LDAPS-encoded
/// unicodePwd, etc.) — for AD user creation, use ActiveDirectoryProvider instead.
///
/// Group creation carries the same caveat, and it is the only group operation that does.
/// Listing, renaming, moving, editing and deleting work against AD's objectClass=group as well
/// as the RFC classes, because they address the entry by DN and touch only cn/description/member
/// — attributes all three directory types share. CreateGroup, however, writes a groupOfNames,
/// which in Active Directory is a valid entry but not a security principal: it gets no SID and no
/// groupType, so AD will not use it for access control. Create AD security groups in AD.
/// </summary>
internal class StandardLdapProvider : ILdapProvider
{
    private readonly LdapSettings _settings;
    private readonly LdapConnectionFactory _connectionFactory;
    private readonly ILogger<StandardLdapProvider> _logger;

    // Covers both OpenLDAP/eDirectory (uid) and AD (sAMAccountName/userPrincipalName)
    // login attributes so MapEntry can resolve a username regardless of directory type.
    // objectClass tells an account from a computer or a contact (see IsAccount); adminCount is
    // how Active Directory marks the privileged (see LdapUserInfo.IsPrivileged); the last four
    // say whether the directory has switched the account off (see IsSwitchedOff).
    // pwdAccountLockedTime is operational, so it comes back only when asked for by name.
    private static readonly string[] RequestedUserAttributes =
    [
        "cn", "displayName", "mail", "uid", "sAMAccountName", "userPrincipalName", "memberOf", "objectClass", "adminCount",
        "userAccountControl", "accountExpires", "pwdAccountLockedTime", "loginDisabled"
    ];

    /// <summary>
    /// Structural classes that derive from person in Active Directory without being a person's
    /// account. "(objectClass=person)" matches all three, so every computer and every mail contact
    /// used to be listed, looked up and edited as a user — users.delete could remove a computer
    /// account, and users.read reached a computer's attributes, LAPS password included. Checked
    /// on the entry rather than in the filter, because a negated objectClass assertion is only
    /// safe in a directory that defines the class, and OpenLDAP defines neither.
    /// </summary>
    private static readonly string[] NonAccountClasses = ["computer", "contact"];

    /// <summary>
    /// Everything the entry carries. "*" is all non-operational attributes; memberOf has to be
    /// named on top of it because both Active Directory and OpenLDAP's memberof overlay compute
    /// it rather than store it, and computed attributes are not covered by "*".
    /// </summary>
    private static readonly string[] AllUserAttributes = ["*", "memberOf"];

    private static readonly string[] RequestedOuAttributes = ["ou", "description"];

    private static readonly string[] RequestedGroupAttributes = ["cn", "description", "member", "uniqueMember", "adminCount"];

    /// <summary>Every attribute a group can list its members in. See GetGroupMembers.</summary>
    private static readonly string[] GroupMemberAttributes = ["member", "uniqueMember", "memberUid"];

    /// <summary>
    /// Matches a group in any of the three directory types: <c>group</c> is Active Directory's
    /// class, <c>groupOfNames</c> and <c>groupOfUniqueNames</c> are the RFC 4519 ones used by
    /// OpenLDAP and eDirectory.
    ///
    /// <c>posixGroup</c> is deliberately excluded. Its membership lives in <c>memberUid</c> as
    /// bare login names rather than distinguished names, so listing one here would report its
    /// members in a form the rest of this API treats as DNs — and any membership edit would be
    /// written to the wrong attribute entirely.
    /// </summary>
    private const string GroupSearchFilter =
        "(|(objectClass=group)(objectClass=groupOfNames)(objectClass=groupOfUniqueNames))";

    /// <summary>
    /// <c>computer</c> is Active Directory's class; <c>device</c> is RFC 4519's nearest
    /// equivalent, which OpenLDAP and eDirectory use for machine entries. They are not the same
    /// thing — a <c>device</c> carries no account, no operating system and no
    /// userAccountControl — so entries from the latter arrive with those fields empty and
    /// <see cref="LdapComputer.Enabled"/> null rather than guessed at.
    /// </summary>
    private const string ComputerSearchFilter =
        "(|(objectClass=computer)(objectClass=device))";

    private static readonly string[] RequestedComputerAttributes =
        ["cn", "description", "dNSHostName", "operatingSystem", "operatingSystemVersion", "userAccountControl"];

    /// <summary>ACCOUNTDISABLE — bit 0x2 of userAccountControl.</summary>
    private const int AccountDisabledFlag = 0x2;

    /// <summary>
    /// WORKSTATION_TRUST_ACCOUNT. Active Directory requires userAccountControl on a new computer
    /// account to say which kind of account it is; without this the entry is created as a
    /// disabled normal account that no machine can ever take over at join time.
    /// </summary>
    private const int WorkstationTrustAccount = 0x1000;

    public StandardLdapProvider(
        IOptions<LdapSettings> options,
        LdapConnectionFactory connectionFactory,
        ILogger<StandardLdapProvider> logger)
    {
        _settings = options.Value;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public string? Authenticate(string username, string password)
    {
        // Two phases with different meanings. Looking the account up is our operation — if it
        // fails, we learned nothing about the credentials. Only the bind is the question we
        // actually asked, so only the bind may answer "no".
        using var adminConn = ConnectOrThrow();

        LdapEntry? entry;
        try
        {
            // Resolve the real DN via search instead of guessing an RDN convention:
            // OpenLDAP/eDirectory typically use uid=..., AD uses CN=... with a login
            // stored in sAMAccountName/userPrincipalName rather than as the RDN itself.
            // SearchOne already tries all of those attribute names.
            entry = SearchOne(adminConn, username);
        }
        catch (LdapException ex)
        {
            // Logged here as well: this is the one path that turns a login into a directory
            // failure, and without a line of its own the operator sees the 502 with nothing
            // in the log explaining what the directory actually said.
            _logger.LogError(ex, "LDAP lookup failed while authenticating {Login}.", LogMask.Login(username));
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectorySearchFailed(), ex);
        }

        if (entry == null)
        {
            _logger.LogWarning("LDAP authentication failed for {Login}: no such entry.", LogMask.Login(username));
            return null;
        }

        try
        {
            // The one connection that carries the user's own password, so it is the one that must
            // not be opened any other way: through the factory, with the directory's TLS.
            using var bindConn = _connectionFactory.Open();
            bindConn.Bind(entry.Dn, password);

            // The DN that was bound to, not just whether the bind held: it is the only thing that
            // says which entry the password belonged to, and the session belongs to that entry.
            return bindConn.Bound ? entry.Dn : null;
        }
        catch (LdapException ex) when (IsCredentialRejection(ex))
        {
            _logger.LogWarning("LDAP authentication failed for {Login}: {Reason}", LogMask.Login(username), ex.Message);
            return null;
        }
        catch (LdapException ex)
        {
            // Everything else — server down, timeout, connect error — is an outage. Returning
            // false here reported it to the caller as "wrong password", which sends the user
            // to reset a credential that was never the problem and hides the outage entirely.
            _logger.LogError(ex, "LDAP authentication could not be completed for {Login}.", LogMask.Login(username));
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectoryCredentialCheckFailed(), ex);
        }
    }

    /// <summary>
    /// True when the directory answered the bind and rejected it — as opposed to failing to
    /// answer at all. A wrong password arrives here as an exception just like a dead server
    /// does, so telling the two apart by result code is what keeps a bad password a 401 and
    /// an outage a 502.
    /// </summary>
    internal static bool IsCredentialRejection(LdapException exception) => exception.ResultCode switch
    {
        LdapException.InvalidCredentials => true,
        LdapException.InappropriateAuthentication => true,
        // AD uses 53 for disabled, locked, expired and must-change-password accounts; OpenLDAP
        // for a disallowed unauthenticated bind. All are the directory saying "not this login",
        // which is an answer, not a failure.
        LdapException.UnwillingToPerform => true,
        _ => false
    };

    public LdapUserInfo? FindUser(string username)
    {
        using var conn = ConnectOrThrow();

        try
        {
            var entry = SearchOne(conn, username);
            return entry == null ? null : MapEntry(entry, GroupsNaming(conn, entry));
        }
        catch (LdapException ex)
        {
            // null must mean one thing only: the search ran and matched nobody. Returning it
            // for a failed search is a lie the caller cannot detect — CreateUser acts on null
            // by writing the account, so during an outage it would duplicate a user who is
            // already there. Callers that genuinely want "absent or unknown" can catch this.
            _logger.LogError(ex, "Error finding LDAP user {Login}.", LogMask.Login(username));
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectorySearchFailed(), ex);
        }
    }

    public LdapUserInfo? FindUserByDn(string distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
            return null;

        var target = DistinguishedName.Create(distinguishedName);
        using var conn = ConnectOrThrow();

        try
        {
            // A base-object read of exactly this entry, and then the groups that name it: the
            // groups decide the session's permissions and whether the account is protected, so
            // memberOf alone — which not every directory keeps — is not enough to go on.
            var entry = conn.Read(target.Value, RequestedUserAttributes);
            return IsAccount(entry) ? MapEntry(entry, GroupsNaming(conn, entry)) : null;
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
        {
            // Moved, renamed or deleted since the DN was handed out. Null, so the caller can tell
            // "this entry is gone" from "we could not ask".
            return null;
        }
        catch (LdapException ex)
        {
            // Not null: membership is what authorisation is decided on, and a failed read answered
            // as "in no groups" would quietly demote an administrator mid-session.
            _logger.LogError(ex, "Error reading LDAP entry {Dn}.", LogMask.DistinguishedName(target.Value));
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectoryGroupMembershipFailed(), ex);
        }
    }

    public DirectoryEntryAttributes? GetUserAttributes(string username)
    {
        using var conn = ConnectOrThrow();

        try
        {
            // AllUserAttributes, not the default: a search returns only what it asks for, so
            // reusing the seven attributes MapEntry needs would have made "every attribute of
            // this entry" mean "the same seven fields the card already shows".
            var entry = SearchOne(conn, username, AllUserAttributes);
            return entry == null
                ? null
                : new DirectoryEntryAttributes(entry.Dn, DirectoryAttributeReader.Read(entry.GetAttributeSet()));
        }
        catch (LdapException ex)
        {
            // Same reasoning as FindUser: null has to keep meaning "matched nobody", so a search
            // that failed is an exception rather than an empty attribute list.
            _logger.LogError(ex, "Error reading LDAP attributes for {Login}.", LogMask.Login(username));
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectorySearchFailed(), ex);
        }
    }

    public DirectoryUserListing GetAllUsers()
    {
        var users = new List<LdapUserInfo>();
        try
        {
            using var conn = _connectionFactory.CreateAdminConnection();
            var pageSize = _settings.UserSearchPageSize > 0 ? _settings.UserSearchPageSize : 500;
            byte[] cookie = [];

            do
            {
                var controls = new LdapControl[]
                {
                    new SimplePagedResultsControl(pageSize, cookie)
                };

                var constraints = (LdapSearchConstraints)conn.SearchConstraints.Clone();
                constraints.SetControls(controls);

                var results = conn.Search(
                    _settings.BaseDn,
                    LdapConnection.ScopeSub,
                    // "person" is the common structural ancestor for AD's "user", and for
                    // OpenLDAP/eDirectory's "inetOrgPerson"/"organizationalPerson" — all three
                    // directory types list the full class chain in objectClass, so this single
                    // filter matches user entries regardless of which directory is behind it.
                    "(objectClass=person)",
                    RequestedUserAttributes,
                    false,
                    constraints);

                // Same referral handling as SearchOne: the references AD appends to each page
                // are not entries, and must not end the enumeration as if it had failed —
                // that reported every AD listing as truncated.
                // memberOf only, without GroupsNaming: that is a search per entry, thousands of them
                // for a listing whose groups are shown and never used to decide anything.
                while (NextEntryOrNull(results) is { } entry)
                {
                    if (IsAccount(entry))
                        users.Add(MapEntry(entry));
                }

                cookie = [];
                var pageControl = results.ResponseControls?
                    .OfType<SimplePagedResultsControl>()
                    .FirstOrDefault();

                if (pageControl?.Cookie is { Length: > 0 })
                    cookie = pageControl.Cookie;
            }
            while (cookie.Length > 0);
        }
        catch (LdapException ex)
        {
            // The pages already read are real; the set is simply no longer the whole directory.
            // Say so instead of handing back a short list that looks complete.
            _logger.LogError(ex, "Error enumerating LDAP users after {EntryCount} entries.", users.Count);
            return DirectoryUserListing.Truncated(users);
        }

        return DirectoryUserListing.Complete(users);
    }

    public void CreateUser(string name, string email, string password)
    {
        EnsureWriteOperationsSupported();
        var uid = Email.Create(email).LocalPart;

        // Escaped: an address's local part may hold ',', '+' or '=', and unescaped they ended the
        // RDN and let the address choose where in the tree the account was written.
        var dn = $"uid={LdapDistinguishedNameEscaper.Escape(uid)},{_settings.BaseDn}";
        var attrs = LdapUserMapper.BuildAttributes(name, email);

        using var conn = ConnectOrThrow();

        EnsureLoginIsUnclaimed(conn, uid);

        try
        {
            conn.Add(new LdapEntry(dn, attrs));
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Error creating LDAP user {Dn}.", LogMask.DistinguishedName(dn));
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedCreate(), ex);
        }

        // The entry is written without a password and given one the directory's own way. If that
        // is refused — a password the policy rejects, a directory with no safe way to take one —
        // the half-made account is removed rather than left behind with no password at all.
        try
        {
            SetPassword(conn, dn, password);
        }
        catch
        {
            try
            {
                conn.Delete(dn);
            }
            catch (LdapException cleanup)
            {
                _logger.LogError(cleanup, "Could not remove {Dn} after its password was refused.", LogMask.DistinguishedName(dn));
            }

            throw;
        }

        _logger.LogInformation("LDAP user created: {Dn}", LogMask.DistinguishedName(dn));
    }

    public void UpdateUser(string distinguishedName, string? newName, string? newEmail, string? newPassword)
    {
        EnsureWriteOperationsSupported();

        var target = DistinguishedName.Create(distinguishedName);
        using var conn = ConnectOrThrow();

        var mods = new List<LdapModification>();
        if (!string.IsNullOrWhiteSpace(newName))
            mods.Add(new LdapModification(LdapModification.Replace, new LdapAttribute("displayName", newName)));
        // Replaces the mail attribute only. The entry keeps its uid RDN — and therefore its
        // DN and its login — because renaming the entry is a moddn operation with its own
        // referential consequences, not a side effect an address edit should trigger. The
        // default search filter matches both uid and mail, so the account stays reachable
        // under either.
        if (!string.IsNullOrWhiteSpace(newEmail))
            mods.Add(new LdapModification(LdapModification.Replace, new LdapAttribute("mail", newEmail)));

        // First, so a password the directory refuses leaves the rest of the entry as it was
        // rather than half-updated around a reset that did not happen.
        if (!string.IsNullOrWhiteSpace(newPassword))
            SetPassword(conn, target.Value, newPassword);

        if (mods.Count > 0)
        {
            try
            {
                conn.Modify(target.Value, [.. mods]);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
            {
                throw new DomainNotFoundException(ApplicationMessages.UserNotFoundInLdap());
            }
            catch (LdapException ex)
            {
                _logger.LogError(ex, "Error updating LDAP user {Dn}.", LogMask.DistinguishedName(target.Value));
                throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedUpdate(), ex);
            }

            _logger.LogInformation("LDAP user updated: {Dn}", LogMask.DistinguishedName(target.Value));
        }
    }

    /// <summary>How this directory takes a password. Asked of the root DSE once per instance.</summary>
    private PasswordWriteMode? _passwordWriteMode;

    private enum PasswordWriteMode
    {
        /// <summary>unicodePwd, which Active Directory accepts only over an encrypted connection.</summary>
        ActiveDirectory,

        /// <summary>RFC 3062 Password Modify — OpenLDAP and most others; the server hashes.</summary>
        PasswordModify,

        /// <summary>Neither. A password is refused rather than written in the clear.</summary>
        Unsupported
    }

    /// <summary>
    /// Sets a password the way the directory itself expects, so that it is hashed, checked against
    /// the directory's policy, and actually becomes the account's password. Never by writing
    /// userPassword: see <see cref="DirectoryPasswords"/> for what that did in each directory.
    /// </summary>
    /// <exception cref="DomainNotFoundException">No entry has that DN.</exception>
    /// <exception cref="DomainValidationException">The directory's password policy refused it.</exception>
    /// <exception cref="DirectoryUnavailableException">The directory would not take it at all.</exception>
    private void SetPassword(LdapConnection conn, string dn, string password)
    {
        var mode = _passwordWriteMode ??= DetectPasswordWriteMode(conn);

        try
        {
            switch (mode)
            {
                case PasswordWriteMode.ActiveDirectory:
                    conn.Modify(dn, new LdapModification(LdapModification.Replace,
                        new LdapAttribute("unicodePwd", DirectoryPasswords.ActiveDirectoryValue(password))));
                    break;

                case PasswordWriteMode.PasswordModify:
                    conn.ExtendedOperation(new LdapExtendedOperation(
                        DirectoryPasswords.PasswordModifyOid,
                        DirectoryPasswords.PasswordModifyRequest(dn, password)));
                    break;

                default:
                    throw new DirectoryUnavailableException(ApplicationMessages.PasswordChangeUnsupported());
            }
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
        {
            throw new DomainNotFoundException(ApplicationMessages.UserNotFoundInLdap());
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.ConstraintViolation)
        {
            // AD's answer to a password that is too short, too simple or too recent; OpenLDAP's
            // ppolicy answers the same way. The caller's to fix, so a 400 and not an outage.
            throw new DomainValidationException(ApplicationMessages.PasswordRejectedByDirectory());
        }
        catch (LdapException ex) when (mode == PasswordWriteMode.ActiveDirectory
                                       && ex.ResultCode == LdapException.UnwillingToPerform)
        {
            // What AD says to unicodePwd over a connection that is not encrypted.
            _logger.LogError(ex, "Active Directory refused a password for {Dn}; the connection is probably not encrypted.",
                LogMask.DistinguishedName(dn));
            throw new DirectoryUnavailableException(ApplicationMessages.PasswordChangeNeedsEncryption(), ex);
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Error setting the password of LDAP user {Dn}.", LogMask.DistinguishedName(dn));
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedUpdate(), ex);
        }
    }

    private PasswordWriteMode DetectPasswordWriteMode(LdapConnection conn)
    {
        try
        {
            var rootDse = conn.Read(string.Empty, ["supportedCapabilities", "supportedExtension"]).GetAttributeSet();

            bool Lists(string attribute, string oid) =>
                rootDse.TryGetValue(attribute, out var values)
                && (values.StringValueArray ?? []).Contains(oid, StringComparer.Ordinal);

            if (Lists("supportedCapabilities", DirectoryPasswords.ActiveDirectoryCapability))
                return PasswordWriteMode.ActiveDirectory;

            if (Lists("supportedExtension", DirectoryPasswords.PasswordModifyOid))
                return PasswordWriteMode.PasswordModify;

            return PasswordWriteMode.Unsupported;
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryUnreachable(), ex);
        }
    }

    public bool DeleteUser(string distinguishedName)
    {
        EnsureWriteOperationsSupported();

        var target = DistinguishedName.Create(distinguishedName);
        using var conn = ConnectOrThrow();

        try
        {
            conn.Delete(target.Value);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
        {
            // The one case that is genuinely "no such user", and the only one allowed to be false.
            return false;
        }
        catch (LdapException ex)
        {
            // Not false: that used to be reported as 404, telling the caller the account did not
            // exist while it sat there untouched.
            _logger.LogError(ex, "Error deleting LDAP user {Dn}.", LogMask.DistinguishedName(target.Value));
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedDelete(), ex);
        }

        _logger.LogInformation("LDAP user deleted: {Dn}", LogMask.DistinguishedName(target.Value));
        return true;
    }

    // --- Organizational units ---

    public DirectoryOrganizationalUnitListing GetAllOrganizationalUnits()
    {
        var units = new List<LdapOrganizationalUnit>();

        // Connecting is separate from reading on purpose: a directory we never reached is an
        // outage (502), while a search that broke off partway did produce real entries and is
        // reported as a truncated listing.
        using var conn = ConnectOrThrow();

        try
        {
            var pageSize = _settings.UserSearchPageSize > 0 ? _settings.UserSearchPageSize : 500;
            byte[] cookie = [];

            do
            {
                var constraints = (LdapSearchConstraints)conn.SearchConstraints.Clone();
                constraints.SetControls(new LdapControl[] { new SimplePagedResultsControl(pageSize, cookie) });

                var results = conn.Search(
                    _settings.BaseDn,
                    LdapConnection.ScopeSub,
                    "(objectClass=organizationalUnit)",
                    RequestedOuAttributes,
                    false,
                    constraints);

                while (NextEntryOrNull(results) is { } entry)
                    units.Add(MapOrganizationalUnit(entry));

                cookie = [];
                var pageControl = results.ResponseControls?
                    .OfType<SimplePagedResultsControl>()
                    .FirstOrDefault();

                if (pageControl?.Cookie is { Length: > 0 })
                    cookie = pageControl.Cookie;
            }
            while (cookie.Length > 0);
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Error enumerating LDAP organizational units after {EntryCount} entries.", units.Count);
            return DirectoryOrganizationalUnitListing.Truncated(units);
        }

        return DirectoryOrganizationalUnitListing.Complete(units);
    }

    public string CreateOrganizationalUnit(string name, string parentDn, string description)
    {
        EnsureWriteOperationsSupported();

        var unitName = (name ?? string.Empty).Trim();
        if (unitName.Length == 0)
            throw new DomainValidationException(ApplicationMessages.OrganizationalUnitNameRequired());

        // No parent means the directory root we are configured against, which is the only
        // container we can be sure exists.
        var parent = string.IsNullOrWhiteSpace(parentDn) ? _settings.BaseDn : parentDn.Trim();
        EnsureInsideBase(parent);
        var dn = $"ou={DistinguishedName.EscapeRdnValue(unitName)},{parent}";

        var attrs = new LdapAttributeSet
        {
            new LdapAttribute("objectClass", ["top", "organizationalUnit"]),
            new LdapAttribute("ou", unitName)
        };

        if (!string.IsNullOrWhiteSpace(description))
            attrs.Add(new LdapAttribute("description", description.Trim()));

        try
        {
            using var conn = ConnectOrThrow();
            conn.Add(new LdapEntry(dn, attrs));
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.EntryAlreadyExists)
        {
            // The caller asked for something the directory already has. That is a conflict with
            // existing state (409), not a failure of ours to talk to the directory (502).
            throw new DomainConflictException(ApplicationMessages.OrganizationalUnitAlreadyExists(dn));
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedCreate(), ex);
        }

        _logger.LogInformation("LDAP organizational unit created: {Dn}", dn);
        return dn;
    }

    public string UpdateOrganizationalUnit(string dn, string? newName, string? newParentDn, string? newDescription)
    {
        EnsureWriteOperationsSupported();

        var current = DistinguishedName.Create(dn);
        var targetName = string.IsNullOrWhiteSpace(newName) ? current.Rdn : newName.Trim();
        var targetParent = string.IsNullOrWhiteSpace(newParentDn) ? current.Parent : newParentDn.Trim();

        var renamed = !string.Equals(targetName, current.Rdn, StringComparison.OrdinalIgnoreCase);
        var moved = !string.Equals(targetParent, current.Parent, StringComparison.OrdinalIgnoreCase);
        if (moved)
            EnsureInsideBase(targetParent);

        using var conn = ConnectOrThrow();
        var effectiveDn = current.Value;

        if (renamed || moved)
        {
            var newRdn = $"ou={DistinguishedName.EscapeRdnValue(targetName)}";
            try
            {
                // deleteOldRdn: true — the old ou value must not linger as a second name on an
                // entry whose DN no longer mentions it.
                if (moved)
                    conn.Rename(current.Value, newRdn, targetParent, true);
                else
                    conn.Rename(current.Value, newRdn, true);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
            {
                throw new DomainNotFoundException(ApplicationMessages.OrganizationalUnitNotFound(current.Value));
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.EntryAlreadyExists)
            {
                throw new DomainConflictException(
                    ApplicationMessages.OrganizationalUnitAlreadyExists($"{newRdn},{targetParent}"));
            }
            catch (LdapException ex)
            {
                throw new DirectoryUnavailableException(
                    ApplicationMessages.DirectoryRejectedUpdate(), ex);
            }

            effectiveDn = string.IsNullOrEmpty(targetParent) ? newRdn : $"{newRdn},{targetParent}";
            _logger.LogInformation("LDAP organizational unit moved: {FromDn} -> {ToDn}", current.Value, effectiveDn);
        }

        if (newDescription == null)
            return effectiveDn;

        // An explicit empty description means "remove it". Replacing with an empty value is not
        // the same thing — the directory would reject it — so a blank becomes a delete.
        var modification = string.IsNullOrWhiteSpace(newDescription)
            ? new LdapModification(LdapModification.Delete, new LdapAttribute("description"))
            : new LdapModification(LdapModification.Replace, new LdapAttribute("description", newDescription.Trim()));

        try
        {
            conn.Modify(effectiveDn, modification);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchAttribute)
        {
            // Deleting a description that was never there is the state the caller asked for.
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
        {
            throw new DomainNotFoundException(ApplicationMessages.OrganizationalUnitNotFound(effectiveDn));
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedUpdate(), ex);
        }

        _logger.LogInformation("LDAP organizational unit updated: {Dn}", effectiveDn);
        return effectiveDn;
    }

    public bool DeleteOrganizationalUnit(string dn)
    {
        EnsureWriteOperationsSupported();

        var target = DistinguishedName.Create(dn);

        using var conn = ConnectOrThrow();

        try
        {
            conn.Delete(target.Value);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
        {
            // The one case that is genuinely "no such unit", and the only one allowed to be false.
            return false;
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NotAllowedOnNonleaf)
        {
            // The unit still holds users, groups or child units. Deleting a container that has
            // contents is exactly the operation an administrator must not perform by accident,
            // so this stays a refusal the caller has to act on rather than a recursive delete.
            throw new DomainConflictException(ApplicationMessages.OrganizationalUnitNotEmpty(target.Value));
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Error deleting LDAP organizational unit {Dn}.", target.Value);
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedDelete(), ex);
        }

        _logger.LogInformation("LDAP organizational unit deleted: {Dn}", target.Value);
        return true;
    }

    // --- Groups ---

    public DirectoryGroupMembers GetGroupMembers(string groupDn)
    {
        if (!DistinguishedName.TryCreate(groupDn, out var target))
            return DirectoryGroupMembers.None;

        using var conn = ConnectOrThrow();

        try
        {
            // A base-object read of the group itself — no search, so it cannot be another entry.
            var attrs = conn.Read(target!.Value, GroupMemberAttributes).GetAttributeSet();
            string[] Values(string name) => attrs.TryGetValue(name, out var values) ? values.StringValueArray ?? [] : [];

            return new DirectoryGroupMembers([.. Values("member"), .. Values("uniqueMember")], Values("memberUid"));
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
        {
            // A role mapping can outlive the group it names. That group lists nobody.
            return DirectoryGroupMembers.None;
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Error reading the members of LDAP group {Dn}.", target!.Value);
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectoryGroupMembershipFailed(), ex);
        }
    }

    public DirectoryGroupListing GetAllGroups()
    {
        var groups = new List<LdapGroup>();

        // Connect first, read second — as in GetAllOrganizationalUnits: never reaching the
        // directory is an outage, while a search that broke off partway produced real entries.
        using var conn = ConnectOrThrow();

        try
        {
            var pageSize = _settings.UserSearchPageSize > 0 ? _settings.UserSearchPageSize : 500;
            byte[] cookie = [];

            do
            {
                var constraints = (LdapSearchConstraints)conn.SearchConstraints.Clone();
                constraints.SetControls(new LdapControl[] { new SimplePagedResultsControl(pageSize, cookie) });

                var results = conn.Search(
                    _settings.BaseDn,
                    LdapConnection.ScopeSub,
                    GroupSearchFilter,
                    RequestedGroupAttributes,
                    false,
                    constraints);

                while (NextEntryOrNull(results) is { } entry)
                {
                    groups.Add(MapGroup(entry));
                }

                cookie = [];
                var pageControl = results.ResponseControls?
                    .OfType<SimplePagedResultsControl>()
                    .FirstOrDefault();

                if (pageControl?.Cookie is { Length: > 0 })
                {
                    cookie = pageControl.Cookie;
                }
            }
            while (cookie.Length > 0);
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Error enumerating LDAP groups after {EntryCount} entries.", groups.Count);
            return DirectoryGroupListing.Truncated(groups);
        }

        return DirectoryGroupListing.Complete(groups);
    }

    public string CreateGroup(string name, string parentDn, string description, IReadOnlyList<string> memberDns)
    {
        EnsureWriteOperationsSupported();

        var groupName = (name ?? string.Empty).Trim();
        if (groupName.Length == 0)
            throw new DomainValidationException(ApplicationMessages.GroupNameRequired());

        var members = (memberDns ?? Array.Empty<string>())
            .Select(dn => (dn ?? string.Empty).Trim())
            .Where(dn => dn.Length > 0)
            .ToArray();

        if (members.Length == 0)
            throw new DomainValidationException(ApplicationMessages.GroupMembersRequired());

        var parent = string.IsNullOrWhiteSpace(parentDn) ? _settings.BaseDn : parentDn.Trim();
        EnsureInsideBase(parent);
        var dn = $"cn={DistinguishedName.EscapeRdnValue(groupName)},{parent}";

        var attrs = new LdapAttributeSet
        {
            new LdapAttribute("objectClass", ["top", "groupOfNames"]),
            new LdapAttribute("cn", groupName),
            new LdapAttribute("member", members)
        };

        if (!string.IsNullOrWhiteSpace(description))
            attrs.Add(new LdapAttribute("description", description.Trim()));

        try
        {
            using var conn = ConnectOrThrow();
            conn.Add(new LdapEntry(dn, attrs));
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.EntryAlreadyExists)
        {
            // A conflict with existing state (409), not a failure to talk to the directory (502).
            throw new DomainConflictException(ApplicationMessages.GroupAlreadyExists(dn));
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedCreate(), ex);
        }

        _logger.LogInformation("LDAP group created: {Dn}", dn);
        return dn;
    }

    public string UpdateGroup(string dn, string? newName, string? newParentDn, string? newDescription, IReadOnlyList<string>? memberDns)
    {
        EnsureWriteOperationsSupported();

        // Checked before anything is written. A rename happens first below, so refusing an
        // impossible membership afterwards would leave the group renamed and the caller holding
        // an error — the one outcome they did not ask for.
        string[]? members = null;
        if (memberDns != null)
        {
            members = memberDns
                .Select(member => (member ?? string.Empty).Trim())
                .Where(member => member.Length > 0)
                .ToArray();

            // Replacing with nothing would delete a mandatory attribute. The caller is told what
            // the schema requires rather than handed the server's object-class violation.
            if (members.Length == 0)
                throw new DomainValidationException(ApplicationMessages.GroupMembersRequired());
        }

        var current = DistinguishedName.Create(dn);
        var targetName = string.IsNullOrWhiteSpace(newName) ? current.Rdn : newName.Trim();
        var targetParent = string.IsNullOrWhiteSpace(newParentDn) ? current.Parent : newParentDn.Trim();

        var renamed = !string.Equals(targetName, current.Rdn, StringComparison.OrdinalIgnoreCase);
        var moved = !string.Equals(targetParent, current.Parent, StringComparison.OrdinalIgnoreCase);
        if (moved)
            EnsureInsideBase(targetParent);

        using var conn = ConnectOrThrow();
        var effectiveDn = current.Value;

        if (renamed || moved)
        {
            var newRdn = $"cn={DistinguishedName.EscapeRdnValue(targetName)}";
            try
            {
                // deleteOldRdn: true — the old cn must not linger as a second name on an entry
                // whose DN no longer mentions it.
                if (moved)
                    conn.Rename(current.Value, newRdn, targetParent, true);
                else
                    conn.Rename(current.Value, newRdn, true);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
            {
                throw new DomainNotFoundException(ApplicationMessages.GroupNotFound(current.Value));
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.EntryAlreadyExists)
            {
                throw new DomainConflictException(
                    ApplicationMessages.GroupAlreadyExists($"{newRdn},{targetParent}"));
            }
            catch (LdapException ex)
            {
                throw new DirectoryUnavailableException(
                    ApplicationMessages.DirectoryRejectedUpdate(), ex);
            }

            effectiveDn = string.IsNullOrEmpty(targetParent) ? newRdn : $"{newRdn},{targetParent}";
            _logger.LogInformation("LDAP group moved: {FromDn} -> {ToDn}", current.Value, effectiveDn);
        }

        if (newDescription != null)
        {
            // An explicit empty description means "remove it". Replacing with an empty value is
            // not the same thing — the directory would reject it — so a blank becomes a delete.
            var modification = string.IsNullOrWhiteSpace(newDescription)
                ? new LdapModification(LdapModification.Delete, new LdapAttribute("description"))
                : new LdapModification(LdapModification.Replace, new LdapAttribute("description", newDescription.Trim()));

            try
            {
                conn.Modify(effectiveDn, modification);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchAttribute)
            {
                // Deleting a description that was never there is the state the caller asked for.
                // This is why the description and the membership are two requests rather than one
                // batch: a modify is atomic, so swallowing NoSuchAttribute for a batch would
                // discard the membership change along with it and report success.
            }
            catch (LdapException ex)
            {
                throw TranslateModifyFailure(ex, effectiveDn);
            }
        }

        if (members != null)
        {
            try
            {
                conn.Modify(effectiveDn, new LdapModification(
                    LdapModification.Replace, new LdapAttribute("member", members)));
            }
            catch (LdapException ex)
            {
                throw TranslateModifyFailure(ex, effectiveDn);
            }
        }

        _logger.LogInformation("LDAP group updated: {Dn}", effectiveDn);
        return effectiveDn;
    }

    private static Exception TranslateModifyFailure(LdapException exception, string dn) =>
        exception.ResultCode == LdapException.NoSuchObject
            ? new DomainNotFoundException(ApplicationMessages.GroupNotFound(dn))
            : new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedUpdate(), exception);

    public bool DeleteGroup(string dn)
    {
        EnsureWriteOperationsSupported();

        var target = DistinguishedName.Create(dn);

        using var conn = ConnectOrThrow();

        try
        {
            conn.Delete(target.Value);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
        {
            // The one case that is genuinely "no such group", and the only one allowed to be false.
            return false;
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NotAllowedOnNonleaf)
        {
            // Unusual for a group, but a directory will happily let entries be created beneath
            // one. Deleting a container with contents stays a refusal the operator has to act on.
            throw new DomainConflictException(ApplicationMessages.GroupNotEmptyContainer(target.Value));
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Error deleting LDAP group {Dn}.", target.Value);
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedDelete(), ex);
        }

        _logger.LogInformation("LDAP group deleted: {Dn}", target.Value);
        return true;
    }

    public DirectoryComputerListing GetAllComputers()
    {
        var computers = new List<LdapComputer>();

        // Connect first, read second — as in GetAllGroups: never reaching the directory is an
        // outage, while a search that broke off partway produced real entries.
        using var conn = ConnectOrThrow();

        try
        {
            var pageSize = _settings.UserSearchPageSize > 0 ? _settings.UserSearchPageSize : 500;
            byte[] cookie = [];

            do
            {
                var constraints = (LdapSearchConstraints)conn.SearchConstraints.Clone();
                constraints.SetControls(new LdapControl[] { new SimplePagedResultsControl(pageSize, cookie) });

                var results = conn.Search(
                    _settings.BaseDn,
                    LdapConnection.ScopeSub,
                    ComputerSearchFilter,
                    RequestedComputerAttributes,
                    false,
                    constraints);

                while (NextEntryOrNull(results) is { } entry)
                {
                    computers.Add(MapComputer(entry));
                }

                cookie = [];
                var pageControl = results.ResponseControls?
                    .OfType<SimplePagedResultsControl>()
                    .FirstOrDefault();

                if (pageControl?.Cookie is { Length: > 0 })
                {
                    cookie = pageControl.Cookie;
                }
            }
            while (cookie.Length > 0);
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Error enumerating LDAP computers after {EntryCount} entries.", computers.Count);
            return DirectoryComputerListing.Truncated(computers);
        }

        return DirectoryComputerListing.Complete(computers);
    }

    public string CreateComputer(string name, string parentDn, string description, string dnsHostName)
    {
        EnsureWriteOperationsSupported();

        var computerName = (name ?? string.Empty).Trim();
        if (computerName.Length == 0)
            throw new DomainValidationException(ApplicationMessages.ComputerNameRequired());

        var parent = string.IsNullOrWhiteSpace(parentDn) ? _settings.BaseDn : parentDn.Trim();
        EnsureInsideBase(parent);
        var dn = $"cn={DistinguishedName.EscapeRdnValue(computerName)},{parent}";

        var attrs = new LdapAttributeSet
        {
            new LdapAttribute("objectClass", ["top", "person", "organizationalPerson", "user", "computer"]),
            new LdapAttribute("cn", computerName),

            // Active Directory's account name for a machine is the name with a trailing '$'.
            // Derived here rather than asked of the caller: it is a convention of the directory,
            // and an account written without it is not one a machine can ever claim at join time.
            new LdapAttribute("sAMAccountName", $"{computerName}$"),
            new LdapAttribute("userAccountControl", WorkstationTrustAccount.ToString()),
        };

        if (!string.IsNullOrWhiteSpace(description))
            attrs.Add(new LdapAttribute("description", description.Trim()));

        if (!string.IsNullOrWhiteSpace(dnsHostName))
            attrs.Add(new LdapAttribute("dNSHostName", dnsHostName.Trim()));

        try
        {
            using var conn = ConnectOrThrow();
            conn.Add(new LdapEntry(dn, attrs));
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.EntryAlreadyExists)
        {
            throw new DomainConflictException(ApplicationMessages.ComputerAlreadyExists(dn));
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedCreate(), ex);
        }

        _logger.LogInformation("LDAP computer created: {Dn}", dn);
        return dn;
    }

    public string UpdateComputer(
        string dn, string? newName, string? newParentDn, string? newDescription, string? newDnsHostName, bool? enabled)
    {
        EnsureWriteOperationsSupported();

        var current = DistinguishedName.Create(dn);
        var targetName = string.IsNullOrWhiteSpace(newName) ? current.Rdn : newName.Trim();
        var targetParent = string.IsNullOrWhiteSpace(newParentDn) ? current.Parent : newParentDn.Trim();

        var renamed = !string.Equals(targetName, current.Rdn, StringComparison.OrdinalIgnoreCase);
        var moved = !string.Equals(targetParent, current.Parent, StringComparison.OrdinalIgnoreCase);
        if (moved)
            EnsureInsideBase(targetParent);

        using var conn = ConnectOrThrow();
        var effectiveDn = current.Value;

        if (renamed || moved)
        {
            var newRdn = $"cn={DistinguishedName.EscapeRdnValue(targetName)}";
            try
            {
                // deleteOldRdn: true, as for groups — the old cn must not linger as a second
                // name on an entry whose DN no longer mentions it.
                if (moved)
                    conn.Rename(current.Value, newRdn, targetParent, true);
                else
                    conn.Rename(current.Value, newRdn, true);
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
            {
                throw new DomainNotFoundException(ApplicationMessages.ComputerNotFound(current.Value));
            }
            catch (LdapException ex) when (ex.ResultCode == LdapException.EntryAlreadyExists)
            {
                throw new DomainConflictException(
                    ApplicationMessages.ComputerAlreadyExists($"{newRdn},{targetParent}"));
            }
            catch (LdapException ex)
            {
                throw new DirectoryUnavailableException(
                    ApplicationMessages.DirectoryRejectedUpdate(), ex);
            }

            effectiveDn = string.IsNullOrEmpty(targetParent) ? newRdn : $"{newRdn},{targetParent}";
            _logger.LogInformation("LDAP computer moved: {FromDn} -> {ToDn}", current.Value, effectiveDn);

            // The account name follows the entry's name. Leaving it behind would give the
            // directory a machine called one thing and authenticating as another.
            ModifyComputerAttribute(conn, effectiveDn, "sAMAccountName", $"{targetName}$");
        }

        if (newDescription != null)
            ModifyComputerAttribute(conn, effectiveDn, "description", newDescription);

        if (newDnsHostName != null)
            ModifyComputerAttribute(conn, effectiveDn, "dNSHostName", newDnsHostName);

        if (enabled != null)
            ApplyEnabledFlag(conn, effectiveDn, enabled.Value);

        _logger.LogInformation("LDAP computer updated: {Dn}", effectiveDn);
        return effectiveDn;
    }

    public bool DeleteComputer(string dn)
    {
        EnsureWriteOperationsSupported();

        var target = DistinguishedName.Create(dn);

        using var conn = ConnectOrThrow();

        try
        {
            conn.Delete(target.Value);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchObject)
        {
            // The one case that is genuinely "no such computer", and the only one allowed to be false.
            return false;
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NotAllowedOnNonleaf)
        {
            throw new DomainConflictException(ApplicationMessages.ComputerNotEmptyContainer(target.Value));
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Error deleting LDAP computer {Dn}.", target.Value);
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedDelete(), ex);
        }

        _logger.LogInformation("LDAP computer deleted: {Dn}", target.Value);
        return true;
    }

    /// <summary>
    /// Replaces a single optional attribute, treating blank as "remove it". One modify per
    /// attribute rather than a batch, for the reason spelled out in <see cref="UpdateGroup"/>: a
    /// modify is atomic, so swallowing NoSuchAttribute for a batch would discard the other
    /// changes along with it and still report success.
    /// </summary>
    private static void ModifyComputerAttribute(LdapConnection conn, string dn, string attribute, string value)
    {
        var modification = string.IsNullOrWhiteSpace(value)
            ? new LdapModification(LdapModification.Delete, new LdapAttribute(attribute))
            : new LdapModification(LdapModification.Replace, new LdapAttribute(attribute, value.Trim()));

        try
        {
            conn.Modify(dn, modification);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.NoSuchAttribute)
        {
            // Deleting an attribute that was never there is the state the caller asked for.
        }
        catch (LdapException ex)
        {
            throw ex.ResultCode == LdapException.NoSuchObject
                ? new DomainNotFoundException(ApplicationMessages.ComputerNotFound(dn))
                : new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedUpdate(), ex);
        }
    }

    /// <summary>
    /// Flips ACCOUNTDISABLE while leaving every other flag alone. userAccountControl is a single
    /// integer holding unrelated facts about the account, so it has to be read before it is
    /// written — assigning a constant here would silently clear whatever else the account had
    /// set, including the trust-account bit that makes it a computer at all.
    /// </summary>
    private void ApplyEnabledFlag(LdapConnection conn, string dn, bool enabled)
    {
        int current;
        try
        {
            var entry = conn.Read(dn, ["userAccountControl"]);
            var raw = Get(entry.GetAttributeSet(), "userAccountControl");

            // No such attribute means the entry is not an AD account — an RFC 4519 device, say.
            // There is no disabled state to set, and inventing one by writing the attribute
            // would change what kind of object the entry is.
            if (!int.TryParse(raw, out current))
                throw new DomainValidationException(ApplicationMessages.ComputerNotFound(dn));
        }
        catch (LdapException ex)
        {
            throw ex.ResultCode == LdapException.NoSuchObject
                ? new DomainNotFoundException(ApplicationMessages.ComputerNotFound(dn))
                : new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedUpdate(), ex);
        }

        var updated = enabled ? current & ~AccountDisabledFlag : current | AccountDisabledFlag;
        if (updated == current)
            return;

        try
        {
            conn.Modify(dn, new LdapModification(
                LdapModification.Replace, new LdapAttribute("userAccountControl", updated.ToString())));
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryRejectedUpdate(), ex);
        }
    }

    private static LdapComputer MapComputer(LdapEntry entry)
    {
        var attrs = entry.GetAttributeSet();
        var dn = DistinguishedName.Create(entry.Dn);
        var cn = Get(attrs, "cn");

        return new LdapComputer
        {
            Dn = dn.Value,
            // The RDN is the authority on the name, as for groups: an entry can carry several
            // cn values and only the one in the DN addresses it.
            Name = string.IsNullOrWhiteSpace(dn.Rdn) ? cn : dn.Rdn,
            ParentDn = dn.Parent,
            Description = Get(attrs, "description"),
            DnsHostName = Get(attrs, "dNSHostName"),
            OperatingSystem = Get(attrs, "operatingSystem"),
            OperatingSystemVersion = Get(attrs, "operatingSystemVersion"),
            Enabled = ReadEnabledFlag(attrs)
        };
    }

    /// <summary>Null when there is no userAccountControl to read — unknown, not enabled.</summary>
    private static bool? ReadEnabledFlag(LdapAttributeSet attrs)
    {
        var raw = Get(attrs, "userAccountControl");
        return int.TryParse(raw, out var flags) ? (flags & AccountDisabledFlag) == 0 : null;
    }

    // --- Helpers ---

    private static LdapGroup MapGroup(LdapEntry entry)
    {
        var attrs = entry.GetAttributeSet();
        var dn = DistinguishedName.Create(entry.Dn);
        var cn = Get(attrs, "cn");

        // Both spellings of the membership attribute: "member" for AD's group and for
        // groupOfNames, "uniqueMember" for groupOfUniqueNames. A group with neither has no
        // members, which is a real state and not the same as one we failed to read.
        var members = attrs.TryGetValue("member", out var member)
            ? member.StringValueArray ?? []
            : attrs.TryGetValue("uniqueMember", out var uniqueMember)
                ? uniqueMember.StringValueArray ?? []
                : [];

        return new LdapGroup
        {
            Dn = dn.Value,
            // The RDN is the authority on the name, as for organizational units: an entry can
            // carry several cn values and only the one in the DN addresses it.
            Name = string.IsNullOrWhiteSpace(dn.Rdn) ? cn : dn.Rdn,
            ParentDn = dn.Parent,
            Description = Get(attrs, "description"),
            Members = members,
            IsPrivileged = IsMarkedPrivileged(attrs)
        };
    }

    private static LdapOrganizationalUnit MapOrganizationalUnit(LdapEntry entry)
    {
        var attrs = entry.GetAttributeSet();
        var dn = DistinguishedName.Create(entry.Dn);
        var ou = Get(attrs, "ou");

        return new LdapOrganizationalUnit
        {
            Dn = dn.Value,
            // The RDN is the authority on the name: an entry can carry several ou values, and
            // only the one in the DN is the one the directory addresses it by.
            Name = string.IsNullOrWhiteSpace(dn.Rdn) ? ou : dn.Rdn,
            ParentDn = dn.Parent,
            Description = Get(attrs, "description")
        };
    }

    /// <param name="attributes">
    /// Which attributes to ask for. Defaults to the handful <see cref="MapEntry"/> needs — an
    /// LDAP search returns only what it requests, so this is not a filter over a full entry but
    /// the entire content of the answer. <see cref="GetUserAttributes"/> passes
    /// <see cref="AllUserAttributes"/> because "every attribute" is exactly its job.
    /// </param>
    private LdapEntry? SearchOne(LdapConnection conn, string username, string[]? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var login = username.Trim();
        var escaped = EscapeLdapFilterValue(login);

        var candidateFilters = new List<string>
        {
            string.Format(_settings.UserSearchFilter, escaped),
            $"(&(objectClass=person)(mail={escaped}))",
            $"(&(objectClass=person)(userPrincipalName={escaped}))",
            $"(&(objectClass=person)(sAMAccountName={escaped}))",
            $"(&(objectClass=person)(uid={escaped}))"
        };

        // Last resort for a UPN-shaped login: match the part before '@' as an account name.
        // Active Directory accepts "user@domain" at bind time through the implicit UPN even
        // when the account carries no userPrincipalName attribute at all — so a login that
        // authenticates perfectly well can be invisible to every filter above. Kept last so a
        // real mail/UPN match always wins, and the search stays inside BaseDn, so a hit is an
        // account in this directory; the password is still proven by the bind afterwards.
        if (SplitUpnLocalPart(login) is { } localPart)
        {
            var escapedLocalPart = EscapeLdapFilterValue(localPart);
            candidateFilters.Add($"(&(objectClass=person)(sAMAccountName={escapedLocalPart}))");
            candidateFilters.Add($"(&(objectClass=person)(uid={escapedLocalPart}))");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filter in candidateFilters)
        {
            if (!seen.Add(filter))
                continue;

            var results = conn.Search(
                _settings.BaseDn,
                LdapConnection.ScopeSub,
                filter,
                attributes ?? RequestedUserAttributes,
                false);

            // A computer's sAMAccountName or a contact's mail can answer a login too; neither is
            // an account a person signs in with or an administrator manages from the users screen.
            while (NextEntryOrNull(results) is { } entry)
            {
                if (IsAccount(entry))
                    return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// The account part of a <c>user@domain</c> login, or null when the login is not shaped
    /// like one. Requires exactly one '@' with text on both sides, so an ordinary account name
    /// is left alone and a malformed login does not turn into a broader search.
    /// </summary>
    internal static string? SplitUpnLocalPart(string login)
    {
        var parts = login.Split('@');
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0
            ? parts[0]
            : null;
    }

    /// <summary>
    /// Returns the next real entry, stepping over continuation references.
    ///
    /// Active Directory answers every subtree search rooted at the domain naming context with
    /// three referrals (ForestDnsZones, DomainDnsZones, CN=Configuration) alongside the matched
    /// entries, and the Novell client reports each of them by throwing
    /// <see cref="LdapReferralException"/> — a subclass of <see cref="LdapException"/> — from
    /// <c>Next()</c>. <c>HasMore()</c> counts those references as results, so a search that
    /// matched nobody used to throw on the very first <c>Next()</c>: an ordinary "no such user"
    /// arrived at the caller as "the directory could not be searched", i.e. an outage. Entries
    /// come before references, so a search that did match was unaffected — which is why this
    /// only ever showed up for logins that do not exist.
    /// </summary>
    private static LdapEntry? NextEntryOrNull(ILdapSearchResults results)
    {
        while (results.HasMore())
        {
            try
            {
                return results.Next();
            }
            catch (LdapReferralException)
            {
                // A pointer to another partition, not an entry, and not a failure either.
            }
        }

        return null;
    }

    /// <summary>
    /// Opens the admin connection, translating Novell's <see cref="LdapException"/> into the
    /// port's own failure type. Without this the library's exception escapes the adapter — it
    /// leaks the LDAP client into the layers above and turns an outage into a generic 500.
    /// </summary>
    private LdapConnection ConnectOrThrow()
    {
        try
        {
            return _connectionFactory.CreateAdminConnection();
        }
        catch (LdapException ex)
        {
            throw new DirectoryUnavailableException(ApplicationMessages.DirectoryUnreachable(), ex);
        }
    }

    /// <summary>
    /// Refuses a parent outside the base DN, for an entry being created or moved.
    ///
    /// The base DN is the part of the directory this application is given to manage: everything
    /// it lists and every id it hands out are inside it. Where a new unit, group or computer went,
    /// though, was whatever DN the request named, so anyone allowed to manage them could write one
    /// anywhere the bind account could write — into AD's built-in containers, or another team's
    /// subtree. Checked before the directory is asked, so nothing is written.
    ///
    /// An empty base DN confines nothing: the whole directory is then the managed part.
    /// </summary>
    /// <exception cref="DomainValidationException">The parent is not the base DN or beneath it.</exception>
    private void EnsureInsideBase(string parentDn)
    {
        if (!DistinguishedName.TryCreate(_settings.BaseDn, out var managed))
            return;

        if (DistinguishedName.TryCreate(parentDn, out var parent) && parent!.IsWithin(managed!))
            return;

        throw new DomainValidationException(ApplicationMessages.ParentOutsideBaseDn(managed!.Value));
    }

    private void EnsureWriteOperationsSupported()
    {
        if (_settings.ResolvedPort is LdapSecurityPorts.GlobalCatalog or LdapSecurityPorts.GlobalCatalogOverTls)
        {
            throw new DirectoryUnavailableException(
                ApplicationMessages.WriteOperationsUnsupportedOnGlobalCatalog());
        }
    }

    private static string EscapeLdapFilterValue(string value)
    {
        return value
            .Replace("\\", "\\5c", StringComparison.Ordinal)
            .Replace("*", "\\2a", StringComparison.Ordinal)
            .Replace("(", "\\28", StringComparison.Ordinal)
            .Replace(")", "\\29", StringComparison.Ordinal)
            .Replace("\0", "\\00", StringComparison.Ordinal);
    }

    /// <param name="namedBy">
    /// Groups found to list the entry as a member, merged with its memberOf. See
    /// <see cref="GroupsNaming"/>.
    /// </param>
    private LdapUserInfo MapEntry(LdapEntry entry, IReadOnlyCollection<string>? namedBy = null)
    {
        var attrs = entry.GetAttributeSet();
        var memberOf = attrs.TryGetValue("memberOf", out var mo) ? mo.StringValueArray ?? [] : [];
        return new LdapUserInfo
        {
            Dn = entry.Dn,
            Username = Get(attrs, "uid", "sAMAccountName", "userPrincipalName", "cn"),
            DisplayName = Get(attrs, "displayName", "cn"),
            Email = Get(attrs, "mail"),
            Groups = namedBy is null ? memberOf : MergeGroups(memberOf, namedBy),
            IsPrivileged = IsMarkedPrivileged(attrs),
            IsServiceAccount = IsServiceAccount(entry.Dn, attrs),
            IsDisabled = IsSwitchedOff(attrs, DateTimeOffset.UtcNow)
        };
    }

    /// <summary>
    /// The groups that list this entry as a member, asked of the groups themselves.
    ///
    /// Membership used to be read from memberOf alone, and memberOf is not stored anywhere: Active
    /// Directory computes it, and OpenLDAP only with the memberof overlay — which a default
    /// install does not load, and which even where it is loaded is often set up for one group
    /// class and not the other. Without it every account came back in no groups at all. An
    /// administrator then signed in as a plain User, which fails safe; the same empty list also
    /// meant no account was ever recognised as a member of a protected group, which does not —
    /// anyone allowed to reset passwords could reset an administrator's.
    ///
    /// So the groups are searched as well, in each shape a directory keeps membership:
    /// groupOfNames and Active Directory's group by member, groupOfUniqueNames by uniqueMember,
    /// and posixGroup by the login in memberUid. The result is merged with memberOf rather than
    /// replacing it, so a directory that has both loses nothing. Nested groups are not followed
    /// — as before, and as memberOf does not follow them either.
    /// </summary>
    /// <exception cref="LdapException">The search failed; the caller reports it as such.</exception>
    private string[] GroupsNaming(LdapConnection conn, LdapEntry entry)
    {
        var filter = GroupMembershipFilter(entry.Dn, Get(entry.GetAttributeSet(), "uid"));

        // "1.1" asks for no attributes: the DN of each group is all this needs.
        var results = conn.Search(_settings.BaseDn, LdapConnection.ScopeSub, filter, ["1.1"], false);

        var groups = new List<string>();
        while (NextEntryOrNull(results) is { } group)
            groups.Add(group.Dn);

        return [.. groups];
    }

    /// <summary>
    /// The filter for every group that names an entry, by its DN or — for posixGroup — by its
    /// login. Both are escaped: a DN can carry parentheses or an asterisk in its own name, and
    /// unescaped they would end the filter early or widen it to groups that do not name it.
    /// </summary>
    internal static string GroupMembershipFilter(string distinguishedName, string? uid)
    {
        var dn = EscapeLdapFilterValue(distinguishedName);
        return "(|"
            + $"(&(objectClass=groupOfNames)(member={dn}))"
            + $"(&(objectClass=group)(member={dn}))"
            + $"(&(objectClass=groupOfUniqueNames)(uniqueMember={dn}))"
            + (string.IsNullOrWhiteSpace(uid)
                ? string.Empty
                : $"(&(objectClass=posixGroup)(memberUid={EscapeLdapFilterValue(uid)}))")
            + ")";
    }

    /// <summary>
    /// Refuses a login that something in the directory already answers to, before the entry that
    /// would carry it exists.
    ///
    /// Another entry with the same uid leaves the login naming two accounts, and sign-in takes
    /// whichever the directory returns first. A posixGroup listing it in memberUid is the sharper
    /// case. That membership is by name rather than by entry — see GroupMembershipFilter — and the
    /// name stays in the group after the account it belonged to is deleted. A new account given
    /// it was counted a member the moment it signed in, and held whatever role the group is mapped
    /// to, an administrator's included: granted by someone who was only allowed to create
    /// accounts, or, with self-registration on, by nobody at all. So the login is refused until
    /// the group stops naming it.
    /// </summary>
    /// <exception cref="DomainConflictException">An entry has the uid, or a posixGroup names it.</exception>
    /// <exception cref="DirectoryUnavailableException">The directory could not be asked.</exception>
    private void EnsureLoginIsUnclaimed(LdapConnection conn, string uid)
    {
        var claimants = new List<LdapEntry>();
        try
        {
            var results = conn.Search(_settings.BaseDn, LdapConnection.ScopeSub, LoginClaimFilter(uid), ["objectClass"], false);
            while (NextEntryOrNull(results) is { } claimant)
                claimants.Add(claimant);
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Could not check whether login {Login} is free.", LogMask.Login(uid));
            throw new DirectoryUnavailableException(ApplicationMessages.DirectorySearchFailed(), ex);
        }

        if (claimants.FirstOrDefault(IsPosixGroup) is { } group)
        {
            _logger.LogWarning("Refused to create {Login}: posixGroup {Group} still lists it in memberUid.",
                LogMask.Login(uid), group.Dn);
            throw new DomainConflictException(ApplicationMessages.LoginStillNamedByGroup());
        }

        if (claimants.Count > 0)
            throw new DomainConflictException(ApplicationMessages.UserAlreadyExistsInDirectory());
    }

    /// <summary>
    /// Every entry that already answers to a login: one that carries it as its uid, or a
    /// posixGroup that names it as a member. Escaped like the filters above.
    /// </summary>
    internal static string LoginClaimFilter(string uid)
    {
        var escaped = EscapeLdapFilterValue(uid);
        return $"(|(uid={escaped})(&(objectClass=posixGroup)(memberUid={escaped})))";
    }

    private static bool IsPosixGroup(LdapEntry entry) =>
        entry.GetAttributeSet().TryGetValue("objectClass", out var classes)
        && (classes.StringValueArray ?? []).Contains("posixGroup", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// memberOf and the searched groups as one list, each group once. The same group can be
    /// spelled differently by the two — case, spacing after commas — so they are compared as
    /// distinguished names rather than as text.
    /// </summary>
    internal static string[] MergeGroups(IEnumerable<string> memberOf, IEnumerable<string> namedBy)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();

        foreach (var group in memberOf.Concat(namedBy))
        {
            var key = DistinguishedName.TryCreate(group, out var parsed) ? parsed!.Normalized : group.Trim();
            if (seen.Add(key))
                merged.Add(group);
        }

        return [.. merged];
    }

    /// <summary>
    /// A person's account rather than a computer or contact — see <see cref="NonAccountClasses"/>.
    /// An entry read without its objectClass is given the benefit of the doubt, as before.
    /// </summary>
    private static bool IsAccount(LdapEntry entry) =>
        !entry.GetAttributeSet().TryGetValue("objectClass", out var classes)
        || !(classes.StringValueArray ?? []).Any(value =>
            NonAccountClasses.Contains(value, StringComparer.OrdinalIgnoreCase));

    /// <summary>adminCount=1: Active Directory's own mark on protected accounts and groups.</summary>
    private static bool IsMarkedPrivileged(LdapAttributeSet attrs) =>
        int.TryParse(Get(attrs, "adminCount"), out var count) && count > 0;

    /// <summary>
    /// The value OpenLDAP's password policy writes to pwdAccountLockedTime for a lock that only an
    /// administrator lifts, as opposed to the time of a lockout that expires on its own.
    /// </summary>
    private const string PermanentLockTime = "000001010000Z";

    /// <summary>
    /// Whether the directory has switched the account off, in any of the ways the directories this
    /// provider speaks to record it:
    /// <list type="bullet">
    /// <item>Active Directory: the ACCOUNTDISABLE bit of userAccountControl, or an accountExpires
    /// in the past (0 and the largest value both mean "never").</item>
    /// <item>OpenLDAP with the password policy overlay: pwdAccountLockedTime set to
    /// <see cref="PermanentLockTime"/>, an administrator's lock.</item>
    /// <item>eDirectory: loginDisabled TRUE.</item>
    /// </list>
    /// A lockout that follows failed sign-ins is deliberately not counted, in either directory:
    /// anyone can cause one by mistyping a password often enough, and counting it would let them
    /// end its owner's session at will. It still stops a new sign-in, at the bind.
    /// </summary>
    internal static bool IsSwitchedOff(LdapAttributeSet attrs, DateTimeOffset now)
    {
        if (int.TryParse(Get(attrs, "userAccountControl"), out var flags) && (flags & AccountDisabledFlag) != 0)
            return true;

        if (long.TryParse(Get(attrs, "accountExpires"), out var expires)
            && expires > 0
            && expires < DateTime.MaxValue.ToFileTimeUtc()
            && DateTime.FromFileTimeUtc(expires) <= now.UtcDateTime)
        {
            return true;
        }

        if (string.Equals(Get(attrs, "pwdAccountLockedTime").Trim(), PermanentLockTime, StringComparison.Ordinal))
            return true;

        return string.Equals(Get(attrs, "loginDisabled").Trim(), "TRUE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the entry is the account this provider binds as. The bind name may be written as a
    /// DN (OpenLDAP's usual cn=admin,…) or as a login (AD's svc@corp or a bare sAMAccountName), so
    /// it is compared with the DN and with every login attribute the entry carries.
    /// </summary>
    private bool IsServiceAccount(string dn, LdapAttributeSet attrs)
    {
        var bindName = _settings.AdminUsername?.Trim();
        if (string.IsNullOrEmpty(bindName))
            return false;

        if (DistinguishedName.TryCreate(dn, out var entryDn) && DistinguishedName.TryCreate(bindName, out var bindDn)
            && string.Equals(entryDn!.Normalized, bindDn!.Normalized, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return new[] { "uid", "sAMAccountName", "userPrincipalName", "mail" }
            .Select(name => Get(attrs, name))
            .Any(value => value.Length > 0 && string.Equals(value, bindName, StringComparison.OrdinalIgnoreCase));
    }

    private static string Get(LdapAttributeSet attrs, params string[] names)
    {
        foreach (var name in names)
            if (attrs.TryGetValue(name, out var attr))
                return attr.StringValue ?? string.Empty;
        return string.Empty;
    }
}
