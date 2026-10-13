# REST API

All endpoints are under `/api/v1`. Requests and responses are JSON with camelCase property names.
In Development, the OpenAPI description and Swagger UI are at `/swagger`; they are generated from
the code, so they always match it.

## Conventions

### Authentication

Sign in, then send the token as a bearer token:

```bash
TOKEN=$(curl -s -X POST http://localhost:5063/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin.demo","password":"Axia-Demo-1"}' | jq -r .token)

curl -s http://localhost:5063/api/v1/users/search?filter=petrov \
  -H "Authorization: Bearer $TOKEN"
```

Tokens last `Jwt:ExpiresInMinutes` (60). There is no refresh; sign in again on `401`.

### Headers

| Header | Direction | Purpose |
| --- | --- | --- |
| `Authorization: Bearer <token>` | request | Every endpoint except sign-in, registration and `providers/selectable`. |
| `X-LDAP-Provider-Id: <id>` | request | Which directory. At sign-in: where to check the password. Afterwards: optional, and must match the directory the token was issued for. |
| `Accept-Language: ru` | request | Language of messages in responses: `en`, `ru`, `kk`, `de`, `es`, `fr`, `zh`, `ar`, `he`. |
| `X-Correlation-ID` | request & response | Request id. Sent back on every response; if you send one (short, alphanumeric), it is used. Quote it when reporting a problem; every log line of the request carries it. |
| `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` | response | On every response, errors and refusals included: nothing the API answers is to be cached, sniffed, framed or rendered. There is no `Server` header. |
| `Retry-After` | response | On `429` and `503`: seconds until the request will be taken. |

### Paging

List endpoints for groups, units, computers, user search and the audit log take:

| Query | Default | |
| --- | --- | --- |
| `filter` | — | Free text; see each endpoint |
| `pageSize` | `50` | 1–200; outside that is `400` |
| `pageToken` | — | From the previous page's `pageToken` |

and answer:

```json
{
  "items": [ … ],
  "pageSize": 200,
  "pageToken": "MjAw",
  "hasMore": true,
  "isComplete": true
}
```

- Keep requesting with the returned `pageToken` until `hasMore` is `false`.
- The token is opaque (today an offset). Entries added or removed between pages can shift results.
- `isComplete: false` means the directory stopped the underlying read partway (a server limit or a
  dropped connection): the items are real, but the set is not the whole directory.

`GET /api/v1/users` is the exception: the whole directory at once, `{ "items": [...], "isComplete": true }`.

### Ids

Users, groups, units and computers have a GUID `id` derived from the entry, not stored. It changes
when the entry is renamed or moved; a stale id gets `404`.

### Errors

Errors carry a JSON body with a human-readable `message`, in the request's language:

```json
{ "message": "The directory rejected the creation of the entry." }
```

Model-binding failures (a required field missing, a wrong type) use ASP.NET Core's standard
validation problem details (`errors` per field) instead.

| Status | Meaning |
| --- | --- |
| `400` | Invalid input: validation, a malformed DN or address, an unknown directory id, an empty batch. |
| `401` | Not signed in, token expired or invalid, or wrong password at sign-in. |
| `403` | Signed in, but lacking the permission, or the target is protected. Does not say which permission. |
| `404` | No such entry (or the id is stale after a rename). |
| `409` | Conflict with the directory's state: already exists, unit not empty, address in use. |
| `429` | Rate limit. `Retry-After` says when the window reopens. |
| `501` | Not supported by this provider (groups on `ActiveDirectory`). |
| `502` | The directory: unreachable, misconfigured, or it refused the operation. |
| `503` | The bulk job queue is full. Nothing was queued; `Retry-After` says when to send it again. |
| `500` | Anything else; the message is generic and the detail is in the log under the correlation id. |

Messages never name an account; they are logged too.

## Endpoints

Permission in brackets. *anon* = no token needed.

### Auth

| Method | Path | | |
| --- | --- | --- | --- |
| POST | `/auth/login` | anon, rate-limited | Sign in. |
| POST | `/auth/register` | anon, rate-limited; off unless `Auth:AllowSelfRegistration` | Create an account. |
| GET | `/me/permissions` | signed in | The caller's permissions. |

`POST /auth/login`

