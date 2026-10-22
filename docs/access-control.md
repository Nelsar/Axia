# Access control

Who may do what, and how that is decided.

In short: **signing in proves who you are** (an LDAP bind). **Your directory groups decide your
roles** (role mappings). **Your roles decide your permissions** (role grants). **Every operation
checks one permission**, and operations on other accounts and groups add a check that the target
is not more powerful than you.

## Permissions

The catalogue is fixed in code. Each permission guards exactly one kind of operation.

| Permission | Allows |
| --- | --- |
| `users.read` | Look up one account, search accounts, read an account's raw attributes. |
| `users.list` | Enumerate every account in the directory (`GET /api/v1/users`). |
| `users.create` | Create an account. |
| `users.update` | Change an account's name, e-mail or password. |
| `users.delete` | Delete an account. |
| `users.bulk` | Submit bulk batches, synchronous or queued, and read their status. Each row also needs `users.create` or `users.update`. |
| `users.generate` | Run the login generator and its conflict check. |
| `groups.read` | List and read groups. |
| `groups.manage` | Create, rename, move, edit membership of, and delete groups. |
| `orgunits.read` | List and read organizational units. |
| `orgunits.manage` | Create, rename, move and delete units. |
| `computers.read` | List and read computer accounts. |
| `computers.manage` | Create, rename, move, enable/disable and delete computer accounts. |
| `audit.read` | Read the audit log. |
| `providers.manage` | Manage the directories stored in the database. |
| `ratelimits.manage` | Read and change rate-limit settings. |
| `roles.manage` | Edit role mappings and what each role may do. **Effectively root**: whoever holds it can grant themselves everything else. |

`GET /api/v1/me/permissions` answers with the caller's current set.

## Roles

Five, fixed: `Admin`, `UserCreator`, `UserEditor`, `UserDeleter`, `User`.

What each holds on a fresh database:

| Permission | User | UserCreator | UserEditor | UserDeleter | Admin |
| --- | :-: | :-: | :-: | :-: | :-: |
| `users.read` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `groups.read` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `orgunits.read` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `computers.read` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `users.list` | | ✓ | | | ✓ |
| `users.create` | | ✓ | | | ✓ |
| `users.generate` | | ✓ | | | ✓ |
| `users.bulk` | | ✓ | ✓ | | ✓ |
| `users.update` | | | ✓ | | ✓ |
| `users.delete` | | | | ✓ | ✓ |
| `groups.manage`, `orgunits.manage`, `computers.manage` | | | | | ✓ |
| `audit.read`, `providers.manage`, `ratelimits.manage`, `roles.manage` | | | | | ✓ |

Notes:

- **`User`** is what everyone gets. An account in no mapped group signs in as `User`, so it can
  read and nothing else.
- **Changing the directory's structure** (groups, units, computers) is Admin-only by default,
  because group management is a path to any role: add yourself to the group mapped to Admin.
- **`users.bulk` never adds rights.** A batch's create rows need `users.create` and its update rows
  `users.update`, checked when it is submitted and again when a queued job runs.

### Editing what a role holds

`PUT /api/v1/roles/{role}/permissions`, or the console's role screen, needs `roles.manage`.
Three rules apply:

- `Admin` cannot be edited. It always holds everything.
- `roles.manage` cannot be granted to any other role.
- Nobody can grant a permission they do not hold themselves.

Changes reach signed-in sessions within 30 seconds (permission cache).

An upgrade note: an early build granted every signed-in account the management permissions. The
`RevokeAccidentalDefaultGrants` migration removes exactly those grants on upgrade.

## Role mappings

A role mapping says: *members of this directory group get this role*.

| Field | Meaning |
| --- | --- |
| `groupName` | The group's **full distinguished name**. Stored normalised. |
| `roleName` | One of the five roles. |
| `providerId` | A directory id to apply only there, or empty for every directory. |
| `priority` | Ordering for the role *shown* for an account. Lower comes first. |

How they are applied, per request:

1. Read the signed-in account's groups (see [Directories → Group membership](directories.md#group-membership)).
2. Collect every mapping whose group is one of them, from this directory's mappings and the global
   ones.
3. **Permissions are the union** of everything those roles grant. When no mapping matches, the
   account has the `User` role and its permissions. `User` is not added on top of mapped roles,
   so a role you edit down to nothing really grants nothing; the default grants include the read
   permissions in every role.
4. The single role shown in the console and in `UserDto.role` is the first match: a mapping scoped
   to this directory wins over a global one for the same group, then lower `priority` wins. This
   is display only; it does not limit the permissions in step 3.

Mappings come from two places:

