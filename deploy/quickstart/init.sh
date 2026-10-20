#!/bin/sh
# Generates what the quick start needs and nobody should have to type: a CA and a certificate for
# the directory, so the API reaches it over LDAPS like any real deployment, the API's signing and
# encryption keys, and the database's password. Runs before everything else on each
# `docker compose up`, and makes only what is missing, so all of it stays the same across restarts.
#
#   /ldap-certs   -> openldap:  ca.crt, ldap.crt, ldap.key
#   /api-secrets  -> api:       ca.crt, api.env (Jwt__Key, Encryption__Key, the connection string)
#   /db-secrets   -> postgres:  postgres-password
#
# One volume per container so that none of them can read another's secrets.
#
# Runs in the OpenLDAP image, for its openssl: nothing is downloaded, so a first start works behind
# a proxy that a package manager inside a container could not get through.

set -eu

# The API runs as the .NET images' unprivileged user, and has to read its keys. Earlier versions
# ran it as root, so files they created are root's; this brings them along on every start.
api_uid=1654
# The postgres user of the postgres:*-alpine images, which reads its password file after dropping
# root.
postgres_uid=70

if [ ! -f /ldap-certs/ldap.crt ]; then
    work=$(mktemp -d)
    cd "$work"

    # The CA signs the one certificate below and is then thrown away: nothing else will ever need
    # signing, and a CA key left lying around is a CA anyone with the volume can use.
    openssl req -x509 -newkey rsa:2048 -nodes -days 825 \
        -subj "/CN=Nelsar.Axia quick start CA" -keyout ca.key -out ca.crt 2>/dev/null

    # "openldap" is the directory's service name, which is what the API connects to — the name
    # the certificate has to carry for the API to accept it.
    openssl req -newkey rsa:2048 -nodes -subj "/CN=openldap" -keyout ldap.key -out ldap.csr 2>/dev/null
    printf 'subjectAltName=DNS:openldap\nextendedKeyUsage=serverAuth\n' > ldap.ext
    openssl x509 -req -days 825 -in ldap.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
        -extfile ldap.ext -out ldap.crt 2>/dev/null

    cp ca.crt ldap.crt ldap.key /ldap-certs/
    cp ca.crt /api-secrets/

    cd /
    rm -rf "$work"
    echo "Generated a CA and a certificate for openldap."
fi

# The API trusts the directory through this copy; a recreated secrets volume gets it back.
[ -f /api-secrets/ca.crt ] || cp /ldap-certs/ca.crt /api-secrets/

umask 077

if [ ! -f /api-secrets/api.env ]; then
    {
        printf 'Jwt__Key=%s\n' "$(openssl rand -base64 48 | tr -d '\n')"
        printf 'Encryption__Key=%s\n' "$(openssl rand -base64 32 | tr -d '\n')"
    } > /api-secrets/api.env
    echo "Generated the API's keys."
fi

# Hex, so that nothing in it means anything inside a connection string. A quick start set up
# before it had a database server gets one here too, on its next start.
if [ ! -f /db-secrets/postgres-password ]; then
    openssl rand -hex 24 | tr -d '\n' > /db-secrets/postgres-password
    echo "Generated the database's password."
fi

# Written afresh on every start, from the password file, so the two cannot drift apart. Quoted: the
# API's entrypoint reads this file with the shell, which would end the value at the first ";".
sed -i '/^ConnectionStrings__DefaultConnection=/d' /api-secrets/api.env
printf "ConnectionStrings__DefaultConnection='Host=postgres;Database=nelsar_axia;Username=nelsar_axia;Password=%s'\n" \
    "$(cat /db-secrets/postgres-password)" >> /api-secrets/api.env

chown "$api_uid" /api-secrets/api.env
chown "$postgres_uid" /db-secrets/postgres-password
