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

PostgreSQL, or SQLite, as `Database:Provider` says, at `ConnectionStrings:DefaultConnection`; see
[Configuration](configuration.md#database-and-connectionstrings). The tables are the same on both.

| Table | Holds |
| --- | --- |
| `Providers` | Directories added through the API, with encrypted bind passwords |
| `RoleMappings` | Group → role |
| `RolePermissions` | Role → permission |
| `RateLimitSettings` | Per-policy limits |
| `AuditEvents` | The audit log (indexed by time) |
| `UserLookups` | id → address map, so a user id can be resolved back to its entry |
| `__EFMigrationsHistory` | Applied migrations. On SQLite, `__EFMigrationsLock` beside it is the lock taken while applying them; PostgreSQL locks the history table itself. |
| `LdapConfigurations`, `LdapAttributeMappings` | Legacy. One row is written on first start; nothing reads it. |

At every start the API, in order:

1. Validates `Database`, `Jwt`, `Encryption`, `Ldap` and `RoleMappings` settings.
2. Applies pending migrations — the database's own set — under the migration lock, so that two
   instances starting at once do not both apply them.
3. Seeds what is missing: role mappings from `RoleMappings:GroupDns` (only on an empty table),
   missing rate-limit policies, default role permissions (only on an empty table), and the unused
   legacy `LdapConfigurations` row.
4. Converts global mappings still written by name to the configured DNs.
5. Logs every remaining mapping by name.
6. Refuses to continue without an Admin mapping by DN.

Seeding never overwrites what an administrator changed.

### Backup and restore

Back up `Encryption:Key` with every backup: without it, the stored bind passwords in the backup
are unreadable. The directory's own data is not in this database; back up the directory by its
own means.

**PostgreSQL**: `pg_dump` while the API runs, `pg_restore` into an empty database to restore.

```bash
pg_dump --format=custom --file="/backups/nelsar-axia-$(date +%F).dump" "host=db.internal dbname=nelsar_axia user=nelsar_axia"
pg_restore --dbname="host=db.internal dbname=nelsar_axia user=nelsar_axia" --no-owner /backups/nelsar-axia-2026-10-20.dump
```

In the quick start: `docker compose exec postgres pg_dump -U nelsar_axia --format=custom nelsar_axia > nelsar-axia.dump`.

**SQLite**: the database is one file (plus `-wal`/`-shm` while the API runs). Back it up
consistently with SQLite's own backup, or stop the API and copy the file:

```bash
sqlite3 /data/nelsar-axia.db ".backup '/backups/nelsar-axia-$(date +%F).db'"
```

To restore either, stop the API, put the database back, start it. Migrations bring an older backup
forward.

### Moving to another database

`copy-database` copies every table from one database into another, whichever two engines they are.
It is how a deployment that began on SQLite — every one before 0.3 did — moves to PostgreSQL.

It copies into the database the API is configured with, read the way the API reads it, so the
steps are: configure the new database, copy, start.

1. Stop the API. Back up the old database.
2. Create an empty PostgreSQL database and an account that owns it.
3. Point the configuration at it — `Database__Provider=PostgreSql` and its
   `ConnectionStrings__DefaultConnection` — but do not start the API yet: on its first start it
   seeds the database, and the copy only goes into an empty one.
4. Run the image with `copy-database`, naming the old database, with the same configuration:

   ```bash
   docker run --rm --env-file axia.env -v /srv/nelsar-axia/data:/old:ro \
     ghcr.io/nelsar/axia-api:x.y.z copy-database --from-provider Sqlite --from "Data Source=/old/nelsar-axia.db"
   ```

   From source: `dotnet Nelsar.Axia.Api.dll copy-database --from-provider Sqlite --from "Data Source=…"`.

5. Start the API. It finds its tables migrated and filled, and seeds nothing.

What it does, and refuses:

- The old database is left as it was. A SQLite file is copied, with its `-wal` if a shutdown left
  one, into a temporary folder, read from there and the copy removed, so the original is never
  opened and can be mounted read-only. A SQLite path that does not exist is an error rather than a
  new empty file.
- It refuses an old database that is behind this version's schema. Start this version on it once,
  with its own `Database:Provider`, which brings it up to date, then copy.
- It refuses a target that already has rows in any table.
- It copies everything in one transaction and checks each table's row count against the old
  database's before committing, so a failure leaves the target as empty as it was.
- Bind passwords are copied as they are stored, encrypted, so the new database needs the same
  `Encryption:Key`.

The same command moves a database back to SQLite, or between two PostgreSQL servers.

**The quick start**, from 0.2 or earlier: its SQLite database is in the `nelsar-axia_api-data`
volume, which the quick start no longer mounts. To bring its audit trail along, before the first
`docker compose up` of the new version:

```bash
docker compose up -d --wait postgres
docker compose run --rm -v nelsar-axia_api-data:/old:ro api copy-database --from-provider Sqlite --from "Data Source=/old/nelsar-axia.db"
docker compose up -d
```

### Audit log retention

There is no automatic pruning. The table grows by one row per sign-in and per change. To trim it,
on PostgreSQL:

```sql
DELETE FROM "AuditEvents" WHERE "Timestamp" < '2025-01-01';
```

On SQLite, where the timestamp is UTC text:

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
