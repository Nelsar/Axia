$ErrorActionPreference = 'Stop'

$version = '0.3.0'
$pp = Get-PackageParameters

# Package parameters map to the MSI's own properties. One that is not given is not passed, so an
# upgrade without parameters keeps the settings an earlier install wrote.
$properties = [ordered]@{
  Port             = 'PORT'
  LdapServer       = 'LDAP_SERVER'
  LdapBaseDn       = 'LDAP_BASEDN'
  LdapUsername     = 'LDAP_USERNAME'
  AdminGroup       = 'ADMIN_GROUP'
  DatabaseProvider = 'DATABASE_PROVIDER'
}

$silentArgs = "/qn /norestart /l*v `"$($env:TEMP)\$($env:ChocolateyPackageName).$($version).MsiInstall.log`""
foreach ($name in $properties.Keys) {
  if ($pp[$name]) {
    $silentArgs += " $($properties[$name])=`"$($pp[$name])`""
  }
}
if ($pp['StartService']) {
  $silentArgs += ' START_SERVICE=1'
}

$packageArgs = @{
  packageName    = $env:ChocolateyPackageName
  fileType       = 'msi'
  url64bit       = "https://github.com/Nelsar/Axia/releases/download/v$version/Nelsar.Axia-$version-x64.msi"
  checksum64     = '76d9730fdc2b7e0154b81dcf56e9168db4af1cf1d382b3f11683d9e5bfcd7b36'
  checksumType64 = 'sha256'
  silentArgs     = $silentArgs
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
