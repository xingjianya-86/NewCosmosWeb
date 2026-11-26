<#
.SYNOPSIS
    生成 NewCosmos 内部自签名代码签名证书，并导出 PFX / CER。

.DESCRIPTION
    使用 New-SelfSignedCertificate -Type CodeSigningCert 在当前用户证书库生成证书，
    然后导出：
      deploy\keys\NewCosmosCodeSign.pfx   含私钥，仅保留在发布机
      deploy\keys\NewCosmosCodeSign.cer   公钥证书，分发到每台客户端导入信任

    PFX 密码只从环境变量读取（默认 NC_CODESIGN_PFX_PASSWORD），不写入文件、不打印。
    自签名证书不会自动获得 SmartScreen 信誉；每台机器仍需把 .cer 导入
    “受信任的根证书颁发机构”和“受信任的发布者”，否则 UAC 仍显示未知发布者。

.PARAMETER Subject
    证书主题，默认 CN=NewCosmos Internal Code Signing。

.PARAMETER OutDir
    导出目录，默认 <仓库根>\deploy\keys。

.PARAMETER ValidYears
    有效期（年），默认 5。

.PARAMETER PasswordEnv
    PFX 密码所在环境变量名，默认 NC_CODESIGN_PFX_PASSWORD。

.PARAMETER Force
    覆盖已存在的 PFX / CER。默认遇到同名文件直接失败，避免误覆盖生产签名密钥。

.PARAMETER TrustForCurrentUser
    生成后把 CER 导入当前用户的 Root / TrustedPublisher 证书库，便于本机测试。
    生产分发仍应在每台机器上按说明导入。

.EXAMPLE
    $env:NC_CODESIGN_PFX_PASSWORD = '请换成强密码'
    powershell -File deploy\scripts\new-codesign-cert.ps1

.EXAMPLE
    $env:NC_CODESIGN_PFX_PASSWORD = '请换成强密码'
    powershell -File deploy\scripts\new-codesign-cert.ps1 -OutDir D:\certs -Force
#>
[CmdletBinding()]
param(
    [string]$Subject = 'CN=NewCosmos Internal Code Signing',
    [string]$FriendlyName = 'NewCosmos Internal Code Signing',
    [string]$OutDir = '',
    [ValidateRange(1, 20)]
    [int]$ValidYears = 5,
    [string]$PasswordEnv = 'NC_CODESIGN_PFX_PASSWORD',
    [switch]$Force,
    [switch]$TrustForCurrentUser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $RepoRoot 'deploy\keys'
}
if (-not [System.IO.Path]::IsPathRooted($OutDir)) {
    $OutDir = Join-Path $RepoRoot $OutDir
}
$OutDir = [System.IO.Path]::GetFullPath($OutDir)

$PfxPath = Join-Path $OutDir 'NewCosmosCodeSign.pfx'
$CerPath = Join-Path $OutDir 'NewCosmosCodeSign.cer'
$PasswordFile = Join-Path $RepoRoot 'deploy\keys\pfx.pass'

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Stop-WithError {
    param([string]$Message, [int]$Code = 1)
    Write-Host "错误：$Message" -ForegroundColor Red
    exit $Code
}

function Read-PlainPassword {
    param([string]$EnvName, [string]$FallbackFile)
    $plain = [Environment]::GetEnvironmentVariable($EnvName)
    if ([string]::IsNullOrWhiteSpace($plain) -and (Test-Path -LiteralPath $FallbackFile -PathType Leaf)) {
        $plain = [System.IO.File]::ReadAllText($FallbackFile, [System.Text.Encoding]::UTF8).Trim()
        if (-not [string]::IsNullOrWhiteSpace($plain)) {
            Write-Host "使用密码文件：$FallbackFile"
        }
    }
    if ([string]::IsNullOrWhiteSpace($plain)) {
        Stop-WithError "未找到 PFX 密码。请设置环境变量 $EnvName，或写入密码文件：`n  $FallbackFile" 2
    }
    if ($plain.Length -lt 8) {
        Write-Warning 'PFX 密码少于 8 个字符，建议使用更长的强密码。'
    }
    return $plain
}

if (-not (Get-Command New-SelfSignedCertificate -ErrorAction SilentlyContinue)) {
    Stop-WithError '当前系统没有 New-SelfSignedCertificate，请确认是 Windows PowerShell 5.1+ 且系统为 Windows 10/11。' 2
}
if (-not (Get-Command Export-PfxCertificate -ErrorAction SilentlyContinue)) {
    Stop-WithError '当前系统没有 Export-PfxCertificate（PKI 模块缺失）。' 2
}
if (-not (Get-Command Export-Certificate -ErrorAction SilentlyContinue)) {
    Stop-WithError '当前系统没有 Export-Certificate（PKI 模块缺失）。' 2
}

