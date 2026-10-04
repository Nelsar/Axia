using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.Providers.Queries;

/// <summary>Null when there is no such provider.</summary>
internal sealed record GetProviderByIdQuery(Guid Id) : IRequest<ProviderDto?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ProvidersManage;
}

internal sealed class GetProviderByIdQueryHandler : IRequestHandler<GetProviderByIdQuery, ProviderDto?>
{
    private readonly IProviderRepository _repo;

    public GetProviderByIdQueryHandler(IProviderRepository repo) => _repo = repo;

    public async Task<ProviderDto?> Handle(GetProviderByIdQuery request, CancellationToken cancellationToken)
    {
        var provider = await _repo.GetByIdAsync(request.Id);
        return provider == null ? null : ProviderMapping.ToDto(provider);
    }
}
