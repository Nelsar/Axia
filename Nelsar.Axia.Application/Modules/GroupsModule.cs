using MediatR;
using Nelsar.Axia.Application.Features.Groups.Commands;
using Nelsar.Axia.Application.Features.Groups.Queries;
using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Modules;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Application.Modules;

/// <summary>
/// The groups module. The update mapping passes every optional field through as-is, nulls
/// included: that is the whole contract of a partial update here — null leaves a value alone,
/// empty clears it, and a mapper that "helpfully" substituted string.Empty would wipe a
/// description on every rename.
/// </summary>
internal sealed class GroupsModule : IGroupsModule
{
    private readonly ISender _sender;

    public GroupsModule(ISender sender)
    {
        _sender = sender;
    }

    public Task<PagedResponse<GroupDto>> SearchAsync(
        string? filter,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken = default) =>
        _sender.Send(new SearchGroupsQuery(filter, pageSize, pageToken), cancellationToken);

    public Task<GroupDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new GetGroupByIdQuery(id), cancellationToken);

    public Task<GroupDto> CreateAsync(CreateGroupRequest request, CancellationToken cancellationToken = default) =>
        _sender.Send(
            new CreateGroupCommand(
                request.Name,
                request.ParentDistinguishedName,
                request.Description,
                request.MemberDistinguishedNames),
            cancellationToken);

    public Task<GroupDto?> UpdateAsync(
        Guid id,
        UpdateGroupRequest request,
        CancellationToken cancellationToken = default) =>
        _sender.Send(
            new UpdateGroupCommand(
                id,
                request.Name,
                request.ParentDistinguishedName,
                request.Description,
                request.MemberDistinguishedNames),
            cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        _sender.Send(new DeleteGroupCommand(id), cancellationToken);
}
