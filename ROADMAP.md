# Road map

Where the project is going, and what is already here. Status is stated per item because a road map
whose completed half is indistinguishable from its planned half is not much use to anyone deciding
whether to depend on it.

- **done** — implemented and covered by tests.
- **partial** — usable, with the gap named.
- **planned** — not started.

For what is deliberately *not* here yet, see [Not in scope yet](README.md#not-in-scope-yet); for
the security gaps specifically, [SECURITY.md](SECURITY.md#known-limitations).

## 1. Architecture, API contracts and multiple directories

**Goal:** one instance serving several directories, isolated from each other or standing in for
each other.

| | |
| --- | --- |
| OpenAPI generated from the code, served at `/swagger` | **done** |
| `X-LDAP-Provider-Id` on the CRUD and search endpoints, documented through an OpenAPI filter | **done** |
| Connection profiles as an API — `/api/v1/providers` | **done** |
| Every endpoint under one versioned prefix, `/api/v1/…` | **done** |
| `ILdapProviderFactory` resolving a provider per request, from the database with a fall back to configuration | **done** |
| Failover across a pool of hosts, with the unreachable ones cached briefly rather than re-probed per request | **done** |
| Attribute differences abstracted — `sAMAccountName` (Active Directory) against `uid` (OpenLDAP) — through `LdapConfiguration`/`LdapAttributeMapping` | **done** |
| A DN builder that escapes per RFC 4514 | **done** |

## 2. Search, paging and filters

**Goal:** a console that stays responsive against a directory of 100 000 entries without the API
holding them all in memory.

| | |
| --- | --- |
| RFC 2696 paging (`SimplePagedResultsControl`) in `StandardLdapProvider` | **done** |
| `GET /api/v1/users/search` with `filter`, `pageSize`, `pageToken` | **done** |
| Friendly search terms converted to LDAP filters, with every value escaped at the boundary | **done** |
| Page tokens as opaque strings | **partial** — `OffsetPageToken` is Base64 of an offset, not encrypted, and the directory's own LDAP cookie is not what travels to the browser |

## 3. Bulk operations and automation

**Goal:** the routine automated without writing scripts.

| | |
| --- | --- |
| `POST /api/v1/users/bulk/sync` answering `207 Multi-Status` with a per-item report | **done** |
| `POST /api/v1/users/bulk` as a background job with status polling | **done** |
| Numeric and alphabetic ranges — `node-{01..99}` | **done** |
| A regular-expression parser for pasted raw text, with named groups | **done** |
| Transliteration (Cyrillic and Kazakh to Latin) and case normalisation for logins | **done** |
| Preview: the generated pool checked for conflicts in the directory before anything is written | **done** |
| Job storage | **partial** — `IBulkJobQueue`/`IBulkJobStore` are in-memory singletons, so queued work does not survive a restart and cannot be shared between instances. See open questions below. |

## 4. Web console

**Goal:** directory administration that someone outside IT can be handed.

| | |
| --- | --- |
| Users, groups, organizational units, computers: search, create, edit, move, delete | **done** |
| Providers, role mappings, rate limits, audit log | **done** |
| The bulk screens and the pattern/regex generator with live preview | **done** |
| Nine languages, including right-to-left | **done** |
| Organizational units as an interactive tree, children loaded lazily | **planned** — the screen is a flat list of DNs today |
| Drag and drop to move entries between units | **planned** |
| A CSV/JSON import wizard, mapping file columns onto directory attributes | **planned** |

## 5. Security, audit and logging

**Goal:** credentials that stay secret and changes that can be accounted for.

| | |
| --- | --- |
| Passwords never logged, on creation or reset | **done** |
| Bind passwords encrypted at rest — `AesGcmEncryptionService`, `enc:<nonce>:<cipher>:<tag>` | **done** |
| Log properties that read like a secret redacted on the way to every sink | **done** |
| Audit trail: every sign-in and modification, successful or not, with actor, object and outcome | **done** |
| Deterministic, stable object ids — `SHA256` of the DN, narrowed to a GUID | **done** |
| Permission-based authorisation resolved per request from directory group membership | **done** |
| Structured logging with switchable sinks (console, file, Logstash, Loki) and a correlation id | **done** |
| **LDAPS or StartTLS to the directory** | **planned** — and the project's largest security gap: user passwords currently cross the network to the directory in the clear. [SECURITY.md](SECURITY.md#known-limitations) |
| Ending a session — sign-out, refresh tokens, or a deny list | **planned** — a token is valid until it expires |
| Password policy and account lockout | **planned** — whatever the directory enforces is what applies |

## 6. Enterprise extensions

**Goal:** the features large deployments ask for. Open-core: the intent is that these are the paid
modules and everything above stays Apache-2.0. All **planned**.

- **Secret stores** — HashiCorp Vault, Azure Key Vault, Yandex Cloud Lockbox instead of
  configuration keys.
- **Multi-factor authentication** for the console — TOTP, WebAuthn.
- **Audit streaming** to a SIEM over Syslog or CEF — Splunk, ELK, ArcSight.
- **OU-level RBAC** — an administrator's rights bounded to particular organizational units.
- **Approval workflows** — two-stage confirmation for critical operations, such as deleting a unit
  or adding somebody to Domain Admins.
- **HR synchronisation** — scheduled import from external systems (1C:ZUP, SAP, BambooHR).
- **Password self-service** — reset by SMS or email code, and expiry notifications to Slack,
  Telegram or Teams.

## Open questions

Decisions that need making rather than coding, and which block the items above that depend on them.

- **Where bulk jobs live.** In-memory today, while providers, role mappings and rate limits are
  already in SQLite. Moving them to the database makes a restart survivable and more than one
  instance possible; it also makes the queue a schema with migrations. Worth doing before anything
  depends on jobs outliving a deployment.
- **Whether SQLite stays.** Fine for a single process. A second instance needs PostgreSQL or SQL
  Server, and that decision is better made before the schema grows rather than after.
- **Two major dependency upgrades**, both attempted and reverted as genuinely breaking:
  `Swashbuckle.AspNetCore` 6.6.2 → 10.x (OpenApi.NET v2, new namespaces and schema API) and
  `Novell.Directory.Ldap.NETStandard` 3.6.0 → 4.x (a reworked connection and control API). Each is
  its own piece of work, not a version bump.
