# Troubleshooting

Find the symptom, then the cause. For any failed request, the response's `X-Correlation-ID` finds
every log line it produced: see [Operations → Following one request](operations.md#following-one-request).

## The API does not start

The last lines of the log name the setting. The common ones:

### No role mapping grants the Admin role

```
[ERR] Role mapping … names its group as 'Administrators' and grants Admin to nobody: mappings by name are ignored. …
[FTL] … No role mapping grants the Admin role by distinguished name, so nobody could administer this application. …
```

**Cause.** No mapping names the Admin group by DN. Either `RoleMappings:GroupDns:Admin` is not
set, or the database holds mappings from an older build that name groups by CN (`Administrators`),
which are ignored.

**Fix.** Set the Admin group's real DN and restart:

```bash
RoleMappings__GroupDns__Admin="CN=Axia-Admins,OU=Groups,DC=corp,DC=example,DC=com"
```

At the next start, global mappings by name for the roles you list are converted to these DNs, once.

**Get the DN right.** The API checks the format, not that the group exists. A wrong DN is saved
into the database, the API starts, and nobody can administer it. To find a group's DN:

```bash
# Active Directory (PowerShell)
Get-ADGroup Axia-Admins | Select-Object DistinguishedName
# any LDAP
ldapsearch -H ldaps://dc01.corp.example.com -D 'svc-axia@corp.example.com' -W \
  -b 'DC=corp,DC=example,DC=com' '(cn=Axia-Admins)' dn
```

**Saved a wrong DN?** Fix it with someone who holds `roles.manage`, or in the database:

```bash
sqlite3 nelsar-axia.db "SELECT Id, GroupName, RoleName FROM RoleMappings;"
sqlite3 nelsar-axia.db "UPDATE RoleMappings SET GroupName='cn=axia-admins,ou=groups,dc=example,dc=org' WHERE RoleName='Admin';"
```

On a development database you can also delete the file and let the API recreate it.

**Note on placeholders.** A placeholder such as `"<DN of the admins group>"` is safe: it is not a
DN, so startup is refused before anything is written. A made-up but well-formed DN is not safe.

### `RoleMappings:GroupDns:X is '…', which is not a distinguished name`

The value must be a full DN (`CN=…,OU=…,DC=…`), not a group name.

### `Jwt:Key …` / `Encryption:Key …`

Missing, a placeholder (`CHANGE_ME…`), too short, or (for `Encryption:Key`) not base64 of 16, 24
or 32 bytes. Generate:

```bash
openssl rand -base64 48   # Jwt:Key
openssl rand -base64 32   # Encryption:Key
```

### `Ldap:Security is Ldaps … and Ldap:Port is 389`

A configuration from before LDAPS was the default. Choose: port 636 (LDAPS), `Security: StartTls`
on 389, or `Security: None` (plain LDAP, passwords readable on the network).

### `ActiveDirectory` provider on Linux/macOS

`ProviderType=ActiveDirectory` needs Windows. Use `StandardLdap`, which works against AD.

### `appsettings.Development.json` seems ignored

It is read only when `ASPNETCORE_ENVIRONMENT=Development`. The `http` launch profile sets that; the
`https` profile runs as **Production**. `dotnet run` without a profile uses the first one (`http`).

### Port already in use

Another API instance is running (`ss -ltnp | grep 5063`), or the console's 8080/5500 is taken.

## Signing in fails

| Response | Likely cause |
| --- | --- |
| `401` | Wrong password, or no account matches the login. Both look the same on purpose. Check the login with `ldapsearch` using the search filter. |
| `401` or `502` for every account | Check the bind account (`Ldap:AdminUsername`/`AdminPassword`): the login is looked up with it before the user's own bind. The log has the directory's error. |
| `429` "Too many requests" | `auth-login` limit: 5 per minute per address. Or `auth-login-account`: 5 failed sign-ins to this account in 15 minutes, from anywhere. Wait for `Retry-After`, or raise the limit under *Rate limiting*. Behind a proxy not listed in `ReverseProxy`, everyone shares one address bucket. |
| `502` "…no server configured" | `Ldap:Server` is empty and the request named no directory. |
| `502` unreachable | Network, firewall, wrong host/port, or the TLS handshake failed. |
| `400` about the directory | `X-LDAP-Provider-Id` names an unknown or inactive directory. |
| Signs in, but only as `User` | No role mapping matches the account's groups. Check that the mapping's DN is the group the account is **directly** in (nesting is not followed), and that the group's membership is visible: `GET /api/v1/users/{id}` shows `groups`. |

### TLS errors

- **Certificate not trusted**: the directory's CA is not in the host's store. Set
  `Ldap:TrustedCaCertificatePath` to the CA's PEM.
- **Name mismatch**: `Ldap:Server` must be a name in the certificate. Connecting by IP to a
  certificate issued for `dc01.corp.example.com` fails; use the name.
- **StartTLS fails**: the server does not offer it on 389, or its certificate fails the same checks.
- Verification cannot be disabled. For a lab without TLS, use `Security: None` on a directory on the
  same host only.

## Listings look wrong

### Exactly 1,000 entries, `isComplete: false`

The log shows `Error enumerating LDAP … after 1000 entries. Size Limit Exceeded`. Two possible
causes:

- **The application's own limit.** The LDAP library sends a default size limit of 1000 with every
  search, and the server applies it to a paged listing as a whole. Builds that include
  `PagedSearchConstraints` in `StandardLdapProvider` lift it; earlier ones stop every listing at
  1000. Update.
- **The server's limit for the bind account.** Raise it (OpenLDAP `olcLimits … size.prtotal`, AD
  `MaxPageSize` / `MaxResultSetSize`) or lower `UserSearchPageSize`.

