using MediatR;
using Nelsar.Axia.Application.Features.Providers.Commands;
using Nelsar.Axia.Application.Features.Providers.Queries;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Application.Modules;

/// <summary>The providers module.</summary>
internal sealed class ProvidersModule : IProvidersModule
{
    private readonly ISender _sender;

    public ProvidersModule(ISender sender)
    {
        _sender = sender;
    }

    public Task<IEnumerable<ProviderDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _sender.Send(new GetAllProvidersQuery(), cancellationToken);

    public Task<IEnumerable<SelectableProviderDto>> GetSelectableAsync(
        CancellationToken cancellationToken = default) =>
        _sender.Send(new GetSelectableProvidersQuery(), cancellationToken);

    public Task<ProviderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new GetProviderByIdQuery(id), cancellationToken);

    public Task<ProviderDto> CreateAsync(
        ProviderCreateRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(
            new CreateProviderCommand(
                request.Name,
                request.Host,
                request.Port,
                request.Security,
                request.ProviderType,
                request.IsActive,
                request.FailoverHosts,
                request.BaseDn,
                request.AdminUsername,
                request.AdminPassword,
                request.UserSearchFilter,
                request.UserSearchPageSize),
            cancellationToken);

    /// <summary>A null <c>AdminPassword</c> means "keep the stored credential"; the handler is
    /// what acts on that, so it travels through unchanged.</summary>
    public Task<ProviderDto?> UpdateAsync(
        Guid id,
        ProviderUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(
            new UpdateProviderCommand(
                id,
                request.Name,
                request.Host,
                request.Port,
                request.Security,
                request.ProviderType,
                request.IsActive,
                request.FailoverHosts,
                request.BaseDn,
                request.AdminUsername,
                request.AdminPassword,
                request.UserSearchFilter,
                request.UserSearchPageSize),
            cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new DeleteProviderCommand(id), cancellationToken);
}
