namespace Nelsar.Axia.Domain.Authorization;

/// <summary>
/// Every operation the API guards, named. One entry here corresponds to exactly one check in
/// the code — which is why the catalogue is code and not a table: a permission an administrator
/// could invent but nothing enforces would be a checkbox that grants nothing, and worse, one
/// they would believe in.
///
/// Which roles hold which permission is the part that <em>is</em> data (see RolePermission), and
/// that is what an administrator edits.
/// </summary>
public static class Permissions
{
    /// <summary>Looking up one account and searching. Open to any authenticated user today.</summary>
    public const string UsersRead = "users.read";

    /// <summary>
    /// Enumerating the whole directory. Separate from <see cref="UsersRead"/> because today the
    /// full listing is restricted while lookup and search are not — one permission could not
    /// express both without changing who can do what.
    /// </summary>
    public const string UsersList = "users.list";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDelete = "users.delete";

    /// <summary>Bulk create/update of directory accounts, synchronous and queued alike.</summary>
    public const string UsersBulk = "users.bulk";

    /// <summary>The mask/regex generator, including its conflict preview against the directory.</summary>
    public const string UsersGenerate = "users.generate";

    public const string GroupsRead = "groups.read";
    public const string GroupsManage = "groups.manage";

    public const string OrganizationalUnitsRead = "orgunits.read";
    public const string OrganizationalUnitsManage = "orgunits.manage";

    public const string ComputersRead = "computers.read";

    /// <summary>Creating, renaming, moving, disabling and deleting computer accounts. Deleting
    /// one revokes a joined machine's trust and obliges it to rejoin the domain.</summary>
    public const string ComputersManage = "computers.manage";

    public const string AuditRead = "audit.read";
    public const string ProvidersManage = "providers.manage";
    public const string RateLimitsManage = "ratelimits.manage";

    /// <summary>
    /// Editing group-to-role mappings and role-to-permission grants. Effectively root: whoever
    /// holds it can grant themselves everything else, so it belongs to the smallest possible
    /// set of people and every use of it is audited.
    /// </summary>
    public const string RolesManage = "roles.manage";

    /// <summary>
    /// The whole catalogue, used to register one authorisation policy per permission and to
    /// let the management API offer a closed list rather than free text.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        UsersRead,
        UsersList,
        UsersCreate,
        UsersUpdate,
        UsersDelete,
        UsersBulk,
        UsersGenerate,
        GroupsRead,
        GroupsManage,
        OrganizationalUnitsRead,
        OrganizationalUnitsManage,
        ComputersRead,
        ComputersManage,
        AuditRead,
        ProvidersManage,
        RateLimitsManage,
        RolesManage,
    ];

    public static bool IsKnown(string permission) => All.Contains(permission);
}
