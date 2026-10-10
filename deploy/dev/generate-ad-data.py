#!/usr/bin/env python3
"""Writes ad-data.ldif: a directory laid out the way an Active Directory domain usually is, for
trying the API out at a realistic size. Needs ad-schema.ldif loaded first (the user, computer and
group classes come from it).

    ou=Corp,dc=example,dc=org
      ou=Users      ou=<office>,ou=<department>   accounts, a few disabled; ou=Disabled Users
      ou=Computers  ou=Workstations,ou=<office>   desktops and laptops
                    ou=Servers,ou=<role>          Windows and Linux servers
                    ou=Printers                   RFC 4519 device entries, not AD computers
      ou=Groups     ou=Security, ou=Distribution, ou=Departments
    cn=Domain Admins, cn=Domain Users, cn=Domain Computers ... in ou=Groups,ou=Corp

Seeded, so the same file comes out every time:

    python3 deploy/dev/generate-ad-data.py                         # 1200 users, 2000 computers
    python3 deploy/dev/generate-ad-data.py --users 3000 --computers 5000

The file is loaded on the first start of an empty directory (see docker-compose.ldap.yml). Every
account's password is Axia-Demo-1. Values are ASCII, so the LDIF needs no base64.
"""

import argparse
import random
from pathlib import Path

BASE = "dc=example,dc=org"
DOMAIN = "example.org"
NETBIOS_LIMIT = 15
CORP = f"ou=Corp,{BASE}"
USERS = f"ou=Users,{CORP}"
COMPUTERS = f"ou=Computers,{CORP}"
GROUPS = f"ou=Groups,{CORP}"
PASSWORD = "Axia-Demo-1"
COMPANY = "Example LLC"

# userAccountControl
NORMAL_ACCOUNT = 0x200
WORKSTATION_TRUST_ACCOUNT = 0x1000
SERVER_TRUST_ACCOUNT = 0x2000
ACCOUNTDISABLE = 0x2
DONT_EXPIRE_PASSWORD = 0x10000

# groupType: scope | 0x80000000 for a security group
GLOBAL, DOMAIN_LOCAL, UNIVERSAL = 0x2, 0x4, 0x8
SECURITY = -0x80000000

OFFICES = {"ALA": "Almaty", "AST": "Astana", "SHY": "Shymkent", "KRG": "Karaganda"}

DEPARTMENTS = {
    "IT": ["Developer", "System administrator", "QA engineer", "DevOps engineer", "Support engineer", "IT manager"],
    "Finance": ["Accountant", "Financial analyst", "Chief accountant", "Payroll specialist", "Finance manager"],
    "Sales": ["Sales manager", "Account manager", "Sales assistant", "Key account manager", "Head of sales"],
    "HR": ["HR manager", "Recruiter", "HR specialist", "Head of HR"],
    "Marketing": ["Marketing manager", "Content manager", "Designer", "SMM specialist"],
    "Support": ["Support agent", "Support team lead", "Quality specialist"],
    "Logistics": ["Logistics coordinator", "Warehouse manager", "Driver", "Dispatcher"],
    "Legal": ["Lawyer", "Legal counsel", "Compliance officer"],
    "Management": ["Director", "Deputy director", "Executive assistant", "Office manager"],
}
DEPARTMENT_WEIGHTS = {"IT": 14, "Finance": 10, "Sales": 18, "HR": 5, "Marketing": 8, "Support": 16,
                      "Logistics": 14, "Legal": 4, "Management": 3}

SERVER_ROLES = {
    "Domain Controllers": ("DC", ["Windows Server 2022 Standard"]),
    "File": ("FS", ["Windows Server 2019 Standard", "Windows Server 2022 Standard"]),
    "Database": ("DB", ["Windows Server 2022 Datacenter", "Ubuntu 22.04 LTS", "Red Hat Enterprise Linux 9"]),
    "Web": ("WEB", ["Ubuntu 22.04 LTS", "Ubuntu 24.04 LTS", "Windows Server 2022 Standard"]),
    "Application": ("APP", ["Windows Server 2019 Standard", "Windows Server 2022 Standard", "Ubuntu 22.04 LTS"]),
    "Remote Desktop": ("RDS", ["Windows Server 2022 Standard"]),
}
OS_VERSIONS = {
    "Windows 11 Enterprise": ["10.0 (22631)", "10.0 (26100)"],
    "Windows 11 Pro": ["10.0 (22631)"],
    "Windows 10 Enterprise": ["10.0 (19045)"],
    "Windows 10 Pro": ["10.0 (19045)", "10.0 (19044)"],
    "Windows Server 2019 Standard": ["10.0 (17763)"],
    "Windows Server 2022 Standard": ["10.0 (20348)"],
    "Windows Server 2022 Datacenter": ["10.0 (20348)"],
    "Ubuntu 22.04 LTS": ["5.15"],
    "Ubuntu 24.04 LTS": ["6.8"],
    "Red Hat Enterprise Linux 9": ["5.14"],
    "macOS 14": ["14.6"],
}
WORKSTATION_OS = (["Windows 11 Enterprise"] * 10 + ["Windows 10 Enterprise"] * 6 + ["Windows 11 Pro"] * 2
                  + ["Windows 10 Pro"] * 2 + ["macOS 14"])

