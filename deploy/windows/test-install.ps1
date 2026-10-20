<#
.SYNOPSIS
  Installs, upgrades and removes the package for real, and checks each step: the service, who it
  runs as, what it answers, who may read its settings, and what an upgrade and an uninstall keep.

.DESCRIPTION
  Run by the Installers workflow on a throwaway Windows runner. It changes the machine it runs on —
  a service, Program Files, ProgramData — so run it yourself only on a machine you can throw away,
  from an elevated PowerShell:

    deploy\windows\test-install.ps1 -Older Nelsar.Axia-0.0.1-x64.msi -Current Nelsar.Axia-0.2.1-x64.msi

  -Older is any package with a lower version, for the upgrade; the workflow builds one from the
  same binaries.
#>
param(
    [Parameter(Mandatory)] [string] $Older,
    [Parameter(Mandatory)] [string] $Current,
    [int] $Port = 8099
)

$ErrorActionPreference = 'Stop'
$service = 'NelsarAxia'
$settings = Join-Path $env:ProgramData 'Nelsar.Axia\appsettings.json'
$installFolder = Join-Path $env:ProgramFiles 'Nelsar.Axia'

function Step([string] $text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Fail([string] $text) { throw "FAILED: $text" }

function Invoke-Msi([string[]] $arguments, [string] $log) {
    $process = Start-Process msiexec.exe -ArgumentList ($arguments + @('/qn', '/l*v', "`"$log`"")) -Wait -PassThru
    if ($process.ExitCode -notin 0, 3010) {
        Get-Content $log -Tail 60 | Write-Host
        Fail "msiexec $($arguments -join ' ') exited with $($process.ExitCode); log above"
    }
}

function Wait-Answers([string] $path) {
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest "http://localhost:$Port$path" -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return $response }
        } catch { Start-Sleep -Seconds 2 }
    }
    Get-ChildItem (Join-Path $env:ProgramData 'Nelsar.Axia\logs') -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime | Select-Object -Last 1 | Get-Content -Tail 40 | Write-Host
    Fail "nothing answered 200 at $path within 90 seconds; newest log above"
}

function Assert-Running {
    $svc = Get-CimInstance Win32_Service -Filter "Name='$service'"
    if (-not $svc) { Fail "service $service is not installed" }
    if ($svc.State -ne 'Running') { Fail "service $service is $($svc.State)" }
    if ($svc.StartName -ne "NT SERVICE\$service") { Fail "service runs as $($svc.StartName)" }
    if ($svc.StartMode -ne 'Auto') { Fail "service starts $($svc.StartMode)" }
}

function Read-Settings { Get-Content $settings -Raw | ConvertFrom-Json }

Step "Install $Older with settings on the command line"
Invoke-Msi @('/i', "`"$Older`"", "PORT=$Port", 'LDAP_SERVER=ldap.example.invalid', 'LDAP_BASEDN=dc=example,dc=org',
    'LDAP_USERNAME=cn=admin,dc=example,dc=org', 'ADMIN_GROUP=cn=admins,dc=example,dc=org', 'START_SERVICE=1') "$env:TEMP\axia-install.log"
Assert-Running
$console = Wait-Answers '/'
if ($console.Headers['Content-Security-Policy'] -notmatch "default-src 'self'") { Fail 'the console came without its policy' }
Wait-Answers '/api/v1/providers/selectable' | Out-Null

Step 'The settings file: written, complete, and readable by nobody but SYSTEM, Administrators and the service'
$first = Read-Settings
if ($first.Ldap.Server -ne 'ldap.example.invalid') { Fail 'LDAP_SERVER did not reach the settings' }
if ($first.RoleMappings.GroupDns.Admin -ne 'cn=admins,dc=example,dc=org') { Fail 'ADMIN_GROUP did not reach the settings' }
if ($first.Urls -ne "http://+:$Port") { Fail "Urls is $($first.Urls)" }
if (-not $first.Jwt.Key -or -not $first.Encryption.Key) { Fail 'the keys were not generated' }
if (-not (Test-Path (Join-Path $env:ProgramData 'Nelsar.Axia\nelsar-axia.db'))) { Fail 'the database is not beside the settings' }

$acl = Get-Acl $settings
$who = $acl.Access | ForEach-Object { $_.IdentityReference.Value } | Sort-Object -Unique
$allowed = 'NT AUTHORITY\SYSTEM', 'BUILTIN\Administrators', "NT SERVICE\$service"
$extra = $who | Where-Object { $_ -notin $allowed }
if ($extra) { Fail "the settings file is also open to: $($extra -join ', ')" }
if ("NT SERVICE\$service" -notin $who) { Fail 'the service cannot read its settings' }

Step "Upgrade to $Current with no settings given"
Invoke-Msi @('/i', "`"$Current`"") "$env:TEMP\axia-upgrade.log"
Assert-Running
Wait-Answers '/' | Out-Null
$after = Read-Settings
if ($after.Jwt.Key -ne $first.Jwt.Key -or $after.Encryption.Key -ne $first.Encryption.Key) { Fail 'the upgrade replaced the keys' }
if ($after.Ldap.Server -ne $first.Ldap.Server -or $after.Urls -ne $first.Urls) { Fail 'the upgrade changed the settings' }
$installed = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' |
    Where-Object DisplayName -eq 'Nelsar.Axia'
if (@($installed).Count -ne 1) { Fail "$(@($installed).Count) Nelsar.Axia entries in Programs and Features" }

Step 'Uninstall'
Invoke-Msi @('/x', "`"$Current`"") "$env:TEMP\axia-uninstall.log"
if (Get-Service $service -ErrorAction SilentlyContinue) { Fail 'the service is still there' }
if (Test-Path $installFolder) { Fail "$installFolder is still there" }
if (-not (Test-Path $settings)) { Fail 'uninstalling removed the settings, and the keys with them' }

Write-Host "`nInstall, upgrade and uninstall all behaved." -ForegroundColor Green
