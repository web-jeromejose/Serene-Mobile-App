<#
.SYNOPSIS
  Gets a Windows laptop ready to build and run MyTube (Android), then proves it with a test build.

.DESCRIPTION
  For every dependency it first looks for a copy that is already on the laptop and re-uses it if
  it is complete and the right version. Only what is missing is downloaded, into a private folder
  (default %USERPROFILE%\.maui-toolchain). No admin rights needed, PATH and system settings unchanged.

    .NET 10 SDK + MAUI Android workload   re-uses an installed .NET 10 SDK that already has maui-android
    Java JDK 17 or 21                     re-uses JAVA_HOME, Visual Studio's, Android Studio's or Microsoft's JDK
    Android SDK (android-36, build-tools 36, platform-tools)
                                          re-uses ANDROID_HOME, Android Studio's or Visual Studio's SDK
    NuGet packages                        always from nuget.org (see nuget.config; company feeds are ignored)

  The chosen paths are saved to %LOCALAPPDATA%\MyTube\toolchain.json, which build-android.ps1 reads.
  Safe to run again. Log: <InstallDir>\setup.log

.PARAMETER BuildApk
  Finish by building the installable APK (instead of the quicker test build).
.PARAMETER Force
  Ignore tools found on the laptop and (re)install private copies of everything.
.PARAMETER InstallDir
  Where missing tools are installed. Use a short path without special characters.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\setup-toolchain.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\setup-toolchain.ps1 -BuildApk
#>
[CmdletBinding()]
param(
    [switch]$BuildApk,
    [switch]$Force,
    [string]$InstallDir = (Join-Path $env:USERPROFILE '.maui-toolchain')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$tc         = [IO.Path]::GetFullPath($InstallDir)
$project    = Join-Path $PSScriptRoot 'MyTube\MyTube.csproj'
$artifacts  = Join-Path $tc 'artifacts'
$log        = Join-Path $tc 'setup.log'
$configDir  = Join-Path $env:LOCALAPPDATA 'MyTube'
$configFile = Join-Path $configDir 'toolchain.json'
$totalSteps = 8

New-Item -ItemType Directory -Force (Join-Path $tc 'downloads') | Out-Null
Start-Transcript -Path $log -Append | Out-Null
Set-Location $PSScriptRoot   # so dotnet picks up this folder's global.json and nuget.config

$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

function Step($n, $text) { Write-Host "`n[$n/$totalSteps] $text" -ForegroundColor Cyan }
function Ok($text)       { Write-Host "      OK    $text" -ForegroundColor Green }
function Found($text)    { Write-Host "      FOUND $text" -ForegroundColor Green }
function Info($text)     { Write-Host "            $text" -ForegroundColor Gray }
function Warn($text)     { Write-Host "      NOTE  $text" -ForegroundColor Yellow }
function Fail($text) {
    Write-Host "`nSETUP FAILED: $text" -ForegroundColor Red
    Write-Host "Log: $log" -ForegroundColor Yellow
    Stop-Transcript | Out-Null
    exit 1
}

# Runs a native tool without letting stderr warnings abort the script; returns its output lines.
function Invoke-Native([string]$exe, [string[]]$arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & $exe @arguments 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $previous }
    return , @($output)
}

function Invoke-Dotnet([string[]]$arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $script:dotnet @arguments } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($arguments[0]) failed with exit code $LASTEXITCODE (details above)." }
}

function Assert-Reachable([string]$hostName) {
    try { $null = Invoke-WebRequest -UseBasicParsing -Method Head -Uri "https://$hostName/" -TimeoutSec 20 }
    catch {
        # Any HTTP answer (even 404) proves the site is reachable; only network errors count.
        if (-not $_.Exception.Response) { Fail "Cannot reach https://$hostName. Check your internet connection, VPN or proxy, or try another network." }
    }
}

function Invoke-Download([string]$url, [string]$outFile) {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try { Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $outFile; return }
        catch {
            if ($attempt -eq 3) { throw }
            Info "Download failed ($($_.Exception.Message)). Retrying in 5 s ($attempt/3)..."
            Start-Sleep -Seconds 5
        }
    }
}

