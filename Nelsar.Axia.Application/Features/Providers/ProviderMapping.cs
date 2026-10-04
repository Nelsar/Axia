using Nelsar.Axia.Contracts.Common;
using Nelsar.Axia.Domain.Entities;

namespace Nelsar.Axia.Application.Features.Providers;

/// <summary>
/// The one place a <see cref="Provider"/> becomes a <see cref="ProviderDto"/>.
///
/// Shared by the five handlers rather than copied into each: the omission that matters here is
/// silent. EncryptedAdminPassword has no place on the DTO, and a second projection written by
/// hand is exactly how a bind credential ends up in a response body.
/// </summary>
internal static class ProviderMapping
{
    public static ProviderDto ToDto(Provider p) => new(
        p.Id, p.Name, p.Host, p.Port.Value, p.ProviderType, p.IsActive,
        p.FailoverHosts, p.BaseDn, p.AdminUsername, p.UserSearchFilter.Template, p.UserSearchPageSize);

    /// <summary>
    /// The same provider as an anonymous caller is allowed to see it: a label and the id to put
    /// in the header, and nothing about where the directory lives.
    ///
    /// It sits next to the projection above on purpose. The two differ by everything that must
    /// not leave the building, and keeping them apart in separate files is how one quietly grows
    /// a field the other already had.
    /// </summary>
    public static SelectableProviderDto ToSelectableDto(Provider p) => new(p.Id, p.Name);
}
