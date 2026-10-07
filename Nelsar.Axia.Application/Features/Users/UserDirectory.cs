using Microsoft.Extensions.Caching.Memory;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Contracts.Exceptions;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Application.Resources;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Entities;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Repositories;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Application.Features.Users;

/// <summary>
/// Reading an account out of the directory and keeping the id it is addressed by resolvable.
///
/// This is deliberately not a use case and has no command of its own. Six handlers need to turn
/// an id back into a user before they can do anything — get, update, delete, attributes, and the
/// two that read a listing — and the resolution is not a lookup but a policy: cache first, then
/// the lookup index, then a full directory walk, with a stale index row dropped on the way. Had
/// that been inlined per handler it would exist in six copies, and the copies would drift.
///
/// What belongs in a handler is the decision a use case makes. What lives here is the shared
/// reading those decisions are made on.
/// </summary>
internal sealed class UserDirectory
{
    private readonly ILdapService _ldapService;
    private readonly IMemoryCache _userCache;
    private readonly IUserLookupRepository _userLookup;

    // An email -> UserDto cache, so a Guid can be resolved quickly after sign-in.
    // Sliding expiration rather than keeping entries forever: without it they would pile up in
    // memory indefinitely. The cache is still process-local — it does not survive a restart and
    // is not shared between instances — so scaling horizontally would need a distributed cache
    // (Redis or similar).
    private static readonly MemoryCacheEntryOptions UserCacheEntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(30)
    };

    public UserDirectory(ILdapService ldapService, IMemoryCache userCache, IUserLookupRepository userLookup)
    {
        _ldapService = ldapService;
        _userCache = userCache;
        _userLookup = userLookup;
    }

    public async Task<UserListingResponse> ListAsync()
    {
        var listing = await _ldapService.GetAllUsersAsync();

        // Materialised deliberately. The provider already builds the whole list before it
        // returns (see StandardLdapProvider.GetAllUsers), so deferring the projection saved
        // nothing real — it only made caching depend on whether, and how far, the caller
        // happened to enumerate the result.
        var users = listing.Users.Select(Map).ToList();

        foreach (var user in users)
            _userCache.Set(user.Id, user, UserCacheEntryOptions);

        return new UserListingResponse { Items = users, IsComplete = listing.IsComplete };
    }

    public async Task<UserDto?> ResolveAsync(Guid id)
    {
        if (_userCache.TryGetValue(id, out UserDto? cachedUser) && cachedUser != null)
            return cachedUser;

        var indexed = await ResolveFromLookupIndexAsync(id);
        if (indexed != null)
            return indexed;

        // Cold index. The id is a hash of the address (see DeterministicIdFactory), so there
        // is no filter the directory could be searched by — the only way left is to walk it
        // and recompute every entry's id. Whatever we find is indexed on the way out, so this
        // happens once per identity instead of once per cache miss.
        var listing = await ListAsync();
        var match = listing.Items.FirstOrDefault(u => u.Id == id);
        if (match != null)
        {
            await RememberAsync(match);
            return match;
        }

        if (!listing.IsComplete)
        {
            // Not finding the id proves nothing when the walk never reached the end — the user
            // may be sitting in the pages that failed to load. Reporting 404 here would tell
            // the caller an account does not exist on the strength of a search that stopped
            // early.
            throw new DirectoryUnavailableException(
                ApplicationMessages.DirectoryEnumerationIncomplete());
        }

        return null;
    }

    public async Task RememberAsync(UserDto user)
    {
        // Only a DTO that knows its entry is worth serving from memory. Updates and deletes act
        // on user.Dn now, so a cached one without it — what CreateUser has in hand before the
        // directory has said where the entry landed — would be an account nothing can be done
        // to for the next half hour. Without the cache the next lookup reads the entry instead.
        if (!string.IsNullOrWhiteSpace(user.Dn))
            _userCache.Set(user.Id, user, UserCacheEntryOptions);

        // Index only identities whose id genuinely derives from the address, so that every
        // lookup can be verified on the way back. Anything else (an entry with neither a mail
        // attribute nor a login) stays walk-only.
        if (DeterministicIdFactory.BuildUserId(user.DirectoryAddress) == user.Id)
            await _userLookup.RecordAsync(user.Id, user.DirectoryAddress);
    }

    /// <summary>
    /// Refuses to give the entry at <paramref name="ownDn"/> an address another entry already
    /// answers to.
    ///
    /// Nothing else stops it: the directory does not keep mail unique, and CreateUser checks for a
    /// clash while an address change did not. A shared address is a login two entries answer to,
    /// and a lookup by it returns whichever the directory lists first — the next update aimed at
    /// one could land on the other, and a person could be told they signed in as somebody else.
    /// </summary>
    /// <exception cref="DomainConflictException">Another entry answers to the address.</exception>
    public async Task EnsureAddressIsFreeAsync(string address, string ownDn)
    {
        var holder = await _ldapService.GetUserDetailsAsync(address);
        if (holder is null)
            return;

        var sameEntry = DistinguishedName.TryCreate(holder.Dn, out var holderDn)
                        && DistinguishedName.TryCreate(ownDn, out var own)
                        && string.Equals(holderDn!.Normalized, own!.Normalized, StringComparison.OrdinalIgnoreCase);

        if (!sameEntry)
            throw new DomainConflictException(ApplicationMessages.EmailAlreadyInUse());
    }

    public async Task ForgetAsync(Guid id)
    {
        _userCache.Remove(id);
        await _userLookup.RemoveAsync(id);
    }

    public static UserDto Map(LdapUserInfo entry)
    {
        var username = entry.Username;
        var name = string.IsNullOrWhiteSpace(entry.DisplayName) ? username : entry.DisplayName;

        // The address the id derives from is still mail-or-login, exactly as before, so no
        // existing id changes. What changed is that the substitution no longer leaks into the
        // Email field: an entry without mail now reports an empty Email and its login under
        // Username, instead of showing a cn to anyone reading a field labelled "email".
        var address = string.IsNullOrWhiteSpace(entry.Email) ? username : entry.Email;

        return new UserDto
        {
            Id = DeterministicIdFactory.BuildUserId(address, entry.Dn, username),
            Email = entry.Email,
            Username = username,
            Name = name,
            Role = UserRoles.User,
            Dn = entry.Dn,
            Groups = entry.Groups
        };
    }

    private async Task<UserDto?> ResolveFromLookupIndexAsync(Guid id)
    {
        var email = await _userLookup.FindEmailAsync(id);
        if (email == null)
            return null;

        var details = await _ldapService.GetUserDetailsAsync(email);
        var resolved = details == null ? null : Map(details);

        // The id derives from the address, so a row that no longer maps back to the same id is
        // stale — the account was renamed or removed. Drop it and let the caller fall back to
        // a walk, rather than serving one user under another's id.
        if (resolved == null || resolved.Id != id)
        {
            await _userLookup.RemoveAsync(id);
            return null;
        }

        _userCache.Set(resolved.Id, resolved, UserCacheEntryOptions);
        return resolved;
    }
}
