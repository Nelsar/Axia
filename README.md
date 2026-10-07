<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/logo-dark.svg">
    <img src="docs/brand/logo.svg" alt="Nelsar.Axia" width="300" height="64">
  </picture>
</p>

# Nelsar.Axia — open-source web console and REST API for Active Directory and LDAP

[![CI](https://github.com/Nelsar/Nelsar.Axia/actions/workflows/ci.yml/badge.svg)](https://github.com/Nelsar/Nelsar.Axia/actions/workflows/ci.yml)
[![CodeQL](https://github.com/Nelsar/Nelsar.Axia/actions/workflows/codeql.yml/badge.svg)](https://github.com/Nelsar/Nelsar.Axia/actions/workflows/codeql.yml)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![React + TypeScript](https://img.shields.io/badge/console-React%20%2B%20TypeScript-3178C6.svg)](frontend/)

**Nelsar.Axia** is a self-hosted web interface and REST API for managing **Active Directory**,
**OpenLDAP** and other LDAP directories from the browser: users, groups, organizational units (OUs)
and computer accounts, across several directories from one instance — with role-based access,
an audit log and bulk user provisioning.

Built on .NET 10 (ASP.NET Core) with a React/TypeScript console. Licensed under
[Apache 2.0](LICENSE).

> **Status: MVP.** The API and console do what is described below, and 681 tests cover it. What is
> not yet here is listed under [Not in scope yet](#not-in-scope-yet) — read it before deciding
> whether this fits.

## Who it is for

- **IT administrators** who want to hand routine directory work — creating accounts, resetting
  attributes, adding people to groups — to helpdesk or HR staff through a browser, without giving
  them RSAT, a domain-joined Windows machine or a direct LDAP client.
- **Teams running OpenLDAP or a mix of AD and LDAP** who want one console and one API for all of
  their directories.
- **Developers automating identity lifecycle** — onboarding, offboarding, bulk account creation —
  who would rather call a documented REST API (OpenAPI/Swagger) than script LDAP by hand.

## How it compares

| | Nelsar.Axia | ADUC / RSAT | phpLDAPadmin | Apache Directory Studio |
| --- | --- | --- | --- | --- |
| Runs in a browser | yes | no — Windows desktop | yes | no — desktop |
| REST API for automation | yes | no (PowerShell) | no | no |
| Active Directory and OpenLDAP | both | AD only | both | both |
| Several directories in one instance | yes | one domain at a time | yes | yes |
| Permissions mapped from directory groups | yes | AD delegation | no — the bind account's rights | no |
| Audit log of changes | yes | Windows event log | no | no |
| Bulk creation with name patterns | yes | no — csvde or PowerShell | LDIF import | LDIF import |
| LDAPS / StartTLS to the directory | yes — LDAPS by default | yes | yes | yes |

## Features

- **Users** — search, read, create, update, delete; raw directory attributes on demand; bulk
  operations (synchronous or queued); a pattern generator for bulk account names with
  transliteration and LDAP conflict preview.
- **Groups, organizational units, computer accounts** — full CRUD against the directory tree.
- **Several directories at once** — connection profiles are rows in the database; a request picks
  one with the `X-LDAP-Provider-Id` header, and a session stays bound to the directory it signed
  in to.
- **Role mappings** — directory groups map to application roles, globally or per directory.
- **Audit log** — every sign-in, and every attempt to change a directory or this application's
  settings, permitted or refused, with who made it.
- **Rate limiting** — per client address, retunable through the API without a restart.
- **Nine interface languages**, including Arabic and Hebrew with a mirrored layout.

Active Directory and OpenLDAP are both supported. The `StandardLdap` provider speaks standard LDAP
(RFC 4511) over LDAPS or StartTLS and works against AD, OpenLDAP and eDirectory alike; a Windows-only `ActiveDirectory`
provider using `System.DirectoryServices` also exists but manages fewer object types.

## Architecture

```
Nelsar.Axia.Domain          entities, value objects, domain events — no outward dependencies
Nelsar.Axia.Application     use cases as MediatR commands/queries; validation, authorisation
                            and logging as pipeline behaviours
Nelsar.Axia.Infrastructure  LDAP providers, EF Core persistence, encryption, token issuance
Nelsar.Axia.Api             HTTP surface, composition root, observability
Nelsar.Axia.Contracts       the shared boundary: DTOs, module interfaces
frontend/                   React + TypeScript console (Vite)
```

Permissions are resolved per request from the caller's directory group membership, checked in the
Application layer before a use case runs — not at the controller. A handler cannot be reached
without passing that check, and a test asserts it.

## Getting started

### Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) (built and tested on 10.0.112)
- [Node.js](https://nodejs.org/) 18 or newer, for the console
- An LDAP or Active Directory server to point at

### 1. Configuration

The API reads `appsettings.json`, which is **not** in the repository: it holds a bind password and
two keys. Copy the examples and fill them in.

```bash
cp Nelsar.Axia.Api/appsettings.json.example Nelsar.Axia.Api/appsettings.json
cp Nelsar.Axia.Api/appsettings.Development.json.example Nelsar.Axia.Api/appsettings.Development.json
```

Generate the two secrets — do not ship the placeholders:

```bash
openssl rand -base64 32   # Encryption:Key — AES key for bind passwords at rest
openssl rand -base64 48   # Jwt:Key — token signing key, at least 32 characters
```

Put the first in `appsettings.Development.json` under `Encryption:Key`, the second in
`appsettings.json` under `Jwt:Key`. Then point `Ldap:*` at your directory — the example file
documents each setting.

The API refuses to start while either key is missing, malformed, too short or still the
placeholder from the example file, and says which one. The placeholders are published in this
repository: a host signing tokens with one would accept a token anybody can forge.

### 2. Run the API

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project Nelsar.Axia.Api
```

It listens on `http://localhost:5063`, creates a SQLite database on first run and applies
migrations. In `Development` it also serves Swagger UI at `/swagger`.

### 3. Run the console

```bash
npm --prefix frontend install
npm --prefix frontend run dev
```

The console defaults to the API at `http://localhost:5063`; the sign-in screen has a field to
change that, and a language picker.

### 4. Sign in

Use an account from your directory. Which application role it gets is decided by its group
membership, through role mappings that name each group by its distinguished name. The first ones
come from configuration, and **the Admin one is required** — the API refuses to start without it:

```json
"RoleMappings": {
  "GroupDns": {
    "Admin": "CN=Axia-Admins,OU=Groups,DC=corp,DC=example,DC=com",
    "UserEditor": "CN=Axia-Editors,OU=Groups,DC=corp,DC=example,DC=com"
  }
}
```

or `RoleMappings__GroupDns__Admin=...` in the environment. Roles are `Admin`, `UserCreator`,
`UserEditor` and `UserDeleter`; the rest can be added later under **Role mappings**.

What each role may do on a fresh database:

| Role | May |
| --- | --- |
| `User` | Read users, groups, organizational units and computers. Nothing else. |
| `UserCreator` | Also list all users, create accounts, run bulk batches and the name generator. |
| `UserEditor` | Also edit accounts, including passwords, singly or in bulk. |
| `UserDeleter` | Also delete accounts. |
| `Admin` | Everything, including groups, units, computers, directories, role mappings and rate limits. |

A bulk batch needs `users.create` for its create rows and `users.update` for its update rows, as
well as `users.bulk`: it is a faster way to do what its submitter could do one account at a time,
never a way to do more.

An account in none of the groups signs in as `User`. Mappings are editable in the console under
**Role mappings**.

A mapping that names its group by CN alone, as earlier builds seeded them (`Administrators`),
grants its role to members of *every* group with that name, wherever it is — so anyone able to
create a group could create an `Administrators` of their own. Such mappings are **ignored**: the
API logs an error for each on every start, and the console marks them. Setting
`RoleMappings:GroupDns` converts the global ones to the DNs given at the next start.
`RoleMappings:AllowMappingsByName=true` makes them count again, hole included, for a deployment
that cannot yet say where its groups live.

Whatever a role allows, nobody may act on an account or group that can do more than they can:
- in this application — an editor can reset a colleague's password but not an administrator's,
  and holding `groups.manage` does not let anyone into a group mapped to a role they do not hold;
- in the directory — an account or group Active Directory marks privileged (`adminCount`, which
  covers Domain Admins and the other protected groups and their members) can only be changed by an
  administrator of this application. OpenLDAP marks nothing, so list the groups that make someone
  its administrator under `DirectoryProtection:ProtectedGroups`;
- and the account the application binds as (`Ldap:AdminUsername`) cannot be changed through it at
  all.

Give that bind account only the rights the application needs — create, edit and reset passwords
in the OUs it manages — never Domain Admin: everything the application does, it does with it.

Passwords are set the way the directory hashes and checks them: `unicodePwd` in Active Directory
(which accepts it only over LDAPS) and the LDAP Password Modify operation elsewhere — never written
as a plain `userPassword` attribute.

Changing a directory's host, port or failover hosts requires entering its bind password again —
otherwise the stored one would be sent to whatever server the new address points at.

> **Upgrading from a build before DN role mappings:** set `RoleMappings:GroupDns:Admin` (and the
> other roles you use) before starting the new build — mappings by group name no longer grant
> anything, and without an Admin mapping by DN the API will not start. Every session is also
> signed out once: tokens issued before now do not say which directory entry they belong to and are
> refused. Signing in again issues one that does.

> **Upgrading from an earlier build:** its first run gave every signed-in account the right to
> manage groups, organizational units and computers, and to run bulk batches — enough to make
> oneself an administrator or reset anyone's password. The `RevokeAccidentalDefaultGrants`
> migration takes those grants back when the new version starts. Only those exact grants are
> removed; anything else in the table is left as it was.

## Configuration

Everything below is settable through `appsettings.json` or environment variables, using the
standard double-underscore form (`Auth__AllowSelfRegistration=true`).

A Docker image never contains your local `appsettings.json`: `.dockerignore` keeps it out, along
with local databases and logs, so the secrets in it cannot travel with the image. Configure a
container through the environment or a mounted file.

| Setting | Default | What it is |
| --- | --- | --- |
| `Auth:AllowSelfRegistration` | `false` | Whether `POST /api/v1/auth/register` creates an account for an unauthenticated caller. See [Security defaults](#security-defaults). |
| `Cors:AllowedOrigins` | — | Origins the browser console may call the API from, as a list of bare origins (`https://host[:port]`, no trailing slash). Empty means loopback only. |
| `Cors:AllowAnyOrigin` | `false` | Admit every website. A switch of its own rather than `*` in the list — see [Security defaults](#security-defaults). |
| `Https:RedirectToHttps` | `false` | Whether plain HTTP is redirected. Leave off when a reverse proxy terminates TLS. |
| `ReverseProxy:KnownProxies` / `KnownNetworks` | — | Proxy addresses whose `X-Forwarded-For` and `X-Forwarded-Proto` are believed. Empty means none. |
| `Ldap:Security` | `Ldaps` | How the connection to the directory is protected: `Ldaps`, `StartTls`, or `None` — plain LDAP, which sends every password unencrypted. |
| `Ldap:Port` | `636`; `389` for `StartTls` and `None` | Leave unset for the mode's standard port. LDAPS on 389, or plain LDAP on 636, stops the API at startup. |
| `Ldap:TrustedCaCertificatePath` | — | A PEM or DER file with a CA — or a self-signed directory's own certificate — to trust besides the system store. The certificate must still name the host; verification cannot be turned off. Applies to `StandardLdap` directories, including those in the database. |
| `Ldap:*` | — | The rest of the default directory: host, base DN, bind account, search filter. |
| `Jwt:Key` / `Jwt:ExpiresInMinutes` | — / `60` | Token signing key and lifetime. Required: at least 32 bytes, not the example placeholder. |
| `Encryption:Key` | — | AES key for provider bind passwords at rest. Required: base64 of 16, 24 or 32 bytes, not the example placeholder. |
| `Observability:Console` / `File` | on | Structured logs: compact JSON to stdout and a rolling file. |
| `Observability:Logstash` / `Loki` | off | Shippers. Set `Enabled` and a `Uri` to turn one on. |
| `Serilog:MinimumLevel` | `Information` | Levels and per-namespace overrides, in the standard Serilog section. ASP.NET Core's own request events, EF Core's SQL statements and `HttpClient` are held at `Warning` unless an override here says otherwise. |

> **Upgrading from an earlier build:** directory connections are now LDAPS unless configured
> otherwise. An `appsettings.json` that says `"Port": 389` and nothing about security stops the
> API at startup, naming the ways out: port 636 for LDAPS, `Ldap:Security=StartTls` to stay on 389
> with TLS, or `Ldap:Security=None` to keep plain LDAP. Directories stored in the database were set
> to `None` by the `AddProviderSecurity` migration, so they keep working exactly as before; the
> console marks them *not encrypted*, and each can be switched once its server offers TLS.

## Security defaults

Read this before exposing the API anywhere.

- **Self-registration is off.** `POST /api/v1/auth/register` writes to the directory with no
  authentication in front of it. Left open, anyone who can reach the port can create accounts in
  your directory; the rate limiter makes that slow rather than impossible. Administrators create
  accounts through `POST /api/v1/users`, behind the `users.create` permission, which is the same
  operation. Turn it on only deliberately.
- **CORS is closed to everything but loopback.** With `Cors:AllowedOrigins` unset, a browser may
  call the API only from `localhost` or `127.0.0.1` — enough for development, and it does not
  silently become an open API when the port is exposed. Deploying the console on its own host
  means listing that host:

  ```json
  "Cors": { "AllowedOrigins": [ "https://axia.example.com" ] }
  ```

  Naming any origin replaces the loopback default rather than adding to it, so a deployment
  that wants both lists both. An entry that can never match a browser's origin — a trailing
  slash, a path, a `*.example.com` wildcard, an empty value — stops the API at startup with the
  value it should have been, rather than leaving a console that cannot reach it.

  `*` is refused too. Admitting every website is `"AllowAnyOrigin": true`, and worth thinking
  about first: on an API reachable only inside a network, it lets any page an employee opens use
  their browser to try passwords against `/api/v1/auth/login` and read the answers.

  Note what the policy is for: it decides which *websites* may read responses in a browser. It
  is not a network control, and nothing about it restrains a non-browser client — that is the
  authentication's job.
- **Directory connections use LDAPS.** Signing in is a simple bind, so a user's password travels to
  the directory as typed, and so does the bind account's on every request; TLS is what keeps them
  off the wire. The certificate has to chain to a CA the system trusts, or to the one named in
  `Ldap:TrustedCaCertificatePath`, and it has to name the host connected to — there is no setting
  that accepts anything else. `StartTls` upgrades a connection on 389 and fails rather than
  continuing unencrypted. Plain LDAP is `Ldap:Security=None`, chosen by name, and logged as a
  warning when used.
- **HTTPS redirection is off, HSTS is on outside Development.** The shipped default suits the
  common deployment, where a reverse proxy terminates TLS and talks plain HTTP to the API; with
  redirection on, that proxy's request would be bounced back to itself. Set
  `Https:RedirectToHttps` when the API itself holds the certificate.
- **A reverse proxy is not trusted until named.** `X-Forwarded-For` is a request header like any
  other: anyone who can reach the port can send one. Honouring it unconditionally would let a
  caller write their own address, and the rate limiter partitions by exactly that address.
  So forwarded headers are read only from the proxies you list:

  ```json
  "ReverseProxy": { "KnownProxies": [ "10.0.0.7" ] }
  ```

  Until then, every request appears to come from the proxy — one rate-limit bucket for all
  clients, and the proxy's address in the audit trail. Both are worth fixing in a real
  deployment, by naming the proxy here.
- **Secrets never reach the log.** Bind passwords are encrypted at rest; log properties whose
  names read like a secret are redacted on the way to every sink; logins and distinguished names
  are masked in the diagnostic log, while the audit trail records the actor in full on purpose.
- **LDAP filters and distinguished names are escaped** at the boundary, so a login containing
  filter metacharacters cannot change the query it lands in.

## Internationalisation

English is the default and the source of message keys. Also available: Russian, Kazakh, German,
Spanish, French, Chinese, Arabic and Hebrew. The console picks the language, and sends it as
`Accept-Language` so the API's own messages match.

Translations beyond English and Russian are machine-quality and have not had a native review —
corrections are welcome. Adding a language is one dictionary file in
`frontend/src/lib/i18n/` plus three `.resx` files on the API side; both sides fail the build or
the tests if a message is left untranslated.

## Tests

```bash
dotnet test
```

681 tests. Beyond the usual, they pin the things that only fail quietly: layer boundaries,
authorisation on every guarded use case, what may reach a log sink, multi-directory isolation, and
translation completeness in every supported language.

## Not in scope yet

Said plainly, so nobody discovers it after deploying:

- **Directory certificates are not checked for revocation.** A revoked certificate that still
  chains and names the host is accepted. See [SECURITY.md](SECURITY.md#known-limitations).
- **A session cannot be ended.** Tokens are bearer JWTs valid until they expire, 60 minutes by
  default, with no sign-out and no deny list.
- **No Docker quick start.** The compose files in the repository are not currently in a working
  state, and the OpenLDAP bootstrap fixtures are missing. Point the API at a directory you already
  have.
- **Password policy, account lockout and group membership editing from the user screen** are not
  implemented.
- The `ActiveDirectory` provider is Windows-only and manages fewer object types than
  `StandardLdap`, which also works against Active Directory. Prefer `StandardLdap`.

What comes next is in [ROADMAP.md](ROADMAP.md).

## Contributing

Small contributions are the useful ones here — a safer default, a failure that reports itself
badly, a translation that reads wrong to someone who speaks the language. See
[CONTRIBUTING.md](CONTRIBUTING.md) for how to run what CI runs, and for the conventions that are
enforced by tests rather than by review.

Found something exploitable? Please report it privately rather than in an issue —
[SECURITY.md](SECURITY.md#reporting-a-vulnerability) says how, and sets out what the project does
and does not currently defend against.

## Licence

Apache License 2.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE).
