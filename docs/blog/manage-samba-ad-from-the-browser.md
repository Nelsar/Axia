# Managing Samba AD from the browser

A Samba Active Directory domain controller behaves like Active Directory to anything that connects
to it — same schema, same Kerberos, same LDAP. That is the whole point of Samba AD, and it is also
why managing it from a browser is quietly awkward: the tools that assume *Windows* AD mostly work,
the tools that assume *OpenLDAP* mostly don't, and a few Samba-specific details catch everyone.

This guide walks the real ways to manage a Samba AD domain — users, groups, password resets — from
a web UI, and the three gotchas that bite regardless of which you pick.

## The built-in options aren't web

Samba's own management is command-line and Windows:

- **`samba-tool`** runs on the domain controller itself. It is complete and scriptable — `samba-tool
  user create`, `samba-tool group addmembers`, `samba-tool user setpassword` — and it is exactly
  what you don't want to hand to helpdesk.
- **RSAT (Active Directory Users and Computers)** connects to a Samba DC from a domain-joined
  Windows machine, because Samba AD speaks the same protocols. Delegation is set with
  `samba-tool dsacl set` or through ADUC, just as on Windows. It works well — but it is a Windows
  desktop tool, so helpdesk needs a domain-joined Windows box, which is often the one thing a
  Samba-on-Linux shop was trying to avoid.

Neither is a browser. For a web UI you reach for an LDAP tool or a management application, and that
is where the Samba specifics start to matter.

## LDAP web tools against Samba AD

Web tools like phpLDAPadmin or LDAP Account Manager can point at a Samba DC over LDAP, but Samba AD
is **AD-shaped, not OpenLDAP-shaped**, so OpenLDAP defaults don't carry over. Three things trip
people up — and they are the same three any tool, including one you build, has to get right:

1. **You must connect over TLS.** Samba refuses a simple bind over plain LDAP by default
   (`ldap server require strong auth = yes`). So the tool has to use **LDAPS or StartTLS**, and it
   has to trust Samba's certificate — which on a fresh install is self-signed, made on first start,
   and valid for the server's own name only. Connect by that exact name and trust its `ca.pem`, or
   give Samba a certificate from your own CA.
2. **A password isn't a normal attribute.** As on Windows AD, you set a Samba AD password by writing
   `unicodePwd` over an encrypted connection — not by writing `userPassword`. A tool that writes
   `userPassword` (the OpenLDAP habit) will appear to succeed and change nothing. A reset also
   doesn't invalidate the old password immediately: Samba honours the previous one for
   `old password allowed period` minutes, 60 by default.
3. **Account state lives in `userAccountControl`**, the AD bitfield, not in an OpenLDAP
   `ppolicy` attribute. Disabling or enabling an account means flipping a bit there.

phpLDAPadmin will happily show you the raw tree, gotchas and all; LAM has Samba support but is
happiest with OpenLDAP. Both can work — you're just doing the AD-specific bookkeeping yourself.

## The admin-protection trap that is unique to Samba

This one deserves its own section, because it is a real security hole and almost nothing warns you
about it.

On Windows AD, a background process called **SDProp** stamps `adminCount=1` on members of Domain
Admins and the other protected groups, about once an hour. Many tools use that stamp as their
definition of "this is a protected admin, don't let helpdesk touch them."

**Samba never runs SDProp.** An administrator created after the domain was provisioned is never
marked ([Samba bug 13954](https://bugzilla.samba.org/show_bug.cgi?id=13954)). So any tool that keys
its protection on `adminCount` will treat your freshly promoted admins as ordinary users — and
anyone allowed to reset passwords can reset theirs.

The fix, which a tool must do for you, is to stop trusting `adminCount` and instead resolve the
protected groups **by their well-known SID** and walk the membership chain the directory itself
reports. If you're evaluating any web tool against Samba AD, test exactly this: make a new admin,
then try to reset their password from a helpdesk-level account. If it lets you, the tool is relying
on a stamp Samba never applied.

## Where a cross-directory web tool fits

If you want a browser UI with no Windows box, that handles the three gotchas above for you, and that
can manage Samba AD *and* Windows AD *and* OpenLDAP with one set of roles, that's the niche for a
management application such as [Nelsar.Axia](https://github.com/Nelsar/Axia) (the project this blog
belongs to). It treats a Samba DC as Active Directory, connects over LDAPS, writes `unicodePwd`,
and finds protected groups by SID rather than by `adminCount` — and its CI runs against a live Samba
4.22 domain controller on every change, so those assumptions are actually tested.

The trade-off is the usual one for this class of tool: it binds with a service account and enforces
permissions itself, so you gain group-based roles, an audit log and an API, but the job of
protecting your admins moves from the directory into the application. See [how to delegate password
resets safely](helpdesk-reset-ad-password-without-rsat.md) and [the web-interface options for
OpenLDAP](openldap-web-interface-options.md) for the wider picture.

## Side by side

| | Browser | No Windows box | Handles unicodePwd / TLS for you | Protects new admins without SDProp |
| --- | --- | --- | --- | --- |
| `samba-tool` | no (CLI on the DC) | yes | n/a | you script it |
| RSAT / ADUC | no (Windows desktop) | no | yes | yes (on Windows), n/a for Samba's own |
| phpLDAPadmin / LAM | yes | yes | you do it by hand | no, unless you build the check |
| Cross-directory tool | yes | yes | yes | only if it resolves groups by SID |

## How to choose

- **Scripting and automation on the DC:** `samba-tool`.
- **You already run domain-joined Windows and are happy in ADUC:** RSAT works against Samba AD
  unchanged.
- **You want a browser, no Windows, and especially if you also run Windows AD or OpenLDAP:** a
  cross-directory web tool — but verify the `unicodePwd` write and the admin-protection-by-SID before
  you trust it with password resets.

---

Nelsar.Axia is an open-source (Apache-2.0) web interface for Active Directory, Samba AD and
OpenLDAP. You can [try it with one Docker command](../installation.md), or read [what it reads and
writes in each directory](../directories.md), Samba's specifics included.
