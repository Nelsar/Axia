# adminCount and SDProp: why your newly promoted admin isn't protected

If you manage Active Directory, you've seen the `adminCount` attribute: `1` on your privileged
accounts, absent on everyone else. It's tempting to treat it as *the* flag for "this is an admin,
handle with care" — scripts filter on it, tools use it to decide who's protected. That shortcut has
three failure modes, and each one is a real security gap. Here's what `adminCount` actually means,
where it lies to you, and what to use instead.

## What sets adminCount

`adminCount` is not something you set. It's stamped by **SDProp** (the Security Descriptor
Propagator), a process on the domain controller holding the PDC emulator role. SDProp takes the ACL
on a special container, **AdminSDHolder**, and copies it onto every member of the **protected
groups** — Domain Admins, Enterprise Admins, Schema Admins, Administrators, Account Operators,
Backup Operators, Server Operators, Print Operators, and a few more. On each such account it:

- disables inheritance and overwrites the ACL with AdminSDHolder's, and
- sets `adminCount = 1`.

This is what stops a delegated "helpdesk can reset passwords in this OU" permission from applying to
a Domain Admin: SDProp keeps re-stamping the privileged accounts with a locked-down ACL. Useful.
The trouble starts when anything treats `adminCount` as a reliable, live answer to "is this account
privileged?"

## Failure 1: the up-to-an-hour gap

SDProp doesn't run continuously. By default it runs **every 60 minutes**. So when you add someone to
Domain Admins, they are *not* marked for up to an hour. For that window:

- their `adminCount` is absent, and
- their ACL still carries whatever inherited and delegated permissions they had.

Any tool that decides "is this a protected admin?" by reading `adminCount` will treat a brand-new
Domain Admin as an ordinary user for that hour — and let anyone with a password-reset delegation
reset their password. The most dangerous account in the domain is the least protected, exactly when
it's just been created.

## Failure 2: adminCount is sticky, so it also lies the other way

SDProp sets `adminCount = 1`. It does **not** clear it. Remove someone from Domain Admins and their
`adminCount` stays `1`, and their inheritance stays disabled, forever — an **orphaned adminCount**.

So the attribute gives you false positives as well as false negatives:

- an account flagged `adminCount=1` may not be an admin at all anymore, and
- its broken inheritance quietly causes "why doesn't this user get the permissions everyone else in
  the OU gets?" tickets months later.

Treating `adminCount=1` as "currently privileged" is wrong in both directions: it misses new admins
and over-counts old ones.

## Failure 3: Samba AD never sets it at all

If you run **Samba AD**, this gets worse. Samba does not run SDProp, and never stamps `adminCount`
on an administrator created after the domain was provisioned
([Samba bug 13954](https://bugzilla.samba.org/show_bug.cgi?id=13954)). So on Samba, a tool that
keys protection on `adminCount` protects *almost nobody*: your admins are simply never marked. The
hour-long gap becomes permanent.

## The right way: resolve protected groups by SID

The reliable question isn't "does this account have `adminCount=1`?" It's "**is this account a
member of a protected group, right now?**" — answered against the directory, not an attribute it
may or may not have stamped.

- Identify the protected groups by their **well-known SID**, not by name (names are localized and
  renameable): Administrators `S-1-5-32-544`, and the domain groups by RID — Domain Admins `…-512`,
  Enterprise Admins `…-519`, Schema Admins `…-518`, and so on.
- Resolve membership through the **whole chain the directory reports**: direct members, groups
  nested inside protected groups, and members by **primary group** (a user whose primary group is
  Domain Admins has no `member` entry — `primaryGroupID` is how that's expressed).

That answer is live, correct after a removal, and works identically on Windows AD and Samba AD,
because it asks the directory what is true now instead of reading a cache SDProp may not have
written.

## Check your own directory

Find everything currently flagged, and the orphans among them:

```powershell
# Accounts marked by SDProp at some point
Get-ADObject -LDAPFilter "(adminCount=1)" -Properties adminCount, memberOf

# Orphaned: adminCount=1 but not actually in a protected group anymore
# (review these — their inheritance is disabled and may need fixing)
```

With raw LDAP:

```text
# Flagged accounts
(adminCount=1)

# Members of Domain Admins by SID, resolved through nesting (AD extensible match)
(memberOf:1.2.840.113556.1.4.1941:=<DN of the protected group>)
```

The `1.2.840.113556.1.4.1941` matching rule (LDAP_MATCHING_RULE_IN_CHAIN) is AD's way to walk the
nested membership chain in one query — the live answer `adminCount` only approximates.

## Doing it once, correctly

[Nelsar.Axia](https://github.com/Nelsar/Axia) (the project this blog belongs to) does not trust
`adminCount`. It finds the protected groups by their well-known SID and resolves their members —
nested and by primary group — by asking the directory, so a freshly promoted admin is protected
immediately, a removed one isn't a false positive, and the same logic holds on Samba AD where
`adminCount` is never set. That's what lets it guarantee a helpdesk role can't reset an
administrator's password; see [how it protects administrators](../access-control.md) and
[the Samba specifics](../directories.md#samba-ad).

---

Nelsar.Axia is an open-source (Apache-2.0) web interface for Active Directory, Samba AD and
OpenLDAP. You can [try it with one Docker command](../installation.md) and test the admin-protection
rule for yourself.
