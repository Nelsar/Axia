using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Application.Common.Validation;
using Nelsar.Axia.Application.Common;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Application.Interfaces;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Responses;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Application.Features.Users.Queries;

/// <summary>
/// Users matching a free-text query, paged.
///
/// Distinct from <see cref="GetAllUsersQuery"/> in more than the filter: it pushes the query down
/// to the directory as an LDAP filter instead of enumerating, and it projects with its own
/// mapping — see the note on <see cref="Map"/>.
/// </summary>
internal sealed record SearchUsersQuery(string? Filter, int PageSize, string? PageToken)
    : IRequest<PagedResponse<UserDto>>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.UsersRead;
}

internal sealed class SearchUsersQueryValidator : AbstractValidator<SearchUsersQuery>
{
    public SearchUsersQueryValidator() => RuleFor(q => q.PageSize).PageSize();
}

internal sealed class SearchUsersQueryHandler : IRequestHandler<SearchUsersQuery, PagedResponse<UserDto>>
{
    private readonly ILdapService _ldapService;
    private readonly ILogger<SearchUsersQueryHandler> _logger;

    public SearchUsersQueryHandler(ILdapService ldapService, ILogger<SearchUsersQueryHandler> logger)
    {
        _ldapService = ldapService;
        _logger = logger;
    }

    public async Task<PagedResponse<UserDto>> Handle(SearchUsersQuery request, CancellationToken cancellationToken)
    {
        string? ldapFilter = null;
        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            ldapFilter = BuildLdapUserSearchFilter(request.Filter);
            _logger.LogDebug("SearchUsers using LDAP filter {Filter}", ldapFilter);
        }

        var listing = string.IsNullOrWhiteSpace(ldapFilter)
            ? await _ldapService.GetAllUsersAsync()
            : await _ldapService.SearchUsersAsync(ldapFilter);

        var mapped = (listing?.Users ?? Array.Empty<LdapUserInfo>()).Select(Map).ToList();

        return PagedProjection.Page(mapped, request.PageSize, request.PageToken, listing?.IsComplete ?? true);
    }

    /// <summary>
    /// Search's own projection, carried over unchanged from LdapManagementService.
    ///
    /// It is NOT <see cref="UserDirectory.Map"/>, and the difference is deliberate rather than an
    /// oversight to tidy: this one still substitutes DisplayName into Email when the entry has no
    /// mail, and derives the id from (Email, Dn, DisplayName) where the listing uses
    /// (mail-or-login, Dn, Username). Unifying them would change the ids search hands out, which
    /// clients hold in URLs. Worth doing, but as its own change with its own test — not smuggled
    /// in under a refactor.
    /// </summary>
    private static UserDto Map(LdapUserInfo entry) => new()
    {
        Id = DeterministicIdFactory.BuildUserId(entry.Email, entry.Dn, entry.DisplayName),
        Email = string.IsNullOrWhiteSpace(entry.Email) ? entry.DisplayName : entry.Email,
        Name = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Email : entry.DisplayName,
        Role = UserRoles.User
    };

    private static string BuildLdapUserSearchFilter(string query)
    {
        var terms = query.Trim()
            .Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(LdapFilterEscaper.Escape)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (terms.Length == 0)
            return "(&(objectClass=user)(objectCategory=person))";

        var fields = new[]
        {
            "mail",
            "displayName",
            "cn",
            "uid",
            "sAMAccountName",
            "userPrincipalName",
            "givenName",
            "sn"
        };

        var clauses = string.Concat(terms.SelectMany(term => fields.Select(field => $"({field}=*{term}*)")));
        return $"(&(objectClass=user)(objectCategory=person)(|{clauses}))";
    }
}
