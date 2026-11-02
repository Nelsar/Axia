# LDAP authentication done right: the traps people hit

Authenticating users against Active Directory or LDAP looks like a solved problem — bind with their
username and password, done. It is solved, and there are about half a dozen ways to get it subtly
wrong, two of which are outright authentication bypasses. This is the list of traps, drawn from
building sign-in for a directory console that has to get it right every time.

## Trap 1: building the DN instead of searching for it

The tempting shortcut is to turn the login into a DN directly —
`uid=<login>,ou=people,dc=example,dc=org` — and bind with that. It breaks the moment users live in
more than one OU, and it couples your app to the directory's layout.

The correct pattern is **search-then-bind**:

1. Bind with a low-privilege **service account** and **search** for the user by their login, with a
   configured filter such as `(&(objectClass=person)(uid={0}))` (or `sAMAccountName` on AD).
2. Take the **DN** from the single result and **bind again** as that DN with the user's password.

The search is what maps a human-typed login to the real entry, wherever it sits. Two guards on the
filter: it must return **exactly one** entry (zero or many → refuse), and the login you substitute
must be **escaped** (RFC 4515) so a `*` or `(` can't turn the filter into "match anyone."

## Trap 2: the empty password that logs everyone in

This one is an authentication bypass, and it's easy to ship.

In LDAP, a bind request with a DN but an **empty password** is an *unauthenticated bind* (RFC 4513).
Many servers accept it — as an **anonymous** session, which succeeds. So if your code does
"bind as the user's DN with whatever they typed" and the user (or an attacker) submits an **empty
password**, the bind can **succeed**, and naïve code reads that as "authenticated."

The fix is a one-liner you must not skip: **reject an empty password before you ever call bind.**
Don't rely on the directory to fail it — some won't.

## Trap 3: sending the password in the clear

The user's password travels inside the bind request. Over plain LDAP, anyone on the path reads it.
**Require LDAPS or StartTLS** for the whole exchange — the service-account search *and* the user
bind — and verify the server certificate. "We're on the internal network" is not an answer; internal
is where credential sniffing happens.

## Trap 4: authenticating without authorizing

Authentication says *who* someone is. It does not say what they may do. The common mistake is to
treat "the bind succeeded" as "this person is allowed in," when the real question is "is this person
in the group that grants access?"

So resolve **group membership** after authentication and check it. And here a directory gotcha bites:
on OpenLDAP, the user's `memberOf` is often empty, so a membership check against it silently passes
*no one* — or, worse, if you invert the logic, everyone. Resolve groups by searching from the group
side instead; see [why memberOf is empty in OpenLDAP](openldap-memberof-empty.md). Base authorization
on group membership you actually resolved, not on an attribute that may not be maintained.

## Trap 5: the session outlives the directory

You authenticate once and issue a token — a cookie, a JWT — good for, say, 60 minutes. But directory
state changes *during* that hour: someone is disabled, or removed from the group that let them in. If
the token alone decides access, a dismissed employee keeps working until it expires.

So **re-check against the directory on a short interval** (a cached 30-second check is a good
balance): is the account still enabled, still in the right groups? Note that "disabled" is spelled
differently in each directory — the `ACCOUNTDISABLE` bit in `userAccountControl` or an expired
`accountExpires` on AD, `pwdAccountLockedTime` with OpenLDAP's ppolicy, `loginDisabled` on eDirectory
— so check the right one for the directory you're on.

## Trap 6: brute force, and the lockout DoS

Password sign-in invites guessing, and the obvious defences each have a twist:

- A **per-IP** rate limit does little against guessing spread over many addresses, and every attempt
  you forward to the directory is a real bind that pushes the account toward the directory's own
  lockout. So add a **per-account** limit *below* the directory's lockout threshold (say 5 failures
  in 15 minutes), and refuse further attempts before they reach the directory. Then guessing through
  your app can't lock the account out in the directory itself.
- But a per-account limit is itself a small **denial-of-service**: an attacker can keep someone from
  signing in by burning their attempts. Keep it bounded (a short window, your app only, no directory
  lockout, live sessions untouched), and return the **same response** for a wrong password and a
  non-existent user, so the limit doesn't become a way to enumerate who exists.

## When not to authenticate with a password at all

The safest password flow is the one you don't run. If you can, delegate sign-in to an identity
provider over **OpenID Connect** — Entra ID, AD FS, Keycloak — and let it handle the password,
MFA and conditional access. Your app stops receiving operators' passwords altogether, and you still
take **authorization** from directory groups. Match the user by a stable identifier the directory
holds (`objectSid`, `objectGUID`, Keycloak's `LDAP_ID`), never by email or name, which can change or
be reassigned. A directory that uses SSO can then switch password sign-in off entirely — a second
factor the password form goes around protects nothing.

## Doing it once, correctly

[Nelsar.Axia](https://github.com/Nelsar/Axia) (the project this blog belongs to) authenticates its
operators this way: service-account search-then-bind, empty passwords refused before the bind, TLS
required, authorization from directory groups resolved rather than trusted from `memberOf`, a
30-second re-check that ends a session when the account is disabled, and a per-account sign-in limit
kept below the directory's own. OpenID Connect sign-in is on the [roadmap](https://github.com/Nelsar/Axia/blob/main/ROADMAP.md).
See [its access control](../access-control.md) for how roles come from groups.

---

Nelsar.Axia is an open-source (Apache-2.0) web interface for Active Directory, Samba AD and
OpenLDAP. You can [try it with one Docker command](../installation.md).
