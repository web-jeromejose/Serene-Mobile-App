<#
.SYNOPSIS
  Builds MyTube for Android with the tools found/installed by setup-toolchain.ps1.

.EXAMPLE
  .\build-android.ps1             # build the installable APK
  .\build-android.ps1 -Run        # build, install and start the app on the phone plugged in by USB
  .\build-android.ps1 -OpenFolder # build the APK, then open the folder that contains it
#>
param(
    [switch]$Run,
    [switch]$OpenFolder
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot   # use this folder's global.json and nuget.config (public nuget.org only)

function Stop-WithError([string]$message) { Write-Host $message -ForegroundColor Red; exit 1 }

# Tool locations written by setup-toolchain.ps1 (per laptop).
$configFile = Join-Path $env:LOCALAPPDATA 'MyTube\toolchain.json'
if (-not (Test-Path $configFile)) { Stop-WithError "This laptop isn't set up yet. Double-click Install-MyTube-Tools.cmd first (see README.md > 'Set up a new PC')." }
$config = Get-Content $configFile -Raw | ConvertFrom-Json

$dotnet  = $config.dotnet
$sdk     = $config.androidSdk
$jdk     = $config.jdk
$adb     = Join-Path $sdk 'platform-tools\adb.exe'
$keytool = Join-Path $jdk 'bin\keytool.exe'
foreach ($required in @($dotnet, $adb, $keytool)) {
    if (-not (Test-Path $required)) { Stop-WithError "$required is missing. Run Install-MyTube-Tools.cmd again to repair the setup." }
}

$project      = Join-Path $PSScriptRoot 'MyTube\MyTube.csproj'
$signDir      = Join-Path $PSScriptRoot 'MyTube\signing'
$keystore     = Join-Path $signDir 'mytube.keystore'
$passwordFile = Join-Path $signDir 'password.txt'

# From here on, tool messages on stderr must not stop the script; exit codes decide success.
$ErrorActionPreference = 'Continue'

# One signing key shared by every PC (the folder syncs through OneDrive), so new APKs install
# as updates over older ones. Created once; back up MyTube\signing and keep it private.
if (-not (Test-Path $keystore)) {
    Write-Host 'Creating the app signing key (first build only)...' -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $signDir | Out-Null
    $bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $password = ([Convert]::ToBase64String($bytes) -replace '[+/=]', 'x')
    # apksigner reads the keystore password and the key password from consecutive lines of the same file.
    [IO.File]::WriteAllText($passwordFile, "$password`n$password`n")
    & $keytool -genkeypair -keystore $keystore -alias mytube -keyalg RSA -keysize 2048 -validity 10000 `
        -storepass $password -keypass $password -dname 'CN=MyTube, O=Personal' -noprompt 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $keystore)) { Stop-WithError 'Could not create the signing key.' }
}
elseif (-not (Test-Path $passwordFile)) {
    Stop-WithError "Signing key found but $passwordFile is missing. Restore it from your backup."
}

$env:DOTNET_ROOT = Split-Path $dotnet -Parent
$env:DOTNET_NOLOGO = '1'
$common = @('-f', 'net10.0-android', '--artifacts-path', $config.artifacts,
            "-p:AndroidSdkDirectory=$sdk", "-p:JavaSdkDirectory=$jdk")

if ($Run) {
    $devices = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\tdevice$' }
    if (-not $devices) { Stop-WithError "No phone found. Plug it in with USB debugging on and tap 'Allow' on the phone." }
    Write-Host 'Building, installing and starting MyTube on your phone...' -ForegroundColor Cyan
    & $dotnet build $project @common -c Release -t:Run
    exit $LASTEXITCODE
}

Write-Host 'Building the MyTube APK (takes a few minutes)...' -ForegroundColor Cyan
& $dotnet publish $project @common -c Release
if ($LASTEXITCODE -ne 0) { Stop-WithError 'Build failed - see the messages above.' }
$apk = Join-Path $config.artifacts 'publish\MyTube\release_net10.0-android\com.jerome.mytube-Signed.apk'
if (-not (Test-Path $apk)) { Stop-WithError "Build finished but the APK was not found at $apk" }
Write-Host "`nDone. APK: $apk" -ForegroundColor Green
if ($OpenFolder) { explorer.exe "/select,$apk" }
exit 0
