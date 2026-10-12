# Changelog

What changed between released versions, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, a minor version may change
configuration or the API in ways that need action on upgrade; its entry says what to do.

How a release is cut is in [CONTRIBUTING.md](CONTRIBUTING.md#releasing).

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

[0.1.0]: https://github.com/Nurlan-Turkpenbay/Axia/releases/tag/v0.1.0
