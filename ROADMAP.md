# Road map

Where the project is heading next. What already works is described in the
[README](README.md#features); what it does not do yet, and the security gaps to know about before
deploying, are in [Not in scope yet](README.md#not-in-scope-yet) and
[SECURITY.md](SECURITY.md#known-limitations).

This file is direction, not a schedule. Work is tracked in the
[issue tracker](https://github.com/Nelsar/Axia/issues), which is also the place to say which
of it matters to you.

## Next

What stands between the MVP and running it comfortably in production, roughly in order.

- **Ending a session.** Sign-out, short-lived tokens with refresh, or a deny list. Today a token is
  valid until it expires unless the account is switched off in the directory, so a stolen one stays
  usable for its lifetime.
- **Two-factor authentication for password sign-in.** TOTP with recovery codes, required by role —
  at least for roles that can reset passwords or change groups, since one password is all such an
  account needs today. A password alone earns a short-lived challenge, never a token; secrets are
  encrypted at rest; resetting someone's enrolment takes a permission of its own and is audited.
- **Sign-in through an identity provider** over OpenID Connect — Entra ID, AD FS, Keycloak —
  configured per directory, beside the directory password. Brings single sign-on and the
  provider's multi-factor and conditional-access policies, and the API stops receiving operators'
  passwords. The account is matched by an identifier the directory holds (`objectSid`,
  `objectGUID`, Keycloak's `LDAP_ID`), never by e-mail or name, and permissions still come from
  directory groups. A directory that uses it can switch password sign-in off: a second factor the
  password form goes around protects nothing.
- **Revocation checks for directory certificates**, without turning an unreachable CRL endpoint
  into an outage.
- **Bulk jobs that survive a restart.** They are held in memory today; moving them into the
  database — PostgreSQL since 0.3 — is also, with the rate-limit and sign-in counters, what stands
  between the API and a second instance.

## Later

Directions rather than commitments.

- Organizational units as a tree that loads its children on demand, instead of a flat list of DNs.
- Moving entries between units by drag and drop.
- A CSV/JSON import wizard that maps file columns onto directory attributes.
- Editing group membership from the user screen.
- Password policy and account lockout enforced by the application, not only by the directory.

## Housekeeping

- Two major dependency upgrades, each its own piece of work rather than a version bump:
  `Swashbuckle.AspNetCore` 6.6.2 → 10.x and `Novell.Directory.Ldap.NETStandard` 3.6.0 → 4.x. Both
  were attempted and reverted as genuinely breaking.
- Page tokens are a Base64 offset. Carrying the directory's own paging cookie instead would keep a
  listing stable while entries are added to it.
