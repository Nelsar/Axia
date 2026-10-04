using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Domain.Repositories;

public interface IRoleMappingRepository
{
    Task<IEnumerable<RoleMapping>> GetAllAsync();
    Task<RoleMapping?> GetByIdAsync(Guid id);
    Task<RoleMapping> AddAsync(RoleMapping mapping);
    Task<RoleMapping?> UpdateAsync(RoleMapping mapping);
    Task<bool> DeleteAsync(Guid id);

    /// <summary>
    /// Mappings relevant to a given group set: global (ProviderId == null) plus
    /// any scoped to the given providerId, ordered by Priority ascending.
    /// </summary>
    Task<IEnumerable<RoleMapping>> GetActiveForProviderAsync(Guid? providerId);
}
