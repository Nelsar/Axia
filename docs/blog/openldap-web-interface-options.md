# A web interface for OpenLDAP: the options compared

OpenLDAP ships with `ldapsearch`, `ldapmodify` and a config file. That is enough to run a
directory and far too much to hand to whoever resets passwords on a Tuesday. So most teams reach for
a web interface. There are several, and they are not variations on one idea — they differ on the one
thing that matters most: **who the tool signs in as, and therefore who enforces permissions.**

That single choice decides how safe the tool is and how much it can do. This guide walks the real
options for managing OpenLDAP from a browser, grouped by that distinction.

## First, what "web interface for OpenLDAP" can mean

Three different jobs hide under the same phrase:

1. **A tree editor** — browse and edit raw LDAP entries and attributes, close to the wire.
2. **An account manager** — users, groups and the everyday lifecycle, with the LDAP hidden behind
   forms.
3. **A self-service portal** — let people reset their own password or update their own details.

Pick the tools by the job, not the logo. And one popular option isn't web at all: **Apache
Directory Studio** is an excellent Eclipse desktop application, not a browser tool — worth knowing
so you don't go looking for a web build that doesn't exist.

## Tools that sign in as the user

With these, each person authenticates to the directory with their *own* DN, and OpenLDAP's own
access rules (`olcAccess`) decide what they can see and change. The tool holds no standing
privileges. This is the safest model: there is no account in the middle that, if compromised, hands
over the directory.

- **phpLDAPadmin** — the classic raw tree editor. It shows the directory as it is, attribute by
  attribute, and is unbeatable for poking at schema or fixing one odd entry. It is also showing its
  age, and its freeform editing is easy to get wrong. A power-user tool, not something to give
  helpdesk.
- **LDAP Account Manager (LAM)** — account-focused: users, groups, hosts, with schema-aware forms
  instead of raw attributes. The free edition covers day-to-day management; a paid Pro edition adds
  self-service and more. If you run pure OpenLDAP and are comfortable writing `olcAccess`, LAM gives
  you a clean web UI with **no privileged middleman** — which is strictly safer than anything in the
  next section.
- **LDAP User Manager / FusionDirectory** — a lightweight self-service and account tool, and a
  heavier plugin-based identity suite, respectively. Both are worth a look if self-service or a full
  IAM platform is what you're after.

The cost of this model is the flip side of its safety: your permissions live in `olcAccess` rules
on the directory tree. They are powerful but hard to read, a mistake in their order silently grants
more than intended, and "what can this person do?" has to be reconstructed from several rules.

## Tools that sign in as a service account

Here the tool binds to the directory once, with a single service account that has broad rights, and
decides *for itself* who may do what. People log into the tool, not the directory.

- **[Nelsar.Axia](https://github.com/Nelsar/Axia)** (the project this blog belongs to) is in this
  group, as are commercial products like Adaxes and ManageEngine ADManager Plus. Directory groups
  map to roles, roles to a set of permissions, and permissions are checked on every request.

Why choose this model at all, if it's less safe by default?

- **It spans directories.** One instance can manage OpenLDAP *and* Active Directory (and Samba AD)
  with the same roles and the same UI. A bind-as-user tool is tied to one directory's auth.
- **Permissions are about people, not the tree.** "Members of this group get this role" is easier to
  reason about than ACLs spread across OUs.
- **It can do things a thin client can't:** bulk provisioning, a login generator, and an audit log
  that records *refused* attempts, not just changes.
- **An API.** Onboarding and offboarding scripts call a documented REST endpoint instead of
  scripting LDAP.

And the catch, stated plainly: once a privileged service account stands between people and the
directory, **the directory stops protecting you from privilege escalation, and the tool has to
rebuild that protection.** The most important rule — someone who can reset passwords must not be
able to reset an administrator's — is now the application's job, not OpenLDAP's. It's worth testing
before you trust any such tool; see [how to delegate password resets
safely](helpdesk-reset-ad-password-without-rsat.md).

## Side by side

| | Signs in as | Best for | AD too | Audit log | REST API |
| --- | --- | --- | --- | --- | --- |
| phpLDAPadmin | the user | raw tree edits, schema work | via LDAP | no | no |
| LDAP Account Manager | the user | OpenLDAP account management | limited | no | no |
| FusionDirectory | a manager DN | full IAM, many plugins | partial | some | no |
| Apache Directory Studio | the user (desktop app) | developers, deep inspection | yes | no | no |
| Nelsar.Axia | a service account | AD + OpenLDAP, role-based delegation | yes | yes (refusals too) | yes |

## How to choose

- **You run pure OpenLDAP and write `olcAccess` with confidence.** Use **LAM**. The directory stays
  the single enforcement point, and there's no privileged middleman to protect.
- **You need to poke at raw entries and schema.** **phpLDAPadmin**, or **Apache Directory Studio**
  on the desktop.
- **You run both AD and OpenLDAP, want to delegate by group rather than by ACL, and want an audit
  log of refusals and an API.** A service-account tool like **Nelsar.Axia** earns its keep — provided
  you verify its admin-protection before production.

There is no single best answer, which is the honest conclusion: the right tool follows from whether
you want the directory or the application to be the thing that enforces your permissions.

---

Nelsar.Axia is an open-source (Apache-2.0) web interface for Active Directory and OpenLDAP. You can
[try it with one Docker command](../installation.md) against a demo OpenLDAP, or read how it
[maps roles from directory groups and protects administrators](../access-control.md).
