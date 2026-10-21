# Nelsar.Axia documentation

Nelsar.Axia is a self-hosted web console and REST API for managing Active Directory, OpenLDAP and
other LDAP directories: users, groups, organizational units and computer accounts, with roles
mapped from directory groups, an audit log and bulk provisioning.

The [README](https://github.com/Nelsar/Axia#readme) is the short version. This folder is the long
one: every feature, every setting, and the details that only matter once you run it against a real
directory.

## Start here

| If you want to… | Read |
| --- | --- |
| See it working in five minutes | [Installation → Quick start](installation.md#path-1-docker-quick-start) |
| Run it from source against your own directory | [Installation → From source](installation.md#path-2-from-source-against-your-directory) |
| Develop against a realistic directory on your machine | [Local test directory](local-directory.md) |
| Install it on Windows as a service | [Windows installer](installation-windows.md) |
| Put it in front of real users | [Production deployment](production.md) |

## Reference

| Page | What it covers |
| --- | --- |
| [Overview](overview.md) | What the application is made of and the concepts the rest of the documentation uses: directories, sessions, roles, permissions, entry ids. |
| [Installation](installation.md) | Requirements and dependencies, and the three ways to get a running instance. |
| [Windows installer](installation-windows.md) | The MSI: a Windows service with no Docker, unattended installs, upgrades, catalogue deployment. |
| [Local test directory](local-directory.md) | The development OpenLDAP in `deploy/dev`: an AD-shaped domain with ~1,500 accounts, ~2,000 computers and ~110 groups. |
| [Production deployment](production.md) | Container image, secrets, reverse proxy and TLS, the bind account, persistence, upgrades, a checklist. |
| [Configuration reference](configuration.md) | Every setting, its default, and what the API refuses to start with. |
| [Directories](directories.md) | Active Directory and OpenLDAP, connection security, several directories at once, failover, what is read and what is written. |
| [Access control](access-control.md) | Roles, the permission catalogue, role mappings, protected accounts and groups. |
| [Features](features.md) | Users, bulk operations, the login generator, groups, organizational units, computers, audit log, rate limits, the console. |
| [REST API](api.md) | Conventions — authentication, headers, paging, errors — and every endpoint. |
| [Operations](operations.md) | Logging and log shipping, the database and migrations, caches, backups. |
| [Troubleshooting](troubleshooting.md) | Startup refusals, sign-in failures and odd listings, with their causes. |
| [Development](development.md) | Architecture, building, testing, and the conventions the tests enforce. |

## Elsewhere in the repository

- [SECURITY.md](https://github.com/Nelsar/Axia/blob/main/SECURITY.md) — the trust model, known
  limitations, and how to report a vulnerability. Read the known limitations before deploying.
- [CONTRIBUTING.md](https://github.com/Nelsar/Axia/blob/main/CONTRIBUTING.md) — how to run what CI
  runs, and the conventions a pull request is held to.
- [ROADMAP.md](https://github.com/Nelsar/Axia/blob/main/ROADMAP.md) — what is planned, and what is
  not done yet.
- [frontend/README.md](https://github.com/Nelsar/Axia/blob/main/frontend/README.md) — the console's
  own structure.
