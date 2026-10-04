using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Users.Queries;

/// <summary>Every attribute of the account's directory entry, or null when there is no such
/// account. Credential attributes are reported by name with their values withheld.</summary>
internal sealed record GetUserAttributesQuery(Guid Id) : IRequest<UserAttributesDto?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersRead;
}

internal sealed class GetUserAttributesQueryHandler : IRequestHandler<GetUserAttributesQuery, UserAttributesDto?>
{
    private readonly UserDirectory _directory;
    private readonly ILdapService _ldapService;

    public GetUserAttributesQueryHandler(UserDirectory directory, ILdapService ldapService)
    {
        _directory = directory;
        _ldapService = ldapService;
    }

    public async Task<UserAttributesDto?> Handle(GetUserAttributesQuery request, CancellationToken cancellationToken)
    {
        var user = await _directory.ResolveAsync(request.Id);
        if (user == null)
            return null;

        var entry = await _ldapService.GetUserAttributesAsync(user.DirectoryAddress);

        // The account resolved a moment ago but its entry no longer answers — it was removed
        // between the two reads. Null again, so the caller reports 404 rather than an empty
        // attribute list, which would read as "this user has no attributes".
        if (entry == null)
            return null;

        return new UserAttributesDto
        {
            Id = user.Id,
            Dn = entry.Dn,
            Attributes = entry.Attributes
                .Select(attribute => new DirectoryAttributeDto
                {
                    Name = attribute.Name,
                    Values = attribute.Values,
                    IsBinary = attribute.IsBinary,
                    IsRedacted = attribute.IsRedacted
                })
                .ToList()
        };
    }
}
