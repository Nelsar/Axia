# Nelsar.Axia — web console and REST API for Active Directory and LDAP

**Nelsar.Axia** is a self-hosted web interface and REST API for managing **Active Directory**,
**OpenLDAP** and other LDAP directories from the browser: users, groups, organizational units (OUs)
and computer accounts, across several directories from one instance — with role-based access
mapped from directory groups, an audit log and bulk user provisioning. A browser-based alternative
to ADUC/RSAT and phpLDAPadmin, with an OpenAPI-documented API for automation. Apache 2.0.

![The users screen](https://raw.githubusercontent.com/Nelsar/Axia/main/docs/screenshots/users.png)

## Images

| Image | What it is |
| --- | --- |
| `nelsar/axia-api` | The REST API (ASP.NET Core, .NET 10). Listens on `8080`. |
| `nelsar/axia-console` | The web console (React), served by nginx on `8080`, which proxies `/api/` to `http://api:8080`. |

Both are built for `linux/amd64` and `linux/arm64` from each release's tag, with build provenance
and an SBOM, and are the same images as `ghcr.io/nelsar/axia-api` and `ghcr.io/nelsar/axia-console`.

| Tag | Points at |
| --- | --- |
| `x.y.z` | that release, always. Pin this — or the digest — in production. |
| `x.y` | the newest patch release of `x.y`. |
| `latest` | the newest release. |

## Try it

The quick start runs the API, the console and an OpenLDAP with demo accounts:

```bash
git clone https://github.com/Nelsar/Axia.git
cd Axia
docker compose up
```

Then open <http://localhost:8080>. The demo logins are listed in the
[README](https://github.com/Nelsar/Axia#quick-start).

## Documentation

- [Installation](https://nelsar.github.io/Axia/installation/)
- [Production deployment](https://nelsar.github.io/Axia/production/) — the images, their
  configuration, TLS and LDAPS
- [Configuration reference](https://nelsar.github.io/Axia/configuration/)
- [Source, issues and releases](https://github.com/Nelsar/Axia)
