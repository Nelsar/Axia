# The LDAP pass-back attack, and how to defend a web console

Anything that connects to a directory on your behalf — a multifunction printer, a NAS, a web
console — stores a bind account: a username and password it uses to talk to LDAP. The **LDAP
pass-back attack** turns that stored credential against you. You don't steal the password; you make
the device hand it to *you*.

It's most famous on office printers, where it's almost trivial. It applies, unchanged, to any web
application that stores a directory connection — including ones you build. Here's how it works and
the one rule that stops it.

## The classic: a printer with an LDAP address book

A networked printer with "scan to email" usually has an LDAP address book. In its admin page you
configure an LDAP server, a bind DN, and a bind password so it can look up addresses. The password
field, naturally, never shows you the stored value.

The attack: open that admin page (printers are often unauthenticated or use a default password),
change the **LDAP server address** to a machine you control, and leave everything else. Then trigger
an address-book lookup. The printer connects to *your* machine and performs a **simple bind** — it
sends the stored bind DN and password, in the clear, to the host you just named. You run a throwaway
LDAP listener and read the credentials off the wire.

The password you never had access to just walked over to you, because the device will bind to
whatever host it's told, with whatever it has stored.

## The same attack on a web console

A web console that manages AD or OpenLDAP stores the same thing: directory host, port, encryption
mode, a service account DN and its password. The password is encrypted at rest and the UI never
returns it. It feels safe.

It isn't, if the console lets someone **change the host while keeping the stored password.** Point
the connection at an attacker-controlled server, leave the password untouched, trigger any operation
that connects, and the server performs a simple bind to the new address — sending the service
account's password exactly as the printer did.

"But only an administrator can edit connections." True, and it still matters, for two reasons:

- **A stolen session shouldn't become permanent access.** An admin's token might live an hour; the
  service account's password lives for years and usually has *more* rights than the admin. Turning a
  one-hour token into the directory's long-lived credential is a big escalation.
- **Least privilege.** Not every administrator of the app should be able to exfiltrate the bind
  account that reaches the whole directory.

## The two variants to close

There are two ways to make the password leave, and a defense has to cover both:

1. **A new endpoint.** The connection is about to reach a host (or port, primary or failover) it has
   never reached before. That's the printer attack.
2. **Dropping encryption.** The host stays the same, but TLS is turned off. Now the next bind sends
   the password in plain text across the network, readable by anyone able to watch it. Same outcome,
   different door.

## The rule that stops it

State it plainly: **if a connection is about to go somewhere it has never been, or stop being
encrypted, the bind password has to be entered again — and only someone who already knows it can
enter it.**

In other words, keeping the stored password is allowed only when the destination and the protection
are unchanged. The moment either changes, the stored secret can't be reused; whoever makes the
change has to supply the password, which an attacker repointing the host doesn't have. In code it's
two checks before saving a connection:

```text
keepsStoredPassword = (no new password supplied) AND (a password is stored)

if keepsStoredPassword AND wouldReachNewEndpoint(host, port, failoverHosts):
    reject — "enter the bind password to point this at a new server"

if keepsStoredPassword AND wouldDropEncryption(security):
    reject — "enter the bind password to send it over an unencrypted connection"
```

Removing a failover host or reordering hosts needs no password — nothing new is reached. Adding any
host or port that wasn't there before does.

## Defending the printers, too

The console rule is for software you build or choose. For the devices already on your network:

- Give each device a **dedicated, least-privilege bind account** — read-only, scoped to what it
  needs, never a domain admin. If it leaks, the blast radius is small.
- Require **LDAPS or StartTLS** and disable simple bind over plain LDAP, so a lookup can't be
  down-graded to cleartext.
- **Lock the device admin interface:** change default passwords, put management on a segment users
  can't reach, and don't expose it.
- Prefer directories that support **channel binding and signing** so a bind can't be relayed as
  easily.

The through-line: a stored bind credential is only as safe as your ability to stop the thing holding
it from binding somewhere new.

## Doing it once, correctly

[Nelsar.Axia](https://github.com/Nelsar/Axia) (the project this blog belongs to) enforces exactly
the rule above: a directory connection that would reach a new host or port, or drop its encryption,
can't keep its stored bind password — you have to re-enter it, which only someone who knows it can
do. The password is stored with AES-GCM, never returned by the API, and the change is written to the
audit log. See [how Axia connects to directories](../directories.md) and
[its access control](../access-control.md).

---

Nelsar.Axia is an open-source (Apache-2.0) web interface for Active Directory, Samba AD and
OpenLDAP. You can [try it with one Docker command](../installation.md).
