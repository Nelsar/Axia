# How to let helpdesk reset Active Directory passwords without RSAT

Resetting passwords is the single most common thing helpdesk does, and the single thing you least
want to hand out carelessly. The person who can reset a password can, in effect, sign in as that
account. So the real question isn't "how do I let helpdesk reset passwords" — it's "how do I let
them reset *ordinary* passwords without also being able to reset an administrator's."

Here are four ways to do it, from the most built-in to the most hands-off, and the trade-off each
one carries.

## Option 1: Delegation of Control in Active Directory

Active Directory has this built in. Right-click an organizational unit (OU), run the **Delegation
of Control Wizard**, pick a group like `Helpdesk`, and grant *Reset user passwords and force
password change at next logon* on that OU.

- **Pro:** the directory itself enforces it. No extra software, no account with broad rights
  sitting in the middle. Delegation is scoped to the OU, so helpdesk can touch `ou=Staff` but not
  `ou=Admins`.
- **Pro:** administrators are protected automatically. AD's **AdminSDHolder / SDProp** mechanism
  strips delegated permissions back off privileged accounts (Domain Admins and the other protected
  groups) about once an hour, so a password-reset delegation simply doesn't apply to them.
- **Con:** it still needs **RSAT** (Active Directory Users and Computers) on a domain-joined
  Windows machine, or a tool that speaks the same delegation. Helpdesk on a Mac, or working from
  home, or on a tablet, is out.
- **Con:** the rules live on the OU tree, so answering "what can this person reset?" means reading
  the ACLs of several OUs.

If you are AD-only and helpdesk already has RSAT on a domain-joined box, this is usually the right
answer. Everything below exists for the cases where that assumption breaks.

## Option 2: RSAT on a jump host

Keep the delegation from Option 1, but instead of installing RSAT on every helpdesk machine, put it
on one **jump host** — a domain-joined server helpdesk reaches over RDP.

- **Pro:** one machine to patch and audit; delegation still enforced by the directory.
- **Con:** an RDP session into a domain-joined server is a bigger footprint than a web page, and
  you're licensing and maintaining that host.

## Option 3: PowerShell behind a constrained endpoint

Wrap `Set-ADAccountPassword` in a script and expose it through **PowerShell constrained endpoints**
(JEA — Just Enough Administration), so helpdesk runs *only* that command, against *only* the OUs you
allow, as a service identity.

- **Pro:** very tight; everything is logged; no broad rights handed to a person.
- **Con:** you are now building and maintaining software. The JEA role configs, the self-protection
  ("don't let this reset an admin"), the audit trail — all of it is yours to write and test. This
  is a real project, not a config change.

## Option 4: A web tool with its own permission model

A browser-based console (ManageEngine ADManager Plus, Adaxes, or open-source tools like
[Nelsar.Axia](https://github.com/Nelsar/Axia)) connects to the directory with one **service
account** and decides for itself who may do what — directory groups map to roles, roles to
permissions.

- **Pro:** no RSAT, no domain-joined machine, works from any browser. One place to answer "what can
  this person do?". Usually works across AD *and* OpenLDAP.
- **Pro:** these tools can also cover what Options 1–3 can't easily: bulk resets, a login generator,
  an audit log that records refused attempts too.
- **Con, and it's the important one:** the directory no longer protects your admins, because every
  change reaches it as the service account, which can change anyone. **That protection now has to be
  rebuilt inside the tool.** AdminSDHolder stops helping you here.

## The rule every option has to hold

Whatever you pick, there is one invariant that must stay true: **someone who can reset passwords
must not be able to reset an administrator's password** and sign in as them.

- Options 1 and 2 get this for free, from AdminSDHolder.
- Options 3 and 4 have to enforce it themselves. The tool must check, on every reset, that the
  target isn't more privileged than the caller — measured both in the tool's own roles *and* in the
  directory's protected groups.

This is worth testing directly. In any tool you evaluate, create a helpdesk account that can reset
passwords, then try to reset a Domain Admin's password with it. If it lets you, the tool isn't
protecting you — and some do, especially for an admin who was promoted recently. Active Directory
only marks protected accounts via SDProp about once an hour, and Samba AD doesn't mark them at all
unless the tool resolves the protected groups itself.

## Which to choose

| | RSAT needed | Protects admins | Works from any browser | Across AD + OpenLDAP |
| --- | --- | --- | --- | --- |
| OU delegation | yes | yes (AdminSDHolder) | no | AD only |
| Jump host | yes (one host) | yes | over RDP | AD only |
| PowerShell + JEA | no | you build it | no | AD only |
| Web tool | no | the tool must | yes | usually yes |

If you're AD-only with RSAT in place, use delegation. If helpdesk is on mixed devices, or you run
OpenLDAP or Samba AD, or you want bulk operations and an audit log of refused attempts, a web tool
is the practical choice — just verify the admin-protection rule before you trust it.

---

Nelsar.Axia is an open-source (Apache-2.0) web interface for Active Directory and OpenLDAP, and it
is where the "try to reset an admin's password" test above comes from: its demo ships an account
that can do everything *except* touch a protected admin. You can [try it with one Docker
command](../installation.md) against a demo directory, or read how it [protects administrators and
maps roles from directory groups](../access-control.md).