- **Configuration**, `RoleMappings:GroupDns`: seeded as global mappings on a fresh database. See
  [Configuration](configuration.md#rolemappings).
- **The API and console**: `/api/v1/role-mappings`, needing `roles.manage`. New and edited
  mappings must name the group by DN.

### Mappings by name (legacy)

Builds before DN mappings stored the group's CN only (`Administrators`). Such a mapping matches
**every** group with that CN anywhere in the directory, so anyone allowed to create a group could
create their own `Administrators`. They are therefore:

- **ignored** by default, with an error logged for each on every start, and marked in the console;
- converted to DNs automatically, once, for global mappings of roles listed in
  `RoleMappings:GroupDns`;
- counted again only if `RoleMappings:AllowMappingsByName=true`, which reopens the hole.

The API refuses to start unless some mapping grants Admin by DN.

## Protected targets

A permission says what kind of change you may make, not to whom. The directory work runs as the
bind account, which can change anybody. So operations on another account or group add a second
check: **the target must not be more powerful than the caller.**

| Target | Who may change it |
| --- | --- |
| An account whose permissions here are not a subset of yours | nobody but someone holding at least those |
| A group mapped to a role that grants something you lack | the same |
| An account or group Active Directory protects: one of its protected groups (Domain Admins, Enterprise Admins, Schema Admins, Administrators, Account/Server/Backup/Print Operators, …), any member of one however nested, the built-in Administrator and `krbtgt`, and anything carrying `adminCount=1` | only a caller holding **every** permission |
| A group listed in `DirectoryProtection:ProtectedGroups`, and its members | only a caller holding **every** permission |
| The bind account (`Ldap:AdminUsername`) | **nobody** |
| A computer that is a domain controller (writable or read-only), a critical system object, a member of a protected group, or has `adminCount=1` | only a caller holding **every** permission |
| An organizational unit, to rename or move, judged by what is beneath it: a group mapped to a role, anything above, the bind account | as for what is beneath it |

Membership is asked of the directory when it matters — by the groups' SIDs and AD's
membership-chain rule — rather than read from `adminCount` alone. `adminCount` is set by SDProp,
which in Windows runs once an hour, so a new domain administrator went unprotected until it next
ran; Samba has no SDProp and never sets it on an administrator made after the domain was created.

This applies to updating, resetting the password of, and deleting accounts (singly and in bulk),
to changing, re-membering and deleting groups, to changing, enabling, disabling and deleting
computers, and to renaming and moving organizational units. A refused bulk row fails that row
only.

A unit is judged by its contents because renaming or moving it re-addresses everything beneath
it: a group mapped to a role by DN stops matching its mapping, and a group in `ProtectedGroups`
stops being protected. Changing only a unit's description, or deleting it (the directory refuses
to delete one that is not empty), carries nothing along and is not checked.

Examples:

- An editor can reset a colleague's password, not an administrator's.
- Someone with `groups.manage` cannot add themselves to the group mapped to Admin unless they
  already hold everything Admin holds.
- A domain administrator whose groups map to no role is a plain `User` here, but their account is
  still protected, because it belongs to Domain Admins.
- Someone with `orgunits.manage` cannot move the unit that holds the group mapped to Admin — which
  would have let them, with `users.update`, reset an administrator's password while the mapping
  matched nobody, and move it back.
- Someone with `computers.manage` can disable a workstation, not a domain controller.

The refusal is a `403` that does not say which permission the target holds; the log has the
detail.

## Sessions and tokens

- A token is issued after a successful bind and lasts `Jwt:ExpiresInMinutes` (60).
- It carries the entry's DN and the directory. Permissions are **not** in it; they are re-read for
  each request (30-second cache).
- Removing someone from a mapped group takes effect within 30 seconds.
- Deleting the account, or switching it off in the directory, ends its session within 30 seconds:
  the next request is answered `401`. Switched off means disabled or expired in Active Directory
  (`userAccountControl`, `accountExpires`), locked by an administrator through OpenLDAP's password
  policy (`pwdAccountLockedTime` of `000001010000Z`), or `loginDisabled` in eDirectory.
- A lockout after failed sign-ins does **not** end a session, on purpose: anyone can cause one by
  mistyping a password. It stops a new sign-in at the bind.
- There is no server-side sign-out or revocation, so a stolen token stays usable until it expires.
  Shorten the lifetime where this matters.
- Tokens issued by builds before DN-bound sessions lack the DN and are refused; signing in again
  issues a new one.

## The console is not a control

The console hides menus and buttons by role, for convenience. Every check described here is made
by the API. A request crafted by hand gets exactly the same answer as one from the console.
