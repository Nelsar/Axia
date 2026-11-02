# Blog

Notes on running Active Directory, Samba AD and OpenLDAP — delegation, directory internals and the
mistakes that are easy to make — from the people building [Nelsar.Axia](../index.md). Each post
stands on its own; Axia shows up only where it is the honest answer.

- [How to let helpdesk reset Active Directory passwords without RSAT](helpdesk-reset-ad-password-without-rsat.md)
  — four ways to delegate password resets, and the one rule every one of them has to hold.
- [A web interface for OpenLDAP: the options compared](openldap-web-interface-options.md)
  — phpLDAPadmin, LAM and the service-account tools, grouped by who enforces permissions.
- [Managing Samba AD from the browser](manage-samba-ad-from-the-browser.md)
  — the web options for a Samba domain, and the three Samba specifics each has to get right.
- [Setting an Active Directory password over LDAP: unicodePwd vs userPassword](set-ad-password-over-ldap-unicodepwd.md)
  — why userPassword fails silently, how unicodePwd works, and what the errors mean.
- [Why memberOf is empty in OpenLDAP, and how to fix it](openldap-memberof-empty.md)
  — the memberof overlay, the cases it still gets wrong, and the reverse search that always works.