GIVEN = [
    "Aidar", "Aigul", "Aizhan", "Akbota", "Alibek", "Almas", "Amina", "Arman", "Arsen", "Asem", "Askar", "Ayana",
    "Azamat", "Bakhyt", "Baurzhan", "Dana", "Darkhan", "Dauren", "Dilnaz", "Erlan", "Gaukhar", "Kairat", "Kamila",
    "Madina", "Marat", "Meruert", "Nurlan", "Nursultan", "Raushan", "Sanzhar", "Saltanat", "Serik", "Togzhan",
    "Yerbol", "Zarina", "Zhanna", "Zhandos", "Aruzhan", "Bekzat", "Diana", "Inkar", "Miras", "Nazerke", "Olzhas",
    "Alexei", "Anna", "Dmitry", "Ekaterina", "Igor", "Irina", "Maxim", "Natalia", "Oleg", "Pavel", "Sergey",
    "Svetlana", "Tatiana", "Viktor", "Yulia", "Andrei", "Elena", "Mikhail", "Olga", "Roman",
    "Daniel", "Emily", "James", "Laura", "Michael", "Sarah", "Thomas", "Sofia", "Lucas", "Mia",
]
SURNAMES = [
    "Abenov", "Akhmetov", "Alimov", "Baimukhanov", "Bekov", "Dzhaksybekov", "Ermekov", "Iskakov", "Kaliyev",
    "Kassymov", "Kenzhebayev", "Mukanov", "Nazarbekov", "Omarov", "Rakhimov", "Sadykov", "Seitkali", "Suleimenov",
    "Tokayev", "Utepov", "Zhakupov", "Zhumabayev", "Amanov", "Bekturov", "Dosanov", "Karimov", "Nurpeisov",
    "Sarsenov", "Tulegenov", "Yessenov",
    "Ivanov", "Kuznetsov", "Morozov", "Novikov", "Orlov", "Pavlov", "Popov", "Smirnov", "Sokolov", "Volkov",
    "Lebedev", "Fedorov", "Zaitsev",
    "Brown", "Davis", "Garcia", "Johnson", "Miller", "Schmidt", "Taylor", "Wilson",
]


def entry(dn, attributes):
    return "\n".join([f"dn: {dn}"] + [f"{name}: {value}" for name, value in attributes])


def ou(name, parent, description=None):
    attrs = [("objectClass", "organizationalUnit"), ("ou", name)]
    if description:
        attrs.append(("description", description))
    return f"ou={name},{parent}", entry(f"ou={name},{parent}", attrs)


def group(cn, parent, description, group_type, members, managed_by=None, mail=None, admin_count=False):
    attrs = [("objectClass", "top"), ("objectClass", "group"), ("cn", cn), ("sAMAccountName", cn),
             ("groupType", str(group_type)), ("description", description)]
    if managed_by:
        attrs.append(("managedBy", managed_by))
    if mail:
        attrs.append(("mail", mail))
    if admin_count:
        attrs.append(("adminCount", "1"))
    attrs += [("member", m) for m in members]
    return f"cn={cn},{parent}", entry(f"cn={cn},{parent}", attrs)


