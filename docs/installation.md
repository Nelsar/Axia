# Installation

Three ways to get a running instance, from quickest to most real:

1. [Docker quick start](#path-1-docker-quick-start): API, console and a demo OpenLDAP, one
   command. For trying it out.
2. [From source, against your directory](#path-2-from-source-against-your-directory): the API and
   console run from the working tree, pointed at an AD or LDAP server you already have.
3. [From source, against the local test directory](#path-3-from-source-against-the-local-test-directory):
   the same, against a throwaway AD-shaped OpenLDAP in Docker. For development.

Production is a separate page: [Production deployment](production.md).

## Requirements

### What you need, by path

| | Quick start | From source | Local test directory |
| --- | --- | --- | --- |
| Docker Engine with Compose v2 | **yes** | — | **yes** |
| .NET SDK 10 | — | **yes** | **yes** |
| Node.js 22 (or 20.19+) and npm | — | **yes** | **yes** |
| An LDAP / AD server you can reach | — | **yes** | — (provided) |
| OpenLDAP client tools (`ldapsearch`, `ldapadd`) | — | optional | optional, for poking at it |
| Python 3 | — | — | only to regenerate test data |
| `openssl` | — | to generate keys | to generate keys |

Versions the project is built and tested with:

| Dependency | Version | Notes |
| --- | --- | --- |
| .NET SDK | 10.0 (10.0.112) | `global.json` is not pinned; any 10.0 SDK should do. |
| ASP.NET Core runtime | 10.0 | For running a published build without the SDK. |
| Node.js | 22 (20.19+ works) | Vite 8 needs 20.19 or 22.12 and later. CI and the console's Docker image build on Node 22. |
| Docker / Compose | Compose v2 (`docker compose`, not `docker-compose`) | |
| OpenLDAP image (quick start, dev) | `osixia/openldap:1.5.0` | |
| nginx image (console container) | `nginxinc/nginx-unprivileged:1.27-alpine` | |

### Operating system

- **Linux, macOS, Windows**: all fine for the API with `ProviderType=StandardLdap`.
- **Windows only**: `ProviderType=ActiveDirectory`. On any other OS the API refuses to start with
  it. `StandardLdap` talks to Active Directory just as well and is the recommended choice.

### Libraries the API depends on

Restored automatically by `dotnet restore`; listed so you know what you are running.

| Package | Used for |
| --- | --- |
| `Novell.Directory.Ldap.NETStandard` 3.6 | LDAP for the `StandardLdap` provider |
| `System.DirectoryServices` (+ `.AccountManagement`) 10.0 | the Windows-only `ActiveDirectory` provider |
| `Microsoft.EntityFrameworkCore.Sqlite` 10.0 | the database |
| `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0 | bearer tokens |
| `MediatR` 12, `FluentValidation` 12 | use-case pipeline and validation |
| `Serilog.AspNetCore` 10, `Serilog.Sinks.Http`, `Serilog.Sinks.Grafana.Loki` | structured logging and shipping |
| `Swashbuckle.AspNetCore` 6.6 | Swagger UI (Development only) |

The console: React 19, TypeScript 7, Vite 8. Nothing else at runtime.

### Network

| From | To | Port | Why |
| --- | --- | --- | --- |
| Browser | console | 5500 (dev) / 8080 (container) | the UI |
| Browser | API | 5063 (dev) | the console calls the API directly in development; in the container setup, nginx proxies `/api/` instead |
| API | directory | 636 (LDAPS, default), 389 (StartTLS or plain), 3268/3269 (Global Catalog, read-only) | every operation |
| API | Logstash / Loki | as configured | only if log shipping is turned on |

The API makes no other outbound connections.

## Path 1: Docker quick start

Needs Docker with Compose v2 and nothing else.

```bash
git clone https://github.com/Nelsar/Axia.git
cd Axia
docker compose up --build
```

The first start builds two images and takes a few minutes. When the API logs `Now listening on`,
open <http://localhost:8080>.

Every demo password is `Axia-Demo-1`:

| Login | Role |
| --- | --- |
| `admin.demo` | Admin |
| `creator.demo` | UserCreator |
| `editor.demo` | UserEditor |
| `user.demo` | User |

What `docker compose up` starts (see [`docker-compose.yml`](../docker-compose.yml)):

| Service | What it does |
| --- | --- |
| `init` | Runs once. Generates a CA and a certificate for the directory, and the API's `Jwt__Key` and `Encryption__Key`, into Docker volumes. Does nothing on later starts. |
| `openldap` | OpenLDAP with the demo content from [`deploy/quickstart/bootstrap.ldif`](../deploy/quickstart/bootstrap.ldif), LDAPS only, not published to the host. |
| `api` | The API, built from the working tree. Reaches the directory over LDAPS and verifies its certificate. Database in the `api-data` volume. |
| `console` | nginx serving the built console on `127.0.0.1:8080`, and proxying `/api/` to the API. The only thing published. |

The port can be changed with `CONSOLE_PORT=9090 docker compose up`.

`docker compose down` stops it; `docker compose down -v` also deletes the directory, its
certificates, the keys and the database.

The quick start's directory is reachable only inside Docker. To run the API from source against a
directory on your machine, use path 3.

## Path 2: From source, against your directory

### 1. Get the code and restore

```bash
git clone https://github.com/Nelsar/Axia.git
cd Axia
dotnet restore
npm --prefix frontend ci
```

### 2. Create the configuration files

The API reads `Nelsar.Axia.Api/appsettings.json` and, in Development,
`appsettings.Development.json` on top of it. Neither is in the repository, because both hold
secrets. Start from the examples:

```bash
cp Nelsar.Axia.Api/appsettings.json.example Nelsar.Axia.Api/appsettings.json
cp Nelsar.Axia.Api/appsettings.Development.json.example Nelsar.Axia.Api/appsettings.Development.json
```

### 3. Generate the two keys

```bash
openssl rand -base64 48   # Jwt:Key: token signing key, at least 32 bytes
openssl rand -base64 32   # Encryption:Key: AES key for stored bind passwords
```

Put the first under `Jwt:Key` and the second under `Encryption:Key`. The API refuses to start
with either missing, too short, malformed, or still the placeholder from the example file.

> Keep `Encryption:Key` once it is in use. Bind passwords of directories stored in the database
> are encrypted with it; a different key makes every one of them unreadable until it is entered
> again.

### 4. Point it at your directory

Edit the `Ldap` section. For Active Directory over LDAPS:

```jsonc
"Ldap": {
  "Server": "dc01.corp.example.com",        // must match a name in the DC's certificate
  "Security": "Ldaps",                       // the default; port 636
  "BaseDn": "DC=corp,DC=example,DC=com",
  "AdminUsername": "svc-axia@corp.example.com",
  "AdminPassword": "…",
  "ProviderType": "StandardLdap"
}
```

For OpenLDAP with StartTLS on 389:

```jsonc
"Ldap": {
  "Server": "ldap.example.org",
  "Security": "StartTls",
  "BaseDn": "dc=example,dc=org",
  "AdminUsername": "cn=axia,ou=services,dc=example,dc=org",
  "AdminPassword": "…"
}
```

If the directory's certificate is issued by an internal CA the machine does not trust, add
`"TrustedCaCertificatePath": "/path/to/ca.pem"`. Certificate verification cannot be turned off.

Every setting is described in the [Configuration reference](configuration.md#ldap). What the bind
account needs to be allowed to do is in [Production → The bind account](production.md#the-bind-account).

### 5. Say who the administrators are

The API will not start until some directory group, named by its full DN, is mapped to the Admin
role:

```jsonc
"RoleMappings": {
  "GroupDns": {
    "Admin":       "CN=Axia-Admins,OU=Groups,DC=corp,DC=example,DC=com",
    "UserCreator": "CN=Axia-Creators,OU=Groups,DC=corp,DC=example,DC=com",
    "UserEditor":  "CN=Axia-Editors,OU=Groups,DC=corp,DC=example,DC=com",
    "UserDeleter": "CN=Axia-Deleters,OU=Groups,DC=corp,DC=example,DC=com"
  }
}
```

Only `Admin` is required. Use the group's real DN; the API checks the format, not that the group
exists. A wrong DN starts fine and leaves nobody able to administer it. See
[Troubleshooting](troubleshooting.md#no-role-mapping-grants-the-admin-role).

On OpenLDAP, also list the groups whose members administer the directory itself, so only an
application Admin can change them (AD marks these on its own):

```jsonc
"DirectoryProtection": { "ProtectedGroups": [ "cn=axia-admins,ou=groups,dc=example,dc=org" ] }
```

### 6. Run the API

```bash
dotnet run --project Nelsar.Axia.Api --launch-profile http
```

The `http` profile sets `ASPNETCORE_ENVIRONMENT=Development` and listens on
<http://localhost:5063>. On first start the API creates the SQLite database
(`nelsar-axia-dev.db` in Development), applies migrations and seeds defaults. Swagger UI is at
<http://localhost:5063/swagger> in Development.

The `https` launch profile runs as **Production**: it does not load `appsettings.Development.json`
and does not serve Swagger.

### 7. Run the console

```bash
npm --prefix frontend run dev
```

Open <http://localhost:5500>. The console calls the API at `http://localhost:5063` by default; the
sign-in screen has an **API address** field to change it. The API admits browsers on any loopback
port without configuration. The field exists on the development server only: the Docker image's
console always calls the API on its own origin.

### 8. Sign in

Sign in with an account from your directory: a login (`uid`, `sAMAccountName`), a
`userPrincipalName`, or an e-mail address, depending on `Ldap:UserSearchFilter`. Members of the
group mapped to Admin get the Admin role. Everyone else gets `User` until more mappings are
added.

## Path 3: From source, against the local test directory

[`deploy/dev`](../deploy/dev) holds an OpenLDAP for development: published on `127.0.0.1:389` in
plain LDAP, with the quick start's demo accounts plus an Active-Directory-shaped domain of around
1,500 accounts, 2,000 computers and 110 groups. [Local test directory](local-directory.md)
describes what is in it.

1. Steps 1–3 of path 2 (code, configuration files, keys).
2. Start the directory:

   ```bash
   docker compose -f deploy/dev/docker-compose.ldap.yml up -d --wait
   ```

   On Linux, if your user is not in the `docker` group, prefix it with `sudo`. The first start
   loads all the data and takes about 40 seconds; `--wait` returns when it is done.

3. In `appsettings.Development.json`, point the API at it:

   ```jsonc
   "Ldap": {
     "Server": "localhost",
     "Port": 389,
     "Security": "None",
     "BaseDn": "dc=example,dc=org",
     "AdminUsername": "cn=admin,dc=example,dc=org",
     "AdminPassword": "adminpassword",
     "UserSearchFilter": "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))",
     "ProviderType": "StandardLdap"
   },
   "RoleMappings": {
     "GroupDns": {
       "Admin":       "cn=axia-admins,ou=groups,dc=example,dc=org",
       "UserCreator": "cn=axia-creators,ou=groups,dc=example,dc=org",
       "UserEditor":  "cn=axia-editors,ou=groups,dc=example,dc=org",
       "UserDeleter": "cn=axia-deleters,ou=groups,dc=example,dc=org"
     }
   },
   "DirectoryProtection": {
     "ProtectedGroups": [ "cn=axia-admins,ou=groups,dc=example,dc=org" ]
   }
   ```

   `Security: None` is plain LDAP. It is acceptable here only because the directory is on the
   same machine; the API logs a warning about it on every connection.

4. Steps 6–7 of path 2. Sign in as `admin.demo` / `Axia-Demo-1`.

## Checking that it works

- `http://localhost:5063/swagger` loads (Development only).
- `POST /api/v1/auth/login` with a directory account answers `200` with a token:

  ```bash
  curl -s -X POST http://localhost:5063/api/v1/auth/login \
       -H 'Content-Type: application/json' \
       -d '{"email":"admin.demo","password":"Axia-Demo-1"}'
  ```

  The field is called `email` but takes any login the search filter matches.
- The console's dashboard shows the signed-in name and role.

If the API stops at startup, the last log line names the setting at fault. See
[Troubleshooting](troubleshooting.md).
