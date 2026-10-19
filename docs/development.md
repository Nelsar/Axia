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
| `Nelsar.Axia.Infrastructure` | LDAP providers, `DefaultLdapProviderFactory`, EF Core `AppDbContext` and migrations, AES-GCM encryption, JWT issuing, the bulk job worker | Application, Domain, Contracts |
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

The integration tests read `Nelsar.Axia.Api/appsettings.json` if present. One test expects
`Ldap:Server` to be unset; see [Troubleshooting → Tests](troubleshooting.md#tests).

There are no tests against a live directory; LDAP is faked at the provider boundary. The
[local test directory](local-directory.md) is for manual and exploratory testing.

## Database migrations

EF Core migrations live in `Nelsar.Axia.Infrastructure/Persistence/Migrations` and are applied at
startup. To add one:

```bash
dotnet tool install -g dotnet-ef        # once
dotnet ef migrations add <Name> \
  --project Nelsar.Axia.Infrastructure --startup-project Nelsar.Axia.Api
```

A migration that changes behaviour for existing databases (as `RevokeAccidentalDefaultGrants` and
`AddProviderSecurity` did) needs an upgrade note in the README.

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
