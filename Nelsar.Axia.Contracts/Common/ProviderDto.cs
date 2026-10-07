using System;

namespace Nelsar.Axia.Contracts.Common;

// AdminPassword is intentionally omitted: this DTO is returned from GET/POST/PUT responses
// and must never expose the bind credential, encrypted or not.
public record ProviderDto
(
    Guid Id,
    string Name,
    string Host,
    int Port,
    string ProviderType,
    bool IsActive,
    string? FailoverHosts = null,
    string BaseDn = "",
    string AdminUsername = "",
    string UserSearchFilter = "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))",
    int UserSearchPageSize = 500,
    string Security = "Ldaps"
);
