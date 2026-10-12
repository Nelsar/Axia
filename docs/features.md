# Features

Everything the application does, by area. Each section names the permission it needs, the API
endpoints behind it, and the console screen. Endpoint details are in the [REST API](api.md)
reference.

- [Signing in](#signing-in)
- [Users](#users)
- [Bulk operations](#bulk-operations)
- [Login generator](#login-generator)
- [Groups](#groups)
- [Organizational units](#organizational-units)
- [Computers](#computers)
- [Directories (providers)](#directories-providers)
- [Role mappings and role permissions](#role-mappings-and-role-permissions)
- [Rate limits](#rate-limits)
- [Audit log](#audit-log)
- [Console](#console)
- [Languages](#languages)

## Signing in

`POST /api/v1/auth/login` · no permission · rate-limited (`auth-login`, 5 per minute per address)

- Field `email` takes any login: `uid`, `sAMAccountName`, `userPrincipalName` or e-mail. How it is
  looked up: [Directories → Accounts](directories.md#accounts).
- The password is checked by binding to the directory as that account. The application never
  stores or compares passwords.
- With several directories, the console offers a picker; the API takes `X-LDAP-Provider-Id`.
- The response carries the token, the user (name, e-mail, DN, groups, role) and the current
  permissions.
- Every attempt, successful or not, is audited.

**Self-registration**: `POST /api/v1/auth/register` creates a directory account for an
unauthenticated caller. Off unless `Auth:AllowSelfRegistration=true`. Leave it off.

## Users

| Action | Permission | Endpoint |
| --- | --- | --- |
| Search | `users.read` | `GET /api/v1/users/search?filter=&pageSize=&pageToken=` |
| Read one | `users.read` | `GET /api/v1/users/{id}` |
| Read all raw attributes | `users.read` | `GET /api/v1/users/{id}/attributes` |
| List everyone | `users.list` | `GET /api/v1/users` |
| Create | `users.create` | `POST /api/v1/users` |
| Edit name, e-mail, password | `users.update` | `PUT /api/v1/users/{id}` |
| Delete | `users.delete` | `DELETE /api/v1/users/{id}` |

**Search** splits the text on spaces, commas and semicolons and matches each term as a substring
of `mail`, `displayName`, `cn`, `uid`, `sAMAccountName`, `userPrincipalName`, `givenName` or `sn`.
The search runs in the directory, then the result is paged.

**List everyone** reads the whole directory in one response (no paging) and says
`isComplete: false` if the directory stopped it partway.

**Raw attributes** shows every attribute of the entry under the directory's own names. Binary
values are flagged; values of credential attributes (passwords, LAPS) are withheld and marked
`isRedacted`.

**Create** takes name, e-mail and password and writes an `inetOrgPerson` at
`uid=<local part>,<BaseDn>`; see [what is written](directories.md#new-accounts), including the
Active Directory caveat. An e-mail already in use is a `409`, and so is a login that another entry
already has or that a `posixGroup` still lists as a member (see
[group membership](directories.md#group-membership)).

**Edit** changes `displayName`, `mail` and/or the password. The login (RDN, `uid`) does not change
with the e-mail. A new e-mail must not belong to another account.

**Protected targets**: editing, resetting the password of, and deleting an account are refused when
it holds more than you do, is privileged in the directory, or is the bind account. See
[Access control](access-control.md#protected-targets).

Computers and contacts never appear here, even though they are `person` entries in AD.

## Bulk operations

`users.bulk`, plus `users.create` for create rows and `users.update` for update rows.

| Mode | Endpoint | Answer |
| --- | --- | --- |
| Synchronous | `POST /api/v1/users/bulk/sync` | `207 Multi-Status` with one result per row |
| Queued | `POST /api/v1/users/bulk` | `202 Accepted` with `{ "jobId": "…" }` |
| Queued job status | `GET /api/v1/users/bulk/status/{jobId}` | state, counts, per-row results |

A batch is a JSON array of rows:

```json
[
  { "operation": "create", "email": "ivan.petrov@example.org", "name": "Ivan Petrov", "password": "S3cure-Pass!" },
  { "operation": "update", "email": "olga.sidorova@example.org", "name": "Olga Sidorova", "newEmail": "o.sidorova@example.org" },
  { "operation": "update", "email": "john.smith@example.org", "name": "John Smith", "password": "N3w-Pass!" }
]
```

| Field | create | update |
| --- | --- | --- |
| `operation` | `"create"` (default) | `"update"` |
| `email` | the new account's address | finds the account to change |
| `name` | required | required; the new display name |
| `password` | required | optional; sets a new password |
| `newEmail` | — | optional; changes the address |

Behaviour:

- **Rows are independent.** A bad row fails on its own with a message; the others go ahead. Results
  are `completed` or `failed` per row.
- **Permission is checked per batch** at submission (and again when a queued job runs, as the
  person who queued it): a batch containing create rows needs `users.create`, update rows
  `users.update`. Protected-target checks apply per row.
- **Every row is audited**, under the submitter's name, including password resets.
- **Queued jobs live in memory.** They are lost if the API restarts, and they are not shared
  between instances. Job states: `pending`, `inprogress`, `completed`, `failed`.
- An empty batch is `400`.

In the console: *Users → Bulk operations*, which accepts pasted rows and shows per-row results,
polling queued jobs.

## Login generator

`users.generate` · `POST /api/v1/users/generate`

Produces candidate logins for a bulk create, and optionally checks which already exist. It writes
nothing.

### From a mask

`{a..b}` ranges are expanded and combined:

| Pattern | Produces |
| --- | --- |
| `node-{01..03}` | `node-01`, `node-02`, `node-03` (zero padding follows the wider bound) |
| `srv-{a..c}` | `srv-a`, `srv-b`, `srv-c` |
| `lab{1..2}-pc{01..02}` | `lab1-pc01`, `lab1-pc02`, `lab2-pc01`, `lab2-pc02` |

A malformed range (`{01-03}`, a descending `{03..01}`) is kept literally. Nested braces produce
nothing.

### From pasted text

Paste one record per line in `rawText`, give a regular expression with named groups in
`regexPattern`, and build each login with `usernameTemplate`:

```json
{
  "rawText": "Иванов Игорь\nҚанат Әсел",
  "regexPattern": "^(?<lastname>\\S+)\\s+(?<firstname>\\S+)$",
  "usernameTemplate": "{firstname}.{lastname}",
  "transliterate": true,
  "checkConflicts": true,
  "maxItems": 100
}
```

→ `igor.ivanov`, `asel.qanat`, each with `exists: true/false` in `details`.

- Without a template, the whole match is used.
- `transliterate` maps Cyrillic, including Kazakh letters, to Latin (`Щербак` → `Shcherbak`,
  `Қанат` → `Qanat`).
- Results are trimmed, lower-cased and de-duplicated.
- The regex has a 500 ms timeout per line; lines that time out or do not match are skipped. Over
  the whole text it has 2 seconds, after which the request is refused with `400`.
- `maxItems`: 1 to 10,000 (default 50).
- Lengths: `pattern` and `usernameTemplate` up to 256 characters, `regexPattern` up to 512,
  `rawText` up to 1,000,000.
- `checkConflicts` looks each login up in the directory. If the directory is unreachable, the
  request fails rather than answering "no conflicts".

## Groups

| Action | Permission | Endpoint |
| --- | --- | --- |
| List / search | `groups.read` | `GET /api/v1/groups?filter=&pageSize=&pageToken=` |
| Read one | `groups.read` | `GET /api/v1/groups/{id}` |
| Create | `groups.manage` | `POST /api/v1/groups` |
| Rename, move, describe, set members | `groups.manage` | `PUT /api/v1/groups/{id}` |
| Delete | `groups.manage` | `DELETE /api/v1/groups/{id}` |

- Lists AD `group`, `groupOfNames` and `groupOfUniqueNames` under the base DN. `posixGroup`
  entries are used for role membership but **not listed** here.
- Search matches every term against the name, DN or description (all terms must match). Member
  DNs are not searched.
- Members are DNs. **Saving replaces the whole membership** with the list sent. A `groupOfNames`
  cannot be left empty (schema rule), so the API refuses an empty list for one.
- Create writes a `groupOfNames` with at least one member. In AD that is **not a security
  group**; see [Directories](directories.md#groups).
- Changing or deleting a group mapped to a role you do not fully hold, or a protected group, is
  refused. So is renaming a group to a name that a more powerful mapping uses.
- Against the Windows `ActiveDirectory` provider, every group endpoint answers `501`.

## Organizational units

| Action | Permission | Endpoint |
| --- | --- | --- |
| List / search | `orgunits.read` | `GET /api/v1/organizational-units?filter=&pageSize=&pageToken=` |
| Read one | `orgunits.read` | `GET /api/v1/organizational-units/{id}` |
| Create | `orgunits.manage` | `POST /api/v1/organizational-units` |
| Rename, move, describe | `orgunits.manage` | `PUT /api/v1/organizational-units/{id}` |
| Delete | `orgunits.manage` | `DELETE /api/v1/organizational-units/{id}` |

- A flat list (name, DN, parent DN, description), not a tree.
- The parent can be any container, including ones not in the list (`CN=Users`, the base DN).
- Deleting a unit that still contains entries is refused by the directory: `409`. Nothing is
  deleted recursively.
- The `id` is derived from the DN, so it changes on rename or move.

## Computers

| Action | Permission | Endpoint |
| --- | --- | --- |
| List / search | `computers.read` | `GET /api/v1/computers?filter=&pageSize=&pageToken=` |
| Read one | `computers.read` | `GET /api/v1/computers/{id}` |
| Pre-stage an account | `computers.manage` | `POST /api/v1/computers` |
| Rename, move, describe, set DNS name, enable/disable | `computers.manage` | `PUT /api/v1/computers/{id}` |
| Delete | `computers.manage` | `DELETE /api/v1/computers/{id}` |

- Lists AD `computer` and RFC 4519 `device` entries. Shows name, DN, DNS name, operating system
  and version, description, and enabled state (`null` for `device` entries, which have none).
- Search matches name, DN, description, DNS name and operating system.
- These manage **directory entries, not machines.** Creating pre-stages an account a machine can
  claim when it joins; renaming does not rename the machine; **deleting revokes a joined machine's
  trust** and it must rejoin.
- Enable/disable flips the `ACCOUNTDISABLE` bit of `userAccountControl` only.
- Creating fails against OpenLDAP; see [Directories](directories.md#computers-1).

## Directories (providers)

`providers.manage` · `/api/v1/providers` · console: *LDAP providers*

Add, edit, deactivate and delete directories stored in the database. Fields: name, host, port,
security (`Ldaps`/`StartTls`/`None`), provider type, failover hosts, base DN, bind account, bind
password (write-only; never returned), user search filter, page size, active.

- The bind password is encrypted with `Encryption:Key` before it is stored.
- Changing host, port or failover hosts requires the password again, as does switching security
  to `None`.
- `GET /api/v1/providers/selectable` is **anonymous** (for the sign-in picker), returns only ids and
  names of active directories, and is rate-limited (`providers-selectable`, 30 per minute).
- The console marks directories using `None` as *not encrypted*.

Details: [Directories → Several directories](directories.md#several-directories).

## Role mappings and role permissions

`roles.manage` · `/api/v1/role-mappings`, `/api/v1/roles` · console: *Role mappings*, *Roles*

Map directory groups (by DN) to roles, globally or per directory, and choose what each role may do.
Admin is fixed. See [Access control](access-control.md).

## Rate limits

`ratelimits.manage` · `/api/v1/rate-limit-settings` · console: *Rate limiting*

Fixed-window limits per client address, retunable without a restart (picked up within 30 seconds).

| Policy | Applies to | Default |
| --- | --- | --- |
| `auth-login` | `/api/v1/auth/login`, `/api/v1/auth/register` | 5 requests / 60 s |
| `providers-selectable` | `/api/v1/providers/selectable` | 30 requests / 60 s |

Each has `permitLimit`, `windowSeconds`, `queueLimit` (requests held rather than refused, default
0) and `enabled`. Over the limit: `429` with a message.

The client address is the TCP peer, or the `X-Forwarded-For` value from a proxy listed in
`ReverseProxy`. Behind an unlisted proxy, everyone shares one bucket.

## Audit log

`audit.read` · `GET /api/v1/audit-events?pageSize=&pageToken=` · console: *Audit log*, and the
dashboard

Written for every sign-in, and for every command that changes the directory or the application's
settings, **whether it was allowed, refused or failed**. Newest first.

| Field | |
| --- | --- |
| `eventType` | The operation, e.g. `CreateUser`, `UpdateGroup`, `DeleteComputer`, `UpsertRateLimitSettings` |
| `actor` | Who: the signed-in account, or for a queued bulk job the person who queued it. Never taken from the request body. |
| `objectId`, `objectDn` | What it was done to, where known |
| `action`, `details` | Description, including the error type and message for failures |
| `success` | Whether it went through |
| `timestamp` | UTC |

Bulk batches get one entry for the batch and one per row. The audit log lives in the same SQLite
database as everything else and is written by the same process; it records what happened, but is
not tamper-proof.

## Console

The React console at `frontend/`. In development it runs on <http://localhost:5500>; in the
container it is served by nginx on port 8080.

| Screen | Shows | Needs |
| --- | --- | --- |
| Sign-in | login, password, directory picker, API address, language | — |
| Dashboard | counts (users, directories, role mappings, groups, units), recent audit events; tiles the role cannot see say so | any |
| Users | search, create, edit, delete, raw attributes; *Bulk operations* tab with the generator | `users.read` and up |
| Groups | list, search, create, edit, members, delete | `groups.read` |
| Organizational units | list, search, create, rename/move, delete | `orgunits.read` |
| Computers | list, search, pre-stage, edit, enable/disable, delete | `computers.read` |
| LDAP providers | directories in the database | `providers.manage` |
| Role mappings, Roles | group → role mappings; role → permissions | `roles.manage` |
| Rate limiting | the two policies | `ratelimits.manage` |
| Audit log | paged events | `audit.read` |

The sidebar footer has the theme switch (light/dark), the language picker and sign-out (which
forgets the token in the browser; the token itself stays valid until it expires).

Menus and buttons are hidden by role for convenience only. The API enforces everything.

## Languages

English (default), Russian, Kazakh, German, Spanish, French, Chinese, Arabic and Hebrew. Arabic and
Hebrew mirror the layout.

The console sends the chosen language as `Accept-Language`, so the API's own messages (errors,
validation) come back in it too. Any API client can do the same.

Translations other than English and Russian are machine-quality. Adding a language is one
dictionary in `frontend/src/lib/i18n/` and three `.resx` files on the API side; both sides fail the
build or the tests if a message is missing.
