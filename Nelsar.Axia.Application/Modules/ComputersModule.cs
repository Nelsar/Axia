using MediatR;
using Nelsar.Axia.Application.Features.Computers.Commands;
using Nelsar.Axia.Application.Features.Computers.Queries;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Application.Modules;

/// <summary>The computer accounts module. Same partial-update contract as
/// <see cref="GroupsModule"/>: optional fields travel through untouched, <c>Enabled</c>
/// included.</summary>
internal sealed class ComputersModule : IComputersModule
{
    private readonly ISender _sender;

    public ComputersModule(ISender sender)
    {
        _sender = sender;
    }

    public Task<PagedResponse<ComputerDto>> SearchAsync(
        string? filter,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new SearchComputersQuery(filter, pageSize, pageToken), cancellationToken);

    public Task<ComputerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new GetComputerByIdQuery(id), cancellationToken);

    public Task<ComputerDto> CreateAsync(
        CreateComputerRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(
            new CreateComputerCommand(
                request.Name,
                request.ParentDistinguishedName,
                request.Description,
                request.DnsHostName),
            cancellationToken);

    public Task<ComputerDto?> UpdateAsync(
        Guid id,
        UpdateComputerRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(
            new UpdateComputerCommand(
                id,
                request.Name,
                request.ParentDistinguishedName,
                request.Description,
                request.DnsHostName,
                request.Enabled),
            cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new DeleteComputerCommand(id), cancellationToken);
}
