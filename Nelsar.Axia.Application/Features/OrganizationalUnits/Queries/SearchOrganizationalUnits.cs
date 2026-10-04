using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Common.Validation;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.OrganizationalUnits.Queries;

/// <summary>Units under the configured base DN, filtered in memory and paged. Carries
/// <c>IsComplete</c>: a truncated enumeration is not proof that a unit is absent.</summary>
internal sealed record SearchOrganizationalUnitsQuery(string? Filter, int PageSize, string? PageToken)
    : IRequest<PagedResponse<OrganizationalUnitDto>>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.OrganizationalUnitsRead;
}

internal sealed class SearchOrganizationalUnitsQueryValidator : AbstractValidator<SearchOrganizationalUnitsQuery>
{
    public SearchOrganizationalUnitsQueryValidator() => RuleFor(q => q.PageSize).PageSize();
}

internal sealed class SearchOrganizationalUnitsQueryHandler
    : IRequestHandler<SearchOrganizationalUnitsQuery, PagedResponse<OrganizationalUnitDto>>
{
    private readonly OrganizationalUnitDirectory _directory;

    public SearchOrganizationalUnitsQueryHandler(OrganizationalUnitDirectory directory) => _directory = directory;

    public async Task<PagedResponse<OrganizationalUnitDto>> Handle(
        SearchOrganizationalUnitsQuery request,
        CancellationToken cancellationToken)
    {
        var (units, isComplete) = await _directory.ListAsync();

        var matched = units
            .Where(unit => OrganizationalUnitDirectory.MatchesFilter(unit, request.Filter))
            // Ordered by DN so paging is stable: LDAP promises no particular order, and without
            // this an entry could be shown on two pages or on none as the server sees fit.
            .OrderBy(unit => unit.Dn, StringComparer.OrdinalIgnoreCase)
            .Select(OrganizationalUnitDirectory.Map)
            .ToList();

        return PagedProjection.Page(matched, request.PageSize, request.PageToken, isComplete);
    }
}
