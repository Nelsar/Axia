using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;
using Nelsar.Axia.Contracts.Responses;

namespace Nelsar.Axia.Contracts.Modules;

/// <summary>
/// Organizational unit management, backed by the directory itself.
///
/// A unit is addressed by <c>id</c>, a hash of its distinguished name — same contract as
/// <see cref="IGroupsModule"/> and <see cref="IComputersModule"/>: stable without anything being
/// stored, and invalidated by a rename or a move.
/// </summary>
public interface IOrganizationalUnitsModule : IOpencoreModule
{
    Task<PagedResponse<OrganizationalUnitDto>> SearchAsync(
        string? filter,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken = default);

    Task<OrganizationalUnitDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OrganizationalUnitDto> CreateAsync(
        CreateOrganizationalUnitRequest request,
        CancellationToken cancellationToken = default);

    Task<OrganizationalUnitDto?> UpdateAsync(
        Guid id,
        UpdateOrganizationalUnitRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
