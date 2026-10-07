using Microsoft.Extensions.Options;
using Nelsar.Axia.Application.Configuration;
using Nelsar.Axia.Domain.ValueObjects;

namespace Nelsar.Axia.Infrastructure.Persistence;

/// <summary>
/// Checks RoleMappings:GroupDns when the host starts, so a typo in a role name or a group given by
/// name stops the application with the setting named — rather than seeding a mapping that matches
/// nothing, and leaving nobody able to sign in as an administrator to find out why.
/// </summary>
internal sealed class RoleMappingOptionsValidation : IValidateOptions<RoleMappingOptions>
{
    public ValidateOptionsResult Validate(string? name, RoleMappingOptions options)
    {
        var problems = new List<string>();

        foreach (var (role, groupDn) in options.GroupDns)
        {
            if (UserRoles.Canonical(role) is null)
            {
                problems.Add($"{RoleMappingOptions.SectionName}:GroupDns names the role '{role}', which does not exist. "
                    + $"Roles are {string.Join(", ", UserRoles.All)}.");
            }

            if (!DistinguishedName.IsWellFormed(groupDn))
            {
                problems.Add($"{RoleMappingOptions.SectionName}:GroupDns:{role} is '{groupDn}', which is not a distinguished "
                    + "name. Give the group's full DN, as in CN=Axia-Admins,OU=Groups,DC=example,DC=com.");
            }
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
