using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Contracts.Requests;

namespace Nelsar.Axia.Contracts.Modules;

/// <summary>Directory providers — the domains this system authenticates and reads against.</summary>
public interface IProvidersModule : IOpencoreModule
{
    Task<IEnumerable<ProviderDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The directories a caller may sign in to, as id and name only — what an anonymous sign-in
    /// page is allowed to know. Active providers only: offering a deactivated one would be
    /// offering a choice that is refused the moment it is used.
    /// </summary>
    Task<IEnumerable<SelectableProviderDto>> GetSelectableAsync(CancellationToken cancellationToken = default);

    Task<ProviderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProviderDto> CreateAsync(ProviderCreateRequest request, CancellationToken cancellationToken = default);

    Task<ProviderDto?> UpdateAsync(
        Guid id,
        ProviderUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
