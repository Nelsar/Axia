using MediatR;
using Nelsar.Axia.Application.Common.Authorization;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Authorization;

namespace Nelsar.Axia.Application.Features.Computers.Queries;

/// <summary>Null when there is no such account. A miss against a truncated enumeration is not
/// absence, and raises rather than returning null.</summary>
internal sealed record GetComputerByIdQuery(Guid Id) : IRequest<ComputerDto?>, IRequirePermission
{
    /// <inheritdoc />
    public string RequiredPermission => Permissions.ComputersRead;
}

internal sealed class GetComputerByIdQueryHandler : IRequestHandler<GetComputerByIdQuery, ComputerDto?>
{
    private readonly ComputerDirectory _directory;

    public GetComputerByIdQueryHandler(ComputerDirectory directory) => _directory = directory;

    public async Task<ComputerDto?> Handle(GetComputerByIdQuery request, CancellationToken cancellationToken)
    {
        var computer = await _directory.ResolveAsync(request.Id);
        return computer == null ? null : ComputerDirectory.Map(computer);
    }
}
