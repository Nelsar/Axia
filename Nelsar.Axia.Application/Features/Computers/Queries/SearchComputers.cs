using FluentValidation;
using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Common.Validation;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Computers.Queries;

/// <summary>Computer accounts under the configured base DN, filtered in memory and paged.
/// Carries whether the underlying enumeration finished.</summary>
internal sealed record SearchComputersQuery(string? Filter, int PageSize, string? PageToken)
    : IRequest<PagedResponse<ComputerDto>>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ComputersRead;
}

internal sealed class SearchComputersQueryValidator : AbstractValidator<SearchComputersQuery>
{
    public SearchComputersQueryValidator() => RuleFor(q => q.PageSize).PageSize();
}

internal sealed class SearchComputersQueryHandler : IRequestHandler<SearchComputersQuery, PagedResponse<ComputerDto>>
{
    private readonly ComputerDirectory _directory;

    public SearchComputersQueryHandler(ComputerDirectory directory) => _directory = directory;

    public async Task<PagedResponse<ComputerDto>> Handle(SearchComputersQuery request, CancellationToken cancellationToken)
    {
        var (computers, isComplete) = await _directory.ListAsync();

        var matched = computers
            .Where(computer => ComputerDirectory.MatchesFilter(computer, request.Filter))
            // Ordered by DN so paging is stable, as for groups and units: LDAP promises no
            // particular order, and without this an entry could appear on two pages or on none.
            .OrderBy(computer => computer.Dn, StringComparer.OrdinalIgnoreCase)
            .Select(ComputerDirectory.Map)
            .ToList();

        return PagedProjection.Page(matched, request.PageSize, request.PageToken, isComplete);
    }
}
