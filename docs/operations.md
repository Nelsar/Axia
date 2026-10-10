# Operations

Running an instance: logs, the database, caches, backups.

## Logging

Structured logs through Serilog. Sinks are switched under `Observability`
([reference](configuration.md#observability)).

| Sink | Default | Format |
| --- | --- | --- |
| Console (stdout) | on | compact JSON; readable lines with `Console:Json=false` |
| File | on | compact JSON, `logs/axia-YYYYMMDD.json`, 14 days, 128 MB per file |
| Logstash (HTTP input) | off | JSON batches |
| Grafana Loki | off | push API with static labels |

### What is in an event

Every event carries `Application`, the machine name, and during a request the `CorrelationId`. One
summary event is written per HTTP request (method, path, status, duration). Use cases log their
start, outcome and duration, and every authorisation decision.

### What is kept out

- **Secrets**: properties whose names look like a secret (`password`, `secret`, `token`, `key`, …)
  are redacted before reaching any sink.
- **Accounts**: logins and DNs are masked in the diagnostic log (`a***@example.org`). The audit log
  records actors in full; that is its purpose.
- **SQL**: EF Core's statements are held at Warning.

### Following one request

Every response has an `X-Correlation-ID` header. Search the logs for it:

```bash
grep '"CorrelationId":"8de46f75c14170e54aef7a3b7da50aae"' logs/axia-*.json | jq .
```

In Kibana: `CorrelationId:"8de46f75…"`. In Loki: `{app="nelsar-axia"} |= "8de46f75…"`.

A client can send its own id in `X-Correlation-ID` (short, alphanumeric) to tie the API's logs to
its own.

### Shipping

```bash
# ELK: either Filebeat tailing logs/, or straight to Logstash's HTTP input
Observability__Logstash__Enabled=true
Observability__Logstash__Uri=http://logstash:8080
#   input { http { port => 8080 codec => json } }

# Grafana Loki
Observability__Loki__Enabled=true
Observability__Loki__Uri=http://loki:3100
Observability__Loki__Labels__app=nelsar-axia
Observability__Loki__Labels__env=prod
```

The shippers buffer in memory and drop the oldest events rather than slowing the API when the
target is down. Keep the file sink on as the fallback.

### Log levels

`Serilog:MinimumLevel` (see [Configuration](configuration.md#serilog)). For a closer look at LDAP
traffic, raise `Nelsar.Axia` to `Debug`:

```bash
Serilog__MinimumLevel__Override__Nelsar.Axia=Debug
```

## Warnings worth alerting on

| Message | Meaning |
| --- | --- |
| `Directory … is reached over plain LDAP because its security is set to None` | Passwords crossing the network in clear. |
| `Role mapping … names its group as '…' and grants … to nobody` | Legacy mapping by name, ignored. |
| `Error enumerating LDAP users/computers/… after N entries` | A listing was cut short; it is reported as incomplete. Usually a server size or time limit. |
| `Request named provider …, which is neither an active row nor a configured section` | A client is asking for a directory that does not exist. |
| `No available LDAP endpoints found for …` | Every host of a directory is unreachable. |
| `… refused after …` (Warning) with a permission | Someone attempted something they may not; see the audit log. |
| `Nelsar.Axia.Api terminated unexpectedly` (Fatal) | Startup failed; the exception says why. |

## The database

SQLite, at `ConnectionStrings:DefaultConnection` (default `nelsar-axia.db` in the working
directory).

| Table | Holds |
| --- | --- |
| `Providers` | Directories added through the API, with encrypted bind passwords |
| `RoleMappings` | Group → role |
| `RolePermissions` | Role → permission |
| `RateLimitSettings` | Per-policy limits |
| `AuditEvents` | The audit log (indexed by time) |
| `UserLookups` | id → address map, so a user id can be resolved back to its entry |
| `__EFMigrationsHistory`, `__EFMigrationsLock` | Applied migrations, and the lock taken while applying them |
| `LdapConfigurations`, `LdapAttributeMappings` | Legacy. One row is written on first start; nothing reads it. |

At every start the API, in order:

1. Validates `Jwt`, `Encryption`, `Ldap` and `RoleMappings` settings.
2. Applies pending migrations, under SQLite's migration lock.
3. Seeds what is missing: role mappings from `RoleMappings:GroupDns` (only on an empty table),
   missing rate-limit policies, default role permissions (only on an empty table), and the unused
   legacy `LdapConfigurations` row.
4. Converts global mappings still written by name to the configured DNs.
5. Logs every remaining mapping by name.
6. Refuses to continue without an Admin mapping by DN.

Seeding never overwrites what an administrator changed.

### Backup and restore

The database is one file (plus `-wal`/`-shm` while the API runs). Back it up consistently with
SQLite's own backup:

```bash
sqlite3 /data/nelsar-axia.db ".backup '/backups/nelsar-axia-$(date +%F).db'"
```

or stop the API and copy the file. Back up `Encryption:Key` alongside: without it, the stored bind
passwords in the backup are unreadable.

To restore, stop the API, put the file back, start it. Migrations bring an older backup forward.

The directory's own data is not in this database; back up the directory by its own means.

### Audit log retention

There is no automatic pruning. The table grows by one row per sign-in and per change. To trim it:

```bash
sqlite3 /data/nelsar-axia.db "DELETE FROM AuditEvents WHERE Timestamp < '2025-01-01'; VACUUM;"
```

Export first if you must keep the history.

## Caches

Everything the API caches, and for how long. A change made outside the API (directly in the
database, or in the directory) is visible after at most this delay.

| What | TTL |
| --- | --- |
| A caller's permissions (from group membership and mappings) | 30 s |
| Directory resolution (which host of which directory to use) | 30 s; failures 5 s |
| Rate-limit settings | 30 s |
| User id → entry lookups | 30 min sliding |

There is no cache of directory objects: lists and lookups always ask the directory.

## Capacity

- Directory-wide listings read the whole directory (paged at `UserSearchPageSize`) and page the
  result for the client. With tens of thousands of entries a listing takes seconds; each page
  request repeats the read.
- Bulk jobs run one at a time in a background worker, row by row.
- A single instance; scale vertically.

## Restarting

Restarting is safe: tokens stay valid (they are signed, not stored), settings are in the database.
**Queued bulk jobs are lost**: their status endpoints answer `404` afterwards. Let running jobs
finish first.
