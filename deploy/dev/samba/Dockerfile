# A Samba Active Directory domain controller for development and CI: the AD that runs anywhere
# Docker does. See provision.sh for what is in it, docs/directories.md for how the API talks to it.
FROM debian:trixie-slim

RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        samba samba-ad-dc samba-ad-provision samba-dsdb-modules samba-vfs-modules \
        winbind ldb-tools ldap-utils openssl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && rm -f /etc/samba/smb.conf

COPY provision.sh /provision.sh

# LDAP, LDAPS and the Global Catalog. Kerberos and the rest are not needed by the API.
EXPOSE 389 636 3268 3269

ENTRYPOINT ["sh", "/provision.sh"]
