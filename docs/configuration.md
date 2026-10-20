# Configuration reference

Every setting the API reads, what it defaults to, and what it refuses.

## How configuration is read

Standard ASP.NET Core configuration, in this order, later sources winning:

1. `Nelsar.Axia.Api/appsettings.json`
2. `Nelsar.Axia.Api/appsettings.{Environment}.json`, e.g. `appsettings.Development.json`
3. Environment variables
4. Command-line arguments

Neither JSON file is in the repository; copy them from the `.example` files next to them. The
files accept `//` comments.

**Environment variables** use a double underscore for each level of nesting, and an index for
array elements:

```bash
Ldap__Server=dc01.corp.example.com
RoleMappings__GroupDns__Admin="CN=Axia-Admins,OU=Groups,DC=corp,DC=example,DC=com"
Cors__AllowedOrigins__0=https://axia.example.com
DirectoryProtection__ProtectedGroups__0="cn=axia-admins,ou=groups,dc=example,dc=org"
```

**The environment name** comes from `ASPNETCORE_ENVIRONMENT` (default `Production`). It decides
whether `appsettings.Development.json` is read, whether Swagger is served (Development only), and
whether HSTS is sent (everywhere but Development).

**A container image never contains `appsettings.json`.** `.dockerignore` keeps it, the database and
the logs out of the image, so the secrets in them cannot travel with it. Configure a container
through the environment or a mounted file.

