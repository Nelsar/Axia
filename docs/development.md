# Development

How the code is organised, how to build and test it, and the conventions a change is held to.
[CONTRIBUTING.md](https://github.com/Nelsar/Axia/blob/main/CONTRIBUTING.md) is the short version for
pull requests.

## Setup

```bash
dotnet restore
npm --prefix frontend ci
cp Nelsar.Axia.Api/appsettings.json.example Nelsar.Axia.Api/appsettings.json
cp Nelsar.Axia.Api/appsettings.Development.json.example Nelsar.Axia.Api/appsettings.Development.json
docker compose -f deploy/dev/docker-compose.ldap.yml up -d --wait
```

Then configure the API for the local directory as in [Installation, path 3](installation.md#path-3-from-source-against-the-local-test-directory),
and run:

```bash
dotnet run --project Nelsar.Axia.Api --launch-profile http     # API on :5063, Swagger at /swagger
npm --prefix frontend run dev                                   # console on :5500
```

`.claude/launch.json` describes the console's dev server for tools that read it.

## Architecture

```
Api ──► Application ──► Domain
 │           ▲
 └──► Infrastructure ──┘        (all use Contracts)
```

| Project | Holds | May depend on |
| --- | --- | --- |
| `Nelsar.Axia.Domain` | Entities (`RoleMapping`, `Provider`, …), value objects (`DistinguishedName`, `Email`, `EncryptedSecret`), the permission catalogue, pure services (`UserPatternGenerator`) | nothing |
| `Nelsar.Axia.Contracts` | DTOs, request/response types, module interfaces (`IUsersModule`, …), exceptions that cross layers | nothing |
| `Nelsar.Axia.Application` | Use cases as MediatR commands and queries, validators, the pipeline behaviours, permission and role resolution, `PrivilegeGuard` | Domain, Contracts |
| `Nelsar.Axia.Infrastructure` | LDAP providers, `DefaultLdapProviderFactory`, EF Core `AppDbContext` and `AxiaDatabase`, AES-GCM encryption, JWT issuing, the bulk job worker | Application, Domain, Contracts |
| `Nelsar.Axia.Migrations.PostgreSql`, `.Sqlite` | Each database's migrations; see [Database migrations](#database-migrations) | Infrastructure |
| `Nelsar.Axia.Api` | Controllers, middleware (correlation id, provider header, exception handler), CORS, rate limiting, Serilog setup, startup | all |

`LayerBoundaryTests` enforces these.

### A request's path

```
HTTP ─► CorrelationIdMiddleware ─► CORS ─► rate limiter ─► JWT auth ─► ProviderHeaderMiddleware
     ─► controller ([Authorize(Policy = permission)]) ─► module (IUsersModule …) ─► MediatR
     ─► LoggingBehaviour ─► AuditBehaviour ─► AuthorizationBehaviour ─► ValidationBehaviour
     ─► handler ─► PrivilegeGuard (for writes to accounts/groups) ─► ILdapService ─► provider ─► directory
```

- **Authorization** is checked twice on purpose: the controller policy, and
  `AuthorizationBehaviour` from the request's `IRequirePermission.RequiredPermission`. The second is
  the one that counts; it also covers requests not coming through HTTP (queued bulk jobs, run as
  the person who queued them).
- **Audit** sits outside authorization, so refused attempts are recorded. Commands opt in with
  `IAuditedCommand`; a test requires every command to.
- **Errors** are domain exceptions (`DomainValidationException` → 400, `DomainNotFoundException` →
  404, `DomainConflictException` → 409, `PermissionDeniedException` → 403,
  `DirectoryUnavailableException` → 502, `FeatureNotImplementedException` → 501), mapped centrally
  by `GlobalExceptionHandler`. Controllers do not catch.

### Directory access

`ILdapService` (Application) → `ILdapProviderFactory` picks the provider for the request (header,
session, cache) → `StandardLdapProvider` (Novell) or `ActiveDirectoryProvider`
(System.DirectoryServices). Everything directory-specific lives in the providers: filters, escaping,
attribute names, password write mode, paging.

### Frontend

React 19 + TypeScript (strict) + Vite. Logic in hooks, markup in presentational components,
wiring in containers. DTOs in `src/api/dto.ts` mirror `Nelsar.Axia.Contracts` by hand.
Details: [frontend/README.md](https://github.com/Nelsar/Axia/blob/main/frontend/README.md).

## Building and testing

What CI runs:

```bash
dotnet build Nelsar.Axia.sln -warnaserror
dotnet test
npm --prefix frontend run build      # typecheck + production build
```

The build must be warning-free. CI also runs the Docker quick start end to end, CodeQL, and fails
on dependencies with known high-severity advisories.

Tests (xUnit, ~780) cover, beyond units:

| Area | Examples |
| --- | --- |
| Architecture | `LayerBoundaryTests` |
| Authorization of every use case | `PermissionAuthorizationTests`, `UseCaseAuthorizationTests`, `BulkPermissionTests`, `PrivilegeGuardTests` |
| Directory isolation | `MultiDomainIntegrationTests`, `SelectableProvidersTests`, `DefaultLdapProviderFactoryTests` |
| Security settings | `DeploymentHardeningTests`, `CorsPolicyTests`, `ReverseProxyOptionsTests`, `DirectoryTlsTests`, `JwtSettingsTests` |
| What reaches logs | `LogMaskingTests`, `ObservabilityTests`, `SecretAndFilterTests` |
| HTTP behaviour | `*ControllerIntegrationTests` (via `ApiTestFactory`, environment `Testing`) |
| Translations | `ResourceMessageTests` |
| Databases: the provider setting, each one's migrations, moving between them | `DatabaseTests` (SQLite), `PostgreSqlTests` (a real server; skipped unless `AXIA_TEST_POSTGRES` names one, which CI does) |
| Active Directory against a live one: accounts and groups created as AD's own, sign-in, password policy, protection without `adminCount` | `SambaAdTests` (the Samba DC in `deploy/dev/samba`; skipped unless `AXIA_TEST_SAMBA` names it, which CI does), `ProtectedSecurityIdentifiersTests` |

To run the PostgreSQL tests locally, against the server in `deploy/dev` or any other the account
may create databases on:

```bash
AXIA_TEST_POSTGRES="Host=localhost;Username=nelsar_axia;Password=nelsar-axia-dev;Database=nelsar_axia" dotnet test --filter PostgreSqlTests
```

And the Samba tests, against the domain controller in `deploy/dev` (see
[Local test directory](local-directory.md#samba-ad)), by `127.0.0.1` rather than `localhost`,
which may resolve to `::1` first:

```bash
AXIA_TEST_SAMBA=127.0.0.1:1636 AXIA_TEST_SAMBA_CA=samba-ca.crt dotnet test --filter SambaAdTests
```

The integration tests read `Nelsar.Axia.Api/appsettings.json` if present. One test expects
`Ldap:Server` to be unset; see [Troubleshooting → Tests](troubleshooting.md#tests).

There are no tests against a live directory; LDAP is faked at the provider boundary. The
[local test directory](local-directory.md) is for manual and exploratory testing.

### The Windows installer

`deploy/windows` builds the MSI described in [Windows installer](installation-windows.md), with
[WiX 5](https://docs.firegiant.com/wix/), which `dotnet build` fetches. On Windows only, and not
part of `Nelsar.Axia.sln`. From the repository root, for `x64` or `arm64`:

```powershell
cd frontend; $env:VITE_API_SAME_ORIGIN = "true"; npm ci; npm run build; cd ..
dotnet publish Nelsar.Axia.Api -c Release -r win-x64 --self-contained -p:AxiaConsoleDist=../frontend/dist -o artifacts/publish/win-x64
dotnet build deploy/windows/Nelsar.Axia.Installer.wixproj -c Release -p:Platform=x64 -p:AxiaPublishDir=artifacts/publish/win-x64 -o artifacts/msi
```

`artifacts/msi/Nelsar.Axia-<version>-x64.msi` is the result, versioned from
`Directory.Build.props`. The build stops if the platform, the publish or the console in it is
missing.

What the package does on the machine is checked by `deploy/windows/test-install.ps1`, which the
*Installers* workflow runs on every pull request touching the API or the installer: install with
settings, the service running as `NT SERVICE\NelsarAxia`, the console and API answering, the
settings file closed to everyone else, an upgrade keeping the keys, an uninstall keeping the data.
It installs a service for real, so run it yourself only on a machine you can throw away.

The settings file is written by the API itself — `Nelsar.Axia.Api init-settings <path>
[Section:Key=value]…`, which the package runs as a custom action — so what it writes is unit
tested in `SettingsFileTests` rather than in WiX.

## Database migrations

The model — `AppDbContext` and the configurations under `Persistence/Configurations` — is one, and
does not know which database it is on. The migrations are per database, because a migration is SQL
for one engine: `Nelsar.Axia.Migrations.PostgreSql/Migrations` and
`Nelsar.Axia.Migrations.Sqlite/Migrations`. `AxiaDatabase` picks the set that `Database:Provider`
names, and the API applies it at startup.

A change to the model needs a migration **in each**, under the same name. Each project is its own
startup project, with a `DesignTimeContextFactory` that knows its database, so nothing needs to be
running:

```bash
dotnet tool install -g dotnet-ef        # once
dotnet ef migrations add <Name> --project Nelsar.Axia.Migrations.PostgreSql --startup-project Nelsar.Axia.Migrations.PostgreSql
dotnet ef migrations add <Name> --project Nelsar.Axia.Migrations.Sqlite --startup-project Nelsar.Axia.Migrations.Sqlite
```

`EachDatabasesMigrations_DescribeTheModel` fails when either set is behind the model. Read what was
generated: the two will differ where the engines do. Data migrations written as SQL have to be
written for each.

A migration that changes behaviour for existing databases (as `RevokeAccidentalDefaultGrants` and
`AddProviderSecurity` did) needs an upgrade note in the README.

### Another database

`DatabaseProvider` lists the databases. To add one: its EF Core provider package in
Infrastructure, a value in the enum and a case in `AxiaDatabase.UseAxiaDatabase`, a
`Nelsar.Axia.Migrations.<Name>` project made like the other two with its own
`DesignTimeContextFactory`, referenced from the API and copied in the `Dockerfile`, and its initial
migration. Anything one engine needs and another must not have goes in `AppDbContext.OnModelCreating`,
behind `Database.Is…()`, as the SQLite audit timestamp and PostgreSQL's UTC conversion do.
`copy-database` moves an existing deployment onto it.

## Adding things

### An endpoint

1. A request/response type in `Contracts` if it crosses the boundary.
2. A command or query in `Application/Features/<Area>`, `internal`, implementing
   `IRequirePermission` (and `IAuditedCommand` for a change), with a FluentValidation validator.
3. A module method in `Application/Modules`, and a controller action with
   `[Authorize(Policy = Permissions.X)]`.
4. Authorization tests (refused when signed out, refused without the permission, the refusal does
   not name it), and behaviour tests.
5. User-facing text in the `.resx` resources, all languages.
6. The DTO in `frontend/src/api/dto.ts` and a method in `frontend/src/api/index.ts`.
7. This documentation: [api.md](api.md) and [features.md](features.md).

### A permission

Add it to `Domain/Authorization/Permissions.cs` (and `All`), decide its default grants in
`DbInitializer.DefaultGrants`, and remember that existing databases do not get new default grants
automatically: it needs a migration or an upgrade note. Update [access-control.md](access-control.md).

### A setting

An options class with a `SectionName`, validation (`IValidateOptions`, `ValidateOnStart`) that
names the key and the fix, the `.example` file, and [configuration.md](configuration.md).

### A language

One `frontend/src/lib/i18n/messages.<code>.ts` (typed against English, so a missing key fails
`tsc`) and three `.resx` files (`ApplicationMessages`, `ApiMessages`, `DomainMessages`).

## Conventions

The ones a test enforces are in
[CONTRIBUTING.md](https://github.com/Nelsar/Axia/blob/main/CONTRIBUTING.md#conventions-worth-knowing-before-you-write).
The rest, briefly:

- No user-facing text in code; no logic that compares translated text.
- Comments and commit messages in English; commit messages say why.
- No real host names, domains, logins or passwords anywhere in the repository, tests or comments.
  Use `example.com` / `example.org`.
- CSS: logical properties only (`margin-inline-start`, not `margin-left`), for right-to-left
  languages.
- A behaviour change comes with a test that fails without it.
