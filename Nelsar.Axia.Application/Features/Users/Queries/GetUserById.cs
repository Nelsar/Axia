using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Users.Queries;

/// <summary>Null when there is no such account. A miss against a truncated directory walk is not
/// absence and raises instead — see <see cref="UserDirectory.ResolveAsync"/>.</summary>
internal sealed record GetUserByIdQuery(Guid Id) : IRequest<UserDto?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersRead;
}

internal sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    private readonly UserDirectory _directory;
    private readonly ILdapService _ldapService;

    public GetUserByIdQueryHandler(UserDirectory directory, ILdapService ldapService)
    {
        _directory = directory;
        _ldapService = ldapService;
    }

    public async Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await _directory.ResolveAsync(request.Id);
        if (user is null || string.IsNullOrWhiteSpace(user.Dn))
            return user;

        // One account, so its groups are read the way sign-in reads them: memberOf together with
        // the groups that list it. What ResolveAsync hands back is often the full listing's copy,
        // cached for half an hour, and the listing takes memberOf alone — the groups for every
        // entry would be a search each. In a directory that keeps no memberOf that is no groups
        // at all, and the detail view said so of accounts that were members.
        var entry = await _ldapService.GetUserByDnAsync(user.Dn);
        if (entry is null)
            return user;

        // A copy: the cached DTO is shared with every other request that reads it.
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            Username = user.Username,
            Name = user.Name,
            Role = user.Role,
            Dn = user.Dn,
            Groups = [.. entry.Groups]
        };
    }
}
