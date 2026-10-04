using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.Providers.Queries;

internal sealed record GetAllProvidersQuery : IRequest<IEnumerable<ProviderDto>>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ProvidersManage;
}

internal sealed class GetAllProvidersQueryHandler : IRequestHandler<GetAllProvidersQuery, IEnumerable<ProviderDto>>
{
    private readonly IProviderRepository _repo;

    public GetAllProvidersQueryHandler(IProviderRepository repo) => _repo = repo;

    public async Task<IEnumerable<ProviderDto>> Handle(GetAllProvidersQuery request, CancellationToken cancellationToken)
    {
        var list = await _repo.GetAllAsync();
        return list.Select(ProviderMapping.ToDto);
    }
}
