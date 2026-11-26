<#
.SYNOPSIS
    Build the NewCosmos Android shell APK (Capacitor WebView) and stage it for publishing.

.DESCRIPTION
    Pipeline:
      1. npm install (mobile/, skipped when -SkipNpmInstall)
      2. npx cap sync android (writes server.url from NC_MOBILE_URL / -ServerUrl)
      3. gradlew assembleRelease | assembleDebug
      4. copy APK to publish/android/newcosmos-<version>.apk (+ sha256, latest.json)

    Release signing:
      - -Keystore / NC_ANDROID_KEYSTORE (default deploy/keys/newcosmos-android.keystore)
      - -KeystorePassword / NC_ANDROID_KEYSTORE_PASS
      - -KeyAlias / NC_ANDROID_KEY_ALIAS (default: newcosmos)
      - -KeyPassword / NC_ANDROID_KEY_PASS (default: keystore password)
      A missing default keystore is generated (self-signed) and its generated password is
      stored next to it as "<keystore>.pass". deploy/keys/ is git-ignored - never commit it.
      Without a usable keystore the Release build falls back to the debug signing key.

.EXAMPLE
    .\build-apk.ps1 -Configuration Debug
    .\build-apk.ps1 -Configuration Release
    $env:NC_MOBILE_URL = "http://192.168.1.10:5173"; .\build-apk.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Optional test server, e.g. http://192.168.1.10:5173 (Debug only).
    [string]$ServerUrl = $env:NC_MOBILE_URL,

    # Update manifest URL baked into BuildConfig (default: production static host).
    [string]$UpdateUrl = $env:NC_MOBILE_UPDATE_URL,

    [string]$Keystore = $env:NC_ANDROID_KEYSTORE,
    [string]$KeystorePassword = $env:NC_ANDROID_KEYSTORE_PASS,
    [string]$KeyAlias = $env:NC_ANDROID_KEY_ALIAS,
    [string]$KeyPassword = $env:NC_ANDROID_KEY_PASS,

    # Release notes written into publish/android/latest.json.
    [string]$Notes = '',

    [string]$BaseUrl = 'https://cosmos-static.sua17.cn:20000/downloads/android',

    [switch]$SkipNpmInstall,
    [switch]$SkipSync,
    [switch]$SkipSigning,
    [switch]$NoPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked([string]$Command, [string[]]$Arguments, [string]$WorkDir) {
    Push-Location $WorkDir
    try {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed ($LASTEXITCODE): $Command $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mobileDir = Join-Path $repoRoot 'mobile'
$androidDir = Join-Path $mobileDir 'android'
$publishDir = Join-Path $repoRoot 'publish\android'
$keysDir = Join-Path $repoRoot 'deploy\keys'

if (-not (Test-Path (Join-Path $mobileDir 'package.json'))) {
    throw "mobile/package.json not found under $repoRoot"
}
if (-not (Test-Path (Join-Path $androidDir 'gradlew.bat'))) {
    throw "Android project is missing. Run: cd mobile; npx cap add android"
}

$packageJson = Get-Content (Join-Path $mobileDir 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$versionName = [string]$packageJson.version
$lastSegment = ($versionName -split '\.')[-1]
if ($lastSegment -notmatch '^\d+$') {
    throw "mobile/package.json version must end with a numeric build number, got '$versionName'"
}
$versionCode = [int]$lastSegment

if ($ServerUrl) {
    $ServerUrl = $ServerUrl.Trim().TrimEnd('/')
    if ($ServerUrl -notmatch '^https?://') {
        throw "-ServerUrl must start with http:// or https://"
    }
    if ($ServerUrl -match '^http://' -and $Configuration -eq 'Release') {
        throw "Cleartext http:// is only allowed for Debug builds. Use -Configuration Debug for local testing."
    }
}
if ($UpdateUrl -and $UpdateUrl -notmatch '^https?://') {
    throw "-UpdateUrl must start with http:// or https://"
}

$sdk = $env:ANDROID_HOME
if (-not $sdk) { $sdk = $env:ANDROID_SDK_ROOT }
if (-not $sdk) {
    $candidate = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
    if (Test-Path $candidate) { $sdk = $candidate }
}
if (-not $sdk -or -not (Test-Path $sdk)) {
    throw "Android SDK not found. Set ANDROID_HOME or install the SDK (Android Studio)."
}
$env:ANDROID_HOME = $sdk
$env:ANDROID_SDK_ROOT = $sdk
Write-Host "Android SDK : $sdk"
Write-Host "Version     : $versionName ($versionCode)"
Write-Host "Config      : $Configuration"

if ($ServerUrl) {
    $env:NC_MOBILE_URL = $ServerUrl
    Write-Host "Server URL  : $ServerUrl"
}
else {
    Remove-Item Env:\NC_MOBILE_URL -ErrorAction SilentlyContinue
    Write-Host "Server URL  : https://cosmos.sua17.cn (production)"
}

if (-not $SkipNpmInstall) {
    Write-Step 'npm install'
    Invoke-Checked 'npm.cmd' @('install', '--no-audit', '--no-fund') $mobileDir
}

if (-not $SkipSync) {
    Write-Step 'npx cap sync android'
    Invoke-Checked 'npx.cmd' @('cap', 'sync', 'android') $mobileDir
}

# ---------------------------------------------------------------------------
# Release signing
# ---------------------------------------------------------------------------
$signingInfo = 'debug key (no release keystore configured)'
if ($Configuration -eq 'Release' -and -not $SkipSigning) {
    Write-Step 'Release signing'
    if (-not $Keystore) {
        $Keystore = Join-Path $keysDir 'newcosmos-android.keystore'
    }
    elseif (-not [System.IO.Path]::IsPathRooted($Keystore)) {
        $Keystore = Join-Path $repoRoot $Keystore
    }

    if (-not $KeyAlias) { $KeyAlias = 'newcosmos' }
    $passwordFile = "$Keystore.pass"

    if (-not $KeystorePassword -and (Test-Path $passwordFile)) {
        $KeystorePassword = [System.IO.File]::ReadAllText($passwordFile, [System.Text.Encoding]::UTF8).Trim()
    }
    if (-not $KeyPassword) { $KeyPassword = $KeystorePassword }

    $generateKeystore = -not (Test-Path $Keystore)
    if ($generateKeystore -and -not $KeystorePassword) {
        $bytes = New-Object 'byte[]' 24
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        $KeystorePassword = ([Convert]::ToBase64String($bytes) -replace '[^A-Za-z0-9]', '').Substring(0, 24)
        $KeyPassword = $KeystorePassword
    }

    if ($generateKeystore) {
        if (-not (Test-Path $keysDir)) { New-Item -ItemType Directory -Path $keysDir | Out-Null }
        $keytool = if ($env:JAVA_HOME -and (Test-Path (Join-Path $env:JAVA_HOME 'bin\keytool.exe'))) {
            Join-Path $env:JAVA_HOME 'bin\keytool.exe'
        }
        else {
            (Get-Command keytool.exe -ErrorAction SilentlyContinue).Source
        }
        if (-not $keytool) { throw "keytool.exe not found. Install a JDK (JAVA_HOME) or pass -SkipSigning." }

        Write-Host "Generating self-signed keystore: $Keystore"
        & $keytool -genkeypair -v -keystore $Keystore -alias $KeyAlias -keyalg RSA -keysize 4096 `
            -validity 10950 -storepass $KeystorePassword -keypass $KeyPassword `
            -dname "CN=NewCosmos Mobile, OU=Mobile, O=SUA17, L=Harbin, ST=Heilongjiang, C=CN"
        if ($LASTEXITCODE -ne 0) { throw "keytool failed ($LASTEXITCODE)" }
        [System.IO.File]::WriteAllText($passwordFile, $KeystorePassword, (New-Object System.Text.UTF8Encoding($false)))
        Write-Warning "Generated keystore password saved to $passwordFile. Keep it out of git; without it future updates cannot be signed."
    }

    if (-not $KeystorePassword) {
        throw "No keystore password. Set NC_ANDROID_KEYSTORE_PASS or -KeystorePassword."
    }

    $env:NC_ANDROID_KEYSTORE = $Keystore
    $env:NC_ANDROID_KEYSTORE_PASS = $KeystorePassword
    $env:NC_ANDROID_KEY_ALIAS = $KeyAlias
    $env:NC_ANDROID_KEY_PASS = $KeyPassword
    $signingInfo = "keystore: $Keystore (alias: $KeyAlias)"
}
elseif ($Configuration -eq 'Release') {
    Write-Warning 'Skipping release keystore setup; Gradle will sign with the debug key.'
}

# ---------------------------------------------------------------------------
# Gradle
# ---------------------------------------------------------------------------
$gradleArgs = @(
    "assemble$Configuration",
    "-PncVersionName=$versionName",
    "-PncVersionCode=$versionCode"
)
if ($UpdateUrl) { $gradleArgs += "-PncUpdateUrl=$UpdateUrl" }
if ($ServerUrl) { $gradleArgs += "-PncServerUrl=$ServerUrl" }

Write-Step "gradlew assemble$Configuration"
Invoke-Checked (Join-Path $androidDir 'gradlew.bat') $gradleArgs $androidDir

$apkDir = if ($Configuration -eq 'Release') {
    Join-Path $androidDir 'app\build\outputs\apk\release'
}
else {
    Join-Path $androidDir 'app\build\outputs\apk\debug'
}
$apkCandidates = @(
    @(
        (Join-Path $apkDir 'app-release.apk'),
        (Join-Path $apkDir 'app-debug.apk')
    ) | Where-Object { Test-Path $_ }
)

if ($apkCandidates.Count -eq 0) {
    throw "APK not found under $apkDir"
}
$builtApk = $apkCandidates[0]

$hash = (Get-FileHash -Path $builtApk -Algorithm SHA256).Hash.ToLowerInvariant()
$sizeBytes = (Get-Item $builtApk).Length
$sizeMb = [Math]::Round($sizeBytes / 1MB, 2)

if (-not $NoPublish) {
    Write-Step 'Publish'
    if (-not (Test-Path $publishDir)) { New-Item -ItemType Directory -Path $publishDir | Out-Null }
    $suffix = if ($Configuration -eq 'Debug') { '-debug' } else { '' }
    $targetName = "newcosmos-$versionName$suffix.apk"
    $targetPath = Join-Path $publishDir $targetName
    Copy-Item -Path $builtApk -Destination $targetPath -Force

    $downloadUrl = "$($BaseUrl.TrimEnd('/'))/$targetName"
    if (-not $Notes) { $Notes = "NewCosmos Android $versionName" }
    $manifest = [ordered]@{
        versionCode = $versionCode
        versionName = $versionName
        url         = $downloadUrl
        sha256      = $hash
        notes       = $Notes
    }
    $manifestName = if ($Configuration -eq 'Debug') { 'latest.debug.json' } else { 'latest.json' }
    $manifestPath = Join-Path $publishDir $manifestName
    $json = ($manifest | ConvertTo-Json -Depth 3) + "`n"
    [System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))

    Write-Host "APK      : $targetPath"
    Write-Host "Size     : $sizeMb MB ($sizeBytes bytes)"
    Write-Host "SHA256   : $hash"
    Write-Host "Manifest : $manifestPath"
    Write-Host "Upload to: $downloadUrl"
}
else {
    Write-Host "APK      : $builtApk"
    Write-Host "Size     : $sizeMb MB ($sizeBytes bytes)"
    Write-Host "SHA256   : $hash"
}

Write-Host "Signing  : $signingInfo"
Write-Host ""
Write-Host "Done." -ForegroundColor Green
