using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Contracts.Modules;

/// <summary>
/// Group management, backed by the directory itself.
///
/// A group is addressed by <c>id</c>, a hash of its distinguished name — stable without anything
/// being stored, and it changes when the group is renamed or moved, because the DN is the group's
/// identity as far as the directory is concerned. A caller holding an id across a rename gets a
/// null back and should re-read the listing.
///
/// Membership is a list of member distinguished names, as the directory stores it. On update it
/// is replaced wholesale when given and left untouched when omitted.
/// </summary>
public interface IGroupsModule : IOpencoreModule
{
    Task<PagedResponse<GroupDto>> SearchAsync(
        string? filter,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken = default);

    Task<GroupDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GroupDto> CreateAsync(CreateGroupRequest request, CancellationToken cancellationToken = default);

    Task<GroupDto?> UpdateAsync(Guid id, UpdateGroupRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
