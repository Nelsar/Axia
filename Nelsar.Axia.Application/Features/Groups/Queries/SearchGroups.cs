using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Common.Validation;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Groups.Queries;

/// <summary>Groups under the configured base DN, filtered in memory and paged. Carries
/// <c>IsComplete</c>: a truncated enumeration is not proof that a group is absent.</summary>
internal sealed record SearchGroupsQuery(string? Filter, int PageSize, string? PageToken)
    : IRequest<PagedResponse<GroupDto>>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.GroupsRead;
}

internal sealed class SearchGroupsQueryValidator : AbstractValidator<SearchGroupsQuery>
{
    public SearchGroupsQueryValidator() => RuleFor(q => q.PageSize).PageSize();
}

internal sealed class SearchGroupsQueryHandler : IRequestHandler<SearchGroupsQuery, PagedResponse<GroupDto>>
{
    private readonly GroupDirectory _directory;

    public SearchGroupsQueryHandler(GroupDirectory directory) => _directory = directory;

    public async Task<PagedResponse<GroupDto>> Handle(SearchGroupsQuery request, CancellationToken cancellationToken)
    {
        var (groups, isComplete) = await _directory.ListAsync();

        var matched = groups
            .Where(group => GroupDirectory.MatchesFilter(group, request.Filter))
            // Ordered by DN for the same reason the unit search is: LDAP promises no particular
            // order, and without this an entry could appear on two pages or on none.
            .OrderBy(group => group.Dn, StringComparer.OrdinalIgnoreCase)
            .Select(GroupDirectory.Map)
            .ToList();

        return PagedProjection.Page(matched, request.PageSize, request.PageToken, isComplete);
    }
}
