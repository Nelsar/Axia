# Setting an Active Directory password over LDAP: unicodePwd vs userPassword

You wrote a new password to a user's `userPassword` attribute over LDAP. The server returned
success. The user still signs in with their *old* password, and the new one is sitting in the
directory in plain text. Or you tried `unicodePwd` and got `unwillingToPerform`, or
`constraintViolation`, and no explanation.

This is one of the most common ways to get LDAP password changes wrong against Active Directory. The
short version: **AD does not set passwords through `userPassword`. It uses `unicodePwd`, with an
exact encoding, over an encrypted connection only.** Here is the whole picture, including the errors
and why each happens.

## Why `userPassword` doesn't change an AD password

In OpenLDAP, `userPassword` is *the* password attribute, and (with a hashing policy) writing it sets
the account's password. The habit carries straight over to Active Directory, where it quietly does
the wrong thing.

In AD, `userPassword` is, by default, an **ordinary string attribute**. Unless the domain's
`dSHeuristics` turns on `fUserPwdSupport`, writing it:

- does **not** change the password the account signs in with, and
- stores your new value as **plain text** on the object, readable by anyone who can read the
  attribute.

So the write "succeeds", the login is unchanged, and you've leaked a password into the directory.
That is the worst of both worlds, and no error tells you.

## The right way: `unicodePwd`

Active Directory sets passwords through the **`unicodePwd`** attribute, and it is fussy about two
things: the encoding and the connection.

**The encoding.** The value is the password **wrapped in double quotes**, then encoded as
**UTF-16LE** (little-endian). In .NET that's `Encoding.Unicode`:

```csharp
// Admin reset: one MODIFY that REPLACEs unicodePwd.
byte[] value = Encoding.Unicode.GetBytes($"\"{password}\"");
// e.g. new DirectoryAttributeModification {
//   Name = "unicodePwd",
//   Operation = DirectoryAttributeOperation.Replace,
//   [ value ] }
```

The quotes are part of the value, not syntax — forget them and AD rejects it. The encoding is
UTF-16LE specifically; UTF-8 won't do.

**The connection.** AD accepts `unicodePwd` **only over an encrypted channel** — LDAPS or LDAP with
StartTLS. Over a plain LDAP connection it answers `unwillingToPerform` and changes nothing. This is
deliberate: the password crosses the wire inside the modify request, so AD refuses to carry it in
the clear.

**Reset vs. self-service change.** There are two shapes:

- **An administrator resetting someone's password** sends a single `REPLACE` of `unicodePwd` with
  the new quoted value.
- **A user changing their own password** sends one MODIFY with two operations: a `DELETE` of the old
  quoted value and an `ADD` of the new one. This proves they know the current password and respects
  minimum-password-age, which a reset bypasses.

## OpenLDAP does it differently: the Password Modify extended operation

If your code talks to both AD and OpenLDAP, note that OpenLDAP's correct path is **not** an
attribute write at all. It's the **Password Modify extended operation** (RFC 3062): you ask the
server to set the password, and it hashes and stores it according to its own policy. Writing
`userPassword` directly on OpenLDAP without a hashing overlay stores the password unhashed — the
same plaintext trap, from the other side.

So "set an LDAP password" has two correct implementations, and you pick by the directory.

## How to tell which directory you're talking to

Don't guess from configuration — ask the server. Read the **root DSE**:

- Active Directory advertises the capability OID **`1.2.840.113556.1.4.800`** in
  `supportedCapabilities`. If it's there, use `unicodePwd`.
- Servers that support Password Modify list **`1.3.6.1.4.1.4203.1.11.1`** in `supportedExtension`.
  If that's there (and the AD capability isn't), use the extended operation.

If neither is present, don't fall back to writing `userPassword` — you'd be writing plaintext.
Better to fail loudly than to store a password in the clear.

## The errors, and what they mean

| LDAP result | Code | What it usually means |
| --- | --- | --- |
| `unwillingToPerform` | 53 | You sent `unicodePwd` over a plain connection. Use LDAPS or StartTLS. |
| `constraintViolation` | 19 | The password broke policy: too short, not complex enough, in the history, or below minimum age. Surface it as a clear "password rejected", not "directory error". |
| success, but login unchanged | — | You wrote `userPassword` on AD. Switch to `unicodePwd`. |
| `insufficientAccessRights` | 50 | The binding account may not reset this user. On a reset, that's an ACL/delegation problem. |

A tool worth using turns `constraintViolation` into a 400-style "that password isn't allowed" rather
than a vague failure, because it's a user mistake, not an outage.

## Doing it once, correctly

This is exactly the kind of detail worth writing once, behind an interface, and never again in each
caller. [Nelsar.Axia](https://github.com/Nelsar/Axia) (the project this blog belongs to) decides
`unicodePwd` vs. Password Modify from the root DSE, sends the quoted UTF-16LE value over an encrypted
connection, refuses to write a password at all if neither capability is present, and maps
`constraintViolation` to a clear "password rejected". The same reset flow then works against
Windows AD, Samba AD and OpenLDAP without the caller knowing which it's talking to — see
[what Axia reads and writes in each directory](../directories.md).

---

Nelsar.Axia is an open-source (Apache-2.0) web interface for Active Directory, Samba AD and
OpenLDAP. You can [try it with one Docker command](../installation.md) and watch a password reset go
to the right place.
