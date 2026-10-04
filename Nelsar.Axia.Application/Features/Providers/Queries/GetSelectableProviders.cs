using MediatR;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.Providers.Queries;

/// <summary>
/// The directories a caller may choose between when signing in.
///
/// It carries no <c>IRequirePermission</c>, and that is the point rather than an oversight. The
/// question it answers comes before authentication: a caller must name a directory to be
/// authenticated against, so requiring an authenticated session to learn which directories exist
/// leaves the only path in through a raw Guid typed from somewhere else. Until now that Guid was
/// only obtainable from <c>GET /api/v1/providers</c>, which needs providers.manage — so in
/// practice nobody but an administrator could sign in to anything but the default directory.
///
/// The omission is asserted, not assumed: see UseCaseAuthorizationTests, which lists the use
/// cases that are open on purpose so that an accidental one stands out against them.
///
/// Inactive providers are filtered out here rather than by the caller. Offering one would be
/// offering a choice that cannot work — DefaultLdapProviderFactory refuses a deactivated
/// directory with a 400 — and a picker whose entries sometimes fail is worse than a shorter one.
/// </summary>
internal sealed record GetSelectableProvidersQuery : IRequest<IEnumerable<SelectableProviderDto>>;

internal sealed class GetSelectableProvidersQueryHandler
    : IRequestHandler<GetSelectableProvidersQuery, IEnumerable<SelectableProviderDto>>
{
    private readonly IProviderRepository _repo;

    public GetSelectableProvidersQueryHandler(IProviderRepository repo) => _repo = repo;

    public async Task<IEnumerable<SelectableProviderDto>> Handle(
        GetSelectableProvidersQuery request,
        CancellationToken cancellationToken)
    {
        var all = await _repo.GetAllAsync();

        return all.Where(provider => provider.IsActive)
            .OrderBy(provider => provider.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ProviderMapping.ToSelectableDto)
            .ToList();
    }
}
