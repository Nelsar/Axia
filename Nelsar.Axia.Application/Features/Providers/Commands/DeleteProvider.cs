using MediatR;
using Nelsar.Axia.Application.Common.Auditing;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Domain.Authorization;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Application.Features.Providers.Commands;

/// <summary>False when there is no such provider.</summary>
internal sealed record DeleteProviderCommand(Guid Id) : IRequest<bool>, IRequirePermission, IAuditedCommand
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ProvidersManage;

    /// <inheritdoc />
    public string AuditTarget => Id.ToString();
}

internal sealed class DeleteProviderCommandHandler : IRequestHandler<DeleteProviderCommand, bool>
{
    private readonly IProviderRepository _repo;

    public DeleteProviderCommandHandler(IProviderRepository repo) => _repo = repo;

    public Task<bool> Handle(DeleteProviderCommand request, CancellationToken cancellationToken) =>
        _repo.DeleteAsync(request.Id);
}