def filetime(rng, days_ago_max):
    # 100 ns intervals since 1601-01-01; anchored to 2026-10-01 so the file does not change daily.
    anchor = (2026 - 1601) * 365.2425 * 86400 * 10**7
    return str(int(anchor - rng.uniform(0, days_ago_max) * 86400 * 10**7))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--users", type=int, default=1200)
    parser.add_argument("--computers", type=int, default=2000, help="workstations and laptops; servers come on top")
    parser.add_argument("--seed", type=int, default=20261010)
    parser.add_argument("--out", type=Path, default=Path(__file__).with_name("ad-data.ldif"))
    args = parser.parse_args()
    rng = random.Random(args.seed)

    blocks = [f"# Generated by generate-ad-data.py --users {args.users} --computers {args.computers} "
              f"--seed {args.seed}. Do not edit by hand.\n# Every account's password is {PASSWORD}."]

    def add(pair):
        blocks.append(pair[1])
        return pair[0]

    # --- Units --------------------------------------------------------------------------------
    add(ou("Corp", BASE, f"{COMPANY}: the AD-style part of the test directory"))
    add(ou("Users", CORP, "User accounts by office and department"))
    disabled_ou = add(ou("Disabled Users", CORP, "Leavers, kept disabled for 90 days"))
    service_ou = add(ou("Service Accounts", CORP, "Accounts that services run as"))
    add(ou("Computers", CORP, "Computer accounts"))
    ws_root = add(ou("Workstations", COMPUTERS, "Desktops and laptops by office"))
    srv_root = add(ou("Servers", COMPUTERS, "Servers by role"))
    printers_ou = add(ou("Printers", COMPUTERS, "Network printers (device entries)"))
    add(ou("Groups", CORP, "Groups"))
    sec_ou = add(ou("Security", GROUPS, "Access groups"))
    dist_ou = add(ou("Distribution", GROUPS, "Mailing lists"))
    dept_ou = add(ou("Departments", GROUPS, "One group per department and office"))

    dept_units = {}
    for code, city in OFFICES.items():
        office_dn = add(ou(city, USERS, f"{city} office ({code})"))
        for dept in DEPARTMENTS:
            dept_units[(code, dept)] = add(ou(dept, office_dn))
    ws_units = {code: add(ou(city, ws_root, f"{city} office workstations")) for code, city in OFFICES.items()}
    srv_units = {role: add(ou(role, srv_root)) for role in SERVER_ROLES}

    # --- Users --------------------------------------------------------------------------------
    taken = set()
    users = []  # dicts: dn, sam, office, dept, enabled
    office_codes = list(OFFICES)
    office_weights = [45, 30, 15, 10]
    depts, dept_weights = list(DEPARTMENT_WEIGHTS), list(DEPARTMENT_WEIGHTS.values())

    def unique_sam(given, surname):
        base = f"{given}.{surname}".lower()[:20]
        sam, n = base, 2
        while sam in taken:
            sam = f"{base[:20 - len(str(n))]}{n}"
            n += 1
        taken.add(sam)
        return sam

    def user_entry(dn, cn, sam, given, surname, attrs_extra, uac):
        return entry(dn, [
            ("objectClass", "top"), ("objectClass", "person"), ("objectClass", "organizationalPerson"),
            ("objectClass", "user"),
            ("cn", cn), ("sn", surname), *([("givenName", given)] if given else []),
            ("displayName", f"{given} {surname}" if given else sam),
            ("sAMAccountName", sam), ("userPrincipalName", f"{sam}@{DOMAIN}"),
            ("userAccountControl", str(uac)), *attrs_extra, ("userPassword", PASSWORD),
        ])

    heads = {}  # (office, dept) -> dn of the first account there, the manager of the rest
    used_dns = set()
    for number in range(1, args.users + 1):
        given, surname = rng.choice(GIVEN), rng.choice(SURNAMES)
        sam = unique_sam(given, surname)
        code = rng.choices(office_codes, office_weights)[0]
        dept = rng.choices(depts, dept_weights)[0]
        enabled = rng.random() > 0.04
        parent = dept_units[(code, dept)] if enabled else disabled_ou
        # cn is the display name, as in AD; two people with the same name in one unit get the
        # login appended, as an AD admin would have to.
        cn = f"{given} {surname}"
        if f"cn={cn},{parent}".lower() in used_dns:
            cn = f"{given} {surname} ({sam})"
        dn = f"cn={cn},{parent}"
        used_dns.add(dn.lower())
        extra = [
            ("mail", f"{sam}@{DOMAIN}"), ("title", rng.choice(DEPARTMENTS[dept])), ("department", dept),
            ("company", COMPANY), ("employeeID", f"{number:06d}"), ("l", OFFICES[code]),
            ("telephoneNumber", f"+7 727 {rng.randint(200, 399)} {rng.randint(1000, 9999)}"),
            ("mobile", f"+7 70{rng.randint(0, 8)} {rng.randint(100, 999)} {rng.randint(1000, 9999)}"),
            ("lastLogonTimestamp", filetime(rng, 30 if enabled else 200)),
        ]
        if enabled and (code, dept) in heads:
            extra.append(("manager", heads[(code, dept)]))
        if not enabled:
            extra.append(("description", f"Left the company; was {dept}, {OFFICES[code]}"))
        uac = NORMAL_ACCOUNT | (0 if enabled else ACCOUNTDISABLE)
        blocks.append(user_entry(dn, cn, sam, given, surname, extra, uac))
        users.append({"dn": dn, "sam": sam, "office": code, "dept": dept, "enabled": enabled})
        if enabled:
            heads.setdefault((code, dept), dn)

    admins = []
    for name in ["adm.timur", "adm.saule", "adm.alexei"]:
        dn = f"cn={name},{service_ou}"
        blocks.append(user_entry(dn, name, name, None, name, [("description", "Administrator account"), ("adminCount", "1"),
                                                         ("department", "IT"), ("company", COMPANY)],
                                 NORMAL_ACCOUNT | DONT_EXPIRE_PASSWORD))
        admins.append(dn)
    services = []
    for name, what in [("svc.backup", "Backup agent"), ("svc.sql", "SQL Server"), ("svc.web", "IIS app pools"),
                       ("svc.scan", "Scan to folder"), ("svc.monitoring", "Monitoring"), ("svc.axia", "Nelsar.Axia bind")]:
        dn = f"cn={name},{service_ou}"
        blocks.append(user_entry(dn, name, name, None, name, [("description", what), ("company", COMPANY)],
                                 NORMAL_ACCOUNT | DONT_EXPIRE_PASSWORD))
        services.append(dn)

    enabled_users = [u for u in users if u["enabled"]]

    # --- Computers ----------------------------------------------------------------------------
    def computer(name, parent, os_name, description, uac, managed_by=None, location=None):
        dn = f"cn={name},{parent}"
        attrs = [
            ("objectClass", "top"), ("objectClass", "person"), ("objectClass", "organizationalPerson"),
            ("objectClass", "user"), ("objectClass", "computer"),
            # sn only because OpenLDAP's person requires it; AD's computers carry none.
            ("cn", name), ("sn", name), ("sAMAccountName", f"{name}$"),
            ("dNSHostName", f"{name.lower()}.{DOMAIN}"),
            ("operatingSystem", os_name), ("operatingSystemVersion", rng.choice(OS_VERSIONS[os_name])),
            ("userAccountControl", str(uac)), ("description", description),
            ("lastLogonTimestamp", filetime(rng, 14 if not uac & ACCOUNTDISABLE else 300)),
        ]
        if managed_by:
            attrs.append(("managedBy", managed_by))
        if location:
            attrs.append(("location", location))
        blocks.append(entry(dn, attrs))
        return dn

    workstations = {code: [] for code in OFFICES}
    all_computers, servers = [], []
    owners = rng.sample(enabled_users, min(len(enabled_users), args.computers))
    for number in range(1, args.computers + 1):
        owner = owners[number - 1] if number <= len(owners) else rng.choice(enabled_users)
        code = owner["office"]
        laptop = rng.random() < 0.45
        name = f"{code}-{'NB' if laptop else 'WS'}-{number:05d}"[:NETBIOS_LIMIT]
        disabled = rng.random() < 0.06
        os_name = rng.choice(WORKSTATION_OS)
        description = (f"{'Laptop' if laptop else 'Desktop'} of {owner['sam']}" if not disabled
                       else "Decommissioned, awaiting deletion")
        dn = computer(name, ws_units[code], os_name, description,
                      WORKSTATION_TRUST_ACCOUNT | (ACCOUNTDISABLE if disabled else 0),
                      managed_by=owner["dn"], location=f"{OFFICES[code]}, floor {rng.randint(1, 9)}")
        workstations[code].append(dn)
        all_computers.append(dn)

    for role, (prefix, os_choices) in SERVER_ROLES.items():
        for code in OFFICES:
            for n in range(1, (3 if role in ("Application", "Web") else 2) + 1):
                name = f"{code}-{prefix}-{n:02d}"
                uac = SERVER_TRUST_ACCOUNT if role == "Domain Controllers" else WORKSTATION_TRUST_ACCOUNT
                dn = computer(name, srv_units[role], rng.choice(os_choices), f"{role} server, {OFFICES[code]}", uac,
                              managed_by=rng.choice(admins), location=f"{OFFICES[code]} server room")
                servers.append(dn)
                all_computers.append(dn)

    for code, city in OFFICES.items():
        for n in range(1, 6):
            name = f"{code}-PRN-{n:02d}"
            blocks.append(entry(f"cn={name},{printers_ou}", [
                ("objectClass", "device"), ("cn", name), ("description", f"Printer, {city} floor {n}"),
                ("l", city), ("serialNumber", f"SN{rng.randint(10**7, 10**8 - 1)}"),
            ]))

    # --- Groups -------------------------------------------------------------------------------
    sec_global = GLOBAL | SECURITY
    sec_local = DOMAIN_LOCAL | SECURITY
    sec_universal = UNIVERSAL | SECURITY

    dept_groups = {}
    for dept in DEPARTMENTS:
        for code, city in OFFICES.items():
            members = [u["dn"] for u in enabled_users if u["dept"] == dept and u["office"] == code]
            dept_groups[(dept, code)] = add(group(f"GG-{dept}-{code}", dept_ou, f"{dept}, {city}", sec_global,
                                                  members, managed_by=heads.get((code, dept))))
        # The department across offices: nested, as AD admins build them. Not followed by the API.
        add(group(f"GG-{dept}", dept_ou, f"{dept}, all offices (nested office groups)", sec_global,
                  [dept_groups[(dept, code)] for code in OFFICES]))

    for code, city in OFFICES.items():
        add(group(f"GG-Office-{code}", dept_ou, f"Everyone in {city}", sec_global,
                  [u["dn"] for u in enabled_users if u["office"] == code]))
        add(group(f"GG-Workstations-{code}", sec_ou, f"Workstations in {city}, for GPO filtering", sec_global,
                  workstations[code]))

    def sample(population, share):
        return rng.sample(population, max(1, int(len(population) * share)))

    user_dns = [u["dn"] for u in enabled_users]
    access = [
        ("DL-VPN-Users", "Remote access over VPN", sample(user_dns, 0.35)),
        ("DL-WiFi-Corp", "Corporate Wi-Fi", sample(user_dns, 0.8)),
        ("DL-RDP-Users", "Remote Desktop to the RDS farm", sample(user_dns, 0.15)),
        ("DL-USB-Allowed", "Exempt from the USB storage block", sample(user_dns, 0.03)),
        ("DL-Internet-Full", "Unfiltered internet access", sample(user_dns, 0.1)),
        ("DL-Local-Admins", "Local administrator on workstations", admins + sample(user_dns, 0.01)),
        ("DL-SQL-Readers", "Read access to reporting databases", sample(user_dns, 0.05)),
        ("DL-Empty-Legacy", "Left over from an old migration; no members", []),
    ]
    for dept in DEPARTMENTS:
        members = [u["dn"] for u in enabled_users if u["dept"] == dept]
        access.append((f"DL-Share-{dept}-RW", f"\\\\fs\\{dept}: modify", sample(members, 0.6) if members else []))
        access.append((f"DL-Share-{dept}-RO", f"\\\\fs\\{dept}: read", sample(user_dns, 0.05)))
    for cn, description, members in access:
        add(group(cn, sec_ou, description, sec_local, members))

    add(group("GG-Servers", sec_ou, "All servers", sec_global, servers))
    add(group("GG-Service-Accounts", sec_ou, "Accounts services run as", sec_global, services))

    for cn, description, members in [
        ("All Staff", "Everyone", [dept_groups[k] for k in dept_groups]),
        ("All Almaty", "Everyone in Almaty", [dept_groups[(d, "ALA")] for d in DEPARTMENTS]),
        ("Managers", "Heads of department", list(heads.values())),
        ("Announcements", "Company announcements", sample(user_dns, 0.5)),
        ("Sales Team", "Sales, all offices", [u["dn"] for u in enabled_users if u["dept"] == "Sales"]),
        ("IT Helpdesk", "helpdesk@ mailbox", [u["dn"] for u in enabled_users if u["dept"] == "Support"][:25]),
    ]:
        add(group(cn, dist_ou, description, UNIVERSAL, members, mail=f"{cn.lower().replace(' ', '-')}@{DOMAIN}"))

    # Built-in-style groups last; Domain Computers is the final entry, which the healthcheck waits for.
    add(group("Domain Admins", GROUPS, "Designated administrators of the domain", sec_global, admins, admin_count=True))
    add(group("Enterprise Admins", GROUPS, "Designated administrators of the enterprise", sec_universal,
              admins[:1], admin_count=True))
    add(group("Server Operators", GROUPS, "Can administer domain servers", sec_local, admins[1:], admin_count=True))
    add(group("Domain Users", GROUPS, "All domain users", sec_global, user_dns + services + admins))
    add(group("Domain Computers", GROUPS, "All workstations and servers joined to the domain", sec_global,
              all_computers))

    args.out.write_text("\n\n".join(blocks) + "\n", encoding="ascii")
    group_count = sum(1 for b in blocks if "objectClass: group\n" in b)
    print(f"Wrote {args.out}: {len(users)} users ({len(users) - len(enabled_users)} disabled), "
          f"{len(admins) + len(services)} admin/service accounts, {len(all_computers)} computers "
          f"({len(servers)} servers), {len(OFFICES) * 5} printers, {group_count} groups")


if __name__ == "__main__":
    main()
