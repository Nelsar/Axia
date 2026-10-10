# Local test directory

[`deploy/dev`](../deploy/dev) is an OpenLDAP for development and manual testing. It holds enough
data to make listings, paging and search behave the way they do against a real domain, and it is
shaped like Active Directory where that matters to the application.

It is **not** the quick start's directory. The quick start runs everything in Docker and reaches
its directory over LDAPS inside the Docker network. This one is published on your machine so that
an API run from the IDE or `dotnet run` can reach it.

## Starting, stopping, resetting

```bash
docker compose -f deploy/dev/docker-compose.ldap.yml up -d --wait     # start (first start loads data, ~40 s)
docker compose -f deploy/dev/docker-compose.ldap.yml down             # stop, keep data
docker compose -f deploy/dev/docker-compose.ldap.yml down -v          # stop and delete everything
```

On Linux without membership in the `docker` group, prefix each with `sudo`.

The data is loaded only on the **first start of an empty directory**. To pick up a change to any
of the LDIF files, `down -v` and start again.

| | |
| --- | --- |
| Address | `ldap://127.0.0.1:389`, plain LDAP, no TLS |
| Base DN | `dc=example,dc=org` |
| Directory admin | `cn=admin,dc=example,dc=org` / `adminpassword` |
| Config admin (`cn=config`) | `cn=admin,cn=config` / `config` (the image's default) |
| Every account's password | `Axia-Demo-1` |

TLS is off on purpose. The quick start's certificate names the host `openldap`, which a process
outside Docker does not reach it as, and certificate verification in the API cannot be relaxed.
Configure the API with `Ldap:Security=None` for this directory; see
[Installation, path 3](installation.md#path-3-from-source-against-the-local-test-directory).

## What is in it

The files are loaded in name order:

| File | Loaded as | Contents |
| --- | --- | --- |
| [`ad-schema.ldif`](../deploy/dev/ad-schema.ldif) | `40-ad-schema` | AD's `user`, `computer` and `group` classes and the attributes the API uses (`sAMAccountName`, `userPrincipalName`, `userAccountControl`, `dNSHostName`, `operatingSystem`, `operatingSystemVersion`, `groupType`, `managedBy`, `adminCount`, `lastLogonTimestamp`, …), with AD's own OIDs. |
| [`ad-memberof.ldif`](../deploy/dev/ad-memberof.ldif) | `45-ad-memberof` | Points the `memberOf` overlay at `group`/`member`, as AD maintains it. |
| [`../quickstart/bootstrap.ldif`](../deploy/quickstart/bootstrap.ldif) | `50-demo` | The quick start's demo accounts and role groups. |
| [`test-data.ldif`](../deploy/dev/test-data.ldif) | `60-test` | A UserDeleter account and group, more units, and the group shapes the API reads: `groupOfNames`, `groupOfUniqueNames`, `posixGroup`, a nested group. |
| [`bulk-users.ldif`](../deploy/dev/bulk-users.ldif) | `70-bulk` | 300 OpenLDAP-style (`inetOrgPerson`) accounts in `ou=staff`, with a group per department. |
| [`ad-data.ldif`](../deploy/dev/ad-data.ldif) | `80-ad-data` | An AD-shaped domain under `ou=Corp`. See below. |

Totals after a fresh start: about 1,525 accounts, 2,056 computer accounts, 20 printers, 110
groups and 74 organizational units.

### Accounts that sign in with a role

| Login | Role | Group |
| --- | --- | --- |
| `admin.demo` | Admin | `cn=axia-admins,ou=groups,dc=example,dc=org` (protected) |
| `creator.demo` | UserCreator | `cn=axia-creators,ou=groups,…` (`groupOfUniqueNames`) |
| `editor.demo` | UserEditor | `cn=axia-editors,ou=groups,…` |
| `deleter.demo` | UserDeleter | `cn=axia-deleters,ou=groups,…` |
| `user.demo` | User | none |

Every other account signs in as `User`.

### The AD-shaped domain (`ou=Corp`)

```
ou=Corp,dc=example,dc=org
├── ou=Users
│   └── ou=Almaty | Astana | Shymkent | Karaganda
│       └── ou=IT | Finance | Sales | HR | Marketing | Support | Logistics | Legal | Management
├── ou=Disabled Users          leavers, userAccountControl 514
├── ou=Service Accounts        adm.* (adminCount=1) and svc.* accounts
├── ou=Computers
│   ├── ou=Workstations
│   │   └── ou=Almaty | Astana | Shymkent | Karaganda     ALA-WS-00001, ALA-NB-00002, …
│   ├── ou=Servers
│   │   └── ou=Domain Controllers | File | Database | Web | Application | Remote Desktop
│   └── ou=Printers            device entries (RFC 4519), not AD computers
└── ou=Groups
    ├── ou=Security            DL-VPN-Users, DL-Share-*-RW/RO, GG-Workstations-*, GG-Servers, …
    ├── ou=Distribution        All Staff, Managers, Sales Team, … (with mail)
    ├── ou=Departments         GG-<dept>-<office>, GG-<dept> (nested), GG-Office-*
    ├── cn=Domain Admins       adminCount=1
    ├── cn=Enterprise Admins   adminCount=1
    ├── cn=Server Operators    adminCount=1
    ├── cn=Domain Users        every enabled account
    └── cn=Domain Computers    every computer account
```

**Accounts** carry `cn` as the display name (as AD does), `sAMAccountName` (`first.last`, at most
20 characters), `userPrincipalName` and `mail` (`first.last@example.org`), `title`,
`department`, `company`, `employeeID`, `l`, `telephoneNumber`, `mobile`, `manager` and
`lastLogonTimestamp`. About 4% are disabled (`userAccountControl` 514) and sit in
`ou=Disabled Users`.

**Computers** carry `sAMAccountName` with a trailing `$`, `dNSHostName`, `operatingSystem` and
`operatingSystemVersion` (Windows 10/11, Windows Server 2019/2022, Ubuntu, RHEL, macOS),
`userAccountControl` (4096 workstation, 8192 domain controller, +2 when disabled, about 6% are),
`managedBy` (the owner), `location` and `lastLogonTimestamp`.

**Groups** are AD `group` objects with `sAMAccountName` and `groupType` (global, domain-local or
universal; security or distribution), some with `managedBy` or `mail`. They include nested groups
(`GG-<dept>` contains the office groups; `All Staff` contains department groups) and one empty
group, `DL-Empty-Legacy`.

Any of these accounts can sign in by `sAMAccountName`, `userPrincipalName` or `mail`, provided
`Ldap:UserSearchFilter` includes those attributes (the default filter does).

### Where it differs from real Active Directory

The point is to exercise the application, not to emulate AD. Things that behave differently:

- **`userAccountControl` is not enforced.** OpenLDAP stores the flag but does not act on it, so a
  disabled account still binds, and signs in to the application, which leaves that check to the
  directory. Active Directory refuses the bind.
- **Computers carry `sn`.** OpenLDAP's `person` class requires it, AD's does not. Because of
  this, **creating a computer through the API fails against this directory** (`502`, "The directory
  rejected the creation of the entry"): the API writes AD's attribute set, which has no `sn`.
  Listing, editing, enabling, disabling, moving and deleting computers all work.
- **`memberOf` follows only `group`/`member`.** The overlay can follow one class. `memberOf` is
  therefore missing for the `groupOfUniqueNames` groups (`axia-creators`, `it-staff`). The API
  also reads membership from the groups themselves, so roles are unaffected.
- **Passwords are `userPassword`**, set through the LDAP Password Modify operation. AD uses
  `unicodePwd`.
- **No `objectGUID` / `objectSid`**, no `whenCreated`, no Global Catalog.
- **Size limits do not apply**: the API binds as the database's root DN, which OpenLDAP exempts
  from limits.

## Regenerating the data

Both generators are seeded, so they produce the same file every time unless asked otherwise.

```bash
python3 deploy/dev/generate-ad-data.py                                  # 1,200 users, 2,000 computers
python3 deploy/dev/generate-ad-data.py --users 5000 --computers 10000   # bigger
python3 deploy/dev/generate-bulk-users.py --count 500                   # the ou=staff accounts
```

Then `down -v` and `up` to load it. To add a generated file to a running directory instead:

```bash
ldapadd -x -H ldap://127.0.0.1 -D cn=admin,dc=example,dc=org -w adminpassword -c -f deploy/dev/ad-data.ldif
```

The compose healthcheck waits for `cn=Domain Computers,ou=Groups,ou=Corp,dc=example,dc=org`, the
last entry the generator writes, so `--wait` returns only once everything is loaded.

## Useful queries

```bash
# everything disabled, users and computers (AD's bitwise-AND rule works in OpenLDAP too)
ldapsearch -x -LLL -H ldap://127.0.0.1 -D cn=admin,dc=example,dc=org -w adminpassword \
  -b ou=Corp,dc=example,dc=org -E pr=500/noprompt '(userAccountControl:1.2.840.113556.1.4.803:=2)' dn

# one account's groups
ldapsearch -x -LLL -H ldap://127.0.0.1 -D cn=admin,dc=example,dc=org -w adminpassword \
  -b dc=example,dc=org '(sAMAccountName=alexei.rakhimov)' memberOf

# count computers (paged, so it is not cut at the server's size limit)
ldapsearch -x -LLL -H ldap://127.0.0.1 -D cn=admin,dc=example,dc=org -w adminpassword \
  -b dc=example,dc=org -E pr=500/noprompt '(objectClass=computer)' dn | grep -c '^dn:'
```
