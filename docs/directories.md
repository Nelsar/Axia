# Directories

How the application connects to Active Directory and LDAP servers, how it works with several at
once, what it reads, and what it writes. The last part matters most before pointing it at
production.

## Supported directories

| Directory | Read, search, sign in | Manage | Notes |
| --- | --- | --- | --- |
| **OpenLDAP** | yes | users, groups, units, computers (with an AD-like schema) | |
| **Active Directory** | yes | users, groups, units, computers | Accounts and groups are created as AD's own `user` and security `group`. |
| **Samba AD** | yes | as Active Directory | Tested in CI against Samba 4.22. See [Samba AD](#samba-ad). |
| **Novell eDirectory** | yes | as OpenLDAP | Less tested. |
| Other LDAP v3 servers | likely | likely | Anything with `person` entries, simple bind and RFC 2696 paging. |

## Provider types

| | `StandardLdap` | `ActiveDirectory` |
| --- | --- | --- |
| Library | Novell LDAP (RFC 4511) | `System.DirectoryServices` |
| OS | any | **Windows only**; startup is refused elsewhere |
| Directories | AD, Samba AD, OpenLDAP, eDirectory | AD |
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

Revocation (CRL/OCSP) is not checked. See
[SECURITY.md](https://github.com/Nelsar/Axia/blob/main/SECURITY.md#known-limitations).

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
  `memberOf`, `objectClass`, `adminCount`, `objectSid`.
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

`POST /api/v1/users`, bulk create rows, and self-registration all write an account, then set its
password. Which kind of account is decided by the directory's root DSE: Active Directory, Windows'
or Samba's, says so in `supportedCapabilities`.

In Active Directory:

```
dn: CN=<name>,<Ldap:BaseDn>
objectClass: top, person, organizationalPerson, user
cn, displayName: <name>
sn: <last word of name>
mail: <e-mail>
sAMAccountName: <local part of the e-mail>
```

AD creates it switched off; once the password is set, `userAccountControl` becomes `512`
(`NORMAL_ACCOUNT`: switched on, password required). The login has to be a valid
`sAMAccountName` — at most 20 characters, none of `" / \ [ ] : ; | = , + * ? < >` — and is refused
with `400` before anything is written otherwise. A name already taken in the base DN is `409`.

Elsewhere:

```
dn: uid=<local part of the e-mail>,<Ldap:BaseDn>
objectClass: top, person, organizationalPerson, inetOrgPerson
cn, displayName: <name>
sn: <last word of name>
mail: <e-mail>
uid: <local part>
```

- New accounts always go to the **base DN**, not to a chosen OU. Move them afterwards with your
  directory's tools.
- If the password is refused, or the account cannot be switched on, the half-made entry is
  deleted again.

### Account changes

| Change | Attribute |
| --- | --- |
| Name | `displayName` |
| E-mail | `mail` only. The RDN and `uid` stay, so the login does not change. |
| Password | as above |
| Delete | the entry |

Accounts are always addressed by DN, never by searching for a login or address again.

### Groups

- Create, in Active Directory, writes `objectClass: top, group` with `cn`, `sAMAccountName` (the
  same name), `groupType: -2147483646` — a global security group, what AD's own tools make — and
  the members. Elsewhere `objectClass: top, groupOfNames` with `cn` and the members. Either way at
  least one member is required, and a `description` if one is given.
- Renaming an AD group changes its `cn`, not its `sAMAccountName` (its pre-Windows 2000 name).
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
- Anything privileged — in Active Directory one of its protected groups, found by SID, or a member
  of one however nested, or anything with `adminCount=1`; or a group in
  `DirectoryProtection:ProtectedGroups` and its members — except by a caller who holds every
  permission.

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

### Samba AD

A Samba domain controller is Active Directory to the API, through `ProviderType: StandardLdap`:
the steps above apply, and so does everything this page says about AD. What differs:

1. **LDAPS, or StartTLS.** Samba refuses a simple bind over plain LDAP by default
   (`ldap server require strong auth = yes`), so `Security: None` fails at the bind; leave it so.
2. **The certificate.** Samba makes a self-signed one on its first start, in
   `/var/lib/samba/private/tls/`, for the server's own name only. Either connect by that name and
   trust its `ca.pem` with `Ldap:TrustedCaCertificatePath`, or — better — give Samba a certificate
   from your CA with every name the API may use (`tls certfile`, `tls keyfile`, `tls cafile` in
   `smb.conf`).
3. **Delegation** is set with `samba-tool dsacl set`, or with Windows' ADUC connected to the Samba
   DC, as for Windows AD. The bind account still should not be a Domain Admin.
4. **No SDProp.** Samba never sets `adminCount` on an administrator made after the domain was
   created ([Samba bug 13954](https://bugzilla.samba.org/show_bug.cgi?id=13954)). The API does
   not rely on it: protected groups are found by SID and their members by AD's membership-chain
   rule, both of which Samba answers.
5. **A reset password** does not end the old one at once: Samba accepts the previous password for
   `old password allowed period` minutes, 60 by default.
6. `ProviderType: ActiveDirectory` (Windows' `System.DirectoryServices`) is not tested against
   Samba; use `StandardLdap`.

[`deploy/dev/samba`](https://github.com/Nelsar/Axia/tree/main/deploy/dev/samba) builds a Samba
domain controller with demo accounts; CI runs `SambaAdTests` against it on every pull request. See
[Local test directory](local-directory.md#samba-ad).

### OpenLDAP

1. TLS (`olcTLSCertificateFile` etc.) and `Security: Ldaps` or `StartTls`.
2. A bind account with write access to the subtrees it manages, through ACLs. Avoid binding as the
   database's root DN in production: it bypasses ACLs and limits.
3. The Password Modify extended operation (enabled by default in `slapd`), so passwords are hashed
   by the server.
4. Role groups (`groupOfNames` is simplest).
5. `DirectoryProtection:ProtectedGroups` listing the groups whose members administer the
   directory, and the Admin group.
6. For computer accounts, a schema with AD's `computer` class.
   [`deploy/dev/ad-schema.ldif`](https://github.com/Nelsar/Axia/blob/main/deploy/dev/ad-schema.ldif)
   is one. Note the `sn` limitation above.