if (-not $Force -and ((Test-Path -LiteralPath $PfxPath) -or (Test-Path -LiteralPath $CerPath))) {
    Stop-WithError "证书文件已存在：`n  $PfxPath`n  $CerPath`n如需重新生成，请加 -Force（会覆盖旧密钥，旧签名将无法再用新证书验证）。" 2
}

$passwordPlain = Read-PlainPassword -EnvName $PasswordEnv -FallbackFile $PasswordFile

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Write-Step '生成自签名代码签名证书'
$notAfter = (Get-Date).AddYears($ValidYears)
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -FriendlyName $FriendlyName `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyExportPolicy Exportable `
    -KeySpec Signature `
    -KeyUsage DigitalSignature `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -NotAfter $notAfter

if ($null -eq $cert) {
    Stop-WithError 'New-SelfSignedCertificate 没有返回证书。' 3
}

Write-Step '导出 PFX（含私钥）'
$securePassword = ConvertTo-SecureString -String $passwordPlain -AsPlainText -Force
try {
    Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $securePassword -Force | Out-Null
    Export-Certificate -Cert $cert -FilePath $CerPath -Type CERT -Force | Out-Null
}
finally {
    $passwordPlain = $null
    $securePassword = $null
}

if (-not (Test-Path -LiteralPath $PfxPath)) { Stop-WithError "PFX 导出失败：$PfxPath" 4 }
if (-not (Test-Path -LiteralPath $CerPath)) { Stop-WithError "CER 导出失败：$CerPath" 4 }

if ($TrustForCurrentUser) {
    Write-Step '导入当前用户信任库（仅本机测试用）'
    try {
        Import-Certificate -FilePath $CerPath -CertStoreLocation 'Cert:\CurrentUser\Root' -ErrorAction Stop | Out-Null
        Import-Certificate -FilePath $CerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPublisher' -ErrorAction Stop | Out-Null
        Write-Host '已导入当前用户的 Root / TrustedPublisher。' -ForegroundColor Green
    }
    catch {
        Write-Warning "导入当前用户信任库失败（证书已生成，可稍后手工导入）：$($_.Exception.Message)"
    }
}

Write-Step '完成'
Write-Host "PFX（私钥，禁止提交/上传）：$PfxPath" -ForegroundColor Yellow
Write-Host "CER（公钥，分发到客户端）：  $CerPath" -ForegroundColor Yellow
Write-Host "主题：$($cert.Subject)"
Write-Host "指纹：$($cert.Thumbprint)"
Write-Host "有效期至：$($cert.NotAfter.ToString('yyyy-MM-dd'))"

Write-Host ''
Write-Host '信任说明（每台需要运行发布客户端的机器都要做一次）' -ForegroundColor Cyan
Write-Host @"
自签名证书默认不被 Windows 信任。只把 .cer 发给客户端，不要发 .pfx。

方式 A：图形界面（推荐给终端用户）
  1. 双击 NewCosmosCodeSign.cer -> 安装证书
  2. 存储位置选择“当前用户”或“本地计算机”
  3. 选择“将所有的证书都放入下列存储”：
       - 受信任的根证书颁发机构
       - 受信任的发布者（再导入一次）
  4. 完成导入后重新运行安装包/程序

方式 B：PowerShell（当前用户，无需管理员）
  Import-Certificate -FilePath .\NewCosmosCodeSign.cer -CertStoreLocation Cert:\CurrentUser\Root
  Import-Certificate -FilePath .\NewCosmosCodeSign.cer -CertStoreLocation Cert:\CurrentUser\TrustedPublisher

方式 C：PowerShell（本机所有用户，需管理员）
  Import-Certificate -FilePath .\NewCosmosCodeSign.cer -CertStoreLocation Cert:\LocalMachine\Root
  Import-Certificate -FilePath .\NewCosmosCodeSign.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher

验证（在客户端上）：
  Get-AuthenticodeSignature .\NewCosmosAgentSetup_*.exe | Format-List Status,StatusMessage,SignerCertificate

注意：
  - 导入后 UAC 的“未知发布者”会消失；但 SmartScreen 仍可能提示“Windows 已保护你的电脑”，
    因为 SmartScreen 还看下载量/文件信誉。内部发布可让用户点“更多信息 -> 仍要运行”，
    或后续改用受信任 CA 证书 / 企业代码签名证书。
  - PFX 只留在发布机，密码只放在环境变量或密钥管理器中；不要写进脚本、文档、Git。
  - 证书到期（$($cert.NotAfter.ToString('yyyy-MM-dd'))）后需要重新生成并重新分发 CER。
"@