### Fewer entries than expected, `isComplete: false`

The directory stopped the search: a time limit, a size limit, or a dropped connection. The log has
the LDAP error.

### A group is missing

`posixGroup` entries are not listed (they are still used for roles). Only `group`, `groupOfNames`
and `groupOfUniqueNames` under the base DN are.

### Dashboard tiles say "API unavailable" for groups or units

A known issue: the dashboard asks for 1,000 entries per page to count them, and the API's maximum
is 200, so it answers `400` (`Page size must be between 1 and 200` in the log). The Groups and
Organizational units pages themselves work.

## Changes are refused

| Response | Cause |
| --- | --- |
| `403` | You lack the permission, or the target is protected: it holds more than you, is in one of Active Directory's protected groups or carries `adminCount`, is in `DirectoryProtection:ProtectedGroups`, or is the bind account. The log line for the refusal says which. |
| `409` | Already exists; or deleting a unit that still has entries; or an e-mail in use by another account. |
| `502` "The directory rejected …" | The directory refused the write. Common: the bind account lacks the right (delegation/ACL); a schema violation (e.g. creating a computer on OpenLDAP, where `person` requires `sn`); a password the directory's policy rejects; a Global Catalog port, which is read-only. |
| `502` on password change against AD | AD accepts `unicodePwd` only over an encrypted connection: use `Ldaps`. |
| `404` after a rename | Ids are derived from the DN; list again to get the new id. |
| `501` | Groups on the Windows `ActiveDirectory` provider; use `StandardLdap`. |

## Bulk jobs

- **Status `404`**: the API restarted; queued jobs live in memory and are lost.
- **Rows fail with "not found for update"**: the `email` of an update row must find the account.
- **Every row fails with a permission message**: the batch has create rows and you lack
  `users.create`, or update rows and you lack `users.update`.

## Local test directory (`deploy/dev`)

- **`permission denied … docker.sock`**: your user is not in the `docker` group. Use `sudo`, or add
  the user to the group (`sudo usermod -aG docker $USER`, then log in again).
- **Data changes do not appear**: LDIF files load only on the first start of an empty directory:
  `down -v`, then `up`.
- **A disabled account binds with `ldapsearch` but cannot sign in**: expected. OpenLDAP does not act
  on `userAccountControl`; the API reads it and refuses the account.
- More in [Local test directory](local-directory.md#where-it-differs-from-real-active-directory).

## Tests

`SigningIn_ToADirectoryWithNoServer_IsAGatewayFailure_NotAnUnexpectedError` fails when your local
`Nelsar.Axia.Api/appsettings.json` sets `Ldap:Server`: the test expects no default server, and the
test host reads that file. CI has no such file. Run the tests with the file moved aside, or ignore
that one locally.
