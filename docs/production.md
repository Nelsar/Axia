# Production deployment

How to run Nelsar.Axia for real users. Read
[SECURITY.md](https://github.com/Nelsar/Axia/blob/main/SECURITY.md), especially the known
limitations, first: this instance holds a credential that can change your directory.

## Shape of a deployment

```
            HTTPS                         HTTP (private)                LDAPS
 browser ─────────► reverse proxy ──┬──► console (nginx, static) 
                    (TLS, :443)     └──► API (:8080) ──────────────────────────► directory
                                              │
                                              └── /data  (SQLite)   /app/logs
```

- **One API instance.** SQLite and in-memory bulk jobs make it single-instance. Run it with a
  restart policy rather than replicas.
- **Console and API on one origin** (`https://axia.example.com/` and `…/api/`), so the browser
  needs no CORS. That is how the quick start's nginx is built, and the simplest to get right.
- **TLS at the proxy**, plain HTTP inside, the proxy named in `ReverseProxy:KnownProxies`.
- **LDAPS to the directory**, with its certificate verified.

## Images

Each release publishes both images to GitHub Container Registry, for `linux/amd64` and
`linux/arm64`, built from the release's tag. The newest version is on the
[releases page](https://github.com/Nelsar/Axia/releases).

```bash
docker pull ghcr.io/nelsar/axia-api:x.y.z
docker pull ghcr.io/nelsar/axia-console:x.y.z
```

| Tag | Points at |
| --- | --- |
| `x.y.z` | that release, always. Pin this — or the digest — in production. |
| `x.y` | the newest patch release of `x.y`. |
| `latest` | the newest release. |

Pinned, an upgrade happens when you decide on it, after reading the
[changelog](https://github.com/Nelsar/Axia/blob/main/CHANGELOG.md): before 1.0 a minor version may
need action on upgrade, and its entry says what. Each image carries its build provenance and an
SBOM; `docker buildx imagetools inspect ghcr.io/nelsar/axia-api:x.y.z` lists them.

To build them yourself instead, from a commit you have reviewed — both Dockerfiles are in the
repository:

```bash
docker build -t nelsar-axia-api:$(git rev-parse --short HEAD) .
docker build -t nelsar-axia-console:$(git rev-parse --short HEAD) frontend
```

| Image | Base | Listens | Notes |
| --- | --- | --- | --- |
| API | `mcr.microsoft.com/dotnet/aspnet:10.0` | `8080` | `ASPNETCORE_ENVIRONMENT` defaults to Production. No `appsettings.json` inside; configure through the environment or a mounted file. |
| Console | `nginxinc/nginx-unprivileged:1.27-alpine` | `8080` | Built with `VITE_API_SAME_ORIGIN=true`: it calls the API on its own origin. Its [`nginx.conf`](https://github.com/Nelsar/Axia/blob/main/frontend/nginx.conf) proxies `/api/` to `http://api:8080` — so on the console's network the API has to answer to the name `api`, or a `nginx.conf` of your own has to be mounted over `/etc/nginx/conf.d/default.conf` — and sends a `Content-Security-Policy` that admits nothing but its own origin (no inline script, no script, style, font or API from elsewhere), `X-Frame-Options: DENY`, `X-Content-Type-Options`, `Referrer-Policy`, `Cross-Origin-Opener-Policy`, `Permissions-Policy` and `Strict-Transport-Security` (effective once TLS is terminated in front). |

If the console is served from a different origin than the API, build it without
`VITE_API_SAME_ORIGIN` (it then defaults to `http://localhost:5063`, changeable on the sign-in
screen) and list its origin in `Cors:AllowedOrigins`.

## Configuration

A minimal production environment for the API:

```bash
ASPNETCORE_ENVIRONMENT=Production

# secrets: from your secret store, never from a file in the image
Jwt__Key=…                      # openssl rand -base64 48
Encryption__Key=…               # openssl rand -base64 32; keep it, see below
Ldap__AdminPassword=…

Ldap__Server=dc01.corp.example.com
Ldap__BaseDn=DC=corp,DC=example,DC=com
Ldap__AdminUsername=svc-axia@corp.example.com
# Ldap__Security=Ldaps          (default)
Ldap__TrustedCaCertificatePath=/run/secrets/corp-ca.pem   # if the CA is not in the image's trust store

RoleMappings__GroupDns__Admin=CN=Axia-Admins,OU=Groups,DC=corp,DC=example,DC=com
RoleMappings__GroupDns__UserEditor=CN=Axia-Helpdesk,OU=Groups,DC=corp,DC=example,DC=com

ConnectionStrings__DefaultConnection=Data Source=/data/nelsar-axia.db
Observability__File__Path=/app/logs/axia-.json

ReverseProxy__KnownProxies__0=10.0.0.7
Jwt__Issuer=axia.example.com
Jwt__Audience=axia.example.com
```

Everything is in the [Configuration reference](configuration.md).

### Secrets

| Secret | Notes |
| --- | --- |
| `Jwt:Key` | Anyone holding it can mint a token for any account. Rotating it signs everyone out, which is harmless. |
| `Encryption:Key` | Encrypts the bind passwords of directories stored in the database. **Losing or changing it makes every stored bind password unreadable**; each directory then has to be given its password again. Back it up with the database. |
| `Ldap:AdminPassword` | The bind account. See below. |
| Bind passwords of other directories | Entered through the API/console; stored encrypted. |

Use your platform's secret mechanism (Docker/Kubernetes secrets, a vault agent writing environment
variables). The quick start shows one way: an `init` container writes keys into a volume, and the
entrypoint sources them just before starting, so they never appear in `docker inspect`.

The API refuses to start with a missing, short or placeholder key.

### Persistence

| Path in the container | Holds | Lose it and |
| --- | --- | --- |
| `/data` (via `ConnectionStrings`) | SQLite: directories, role mappings, role permissions, rate limits, audit log | settings revert to defaults from configuration; **the audit log is gone** |
| `/app/logs` (via `Observability:File:Path`) | rolling JSON logs | logs are gone (unless shipped) |

Mount both as volumes. The image does not declare them.

The API runs as the .NET images' unprivileged user, uid **1654**, not as root. A named volume on
either path starts out owned by it. A host directory, or a volume created by a version that ran as
root, has to be handed over first:

```bash
sudo chown -R 1654 /srv/nelsar-axia/data /srv/nelsar-axia/logs
```

The quick start's `init` container does this for its own volumes on every start.

## Reverse proxy and TLS

Any proxy works. The rules:

1. Terminate TLS at the proxy; leave `Https:RedirectToHttps` off.
2. Send `X-Forwarded-For` and `X-Forwarded-Proto`, and list the proxy's address in
   `ReverseProxy:KnownProxies` (or its network in `KnownNetworks`). Otherwise all users share one
   rate-limit bucket and the audit log records the proxy's address.
3. Proxy `/api/` to the API and everything else to the console (or serve the console's static
   files directly).

nginx in front of both containers:

```nginx
server {
    listen 443 ssl http2;
    server_name axia.example.com;
    ssl_certificate     /etc/ssl/axia.crt;
    ssl_certificate_key /etc/ssl/axia.key;

    location /api/ {
        proxy_pass http://axia-api:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
    location / {
        proxy_pass http://axia-console:8080;
    }
}
```

The API sends HSTS outside Development. Swagger is off outside Development.

If the API itself must hold the certificate (no proxy), configure Kestrel
(`ASPNETCORE_URLS=https://+:8443` and a certificate) and set `Https:RedirectToHttps=true`.

## The bind account

Every operation is performed with `Ldap:AdminUsername`. Its rights in the directory are the ceiling
of what the application can do, for everyone who uses it, so give it **only what the instance
needs**. Never Domain Admin, never the OpenLDAP root DN.

### Active Directory

A dedicated service account (e.g. `svc-axia`), not interactive, with a long random password. Delegate
on the OUs the application manages (*Delegate Control* in ADUC, or `dsacls`), choosing only what
you use:

| Feature | Delegation on the managed OUs |
| --- | --- |
| Read anything | default *Authenticated Users* read is enough |
| Edit users | Write `displayName`, `mail` on user objects |
| Reset passwords | *Reset Password* on user objects |
| Create / delete users | Create/Delete *User* (and *inetOrgPerson*) objects |
| Groups | Write `member`, `description`; Create/Delete *Group* objects |
| Units | Create/Delete *organizationalUnit* objects |
| Computers | Create/Delete *Computer* objects; Write `userAccountControl`, `description`, `dNSHostName` |
| Moving entries | Delete in the source OU and Create in the target OU |

Protected accounts (`adminCount=1`) are under AdminSDHolder, which overrides delegation; the
application refuses to touch them unless the caller holds every permission here, and even then the
bind account needs explicit rights on them.

The account must be able to read the root DSE (it can by default): the application reads it to
choose how to set passwords.

### OpenLDAP

A dedicated entry, e.g. `cn=axia,ou=services,dc=example,dc=org`, and ACLs giving it write access
only where needed, for example:

```
olcAccess: to dn.subtree="ou=people,dc=example,dc=org"
  by dn.exact="cn=axia,ou=services,dc=example,dc=org" write
  by self read
  by users read
  by * none
olcAccess: to dn.subtree="ou=groups,dc=example,dc=org"
  by dn.exact="cn=axia,ou=services,dc=example,dc=org" write
  by users read
  by * none
```

Plus `auth` access to `userPassword` for anonymous binds (sign-in) and the Password Modify extended
operation (on by default). Mind the server's size limit for this account
(`olcLimits: dn.exact="cn=axia,…" size.prtotal=unlimited`) if your directory has more entries than
the limit.

Add the groups that make someone an administrator to `DirectoryProtection:ProtectedGroups`.

## Running more than one directory

Add directories through the console (*LDAP providers*) or `POST /api/v1/providers`. Each has its
own bind account and password (stored encrypted), and its own security setting. Use LDAPS for all
of them. Role mappings can be scoped to one directory. See [Directories](directories.md#several-directories).

## Upgrading

1. Read the upgrade notes in the [README](https://github.com/Nelsar/Axia#readme) for anything
   between your version and the new one. Some upgrades need a setting before the new build will
   start (for example `RoleMappings:GroupDns:Admin`).
2. **Back up the database** (see [Operations](operations.md#backup-and-restore)).
3. Deploy the new image. Migrations are applied automatically at startup, under a lock, before the
   API accepts requests.
4. Watch the startup log. A refusal names the setting to change.

There is no downgrade path for the database other than restoring the backup.

## Checklist

Before letting users in:

- [ ] Built from a reviewed commit; `dotnet test` passes.
- [ ] `ASPNETCORE_ENVIRONMENT=Production` (no Swagger, HSTS on).
- [ ] `Jwt:Key` and `Encryption:Key` generated, stored as secrets, `Encryption:Key` backed up.
- [ ] `Jwt:Issuer`/`Audience` name this deployment; `Jwt:ExpiresInMinutes` as short as tolerable.
- [ ] Every directory uses `Ldaps` or `StartTls`, with certificates that verify.
- [ ] The bind account is a dedicated, least-privilege account, not a Domain Admin or root DN.
- [ ] `RoleMappings:GroupDns:Admin` names a small, real group, by DN.
- [ ] On OpenLDAP, `DirectoryProtection:ProtectedGroups` lists the admin groups.
- [ ] `Auth:AllowSelfRegistration` is `false`.
- [ ] `Cors` is empty (same origin) or lists exactly the console's origin; `AllowAnyOrigin` is `false`.
- [ ] TLS at the proxy; `ReverseProxy:KnownProxies` names it.
- [ ] `/data` and the log directory are volumes; backups scheduled.
- [ ] Logs reach somewhere someone looks (file + shipper).
- [ ] Rate limits reviewed (`/api/v1/rate-limit-settings`).
- [ ] The *not in scope* list in the README and SECURITY.md's known limitations read and accepted:
      no token revocation, no revocation checks on directory certificates, single instance.
