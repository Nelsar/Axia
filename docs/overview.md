# Overview

This page describes what Nelsar.Axia is made of and defines the terms the rest of the
documentation uses.

## The pieces

```
 browser ──► console (React, static files) ──► API (ASP.NET Core, :5063 / :8080) ──► directory (LDAP/LDAPS)
                                                  │
                                                  └──► SQLite database (settings, audit log)
```

| Piece | What it is | Where it lives |
| --- | --- | --- |
| **API** | `Nelsar.Axia.Api`, an ASP.NET Core application on .NET 10. Every operation goes through it, and it does all the checks. | `Nelsar.Axia.*` projects |
| **Console** | A single-page React + TypeScript application. It holds no state beyond the session and enforces nothing; it is a convenience over the API. | `frontend/` |
| **Directory** | Your Active Directory, OpenLDAP or other LDAP server. Accounts, groups, units and computers live there and only there. | Yours |
| **Database** | SQLite. Holds what is not in the directory: extra directory connections, role mappings, role permissions, rate-limit settings and the audit log. Created and migrated on first start. | A file next to the API, or a volume |

The application never copies directory objects into its database. Every list and every lookup is
a live LDAP query; every change is written straight to the directory.

## Concepts

### Directory (provider)

A *directory* is one LDAP server, or one set of failover servers, with a base DN and a bind
account. The code and the API call it a *provider*. There are three places one can come from:

1. **The default directory**: the `Ldap` section of configuration. There always is one; a request
   that names no directory uses it.
2. **Directories stored in the database**: created through `POST /api/v1/providers` or the
   console's *LDAP providers* screen. Their bind passwords are encrypted with `Encryption:Key`.
3. **Directories declared in configuration** under `Ldap:Providers:{id}`, mostly for scripted
   deployments.

A request picks a directory with the `X-LDAP-Provider-Id` header. See
[Directories](directories.md).

### Provider type

How the API talks to a directory:

- **`StandardLdap`** (the default, and the recommended one): plain LDAP v3 (RFC 4511) through the
  Novell LDAP library. Works against Active Directory, OpenLDAP and eDirectory, on any OS, over
  LDAPS or StartTLS.
- **`ActiveDirectory`**: `System.DirectoryServices`. It runs on Windows only, the API refuses to
  start with it elsewhere, and it manages fewer object types: groups return `501 Not Implemented`.
  Prefer `StandardLdap` even against AD.

### Session

Signing in is an LDAP *bind* with the user's own login and password against the chosen directory.
If the bind succeeds, the API issues a JWT bearer token. The token records:

- the directory entry (its DN) that proved the password;
- the directory it signed in to.

Every later request is made *as that entry, in that directory*. A request that names a different
directory in `X-LDAP-Provider-Id` is refused rather than served from another one.

Tokens last `Jwt:ExpiresInMinutes` (60 by default) and cannot be revoked. There is no sign-out on
the server side and no refresh token. See [SECURITY.md](../SECURITY.md#known-limitations).

### Roles and permissions

- A **permission** is one guarded operation, for example `users.create` or `groups.manage`. The
  catalogue is fixed in code; there are 17. See [Access control](access-control.md#permissions).
- A **role** is a named set of permissions: `Admin`, `UserCreator`, `UserEditor`, `UserDeleter`,
  `User`. The set of roles is fixed; what each one holds is editable, except `Admin`, which always
  holds everything.
- A **role mapping** grants a role to the members of one directory group, named by its DN.

Permissions are worked out on every request (cached for 30 seconds) from the signed-in entry's
current group membership. They are never read from the token, so removing someone from a group
takes effect within half a minute without signing them out.

An account in no mapped group signs in with the `User` role, which can only read.

### Entry ids

Users, groups, units and computers are addressed in the API by an `id` (a GUID). The id is not
stored anywhere. It is derived from the entry itself (for groups, units and computers, from the
DN), so **it changes when an entry is renamed or moved**. A client that holds an id across a
rename gets `404` and should list again.

### Audit log

Every sign-in, and every attempt to change the directory or the application's settings, is
written to the audit log: who did it, to what, whether it was allowed, and when. That includes
refused attempts. See [Features → Audit log](features.md#audit-log).

## Code layout

```
Nelsar.Axia.Domain          entities, value objects (DistinguishedName, Email), pure services
Nelsar.Axia.Application     use cases as MediatR commands/queries; validation, permission checks,
                            auditing and logging as pipeline behaviours
Nelsar.Axia.Infrastructure  LDAP providers, EF Core + SQLite, encryption, tokens, background jobs
Nelsar.Axia.Api             controllers, middleware, composition root, observability
Nelsar.Axia.Contracts       DTOs and module interfaces shared across layers
Nelsar.Axia.Tests           xUnit tests (unit, integration, architecture)
frontend/                   the console (React, TypeScript, Vite)
deploy/quickstart           what the Docker quick start mounts: demo LDIF, key/cert generator
deploy/dev                  the local test directory for development
```

Permission checks happen in the Application layer, before a use case runs, not in the controllers.
A handler cannot be reached without passing the check, and a test asserts that for every guarded
use case. See [Development](development.md).
