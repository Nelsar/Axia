using Microsoft.AspNetCore.Authorization;

namespace Nelsar.Axia.Api.Authorization;

/// <summary>One permission from <see cref="Domain.Authorization.Permissions"/> that an endpoint demands.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
