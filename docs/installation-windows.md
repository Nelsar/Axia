# Windows installer

Nelsar.Axia as a Windows service, from one MSI: the API and the console it serves, on one port,
with no Docker, nginx or .NET runtime to install first. For Windows Server 2016 and later, and
Windows 10 and 11.

It installs the way software catalogues expect — silently, with its settings on the command line,
upgradable in place — so it can be deployed by hand, through Group Policy, Configuration Manager,
Intune or any tool that runs `msiexec`.

## Download

Each [release](https://github.com/Nelsar/Axia/releases) carries two packages, built from its tag:

| File | For |
| --- | --- |
| `Nelsar.Axia-x.y.z-x64.msi` | 64-bit Intel and AMD |
| `Nelsar.Axia-x.y.z-arm64.msi` | 64-bit ARM |

The packages are not signed yet, so SmartScreen warns when one is opened from a browser, and a
policy that admits signed installers only will refuse them. What each one does carry is its build
provenance: a statement, kept by GitHub, that this file was built by this repository's workflow from
the release's tag. With the [GitHub CLI](https://cli.github.com/):

```powershell
gh attestation verify Nelsar.Axia-x.y.z-x64.msi --repo Nelsar/Axia
```

## What goes where

| Where | What | On upgrade | On uninstall |
| --- | --- | --- | --- |
| `%ProgramFiles%\Nelsar.Axia\` | The API, the .NET runtime it brings, the console in `wwwroot\` | replaced | removed |
| `%ProgramData%\Nelsar.Axia\appsettings.json` | All settings, including the signing and encryption keys | kept | **kept** |
| `%ProgramData%\Nelsar.Axia\nelsar-axia.db` | The SQLite database, when SQLite is used | kept | **kept** |
| `%ProgramData%\Nelsar.Axia\logs\` | Rolling JSON logs | kept | **kept** |
| Service `NelsarAxia` ("Nelsar.Axia") | Starts automatically, restarts on failure | replaced | removed |

The service runs as its own virtual account, `NT SERVICE\NelsarAxia`, not as `LocalSystem`. It
can write to `%ProgramData%\Nelsar.Axia\` and nowhere else. That folder is closed to everyone but
SYSTEM, Administrators and the service: `appsettings.json` holds the keys that sign every session
and decrypt every stored bind password.

The service is started with `--SettingsFile "%ProgramData%\Nelsar.Axia\appsettings.json"`.
Environment variables still override the file, as described in
[Configuration](configuration.md#how-configuration-is-read).

## Installing

### Interactively

Run the MSI and confirm the elevation prompt. There is no wizard: it installs, creates
`appsettings.json` with new keys, and leaves the service **stopped**, because there is no
directory configured yet. Then [finish the configuration](#finishing-the-configuration).

### Unattended

```powershell
msiexec /i Nelsar.Axia-x.y.z-x64.msi /qn /l*v axia-install.log `
  LDAP_SERVER=dc01.corp.example.com `
  LDAP_BASEDN="DC=corp,DC=example,DC=com" `
  LDAP_USERNAME=svc-axia@corp.example.com `
  ADMIN_GROUP="CN=Axia-Admins,OU=Groups,DC=corp,DC=example,DC=com"
```

| Property | Writes | Default |
| --- | --- | --- |
| `PORT` | `Urls` = `http://+:PORT` | `8080` |
| `LDAP_SERVER` | `Ldap:Server` | — |
| `LDAP_BASEDN` | `Ldap:BaseDn` | — |
| `LDAP_USERNAME` | `Ldap:AdminUsername` | — |
| `ADMIN_GROUP` | `RoleMappings:GroupDns:Admin` | — |
| `DATABASE_PROVIDER` | `Database:Provider` (`Sqlite` or `PostgreSql`) | `Sqlite` |
| `START_SERVICE` | `1` starts the service when the install finishes | not started |

A property that is not given changes nothing, so running the same package again, or a newer one,
without properties keeps the settings as they are. One that is given replaces the setting.

**No secret goes on the command line.** msiexec writes its command line to its log, and the log
is readable by more people than the settings file. The bind password, and a PostgreSQL connection
string, go into `appsettings.json` afterwards — see the next section.

Exit codes are the standard ones: `0` success, `3010` success with a reboot needed (not expected),
`1603` failure — the `/l*v` log says why.

### Finishing the configuration

Open `%ProgramData%\Nelsar.Axia\appsettings.json` in an editor started as administrator and add
what the properties did not cover — at least the bind password:

```json
{
  "Ldap": {
    "Server": "dc01.corp.example.com",
    "BaseDn": "DC=corp,DC=example,DC=com",
    "AdminUsername": "svc-axia@corp.example.com",
    "AdminPassword": "…"
  }
}
```

Every setting in the [Configuration reference](configuration.md) can go in this file. Keys the
installer wrote — `Jwt:Key`, `Encryption:Key`, the database and log paths, `Urls` — are best left
alone; see [What not to change](#what-not-to-change). Then start the service:

```powershell
Start-Service NelsarAxia
```

and open `http://<server>:8080/`. If it does not answer, the newest file in
`%ProgramData%\Nelsar.Axia\logs\` says why; most startup refusals are listed in
[Configuration → What stops startup](configuration.md#what-stops-startup).

The installer does not open the Windows Firewall. To reach the console from other machines:

```powershell
New-NetFirewallRule -DisplayName "Nelsar.Axia" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow
```

### HTTPS

The service speaks plain HTTP. Put it behind a reverse proxy that terminates TLS — IIS with
Application Request Routing, nginx, a load balancer — and name the proxy in
`ReverseProxy:KnownProxies`, as [Production deployment](production.md) describes. Or give Kestrel
a certificate of its own with a `Kestrel:Endpoints` section in the settings file and set
`Https:RedirectToHttps`.

## Upgrading

Install the newer MSI over the older one, the same way. The service is stopped, the binaries
replaced, `appsettings.json` brought up to date — anything missing added, nothing already there
changed — and the service started again. Database migrations run when it starts.

Read the [changelog](https://github.com/Nelsar/Axia/blob/main/CHANGELOG.md) first: before 1.0, a
minor version may need a setting changed, and its entry says which. Installing an older version
over a newer one is refused.

## Uninstalling

From *Apps* / *Programs and Features*, or:

```powershell
msiexec /x Nelsar.Axia-x.y.z-x64.msi /qn
```

The service and `%ProgramFiles%\Nelsar.Axia\` go. **`%ProgramData%\Nelsar.Axia\` stays** — the
keys, the database and its audit trail are not something an uninstall should destroy. Reinstalling
picks them up again. To remove them as well, delete the folder by hand once you are sure.

## Deploying through a catalogue

| | |
| --- | --- |
| Install command | `msiexec /i Nelsar.Axia-x.y.z-x64.msi /qn` and the [properties](#unattended) |
| Uninstall command | `msiexec /x {ProductCode} /qn` |
| Detection | The MSI product code, or the `UpgradeCode` `{79655DA3-9C1A-4CDE-B8EC-50CA96C5DEF3}` with a version — the product code changes with every version, the upgrade code never does |
| Install context | System (per machine); needs administrator rights |
| Restart | None |

**Group Policy** assigns an MSI without properties. The service is installed stopped, with an
empty configuration; put `appsettings.json` in place with a Group Policy Preferences file item or
a startup script and start the service, or deploy through a tool that passes properties.

**Intune**: add the MSI as a *Line-of-business app*, with the properties in the command-line
arguments. Intune detects it by product code.

**Configuration Manager**: a *Windows Installer (\*.msi file)* deployment type, with the install
command above.

## What not to change

- **`Encryption:Key`.** It decrypts the bind passwords of every directory added through the
  console. Changed or lost, each has to be entered again. Back it up with the database.
- **`Jwt:Key`.** Changing it is harmless — everyone is signed out — but there is no reason to.
- **The service account.** The settings folder is open to `NT SERVICE\NelsarAxia` by its SID; a
  service running as anything else cannot read its own settings.

## Building the MSI

See [Development → The Windows installer](development.md#the-windows-installer).