function Expand-Zip([string]$zip, [string]$destination) {
    if (Test-Path $destination) { Remove-Item -Recurse -Force $destination }
    Expand-Archive -Path $zip -DestinationPath $destination -Force
}

# ---------- detection helpers ----------

# Returns @{ Exe; Sdks; HasWorkload } for a dotnet.exe, or $null if it has no .NET 10 SDK.
function Get-DotnetInfo([string]$exe) {
    if (-not $exe -or -not (Test-Path $exe)) { return $null }
    $sdks = Invoke-Native $exe @('--list-sdks') | Where-Object { $_ -match '^10\.0\.\d+' } | ForEach-Object { ($_ -split ' ')[0] }
    if (-not $sdks) { return $null }
    $workloads = (Invoke-Native $exe @('workload', 'list')) -join "`n"
    return @{ Exe = $exe; Sdks = @($sdks); HasWorkload = ($workloads -match 'maui-android') }
}

# Returns @{ Path; Version } for a JDK 17 or 21 (the versions .NET for Android supports), else $null.
function Get-JdkInfo([string]$dir) {
    if (-not $dir) { return $null }
    $release = Join-Path $dir 'release'
    if (-not (Test-Path $release) -or -not (Test-Path (Join-Path $dir 'bin\java.exe')) -or -not (Test-Path (Join-Path $dir 'bin\keytool.exe'))) { return $null }
    $match = Select-String -Path $release -Pattern '^JAVA_VERSION="([^"]+)"' | Select-Object -First 1
    if (-not $match) { return $null }
    $version = $match.Matches[0].Groups[1].Value
    $major = [int](($version -split '[.+-]')[0])
    if ($major -ne 17 -and $major -ne 21) { return $null }
    return @{ Path = $dir; Version = $version }
}

# True if the Android SDK folder has everything this app's build needs.
function Test-AndroidSdk([string]$dir) {
    if (-not $dir -or -not (Test-Path $dir)) { return $false }
    if (-not (Test-Path (Join-Path $dir 'platforms\android-36\android.jar'))) { return $false }
    if (-not (Test-Path (Join-Path $dir 'platform-tools\adb.exe'))) { return $false }
    $buildTools = Get-ChildItem (Join-Path $dir 'build-tools') -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^36\.' -and (Test-Path (Join-Path $_.FullName 'lib\apksigner.jar')) }
    return [bool]$buildTools
}