```json
// request
{ "email": "admin.demo", "password": "Axia-Demo-1" }

// 200
{
  "token": "eyJ…",
  "user": {
    "id": "e90883d9-…", "email": "admin.demo@example.org", "username": "admin.demo",
    "name": "Aigerim Admin", "role": "Admin",
    "dn": "uid=admin.demo,ou=people,dc=example,dc=org",
    "groups": [ "cn=axia-admins,ou=groups,dc=example,dc=org" ]
  },
  "permissions": [ "users.read", "users.list", … ]
}
```

`email` accepts any login. `401` on a wrong login or password, without saying which.

`POST /auth/register`: `{ "email", "name", "password" }` → `201` with the user.

### Users

| Method | Path | Permission | |
| --- | --- | --- | --- |
| GET | `/users` | `users.list` | Everyone; `{ items, isComplete }` |
| GET | `/users/search` | `users.read` | Paged; `filter` matched against mail, names, uid, sAMAccountName, UPN |
| GET | `/users/{id}` | `users.read` | One user |
| GET | `/users/{id}/attributes` | `users.read` | `{ id, dn, attributes: [{ name, values, isBinary, isRedacted }] }` |
| POST | `/users` | `users.create` | `{ email, name, password }` → `201` |
| PUT | `/users/{id}` | `users.update` | `{ name, email?, password? }` → `200` |
| DELETE | `/users/{id}` | `users.delete` | `204` |
| POST | `/users/bulk/sync` | `users.bulk` (+ create/update) | Array of rows → `207` with per-row results |
| POST | `/users/bulk` | `users.bulk` (+ create/update) | Array of rows → `202 { jobId }` |
| GET | `/users/bulk/status/{jobId}` | `users.bulk` | Job state; `404` if unknown (or lost to a restart) |
| POST | `/users/generate` | `users.generate` | Login generator |

User:

```json
{ "id": "…", "email": "…", "username": "…", "name": "…", "role": "User", "dn": "…", "groups": [ "…" ] }
```

Bulk row: `{ "operation": "create" | "update", "email", "name", "password"?, "newEmail"? }`.
Bulk response:

```json
{
  "jobId": "…", "state": "completed",
  "completedCount": 2, "failedCount": 1, "errorMessage": null,
  "items": [ { "email": "…", "status": "completed" | "failed", "message": "…", "user": { … } } ]
}
```

