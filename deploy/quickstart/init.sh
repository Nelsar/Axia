#!/bin/sh
# Generates what the quick start needs and nobody should have to type: a CA and a certificate for
# the directory, so the API reaches it over LDAPS like any real deployment, and the API's signing
# and encryption keys. Runs before everything else on each `docker compose up`, and does nothing
# once its files exist, so the keys stay the same across restarts.
#
#   /ldap-certs   -> openldap:  ca.crt, ldap.crt, ldap.key
#   /api-secrets  -> api:       ca.crt, api.env (Jwt__Key, Encryption__Key)
#
# Two volumes rather than one so that neither container can read the other's secrets.
#
# Runs in the OpenLDAP image, for its openssl: nothing is downloaded, so a first start works behind
# a proxy that a package manager inside a container could not get through.

set -eu

# The API runs as the .NET images' unprivileged user, and has to read its keys and write its
# database. Earlier versions ran it as root, so volumes they created are root's; this brings them
# along on every start, before the early exit below.
api_uid=1654
chown -R "$api_uid" /api-data
if [ -f /api-secrets/api.env ]; then chown "$api_uid" /api-secrets/api.env; fi

if [ -f /api-secrets/api.env ] && [ -f /ldap-certs/ldap.crt ]; then
    echo "Quick start keys and certificates already exist; leaving them as they are."
    exit 0
fi

work=$(mktemp -d)
cd "$work"

# The CA signs the one certificate below and is then thrown away: nothing else will ever need
# signing, and a CA key left lying around is a CA anyone with the volume can use.
openssl req -x509 -newkey rsa:2048 -nodes -days 825 \
    -subj "/CN=Nelsar.Axia quick start CA" -keyout ca.key -out ca.crt 2>/dev/null

# "openldap" is the directory's service name, which is what the API connects to — the name the
# certificate has to carry for the API to accept it.
openssl req -newkey rsa:2048 -nodes -subj "/CN=openldap" -keyout ldap.key -out ldap.csr 2>/dev/null
printf 'subjectAltName=DNS:openldap\nextendedKeyUsage=serverAuth\n' > ldap.ext
openssl x509 -req -days 825 -in ldap.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
    -extfile ldap.ext -out ldap.crt 2>/dev/null

cp ca.crt ldap.crt ldap.key /ldap-certs/
cp ca.crt /api-secrets/

umask 077
{
    printf 'Jwt__Key=%s\n' "$(openssl rand -base64 48 | tr -d '\n')"
    printf 'Encryption__Key=%s\n' "$(openssl rand -base64 32 | tr -d '\n')"
} > /api-secrets/api.env
chown "$api_uid" /api-secrets/api.env

cd /
rm -rf "$work"
echo "Generated a CA, a certificate for openldap and the API's keys."