**Settings are checked at startup.** Most mistakes stop the API before it accepts a request, with a
log line that names the setting and the fix. See [What stops startup](#what-stops-startup).

## Quick index

| Section | Required | Purpose |
| --- | --- | --- |
| [`Jwt`](#jwt) | **yes** (`Key`) | Token signing |
| [`Encryption`](#encryption) | **yes** (`Key`) | Encrypting stored bind passwords |
| [`Ldap`](#ldap) | **yes** in practice | The default directory |
| [`RoleMappings`](#rolemappings) | **yes** (`GroupDns:Admin`) | Which groups get which role |
| [`DirectoryProtection`](#directoryprotection) | on OpenLDAP | Groups only an Admin may change |
| [`Database`](#database-and-connectionstrings) | **yes** (`Provider`) | Which database |
| [`ConnectionStrings`](#database-and-connectionstrings) | **yes** with PostgreSQL | Where it is |
| [`Auth`](#auth) | no | Self-registration |
| [`Cors`](#cors) | when the console is not on localhost | Which websites may call the API |
| [`ReverseProxy`](#reverseproxy) | behind a proxy | Whose `X-Forwarded-*` to believe |
| [`Https`](#https) | when the API holds the certificate | HTTP → HTTPS redirect |
| [`Observability`](#observability) | no | Log sinks and correlation ids |
| [`Serilog`](#serilog) | no | Log levels |

## `Jwt`

| Key | Default | Description |
| --- | --- | --- |
| `Jwt:Key` | none, **required** | HMAC-SHA256 signing key for bearer tokens. At least 32 bytes (UTF-8). Generate with `openssl rand -base64 48`. |
| `Jwt:Issuer` | `Nelsar.Axia.Api` | Written into each token and required on the way back. |
| `Jwt:Audience` | `Nelsar.Axia.Api` | Same, for the audience claim. |
| `Jwt:ExpiresInMinutes` | `60` | Token lifetime. Tokens cannot be revoked, so this is how long a stolen one stays usable. Switching the account off in the directory ends its session sooner, within 30 seconds (see [Access control](access-control.md#sessions-and-tokens)). |

Refused at startup: missing key, a key containing `CHANGE_ME` (the published placeholder), a key
under 32 bytes, an empty `Issuer` or `Audience`, a lifetime under one minute.

Changing the key signs out everyone: existing tokens no longer validate.

## `Encryption`

| Key | Default | Description |
| --- | --- | --- |
| `Encryption:Key` | none, **required** | Base64 of a 16, 24 or 32-byte AES key. Used with AES-GCM to encrypt the bind passwords of directories stored in the database. Generate with `openssl rand -base64 32`. |

Refused at startup: missing, `CHANGE_ME` placeholder, not valid base64, or the wrong length.

There is no rotation procedure. With a different key, every stored bind password becomes
unreadable; the affected directories report it (rather than crashing) until each password is
entered again.

## `Ldap`

The default directory, used by every request that does not name another one.

| Key | Default | Description |
| --- | --- | --- |
| `Ldap:Server` | empty | Host name or address. For LDAPS and StartTLS it must match a name in the server's certificate. Empty means there is no default directory (all directories come from the database); sign-in without a directory then answers `502` "no server configured". |
| `Ldap:Security` | `Ldaps` | `Ldaps` (TLS from the first byte), `StartTls` (plain connection upgraded; fails rather than continuing unencrypted), or `None` (plain LDAP; every password crosses the network readable; logged as a warning on each connection). |
| `Ldap:Port` | `636` for `Ldaps`; `389` for `StartTls`/`None` | Leave unset for the standard port. A port that contradicts the mode stops startup (LDAPS on 389, StartTLS/None on 636). `3268`/`3269` (Global Catalog) are accepted but **read-only**: every write is refused. |
| `Ldap:TrustedCaCertificatePath` | empty | PEM or DER file with a CA (or a self-signed server's own certificate) to trust besides the OS store. Applies to every `StandardLdap` directory, including those in the database. The host name is still checked; verification cannot be turned off. An unreadable file stops startup. |
| `Ldap:BaseDn` | empty | Where searches start. Also where **new accounts are created** (see [Directories](directories.md#what-the-application-writes)). |
| `Ldap:AdminUsername` | empty | The bind account: a DN (`cn=admin,dc=example,dc=org`), or for AD a UPN (`svc-axia@corp.example.com`). Every operation is performed with it. It can never be changed through the application; that protection recognises it by DN or by its login attributes, so avoid the `DOMAIN\name` form, which AD accepts but the protection cannot match. |
| `Ldap:AdminPassword` | empty | Its password. Keep it out of files in production: `Ldap__AdminPassword`. |
| `Ldap:UserSearchFilter` | `(&(objectClass=person)(\|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))` | How a login typed at sign-in is looked up, tried first. `{0}` is replaced by the escaped login. The default covers OpenLDAP (`uid`), AD (`sAMAccountName`, `userPrincipalName`) and e-mail. When it finds nothing, the API also tries `mail`, `userPrincipalName`, `sAMAccountName` and `uid` on their own (see [Directories](directories.md#accounts)), so narrowing this filter does not by itself stop people signing in by those. |
| `Ldap:ProviderType` | `StandardLdap` | `StandardLdap` (any OS, any LDAP v3 directory) or `ActiveDirectory` (Windows only; refused at startup elsewhere; no group management). |
| `Ldap:UserSearchPageSize` | `500` | Page size for directory-wide listings (RFC 2696 paged results). Keep it at or below the server's own limit (AD: `MaxPageSize`, 1000 by default). |
| `Ldap:ConnectTimeoutSeconds` | `10` | How long to wait for a directory to accept a connection, the TLS handshake included. 1 to 300; anything else stops startup. Applies to every directory, including those in the database; an `Ldap:Providers:{id}` section may set its own. |
| `Ldap:OperationTimeoutSeconds` | `30` | How long to wait for a directory to answer any one operation — a bind, a page of a search, a write — and the time limit each search hands to the server. 1 to 300. Same scope as above. A directory that does not answer in time is reported as unavailable (`502`). |

### `Ldap:Providers:{id}`

Extra directories declared in configuration rather than in the database, selected by
`X-LDAP-Provider-Id: {id}`. Each takes the same keys as `Ldap`, plus:

| Key | Description |
| --- | --- |
| `Ldap:Providers:{id}:Servers` | Comma-separated hosts tried in order (failover). Falls back to `Server`. |

These are checked only when a request names one; a broken one is reported then, not at startup.
The console's directory picker lists only directories stored in the database.

## `RoleMappings`

| Key | Default | Description |
| --- | --- | --- |
| `RoleMappings:GroupDns:{Role}` | none; **`Admin` required** | Full DN of the directory group whose members get `{Role}`. Roles: `Admin`, `UserCreator`, `UserEditor`, `UserDeleter`. |
| `RoleMappings:AllowMappingsByName` | `false` | Whether old mappings that name a group by CN alone count. **Leave off**: such a mapping matches every group of that name anywhere in the directory. |

At each start:

- **Fresh database**: one global mapping is created per entry in `GroupDns`.
- **Existing database**: any global mapping still written by name for a role listed here is
  converted to the DN given, **once**, and saved. Changing `GroupDns` later does not move a mapping
  that is already a DN; edit it under *Role mappings* or through the API.
- An error is logged for every mapping still written by name, on every start.
- **Startup is refused** if no mapping grants Admin by DN (unless `AllowMappingsByName` is on).

Refused at startup: an unknown role name, or a value that is not a well-formed DN.

The API checks that each value is a well-formed DN, not that the group exists. A wrong DN starts
fine and grants the role to nobody.

## `DirectoryProtection`

| Key | Default | Description |
| --- | --- | --- |
| `DirectoryProtection:ProtectedGroups` | `[]` | DNs of groups that, together with their members, only a caller holding **every** permission may change. |

Active Directory needs nothing here: `adminCount=1` already marks Domain Admins and the other
protected groups and their members, and the application honours it. OpenLDAP marks nothing, so list
the groups that make someone an administrator of the directory, and the group mapped to Admin.

## `Database` and `ConnectionStrings`

Where the API keeps what is not in the directory: the directories it connects to, role mappings,
role permissions, rate limits and the audit trail.

| Key | Default | Description |
| --- | --- | --- |
| `Database:Provider` | none, **required** | `PostgreSql` or `Sqlite`, in any case. |
| `ConnectionStrings:DefaultConnection` | **required** with `PostgreSql`; `Data Source=nelsar-axia.db` with `Sqlite` | The connection string, in the form the provider's driver takes. |

**PostgreSQL** is the database for a deployment. Any supported PostgreSQL version works; the quick
start and CI use 17. The account needs to create tables in its database on the first start, and
whenever an upgrade brings a migration:

```bash
Database__Provider=PostgreSql
ConnectionStrings__DefaultConnection="Host=db.internal;Database=nelsar_axia;Username=nelsar_axia;Password=…;SSL Mode=VerifyFull"
```

Use `SSL Mode=VerifyFull` whenever the server is on another host. GSS encryption is off unless the
connection string names it (`GSS Encryption Mode=Prefer`): the API's image carries no Kerberos
library, and the driver would otherwise look for one on every new connection.

**SQLite** keeps everything in one file and needs no server. The unit tests run on it, and it is
still a choice for a single small instance, or for running the API from source without a database
server. Relative paths are relative to the working directory:

```bash
Database__Provider=Sqlite
ConnectionStrings__DefaultConnection="Data Source=/data/nelsar-axia.db"
```

There is no default provider. Versions before 0.3 used SQLite without being told to, so a
configuration written for one of them names a SQLite file and no provider. Rather than guess, the
API stops and says so: set `Sqlite` to go on with that file, or copy it into PostgreSQL first —
see [Moving to another database](operations.md#moving-to-another-database).

Migrations are applied automatically at startup, each database from its own set.

## `Auth`

| Key | Default | Description |
| --- | --- | --- |
| `Auth:AllowSelfRegistration` | `false` | Whether `POST /api/v1/auth/register` creates a directory account for an unauthenticated caller. Off, and worth leaving off: only the rate limiter stands in front of it. Administrators create accounts with `POST /api/v1/users`. |

## `Cors`

Which websites may call the API from a browser.

| Key | Default | Description |
| --- | --- | --- |
| `Cors:AllowedOrigins` | `[]` | Bare origins: `https://host[:port]`, no path, no trailing slash, no wildcard. **Empty means loopback only** (`localhost`, `127.0.0.1`, any port). Naming any origin replaces the loopback default; list both if you need both. |
| `Cors:AllowAnyOrigin` | `false` | Admit every website. `*` in the list is refused; this switch is the only way, so that it is a deliberate choice. |

Refused at startup: an entry with a path, a trailing slash, a wildcard, or empty.

CORS decides which web pages can read a response in a browser. It is not a network control and
does nothing against non-browser clients.

When the console is served from the same origin as the API, as in the quick start's nginx, CORS
is not involved at all.

## `ReverseProxy`

| Key | Default | Description |
| --- | --- | --- |
| `ReverseProxy:KnownProxies` | `[]` | Proxy IP addresses whose `X-Forwarded-For` and `X-Forwarded-Proto` are believed. |
| `ReverseProxy:KnownNetworks` | `[]` | The same, as CIDR ranges (`10.0.0.0/8`). |

Until the proxy is named, every request appears to come from the proxy's address: one rate-limit
bucket shared by everyone, and the proxy's address in the audit log. Naming an address that is not
really your proxy lets callers choose their own rate-limit bucket.

## `Https`

| Key | Default | Description |
| --- | --- | --- |
| `Https:RedirectToHttps` | `false` | Redirect plain HTTP to HTTPS. Leave off behind a TLS-terminating proxy, which talks HTTP to the API and would be redirected to itself. Turn on when the API holds the certificate. |

HSTS is sent outside Development regardless.

The API listens where ASP.NET Core is told to: `ASPNETCORE_URLS` or `ASPNETCORE_HTTP_PORTS`
(the container image defaults to port `8080`), or the launch profile in development (`5063`).

## `Observability`

| Key | Default | Description |
| --- | --- | --- |
| `Observability:ApplicationName` | `Nelsar.Axia.Api` | Attached to every log event. |
| `Observability:Console:Enabled` | `true` | Log to stdout. |
| `Observability:Console:Json` | `true` | Compact JSON (for collectors). `false` gives readable lines, with control characters in values written as escapes (`\n`, `\u001b`) so a value cannot start a line; the Development example uses that. |
| `Observability:File:Enabled` | `true` | Rolling file sink, compact JSON, one file per day. |
| `Observability:File:Path` | `logs/axia-.json` | File name pattern; the date goes before the extension. Mount it in a container. |
| `Observability:File:RetainedFileCountLimit` | `14` | Days of files kept. |
| `Observability:File:FileSizeLimitBytes` | `134217728` (128 MB) | Per-file cap; rolls to a new file when reached. |
| `Observability:Logstash:Enabled` | `false` | Ship to Logstash's HTTP input. |
| `Observability:Logstash:Uri` | none | e.g. `http://logstash:8080`. |
| `Observability:Logstash:QueueLimitBytes` | `33554432` (32 MB) | In-memory buffer; oldest events are dropped rather than blocking the API. |
| `Observability:Logstash:BatchSizeLimitBytes` | `5242880` (5 MB) | Per request. |
| `Observability:Logstash:BatchPostingLimit` | `1000` | Events per request. |
| `Observability:Logstash:PeriodSeconds` | `2` | Flush interval. |
| `Observability:Loki:Enabled` | `false` | Ship to Grafana Loki. |
| `Observability:Loki:Uri` | none | Base URI, e.g. `http://loki:3100`; `/loki/api/v1/push` is appended. |
| `Observability:Loki:Labels` | `{}` | Static labels. Keep them few and low-cardinality. |
| `Observability:Loki:Username` / `Password` / `Tenant` | none | For a Loki behind a gateway. Supply the password through the environment. |
| `Observability:Correlation:HeaderName` | `X-Correlation-ID` | Request header read for an incoming correlation id, and response header it is returned in. |

More than one sink may be on. Keeping the file sink under a shipper means the log survives the
shipper being down.

## `Serilog`

Only log levels are read from here; sinks are chosen under `Observability`.

```jsonc
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
      "System.Net.Http.HttpClient": "Warning"
    }
  }
}
```

The three overrides are also the built-in defaults. EF Core's SQL is held at Warning because
directory bind DNs and the encrypted-secrets column pass through those statements.

## Unused keys

`LdapBootstrap:EncryptedBindPasswordExample` in the Development example is not read by anything.
`AllowedHosts` is ASP.NET Core's host filtering (`*` by default).

## Settings stored in the database

Some configuration lives in the database and is changed through the API or the console, without a
restart:

| What | Where | Picked up |
| --- | --- | --- |
| Additional directories | `/api/v1/providers` | within 30 s (resolution cache) |
| Role mappings | `/api/v1/role-mappings` | within 30 s (permission cache) |
| What each role may do | `/api/v1/roles` | within 30 s |
| Rate limits | `/api/v1/rate-limit-settings` | within 30 s, as new windows start |

## What stops startup

| Log message (abridged) | Fix |
| --- | --- |
| `Jwt:Key is not set` / `…still the placeholder…` / `…shorter than 32 bytes…` | Generate one: `openssl rand -base64 48`. |
| `Encryption:Key is not set` / `…not valid base64` / `…decodes to N bytes…` | Generate one: `openssl rand -base64 32`. |
| `Database:Provider is not set` | `PostgreSql` with a connection string, or `Sqlite` to keep a database from 0.2 or earlier. See [Database](#database-and-connectionstrings). |
| `Database:Provider is PostgreSql, and ConnectionStrings:DefaultConnection is not set` | Name the server. |
| `Ldap:Security is Ldaps … and Ldap:Port is 389…` | Use 636, or `Security: StartTls`, or (knowingly) `None`. |
| `Ldap:ProviderType is ActiveDirectory, which cannot use StartTls` | Use `Ldaps`, or `StandardLdap`. |
| `ActiveDirectory … Windows` | Use `StandardLdap` on Linux/macOS. |
| `RoleMappings:GroupDns names the role 'X', which does not exist` | Roles are `Admin`, `UserCreator`, `UserEditor`, `UserDeleter`. |
| `RoleMappings:GroupDns:X is '…', which is not a distinguished name` | Give the full DN. |
| `No role mapping grants the Admin role by distinguished name…` | Set `RoleMappings:GroupDns:Admin`. |
| a `Cors:AllowedOrigins` entry quoted with its corrected form | Use a bare origin. |
| a `TrustedCaCertificatePath` problem | Point it at a readable PEM/DER file. |

[Troubleshooting](troubleshooting.md) has more, including failures after startup.
