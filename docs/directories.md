# Directories

How the application connects to Active Directory and LDAP servers, how it works with several at
once, what it reads, and what it writes. The last part matters most before pointing it at
production.

## Supported directories

| Directory | Read, search, sign in | Manage | Notes |
| --- | --- | --- | --- |
| **OpenLDAP** | yes | users, groups, units, computers (with an AD-like schema) | The primary target of the write paths. |
| **Active Directory** | yes | edit, delete, enable/disable, move, group membership, units, computers | Creating users and groups writes OpenLDAP-style entries. See [What the application writes](#what-the-application-writes). |
| **Novell eDirectory** | yes | as OpenLDAP | Less tested. |
| Other LDAP v3 servers | likely | likely | Anything with `person` entries, simple bind and RFC 2696 paging. |

## Provider types

| | `StandardLdap` | `ActiveDirectory` |
| --- | --- | --- |
| Library | Novell LDAP (RFC 4511) | `System.DirectoryServices` |
| OS | any | **Windows only**; startup is refused elsewhere |
| Directories | AD, OpenLDAP, eDirectory | AD |
| Connection security | LDAPS, StartTLS, None | LDAPS, None (no StartTLS) |
| Groups | full | `501 Not Implemented` |
| Recommended | **yes** | no |

The rest of this page is about `StandardLdap` unless it says otherwise.

## Connection security

| `Security` | Port | On the wire |
| --- | --- | --- |
| `Ldaps` (default) | 636 | TLS from the first byte. |
| `StartTls` | 389 | Plain connection upgraded with StartTLS; fails if the upgrade fails, never falls back. |
| `None` | 389 | Plain LDAP. **Every user's password at sign-in, and the bind password on every connection, cross the network readable.** Logged as a warning each time. |

Sign-in is a simple bind with the user's password. That is why TLS matters here more than for most
LDAP clients.

**Certificates are verified, always.** The server's certificate must chain to a CA trusted by the
host or named in `Ldap:TrustedCaCertificatePath`, and must name the host the API connects to.
There is no setting that skips either check. To trust an internal CA without installing it
system-wide:

```jsonc
"Ldap": { "TrustedCaCertificatePath": "/etc/nelsar-axia/corp-ca.pem" }
```

The file can hold the CA certificate or, for a self-signed server, the server's own certificate.
One file applies to every `StandardLdap` directory, including those stored in the database.

Revocation (CRL/OCSP) is not checked. See [SECURITY.md](../SECURITY.md#known-limitations).

**Active Directory** needs a certificate on its domain controllers for LDAPS (AD CS, or any CA the
API host trusts). AD accepts password changes (`unicodePwd`) only over an encrypted connection, so
`Security=None` against AD makes password setting fail.

**Global Catalog** ports 3268 and 3269 are accepted for reading. Every write is refused with a
message saying so, because the Global Catalog is read-only.

## Several directories

One instance can serve several directories: two AD forests, AD plus OpenLDAP, a test and a
production directory.

### Where directories come from

| Source | Managed through | Bind password | Shown in the sign-in picker |
| --- | --- | --- | --- |
| `Ldap` section | configuration | config / environment | as "default" (no id) |
| Database rows | `/api/v1/providers`, console → *LDAP providers* | encrypted with `Encryption:Key` | yes, when active |
| `Ldap:Providers:{id}` sections | configuration | config / environment | no |

A database directory has: name, host, port, security, provider type, failover hosts, base DN,
bind account and password, user search filter, page size, and an active flag. An inactive one
cannot be used or selected.

A fresh database has no directory rows: only the default directory exists until one is added.
(The API also writes a row into a legacy `LdapConfigurations` table on first start, pointing at
`localhost:389`. Nothing reads it; it is not a directory and does not appear anywhere.)

### Choosing one per request

The `X-LDAP-Provider-Id` header names the directory, by the row's GUID or the
`Ldap:Providers:{id}` key.

- **No header**: the default directory (`Ldap` section).
- **At sign-in**: the header picks the directory the credentials are checked against. The console
  shows a picker when there is more than one (it reads `GET /api/v1/providers/selectable`,
  anonymous and rate-limited, which returns ids and names only).
- **After sign-in**: the token is bound to the directory it signed in to. A request naming another
  directory is refused, never served from a different one. Omitting the header is fine.

Failures are kept apart:

| Situation | Status |
| --- | --- |
| Unknown or inactive directory id | `400` |
| Directory misconfigured (e.g. port contradicts security) | `502` |
| No host of the directory reachable | `502` |

### Failover

A directory's *failover hosts* (comma-separated) are tried in order after the main host, each
with a 2-second TCP connect probe. The first that answers is used. The answer is cached for 30
seconds; a failure is cached for 5 seconds, so a dead directory is not probed on every request.

Changing a directory's host, port or failover hosts **requires entering its bind password again**.
Otherwise the stored password would be sent to whatever the new address points at. Switching its
security to `None` requires it too: the stored password would otherwise cross the network
unencrypted. Moving between `Ldaps` and `StartTls`, or turning encryption on, does not.

Directories created before LDAPS existed were set to `None` by the `AddProviderSecurity`
migration so that they kept working. The console marks them *not encrypted*.

## What the application reads

### Accounts

- An account is any entry with `objectClass=person` that is **not** `computer` or `contact`.
  Computers and contacts are left out of every user lookup, listing and write.
- Sign-in looks the login up under the base DN, trying in turn: `Ldap:UserSearchFilter` (`{0}`
  replaced by the escaped login), then `mail`, `userPrincipalName`, `sAMAccountName` and `uid`
  equal to the login, and, for a `user@domain` login, `sAMAccountName` or `uid` equal to the part
  before `@`. That last step is for AD accounts that bind by implicit UPN without carrying a
  `userPrincipalName` attribute. The first account entry found is the one the password is then
  bound against. If two accounts share a login or an address, the first one found wins, so keep
  those unique.
- Read attributes: `cn`, `displayName`, `mail`, `uid`, `sAMAccountName`, `userPrincipalName`,
  `memberOf`, `objectClass`, `adminCount`.
- `GET /api/v1/users/{id}/attributes` returns every attribute of the entry, with credential-like
  attributes (passwords, LAPS) listed by name and their values withheld.

### Group membership

Membership, for roles and for display, is read from both sides, so a directory without the
`memberof` overlay works too:

| Group class | Membership attribute | Matched by |
| --- | --- | --- |
| AD `group`, `groupOfNames` | `member` | account DN |
| `groupOfUniqueNames` | `uniqueMember` | account DN |
| `posixGroup` | `memberUid` | account login |
| any | the account's own `memberOf` | group DN |

**Nested groups are not followed.** Map the group an account is directly in.

`memberUid` names a login, not an entry, and it stays in the group after the account is deleted.
Creating an account whose login a `posixGroup` still lists is therefore refused with `409`, since
the new account would join that group — and hold its role — the moment it signed in. Remove the
login from the group first, or use a different address.

### Listings

Directory-wide listings (all users, groups, units, computers) use RFC 2696 paged results, page size
`UserSearchPageSize` (500). They read the whole directory and page the result for the client. When
the directory stops a listing partway, the pages already read are returned with
`isComplete: false` rather than presented as the whole set. Common causes are a server-side size
or time limit, or a lost connection.

### Computers

Entries with `objectClass=computer` (AD) or `objectClass=device` (RFC 4519, as OpenLDAP uses for
printers and appliances). From `device` entries the operating system and enabled state are
reported as empty/`null`, not guessed.

## What the application writes

Everything is written with the bind account. Distinguished names and filter values are escaped.

### Passwords

Set the way the directory hashes them, never as a plain attribute. The method is detected from the
directory's root DSE:

| Directory advertises | Method |
| --- | --- |
| AD capabilities | `unicodePwd` replace (encrypted connection required) |
| Password Modify extended operation (RFC 3062) | Password Modify |
| neither | refused: no password is written |

When a new account is created and its password is then refused (for example, by policy), the
half-made account is deleted again.

### New accounts

`POST /api/v1/users`, bulk create rows, and self-registration all write:

```
dn: uid=<local part of the e-mail>,<Ldap:BaseDn>
objectClass: top, person, organizationalPerson, inetOrgPerson
cn, displayName: <name>
sn: <last word of name>
mail: <e-mail>
uid: <local part>
```

then set the password.

- New accounts always go to the **base DN**, not to a chosen OU. Move them afterwards with your
  directory's tools.
- **Against Active Directory this is not a real AD user**: no `sAMAccountName`, no
  `userAccountControl`, `uid` as the RDN. AD may refuse it, or create an `inetOrgPerson` that is
  not usable as a normal account. Create AD accounts with AD's tools until this is addressed, and
  try it in a test domain first.

### Account changes

| Change | Attribute |
| --- | --- |
| Name | `displayName` |
| E-mail | `mail` only. The RDN and `uid` stay, so the login does not change. |
| Password | as above |
| Delete | the entry |

Accounts are always addressed by DN, never by searching for a login or address again.

### Groups

- Create writes `objectClass: top, groupOfNames` with `cn`, `description` and at least one
  `member` (`groupOfNames` requires one). In Active Directory that is a valid entry but **not a
  security group**: no SID, no `groupType`, unusable for permissions. Create AD groups with AD's
  tools.
- Rename, move, description and membership work on AD `group` as well as `groupOfNames` and
  `groupOfUniqueNames`. Saving membership **replaces it wholesale** with the list sent.

### Organizational units

`objectClass: top, organizationalUnit` with `ou` and `description`. Rename and move are LDAP
ModDN operations. Deleting a unit that still has entries is refused by the directory and reported
as `409`; nothing is deleted recursively.

### Computers

Create writes AD's shape:

```
objectClass: top, person, organizationalPerson, user, computer
cn: <name>
sAMAccountName: <name>$
userAccountControl: 4096           (WORKSTATION_TRUST_ACCOUNT)
description, dNSHostName           (when given)
```

This *pre-stages* an account a machine can claim when it joins the domain. It does not join a
machine. On OpenLDAP the `computer` class must exist in the schema, and OpenLDAP's `person`
requires `sn`, which is not written, so **creating computers fails against OpenLDAP** (`502`).
Everything else works.

Enable and disable flip bit `0x2` of `userAccountControl`, leaving the other flags alone. Rename
updates `cn` and `sAMAccountName`. **Deleting a computer account revokes the joined machine's
trust**; it has to rejoin the domain.

### What is never changed

- The bind account itself (`Ldap:AdminUsername`), by anyone.
- Anything marked privileged (`adminCount=1`, or in `DirectoryProtection:ProtectedGroups`),
  except by a caller who holds every permission.

See [Access control](access-control.md#protected-targets).

## Preparing a directory

### Active Directory

1. LDAPS on the domain controllers, with a certificate whose name matches what the API connects to.
2. A service account for the bind (see [Production → The bind account](production.md#the-bind-account)),
   not a Domain Admin.
3. Security groups for the roles (e.g. `Axia-Admins`, `Axia-Editors`). Put people directly in them;
   nesting is not followed.
4. `RoleMappings:GroupDns:Admin` set to the admins group's DN.
5. `UserSearchFilter`: the default works. Computers and contacts are excluded from sign-in by the
   API itself, whatever the filter says.
6. `UserSearchPageSize` at or below AD's `MaxPageSize` (1000).

### OpenLDAP

1. TLS (`olcTLSCertificateFile` etc.) and `Security: Ldaps` or `StartTls`.
2. A bind account with write access to the subtrees it manages, through ACLs. Avoid binding as the
   database's root DN in production: it bypasses ACLs and limits.
3. The Password Modify extended operation (enabled by default in `slapd`), so passwords are hashed
   by the server.
4. Role groups (`groupOfNames` is simplest).
5. `DirectoryProtection:ProtectedGroups` listing the groups whose members administer the
   directory, and the Admin group.
6. For computer accounts, a schema with AD's `computer` class. [`deploy/dev/ad-schema.ldif`](../deploy/dev/ad-schema.ldif)
   is one. Note the `sn` limitation above.
