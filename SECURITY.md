# Security policy

Nelsar.Axia hands out and withdraws accounts in a directory. An instance of it holds a bind
credential with whatever rights an administrator gave it, so a compromise of the API is a
compromise of the directory up to those rights. Please treat it accordingly, and please report
what you find.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting: the **Security** tab → **Report a vulnerability**.
It opens a thread visible only to the maintainers, and it keeps the report attached to the
repository rather than to anyone's inbox.

Please do not open a public issue for something exploitable. A public issue is the right place for
hardening suggestions, a default that could be safer, or a question about the threat model below.

Useful in a report, in rough order of usefulness:

- what an attacker gets, and what they need in order to start — unauthenticated, a signed-in
  account, a particular permission, a position on the network;
- the configuration it needs, if it depends on one (which `ProviderType`, whether
  `Auth:AllowSelfRegistration` is on, whether a reverse proxy is in front);
- the smallest sequence of requests that shows it;
- the version — a commit hash, since there are no releases yet.

What to expect: this is a small project without a security team or a bounty. Reports are
acknowledged when a maintainer next sees them, and anything exploitable is prioritised over
features. You will be credited in the fix unless you would rather not be.

## Supported versions

Pre-1.0 and pre-release: only the current `main` is supported. There are no backports, because
there is nothing yet to backport to.

## What is in the trust model

| Boundary | Assumption |
| --- | --- |
| Browser → API | Untrusted. Every request is authenticated and authorised on its own; the console is a convenience, not a control. |
| API → directory | **Trusted network.** See the first known limitation below — this one is load-bearing. |
| API → its own database | Trusted. SQLite on the same host, holding providers, role mappings, rate-limit settings and the audit trail. |
| Operator → configuration | Trusted. Anyone who can edit `appsettings.json` or the environment already controls the instance. |

Inside that, the following are meant to hold, and a case where they do not is worth reporting:

- **An operation cannot be performed without the permission for it.** Permissions are resolved per
  request from directory group membership and the role mappings, never from a claim the token
  carries. Every guarded use case has a test asserting the refusal. Signing in grants read access
  and nothing more; a bulk batch needs the single-account permission for each kind of row it holds.
- **A permission does not reach targets that hold more than the caller.** Updating, resetting the
  password of or deleting an account, and changing or deleting a group mapped to a role, are
  refused when the target confers any permission the caller lacks.
- **A session is the directory entry that proved the password.** The token carries that entry's
  DN, permissions are read from the entry at that DN, and writes to an account are addressed by
  its DN — never by searching for a login or address, which two entries can share. A token
  without the DN is refused.
- **A role mapping names one group.** Mappings take the group's DN. Rows written by CN alone, by
  earlier builds, are ignored unless `RoleMappings:AllowMappingsByName` is set, and the API will not
  start without an Admin mapping by DN.
- **A target privileged in the directory is an administrator's to change.** Accounts and groups
  with `adminCount` set, or in `DirectoryProtection:ProtectedGroups`, can only be changed by a
  caller holding every permission; the bind account cannot be changed through the application.
- **Passwords are never written in clear.** They are set with `unicodePwd` in Active Directory and
  the Password Modify operation elsewhere, so the directory hashes and checks them; a directory
  offering neither is refused a password rather than given one as a plain attribute.
- **Computers and contacts are not users.** Entries of those classes are left out of every lookup,
  listing and write made through the users endpoints, and attributes whose names mark them as
  secrets — LAPS passwords among them — are withheld from the attributes view.
- **A stored bind password is only sent where it was entered for.** Moving a directory to a new
  host, port or failover host requires the password again.
- **An operation cannot cross into another directory.** The directory is bound to the session at
  sign-in and carried in the token; a request naming a different one in `X-LDAP-Provider-Id` is
  refused rather than silently served from the default.
- **A login cannot change the query it lands in.** Filters and distinguished names are escaped at
  the boundary, and the escaping is tested against the metacharacters that matter.
- **Every change is attributable.** Every attempt to change a directory or this application's
  settings is written to the audit trail with the principal who made it — the signed-in caller, or
  for a queued bulk job the person who queued it — whether it was permitted or refused. A test
  requires every command to be audited.
- **Secrets do not reach the log.** Bind passwords are encrypted at rest with AES-GCM; log
  properties whose names read like a secret are redacted on the way to every sink. Logins and
  distinguished names are masked in the diagnostic log; the audit trail records the actor in full,
  deliberately.

## Known limitations

These are real, they are not in the issue tracker as vulnerabilities, and an operator should read
them before deploying.

- **The connection to the directory is plain LDAP.** There is no LDAPS and no StartTLS. Every
  sign-in binds as the user, which means the user's password crosses the network to the directory
  in the clear, and so does the bind credential on every connection. Until this is fixed, the API
  and the directory must sit on a network where that is acceptable — the same host, or a link that
  is itself encrypted. This is the project's largest security gap and the top item for it.
- **A token cannot be revoked.** Sessions are bearer JWTs valid until they expire, 60 minutes by
  default; there is no sign-out, no refresh and no deny list. Removing an account from a group
  takes effect on the next request, because permissions are resolved per request — but disabling
  the account entirely does not end a session already issued. Shorten `Jwt:ExpiresInMinutes` where
  that matters.
- **`Jwt:Key` and `Encryption:Key` are read from configuration**, so their protection is the
  deployment's: environment variables, a mounted secret, a vault. The API refuses to start when
  either is missing, malformed, too short or the example placeholder. There is no key rotation
  procedure — rotating `Encryption:Key` without re-encrypting the stored bind passwords makes
  every configured directory unusable until they are re-entered (reported as such, rather than as
  a crash).
- **No password policy, no account lockout.** Password rules are whatever the directory enforces.
  The rate limiter slows repeated sign-in attempts per client address; it does not lock an account.
- **The audit trail is in the same database as everything else**, writable by the same process, so
  it records what happened rather than proving it to someone who does not trust the host.

## Deployment choices that are not vulnerabilities

Each of these is a documented setting whose default is the safe one. Turning it the other way is a
decision, and worth making deliberately — see [Security defaults](README.md#security-defaults).

- `Auth:AllowSelfRegistration=true` — an unauthenticated caller can then create directory
  accounts. That is the point of the setting; it ships off.
- `Cors:AllowAnyOrigin=true`, or `Cors:AllowedOrigins` naming an origin wider than the console's.
  Any listed website can use a visitor's browser to call the API and read the answers — on an
  internal API, to try passwords from inside the network.
- `Https:RedirectToHttps=false` where nothing else terminates TLS. The default suits a proxy that
  does; exposing the API directly on plain HTTP is a deployment error, not a code one.
- `ReverseProxy:KnownProxies` naming an address that is not actually the proxy — forwarded headers
  are then believed from somewhere they should not be, which lets a caller choose their own
  rate-limit bucket. Naming nothing is the default and is safe.
- Giving the bind account more rights in the directory than the instance needs.