Generator request: `{ pattern?, rawText?, regexPattern?, usernameTemplate?, transliterate, checkConflicts, maxItems }`.
Response: `{ items: [...], count, details?: [{ value, exists }] }`. See
[Features → Login generator](features.md#login-generator).

### Groups

| Method | Path | Permission | |
| --- | --- | --- | --- |
| GET | `/groups` | `groups.read` | Paged; `filter` on name, DN, description |
| GET | `/groups/{id}` | `groups.read` | |
| POST | `/groups` | `groups.manage` | `{ name, parentDistinguishedName, description, memberDistinguishedNames: [...] }` → `201` |
| PUT | `/groups/{id}` | `groups.manage` | Any of `{ name, parentDistinguishedName, description, memberDistinguishedNames }`; omitted fields unchanged; members replaced wholesale when given |
| DELETE | `/groups/{id}` | `groups.manage` | `204` |

```json
{ "id": "…", "name": "axia-admins", "distinguishedName": "cn=axia-admins,ou=groups,dc=example,dc=org",
  "parentDistinguishedName": "ou=groups,dc=example,dc=org", "description": "…", "members": [ "…" ] }
```

### Organizational units

| Method | Path | Permission | |
| --- | --- | --- | --- |
| GET | `/organizational-units` | `orgunits.read` | Paged; `filter` on name, DN, description |
| GET | `/organizational-units/{id}` | `orgunits.read` | |
| POST | `/organizational-units` | `orgunits.manage` | `{ name, parentDistinguishedName, description }` → `201` |
| PUT | `/organizational-units/{id}` | `orgunits.manage` | Any of the same; changing name or parent renames/moves |
| DELETE | `/organizational-units/{id}` | `orgunits.manage` | `204`; `409` if not empty |

### Computers

| Method | Path | Permission | |
| --- | --- | --- | --- |
| GET | `/computers` | `computers.read` | Paged; `filter` on name, DN, description, DNS name, OS |
| GET | `/computers/{id}` | `computers.read` | |
| POST | `/computers` | `computers.manage` | `{ name, parentDistinguishedName, description, dnsHostName }` → `201` |
| PUT | `/computers/{id}` | `computers.manage` | Any of `{ name, parentDistinguishedName, description, dnsHostName, enabled }` |
| DELETE | `/computers/{id}` | `computers.manage` | `204`; revokes a joined machine's trust |

```json
{ "id": "…", "name": "ALA-WS-00001", "distinguishedName": "…", "parentDistinguishedName": "…",
  "description": "…", "dnsHostName": "ala-ws-00001.example.org",
  "operatingSystem": "Windows 11 Enterprise", "operatingSystemVersion": "10.0 (22631)", "enabled": true }
```

`enabled` is `null` for entries that have no `userAccountControl` (RFC 4519 `device`).

### Directories

| Method | Path | Permission | |
| --- | --- | --- | --- |
| GET | `/providers/selectable` | anon, rate-limited | `[{ id, name }]` of active directories |
| GET | `/providers` | `providers.manage` | All |
| GET | `/providers/{id}` | `providers.manage` | |
| POST | `/providers` | `providers.manage` | → `201` |
| PUT | `/providers/{id}` | `providers.manage` | |
| DELETE | `/providers/{id}` | `providers.manage` | `204` |

Create/update body:

```json
{
  "name": "Corp AD", "host": "dc01.corp.example.com", "port": 636, "security": "Ldaps",
  "providerType": "StandardLdap", "isActive": true, "failoverHosts": "dc02.corp.example.com,dc03.corp.example.com",
  "baseDn": "DC=corp,DC=example,DC=com", "adminUsername": "svc-axia@corp.example.com",
  "adminPassword": "…",
  "userSearchFilter": "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})(userPrincipalName={0})(mail={0})))",
  "userSearchPageSize": 500
}
```

`adminPassword` is write-only and never returned. On update, omit it to keep the stored one,
except that a change of host, port or failover hosts requires it.

### Role mappings and roles

| Method | Path | Permission | |
| --- | --- | --- | --- |
| GET | `/role-mappings` | `roles.manage` | `[{ id, providerId, groupName, roleName, priority, identifiesGroupByNameOnly }]` |
| GET | `/role-mappings/{id}` | `roles.manage` | |
| POST | `/role-mappings` | `roles.manage` | `{ providerId?, groupName (DN), roleName, priority }` → `201` |
| PUT | `/role-mappings/{id}` | `roles.manage` | same body |
| DELETE | `/role-mappings/{id}` | `roles.manage` | `204` |
| GET | `/roles` | `roles.manage` | `{ permissions: [...all], roles: [{ roleName, permissions, isEditable }] }` |
| PUT | `/roles/{roleName}/permissions` | `roles.manage` | `{ permissions: [...] }`: replaces the role's set |

### Rate limits

| Method | Path | Permission | |
| --- | --- | --- | --- |
| GET | `/rate-limit-settings` | `ratelimits.manage` | `[{ id, policyName, permitLimit, windowSeconds, queueLimit, enabled }]` |
| GET | `/rate-limit-settings/{policyName}` | `ratelimits.manage` | |
| PUT | `/rate-limit-settings/{policyName}` | `ratelimits.manage` | `{ permitLimit ≥ 1, windowSeconds ≥ 1, queueLimit ≥ 0, enabled }` |

### Audit

| Method | Path | Permission | |
| --- | --- | --- | --- |
| GET | `/audit-events` | `audit.read` | Paged, newest first: `{ id, eventType, objectId, objectDn, actor, action, success, details, timestamp }` |

## Scripting examples

Page through every computer:

```bash
token=""; while :; do
  page=$(curl -s -H "Authorization: Bearer $TOKEN" \
    "http://localhost:5063/api/v1/computers?pageSize=200${token:+&pageToken=$token}")
  echo "$page" | jq -r '.items[].name'
  [ "$(echo "$page" | jq -r .hasMore)" = "true" ] || break
  token=$(echo "$page" | jq -r .pageToken)
done
```

Queue a bulk batch and wait for it:

```bash
job=$(curl -s -X POST http://localhost:5063/api/v1/users/bulk \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d @batch.json | jq -r .jobId)

until state=$(curl -s -H "Authorization: Bearer $TOKEN" \
        http://localhost:5063/api/v1/users/bulk/status/$job | jq -r .state);
      [ "$state" = completed ] || [ "$state" = failed ]; do sleep 2; done
```

Disable a computer:

```bash
curl -s -X PUT http://localhost:5063/api/v1/computers/$ID \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"enabled": false}'
```
