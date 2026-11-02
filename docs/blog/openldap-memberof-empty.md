# Why memberOf is empty in OpenLDAP, and how to fix it

You query a user in OpenLDAP, expect to see their groups in `memberOf`, and the attribute isn't
there — or it's empty, even though the user is clearly a member of a group. A filter like
`(memberOf=cn=admins,ou=groups,dc=example,dc=org)` returns nobody. In Active Directory this just
works; in OpenLDAP it doesn't.

The reason is simple and catches almost everyone: **OpenLDAP does not maintain `memberOf` by
default.** Membership is stored on the *group* (`member` points at the user), not on the user. The
back-reference on the user only exists if you turn it on. Here's how, and — more importantly — why
you probably shouldn't rely on it anyway.

## Why it's empty

In Active Directory, `memberOf` is computed and always present. In OpenLDAP, a group is a
`groupOfNames` whose `member` attributes list the users. Nothing writes the reverse link onto each
user unless the **`memberof` overlay** (`slapo-memberof`) is loaded. A default install doesn't load
it, so `memberOf` is simply never populated.

## Fix 1: enable the memberof overlay

On a modern `cn=config` (dynamic config) setup, load the module and attach the overlay to your
database. Adjust `{1}mdb` to your actual database (check with
`ldapsearch -Y EXTERNAL -H ldapi:/// -b cn=config olcDatabase`):

```ldif
# Load the module (once per server)
dn: cn=module{0},cn=config
changetype: modify
add: olcModuleLoad
olcModuleLoad: memberof

# Attach the overlay to the database
dn: olcOverlay=memberof,olcDatabase={1}mdb,cn=config
changetype: add
objectClass: olcOverlayConfig
objectClass: olcMemberOf
olcOverlay: memberof
olcMemberOfRefInt: TRUE
olcMemberOfGroupOC: groupOfNames
olcMemberOfMemberAD: member
olcMemberOfMemberOfAD: memberOf
```

Apply it with `ldapmodify -Y EXTERNAL -H ldapi:/// -f memberof.ldif`.

Add the **referential integrity (`refint`) overlay** too, so `memberOf` (and `member`) stay correct
when an entry is renamed or deleted — without it, deleting a user leaves dangling `member` values:

```ldif
dn: cn=module{0},cn=config
changetype: modify
add: olcModuleLoad
olcModuleLoad: refint

dn: olcOverlay=refint,olcDatabase={1}mdb,cn=config
changetype: add
objectClass: olcOverlayConfig
objectClass: olcRefintConfig
olcOverlay: refint
olcRefintAttribute: memberof member manager owner
```

## The three catches the overlay leaves you with

Turning it on is not the end of it. These are exactly the cases where `memberOf` still comes back
wrong:

1. **It doesn't backfill.** The overlay only maintains `memberOf` for memberships changed *after*
   it's loaded. Users who were already in groups get nothing until their membership is rewritten.
   To populate existing entries, re-add the memberships (remove and re-add each `member`), or reload
   the database with `slapcat` / `slapadd`.
2. **It watches one object class and one attribute.** By default that's `groupOfNames` with
   `member`. If your groups are **`groupOfUniqueNames`** (with `uniqueMember`) or POSIX
   **`posixGroup`** (with `memberUid`), the overlay ignores them and `memberOf` stays empty for
   those memberships. You can point `olcMemberOfGroupOC` / `olcMemberOfMemberAD` at one alternative,
   but not all of them at once.
3. **It doesn't expand nested groups.** `memberOf` lists only the groups the user is a *direct*
   member of. A user in a group that is itself a member of another group won't show the outer group.

On a replicated setup there's a fourth: run the overlay where writes happen, and let replication
carry the resulting `memberOf` to the replicas, rather than adding it independently on each.

## Fix 2 (the robust one): don't rely on memberOf at all

If what you actually need is "which groups is this user in?" — for a login, a permission check, an
app — the dependable answer is to **search from the group side**, which works whether or not the
overlay is loaded and whatever object class your groups use. Ask the directory for every group that
names this user, in all three membership forms at once:

```text
(|(&(objectClass=groupOfNames)(member=<userDN>))
  (&(objectClass=groupOfUniqueNames)(uniqueMember=<userDN>))
  (&(objectClass=posixGroup)(memberUid=<uid>)))
```

A few rules make this safe:

- **Escape the DN** you substitute in. A name can contain parentheses or an asterisk, and unescaped
  they end the filter early or widen it to other people's groups (RFC 4515 escaping).
- It returns **direct** memberships, same as the overlay; expand nesting yourself only if you
  actually need it.
- It's one extra query per user, which is fine for a login or a permission check, though not for
  listing thousands of users at once.

This is the approach to prefer in code, because it has no server-side prerequisite and no "did
someone load the overlay?" failure mode. An application that silently gets an empty group list is
worse than one that always asks the right question.

## How to verify

- Overlay loaded? `ldapsearch -Y EXTERNAL -H ldapi:/// -b cn=config "(olcOverlay=memberof)"`.
- A user's groups, the reliable way: run the filter above with their DN and uid.
- A specific membership: `ldapsearch -b dc=example,dc=org "(&(objectClass=groupOfNames)(member=<userDN>))" dn`.

If the overlay route shows nothing but the reverse search does, you're hitting one of the three
catches above — most often backfill, or the wrong object class.

## Doing it once, correctly

[Nelsar.Axia](https://github.com/Nelsar/Axia) (the project this blog belongs to) takes the reverse
approach: it searches for the groups that name the account in all three forms, merges that with
`memberOf` where the directory does provide it, and escapes the DN. So group membership — and the
permissions that hang off it — are right against a default OpenLDAP with no overlay, against one
configured only for `groupOfNames`, and against Active Directory alike.

---

Nelsar.Axia is an open-source (Apache-2.0) web interface for Active Directory, Samba AD and
OpenLDAP. You can [try it with one Docker command](../installation.md) against a demo OpenLDAP and
see group membership resolved without the overlay.
