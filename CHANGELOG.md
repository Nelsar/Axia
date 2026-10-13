# Changelog

What changed between released versions, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, a minor version may change
configuration or the API in ways that need action on upgrade; its entry says what to do.

How a release is cut is in [CONTRIBUTING.md](CONTRIBUTING.md#releasing).

## [0.2.0] - 2026-10-13

Hardening after the first release, and fixes to the console. **Upgrading from 0.1.0 takes one
step:** the API's container no longer runs as root, so its volumes have to be handed to uid 1654
before the new image starts — see *Changed*.

### Security

- An account switched off in the directory loses its session within 30 seconds, instead of keeping
  it until the token expires: disabled or expired in Active Directory, locked by an administrator
  through OpenLDAP's password policy, or login-disabled in eDirectory. It can no longer sign in
  either, including against a directory that does not enforce those flags itself. A lockout after
  failed sign-ins does not count, so that nobody can end someone else's session by mistyping their
  password.
- Directory calls give up instead of waiting indefinitely: a directory that accepts the connection
  and then does not answer is reported as unavailable after `Ldap:OperationTimeoutSeconds`, so it
  can no longer tie up the API's request threads. This includes StartTLS, which the LDAP library
  could not time out on its own.
- Bulk batches are bounded: 100 rows when processed while the caller waits, 10,000 when queued,
  and at most 10 jobs waiting in the queue — a full queue answers `503` with `Retry-After`. A
  queued job's status and results can only be read by the account that queued it, in the same
  directory; before, anyone holding `users.bulk` could read any job by its id.
- Organizational units and computers are held to the same rule as accounts and groups: nobody
  acts on something more powerful than they are. Renaming or moving a unit is judged by what it
  holds — a group mapped to a role the caller lacks, anything privileged in the directory, or the
  bind account — because everything beneath it is re-addressed; with `orgunits.manage` and
  `users.update`, moving the unit that held the Admin group used to be a way to reset an
  administrator's password. Domain controllers, read-only ones included, and other privileged
  computers can only be changed or deleted by someone holding every permission.
- Units, groups and computers can only be created in, or moved to, a parent inside the base DN.
  The parent used to be whatever DN the request named, so anyone allowed to manage them could
  write one anywhere the bind account could.
- Failed sign-ins are limited per account as well as per client address: after 5 in 15 minutes,
  from any number of addresses, the account's attempts are refused with `429` until the window
  ends, without being passed to the directory, so guesses spread over many addresses no longer
  count towards its lockout there. A successful sign-in clears the count. The limits are the new
  `auth-login-account` row on the Rate limiting page.
- The console's image sends a Content-Security-Policy that allows nothing but its own origin, along
  with `Cross-Origin-Opener-Policy`, `Permissions-Policy` and `Strict-Transport-Security`. A script
  injected into the page, from another site or written inline, does not run.
- Where the console is served with the API — built with `VITE_API_SAME_ORIGIN=true`, as the image
  is — its sign-in screen no longer offers to change the API address, and an address saved in the
  browser earlier is ignored. A link that set one could send the next sign-in, password included,
  to someone else's server.
- Every API response says `Cache-Control: no-store`, so a session token, directory entries or the
  audit trail are kept by no cache, together with `X-Content-Type-Options`, `X-Frame-Options`,
  `Referrer-Policy` and a Content-Security-Policy. The `Server` header is no longer sent.
- The API's container runs as the .NET images' unprivileged user, uid 1654, instead of root.

### Added

- `Ldap:ConnectTimeoutSeconds` (10) and `Ldap:OperationTimeoutSeconds` (30).
- A rate-limited request is answered with `Retry-After`.

### Changed

- The console is built with React 19, Vite 8 and TypeScript 7, and CI builds it on Node 22. Building
  it from source now needs Node 20.19 or later, 22 recommended.
- **On upgrade:** the API no longer runs as root, so whatever it writes to — `/data` and
  `/app/logs` — has to belong to uid 1654. Volumes created by 0.1.0 and host directories belong to
  root; hand them over before starting the new image:
  `sudo chown -R 1654 /srv/nelsar-axia/data /srv/nelsar-axia/logs`, with your own paths. The quick
  start's `init` container does this for its own volumes.

### Fixed

- An attempt to change something that an endpoint refused for want of a permission is recorded in
  the audit trail, as a failure, with who made it and the permission it needed. Only refusals made
  inside a use case were recorded before, so the commonest one — a permission not held at all —
  left no trace.
- An account's detail view shows its groups. In a directory without `memberOf` it showed none,
  for accounts that were members, whenever the full listing had been read first.
- An account opened from a search shows its login and DN, which read "—".
- The dashboard counts groups and organizational units again; it asked for a page larger than the
  API serves and showed "API unavailable" for both.
- The audit log's details wrap inside the table instead of running past its edge, and the
  signed-in user's name in the sidebar is no longer drawn under the theme toggle.

## [0.1.0] - 2026-10-12

The first public release.

### Directories

- Users: search, read, create, edit and delete; every raw attribute on demand, with credential
  attributes (passwords, LAPS, BitLocker) withheld; bulk create and update, synchronous or queued;
  a generator for account names from masks or pasted text, with Cyrillic and Kazakh
  transliteration and a check for names already taken.
- Groups, organizational units and computer accounts: create, edit, move and delete.
- Active Directory and OpenLDAP alike, several directories in one instance. A session stays bound
  to the directory it signed in to.
- LDAPS by default, with the certificate and host name verified; StartTLS, or plain LDAP when
  chosen explicitly.
- Passwords written the directory's own way: `unicodePwd` in Active Directory, the Password Modify
  operation elsewhere.

### Access control

- Sign-in with a directory account. Permissions are resolved on every request from the account's
  groups, mapped to roles by group DN, globally or per directory.
- Seventeen permissions across five roles, and an editor for what each role other than Admin may
  do.
- Nobody can change an account or group that confers more than they hold. Accounts and groups the
  directory marks as privileged take every permission to change, and the bind account cannot be
  changed through the application at all.

### Operations

- An audit log of every sign-in and every attempted change, permitted or refused.
- Rate limits per client address, retunable without a restart.
- Structured logs with secrets redacted, to the console, a file, Loki or an HTTP endpoint.
- A REST API described by OpenAPI, and a console in nine languages, Arabic and Hebrew mirrored.
- A Docker quick start: the API, the console and an OpenLDAP with demo accounts, over LDAPS with
  generated keys.

### Known limitations

Read before deploying: [SECURITY.md](SECURITY.md#known-limitations) and
[Not in scope yet](README.md#not-in-scope-yet). Above all, a session cannot be ended before its
token expires, and directory certificates are not checked for revocation.

[0.2.0]: https://github.com/Nelsar/Axia/releases/tag/v0.2.0
[0.1.0]: https://github.com/Nelsar/Axia/releases/tag/v0.1.0
