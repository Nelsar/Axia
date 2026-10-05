# Nelsar.Axia

A REST API and web console for administering Active Directory and LDAP directories — users,
groups, organizational units and computer accounts — across several directories from one instance.

Built on .NET 10 with a React/TypeScript console. Licensed under
[Apache 2.0](LICENSE).

> **Status: MVP.** The API and console do what is described below, and 545 tests cover it. What is
> not yet here is listed under [Not in scope yet](#not-in-scope-yet) — read it before deciding
> whether this fits.

## What it does

- **Users** — search, read, create, update, delete; raw directory attributes on demand; bulk
  operations (synchronous or queued); a pattern generator for bulk account names with
  transliteration and LDAP conflict preview.
- **Groups, organizational units, computer accounts** — full CRUD against the directory tree.
- **Several directories at once** — connection profiles are rows in the database; a request picks
  one with the `X-LDAP-Provider-Id` header, and a session stays bound to the directory it signed
  in to.
- **Role mappings** — directory groups map to application roles, globally or per directory.
- **Audit log** — authentications and changes to directory objects.
- **Rate limiting** — per client address, retunable through the API without a restart.
- **Nine interface languages**, including Arabic and Hebrew with a mirrored layout.

Active Directory and OpenLDAP are both supported. The `StandardLdap` provider speaks plain LDAP
(RFC 4511) and works against AD, OpenLDAP and eDirectory alike; a Windows-only `ActiveDirectory`
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
membership through the role mappings seeded on first run:

| Directory group | Application role |
| --- | --- |
| `Administrators` | `Admin` |
| `UserCreators` | `UserCreator` |
| `UserEditors` | `UserEditor` |
| `UserDeleters` | `UserDeleter` |

An account in none of them signs in as `User` and may read little. Mappings are editable in the
console under **Role mappings**.

## Configuration

Everything below is settable through `appsettings.json` or environment variables, using the
standard double-underscore form (`Auth__AllowSelfRegistration=true`).

| Setting | Default | What it is |
| --- | --- | --- |
| `Auth:AllowSelfRegistration` | `false` | Whether `POST /api/v1/auth/register` creates an account for an unauthenticated caller. See [Security defaults](#security-defaults). |
| `Cors:AllowedOrigins` | — | Origins the browser console may call the API from, as a list. Empty means loopback only. |
| `Https:RedirectToHttps` | `false` | Whether plain HTTP is redirected. Leave off when a reverse proxy terminates TLS. |
| `ReverseProxy:KnownProxies` / `KnownNetworks` | — | Proxy addresses whose `X-Forwarded-For` and `X-Forwarded-Proto` are believed. Empty means none. |
| `Ldap:*` | — | The default directory: host, port, base DN, bind account, search filter. |
| `Jwt:Key` / `Jwt:ExpiresInMinutes` | — / `60` | Token signing key and lifetime. |
| `Encryption:Key` | — | AES key for provider bind passwords at rest. |
| `Observability:Console` / `File` | on | Structured logs: compact JSON to stdout and a rolling file. |
| `Observability:Logstash` / `Loki` | off | Shippers. Set `Enabled` and a `Uri` to turn one on. |
| `Serilog:MinimumLevel` | `Information` | Levels and per-namespace overrides, in the standard Serilog section. |

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
  that wants both lists both. Note what the policy is for: it decides which *websites* may read
  responses in a browser. It is not a network control, and nothing about it restrains a
  non-browser client — that is the authentication's job.
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

545 tests. Beyond the usual, they pin the things that only fail quietly: layer boundaries,
authorisation on every guarded use case, what may reach a log sink, multi-directory isolation, and
translation completeness in every supported language.

## Not in scope yet

Said plainly, so nobody discovers it after deploying:

- **The connection to the directory is plain LDAP** — no LDAPS, no StartTLS. Every sign-in binds
  as the user, so the user's password crosses the network to the directory in the clear, and so
  does the bind credential on every connection. The API and the directory have to sit on a network
  where that is acceptable. This is the project's largest security gap; see
  [SECURITY.md](SECURITY.md#known-limitations) for the rest.
- **A session cannot be ended.** Tokens are bearer JWTs valid until they expire, 60 minutes by
  default, with no sign-out and no deny list.
- **No Docker quick start.** The compose files in the repository are not currently in a working
  state, and the OpenLDAP bootstrap fixtures are missing. Point the API at a directory you already
  have.
- **Password policy, account lockout and group membership editing from the user screen** are not
  implemented.
- The `ActiveDirectory` provider is Windows-only and manages fewer object types than
  `StandardLdap`, which also works against Active Directory. Prefer `StandardLdap`.

What is planned, and the status of each piece of it, is in [ROADMAP.md](ROADMAP.md).

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