try {
    Write-Host '==========================================================' -ForegroundColor White
    Write-Host '  MyTube - get this laptop ready to build the Android app' -ForegroundColor White
    Write-Host "  Missing tools go to: $tc" -ForegroundColor White
    Write-Host '==========================================================' -ForegroundColor White

    # =====================================================================================
    Step 1 'Checking this laptop'
    if ($PSVersionTable.PSVersion.Major -lt 5) { Fail "Windows PowerShell 5.1 or newer is needed (found $($PSVersionTable.PSVersion))." }
    if (-not [Environment]::Is64BitOperatingSystem) { Fail 'A 64-bit version of Windows is required.' }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    if ($arch -eq 'Arm64') { $rid = 'win-arm64'; $jdkArch = 'aarch64' } else { $rid = 'win-x64'; $jdkArch = 'x64' }
    Ok "Windows $([Environment]::OSVersion.Version), $arch, PowerShell $($PSVersionTable.PSVersion)"

    # The SDKs nest files deeply (Windows' 260-character limit) and Android tools dislike non-ASCII paths.
    if ($tc.Length -gt 80) { Fail "The install folder path is too long ($($tc.Length) characters). Run again with a short one, e.g.  -InstallDir C:\maui-toolchain" }
    if ($tc -match '[^\x20-\x7E]') { Fail "The install folder contains special characters (often from the Windows user name): $tc. Run again with  -InstallDir C:\maui-toolchain" }
    Ok "Install folder OK: $tc"

    if (-not (Test-Path $project)) { Fail 'MyTube\MyTube.csproj not found next to this script. Keep the script in the Maui-app folder.' }
    # OneDrive "Files On-Demand" may only have placeholders on a new laptop: read every file once so it is downloaded.
    $unreadable = @()
    foreach ($f in Get-ChildItem (Join-Path $PSScriptRoot 'MyTube') -Recurse -File) {
        try { $null = [IO.File]::ReadAllBytes($f.FullName) } catch { $unreadable += $f.FullName }
    }
    if ($unreadable.Count -gt 0) { Fail "Some project files can't be read (OneDrive still downloading or offline?): $($unreadable[0]). In File Explorer, right-click the Maui-app folder > 'Always keep on this device', wait for the sync, and run again." }
    Ok 'Project files present (and downloaded from OneDrive)'

    $drive = Get-PSDrive -Name $tc.Substring(0, 1)
    $freeGb = [math]::Round($drive.Free / 1GB, 1)
    if ($freeGb -lt 6) { Fail "Only $freeGb GB free on drive $($drive.Name):. At least 6 GB is needed." }
    Ok "$freeGb GB free on drive $($drive.Name):"

    Assert-Reachable 'api.nuget.org'
    Ok 'Internet / nuget.org reachable'

    # =====================================================================================
    Step 2 '.NET 10 SDK'
    $privateDotnet = Join-Path $tc 'dotnet\dotnet.exe'
    $dotnetInfo = $null

    if (-not $Force) {
        $dotnetInfo = Get-DotnetInfo $privateDotnet
        if ($dotnetInfo) {
            Found ".NET SDK $($dotnetInfo.Sdks[-1]) (private copy)"
        }
        else {
            $globalCmd = Get-Command dotnet.exe -ErrorAction SilentlyContinue
            $global = if ($globalCmd) { Get-DotnetInfo $globalCmd.Source } else { $null }
            if ($global -and $global.HasWorkload) {
                $dotnetInfo = $global
                Found ".NET SDK $($global.Sdks[-1]) with the MAUI Android workload, installed on this laptop: $($global.Exe)"
            }
            elseif ($global) {
                Warn ".NET SDK $($global.Sdks[-1]) is installed but without the MAUI Android workload (adding it there needs admin rights)."
                Info 'Installing a private copy instead.'
            }
        }
    }

    if (-not $dotnetInfo) {
        Assert-Reachable 'builds.dotnet.microsoft.com'
        $rel = Invoke-RestMethod -UseBasicParsing 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
        $sdkVersion = $rel.'latest-sdk'
        $sdk = $rel.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $sdkVersion } | Select-Object -First 1
        $file = $sdk.files | Where-Object { $_.rid -eq $rid -and $_.name -like '*.zip' } | Select-Object -First 1
        if (-not $file) { Fail "Could not find the $rid download for .NET SDK $sdkVersion." }

        $zip = Join-Path $tc "downloads\dotnet-sdk-$sdkVersion-$rid.zip"
        $hashOk = (Test-Path $zip) -and ((Get-FileHash -Algorithm SHA512 $zip).Hash -eq $file.hash.ToUpperInvariant())
        if (-not $hashOk) {
            Info "Downloading .NET SDK $sdkVersion for $rid (about 280 MB)..."
            Invoke-Download $file.url $zip
            if ((Get-FileHash -Algorithm SHA512 $zip).Hash -ne $file.hash.ToUpperInvariant()) {
                Remove-Item $zip -Force
                Fail 'The .NET download is corrupt (SHA-512 mismatch). Run the script again.'
            }
        }
        Ok 'Download verified (SHA-512)'
        Info 'Extracting...'
        Expand-Zip $zip (Join-Path $tc 'dotnet')
        $dotnetInfo = Get-DotnetInfo $privateDotnet
        if (-not $dotnetInfo) { Fail 'The .NET SDK was extracted but does not run. Delete the dotnet folder and run again.' }
        Ok ".NET SDK $($dotnetInfo.Sdks[-1]) installed"
    }

    $script:dotnet = $dotnetInfo.Exe
    $env:DOTNET_ROOT = Split-Path $script:dotnet -Parent

    # =====================================================================================
    Step 3 '.NET MAUI Android workload'
    if ($dotnetInfo.HasWorkload -and -not $Force) {
        Found 'maui-android workload'
    }
    else {
        Info 'Installing maui-android (about 600 MB, several minutes)...'
        Invoke-Dotnet @('workload', 'install', 'maui-android', '--source', 'https://api.nuget.org/v3/index.json')
        Ok 'maui-android installed'
    }

    # =====================================================================================
    Step 4 'Java JDK (17 or 21)'
    $jdk = $null
    $privateJdk = Join-Path $tc 'jdk'
    $jdkCandidates = @($privateJdk)
    if (-not $Force) {
        $jdkCandidates += @($env:JAVA_HOME)
        foreach ($root in @("$env:ProgramFiles\Android\openjdk", "$env:ProgramFiles\Microsoft", "$env:ProgramFiles\Eclipse Adoptium", "$env:ProgramFiles\Java", "$env:ProgramFiles\Zulu")) {
            $jdkCandidates += @(Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | ForEach-Object FullName)
        }
        $jdkCandidates += @("$env:ProgramFiles\Android\Android Studio\jbr", "$env:LOCALAPPDATA\Programs\Android Studio\jbr")
    }
    foreach ($candidate in $jdkCandidates) {
        $jdk = Get-JdkInfo $candidate
        if ($jdk) { break }
    }

    if ($jdk) {
        Found "JDK $($jdk.Version): $($jdk.Path)"
    }
    else {
        Assert-Reachable 'aka.ms'
        $jdkZipUrl = "https://aka.ms/download-jdk/microsoft-jdk-17-windows-$jdkArch.zip"
        $jdkZip = Join-Path $tc "downloads\microsoft-jdk-17-windows-$jdkArch.zip"
        Info 'Downloading Microsoft OpenJDK 17 (about 190 MB)...'
        Invoke-Download $jdkZipUrl $jdkZip
        # The checksum file is served as binary, so save it and read it as text ("<sha256>  <file name>").
        $shaFile = "$jdkZip.sha256sum.txt"
        Invoke-Download "$jdkZipUrl.sha256sum.txt" $shaFile
        $expected = ([IO.File]::ReadAllText($shaFile).Trim() -split '\s+')[0].ToUpperInvariant()
        if ($expected -notmatch '^[0-9A-F]{64}$') { Fail 'Could not read the JDK checksum from Microsoft. Run the script again.' }
        if ((Get-FileHash -Algorithm SHA256 $jdkZip).Hash -ne $expected) { Remove-Item $jdkZip -Force; Fail 'The JDK download is corrupt (SHA-256 mismatch). Run the script again.' }
        Ok 'Download verified (SHA-256)'
        $staging = Join-Path $tc 'jdk-staging'
        Expand-Zip $jdkZip $staging
        $inner = Get-ChildItem $staging -Directory | Select-Object -First 1   # zip contains a single jdk-17.x folder
        if (Test-Path $privateJdk) { Remove-Item -Recurse -Force $privateJdk }
        Move-Item $inner.FullName $privateJdk
        Remove-Item -Recurse -Force $staging
        $jdk = Get-JdkInfo $privateJdk
        if (-not $jdk) { Fail 'The JDK was extracted but is not usable. Run the script again.' }
        Ok "JDK $($jdk.Version) installed"
    }

    # =====================================================================================
    Step 5 'Android SDK (android-36, build-tools 36, platform-tools)'
    $androidSdk = $null
    $privateSdk = Join-Path $tc 'android-sdk'
    $sdkCandidates = @($privateSdk)
    if (-not $Force) {
        $sdkCandidates += @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "$env:LOCALAPPDATA\Android\Sdk", "${env:ProgramFiles(x86)}\Android\android-sdk", "$env:ProgramFiles\Android\android-sdk")
    }
    foreach ($candidate in $sdkCandidates) {
        if (Test-AndroidSdk $candidate) { $androidSdk = $candidate; break }
    }

    if ($androidSdk) {
        Found "Android SDK: $androidSdk"
    }
    else {
        foreach ($candidate in $sdkCandidates | Select-Object -Skip 1) {
            if ($candidate -and (Test-Path $candidate)) { Warn "Android SDK at $candidate is missing android-36 / build-tools 36; installing a private one instead of changing it." }
        }
        Assert-Reachable 'dl.google.com'
        Info 'Downloading Android SDK packages (about 500 MB)...'
        Invoke-Dotnet @('build', $project, '-t:InstallAndroidDependencies', '-f', 'net10.0-android', '--artifacts-path', $artifacts,
            "-p:AndroidSdkDirectory=$privateSdk", "-p:JavaSdkDirectory=$($jdk.Path)", '-p:AcceptAndroidSDKLicenses=True', '-v:q', '-nologo')
        if (-not (Test-AndroidSdk $privateSdk)) { Fail 'The Android SDK install did not complete. Run the script again.' }
        $androidSdk = $privateSdk
        Ok "Android SDK installed: $androidSdk"
    }

    # =====================================================================================
    Step 6 'NuGet packages (from nuget.org only)'
    Invoke-Dotnet @('restore', $project, '--artifacts-path', $artifacts, "-p:AndroidSdkDirectory=$androidSdk", "-p:JavaSdkDirectory=$($jdk.Path)", '-v:q', '-nologo')
    Ok 'CommunityToolkit.Maui, MediaElement, Mvvm and MAUI packages restored'

    # =====================================================================================
    Step 7 'Saving the tool locations'
    New-Item -ItemType Directory -Force $configDir | Out-Null
    [ordered]@{
        dotnet     = $script:dotnet
        androidSdk = $androidSdk
        jdk        = $jdk.Path
        artifacts  = $artifacts
        updated    = (Get-Date).ToString('s')
    } | ConvertTo-Json | Set-Content -Path $configFile -Encoding UTF8
    $workloadVersion = (Invoke-Native $script:dotnet @('workload', '--version')) | Where-Object { $_.Trim() } | Select-Object -Last 1
    Ok ".NET SDK       $((Invoke-Native $script:dotnet @('--version')) | Select-Object -Last 1)  ($script:dotnet)"
    Ok "MAUI workload  $workloadVersion"
    Ok "Java JDK       $($jdk.Version)  ($($jdk.Path))"
    Ok "Android SDK    $androidSdk"
    Ok "Saved to       $configFile"

    # =====================================================================================
    $buildScript = Join-Path $PSScriptRoot 'build-android.ps1'
    if ($BuildApk) {
        Step 8 'Building the APK (3-6 minutes)'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript -OpenFolder
        if ($LASTEXITCODE -ne 0) { Fail 'Building the APK failed (see the messages above).' }
        Ok 'APK built - its folder is open in File Explorer'
    }
    else {
        Step 8 'Test build (proves everything works, 1-3 minutes)'
        Invoke-Dotnet @('build', $project, '-f', 'net10.0-android', '-c', 'Debug', '--artifacts-path', $artifacts,
            "-p:AndroidSdkDirectory=$androidSdk", "-p:JavaSdkDirectory=$($jdk.Path)", '-v:q', '-nologo')
        Ok 'MyTube compiles on this laptop'
    }

    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host '  Setup complete - this laptop can build MyTube.' -ForegroundColor Green
    if (-not $BuildApk) { Write-Host '  Build the APK:   double-click Build-MyTube-APK.cmd' -ForegroundColor Green }
    Write-Host '  Run on a phone:  powershell -ExecutionPolicy Bypass -File .\build-android.ps1 -Run' -ForegroundColor Green
    Write-Host "  Log:             $log" -ForegroundColor Green
    Write-Host '==========================================================' -ForegroundColor Green
    Stop-Transcript | Out-Null
    exit 0
}
catch {
    Fail $_.Exception.Message
}
