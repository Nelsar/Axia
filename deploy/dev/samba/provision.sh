#!/bin/sh
# Provisions a Samba AD domain on the first start, fills it with the accounts the API's tests and
# demos expect, and runs the domain controller in the foreground. Later starts only run it.
#
#   Domain        AXIA.TEST (NetBIOS AXIA), base DN DC=axia,DC=test
#   Administrator Administrator / $SAMBA_ADMIN_PASSWORD
#   Accounts      admin.demo, creator.demo, editor.demo, user.demo — password Axia-Demo-1, in the
#                 axia-admins, axia-creators and axia-editors groups as the quick start has them
#   it.admin      a member of Domain Admins, added after provisioning: Samba never sets adminCount
#                 on it (no SDProp), which is what the API's protection must not depend on
#   disabled.demo switched off
#   LDAPS         a certificate for dc1, samba, localhost and 127.0.0.1, from a CA written to
#                 /export/ca.crt for whoever has to trust it
#
# Not for anything but development and tests: every password above is in this repository.

set -eu

realm="${SAMBA_REALM:-AXIA.TEST}"
domain="${SAMBA_DOMAIN:-AXIA}"
admin_password="${SAMBA_ADMIN_PASSWORD:-Axia-Admin-1}"
demo_password="Axia-Demo-1"
private=/var/lib/samba/private
tls="$private/tls"

if [ ! -f "$private/sam.ldb" ]; then
    # xattr_tdb rather than the filesystem's extended attributes: sysvol's ACLs need
    # security.* attributes, which a container without CAP_SYS_ADMIN cannot write.
    samba-tool domain provision \
        --realm="$realm" --domain="$domain" --host-name=dc1 \
        --server-role=dc --dns-backend=SAMBA_INTERNAL \
        --adminpass="$admin_password" --use-rfc2307 \
        --option="vfs objects = dfs_samba4 acl_xattr xattr_tdb" \
        --option="xattr_tdb:file = /var/lib/samba/xattr.tdb" \
        --option="dns forwarder = 127.0.0.11"

    # Samba makes a self-signed certificate on its own, for the host's name only. One the API can
    # verify instead, whichever name it is reached by.
    mkdir -p "$tls" /export
    work=$(mktemp -d)
    openssl req -x509 -newkey rsa:2048 -nodes -days 825 -subj "/CN=Axia Samba test CA" \
        -keyout "$work/ca.key" -out "$tls/ca.pem" 2>/dev/null
    openssl req -newkey rsa:2048 -nodes -subj "/CN=dc1.$(echo "$realm" | tr 'A-Z' 'a-z')" \
        -keyout "$tls/key.pem" -out "$work/dc.csr" 2>/dev/null
    printf 'subjectAltName=DNS:dc1.%s,DNS:dc1,DNS:samba,DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\n' \
        "$(echo "$realm" | tr 'A-Z' 'a-z')" > "$work/dc.ext"
    openssl x509 -req -days 825 -in "$work/dc.csr" -CA "$tls/ca.pem" -CAkey "$work/ca.key" \
        -CAcreateserial -extfile "$work/dc.ext" -out "$tls/cert.pem" 2>/dev/null
    chmod 600 "$tls/key.pem"
    rm -rf "$work"

    # Complex enough for the default password policy, which stays on: the API is expected to
    # report a refusal from it.
    for login in admin.demo creator.demo editor.demo user.demo it.admin disabled.demo; do
        samba-tool user create "$login" "$demo_password" \
            --mail-address="$login@$(echo "$realm" | tr 'A-Z' 'a-z')" \
            --given-name="$(echo "$login" | cut -d. -f1)" --surname="$(echo "$login" | cut -d. -f2)"
    done

    for group in axia-admins axia-creators axia-editors; do
        samba-tool group add "$group"
    done
    samba-tool group addmembers axia-admins admin.demo
    samba-tool group addmembers axia-creators creator.demo
    samba-tool group addmembers axia-editors editor.demo
    samba-tool group addmembers "Domain Admins" it.admin
    samba-tool user disable disabled.demo

    echo "Provisioned $realm."
fi

cp "$tls/ca.pem" /export/ca.crt 2>/dev/null || true

exec samba --foreground --no-process-group --debug-stdout
