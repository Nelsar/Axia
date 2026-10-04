using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Contracts.Modules;

/// <summary>Which directory group grants which role, per provider and in priority order.</summary>
public interface IRoleMappingsModule : IOpencoreModule
{
    Task<IEnumerable<RoleMappingDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<RoleMappingDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RoleMappingDto> CreateAsync(RoleMappingCreateRequest request, CancellationToken cancellationToken = default);

    Task<RoleMappingDto?> UpdateAsync(
        Guid id,
        RoleMappingUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
