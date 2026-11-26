<#
.SYNOPSIS
    为 Windows 客户端安装包生成 latest.json 更新清单。

.DESCRIPTION
    计算安装包 SHA256，按固定字段写出：
      {
        "version": "...",
        "url": "https://cosmos-static.sua17.cn:20000/downloads/windows/...",
        "sha256": "...",
        "notes": "...",
        "publishedAt": "2026-11-26T12:00:00+08:00"
      }

    默认输出 deploy\update\latest.json（UTF-8 无 BOM）。
    -Sign 会额外生成 latest.json.sig：对 JSON 内容做 CMS/PKCS#7 分离签名，
    与 exe 的 Authenticode 签名是两套东西（Authenticode 不能签 JSON）。

    退出码：
      0  成功
      2  参数/文件错误
      3  写文件失败
      4  -Sign 的 PFX/密码不可用
      5  签名或验签失败

.PARAMETER InstallerPath
    安装包路径（.exe / .msi）。

.PARAMETER Version
    清单版本，例如 2.0.20261124。

.PARAMETER Notes
    更新说明，可含中文/换行。

.PARAMETER Url
    下载地址；不传则用 -BaseUrl + 文件名。

.PARAMETER PublishedAt
    发布时间（ISO 8601）；不传则用当前时间（带本地时区偏移）。

.PARAMETER OutputPath
    输出 JSON 路径，默认 deploy\update\latest.json。

.PARAMETER Sign
    用代码签名 PFX 生成 CMS 分离签名 .sig。

.EXAMPLE
    powershell -File deploy\scripts\write-update-manifest.ps1 `
      -InstallerPath publish\installers\NewCosmosShellSetup_2.0.20261124.exe `
      -Version 2.0.20261124 `
      -Notes '修复打印任务重试；更新 Shell 启动页。'

.EXAMPLE
    $env:NC_CODESIGN_PFX_PASSWORD = '<强密码>'
    powershell -File deploy\scripts\write-update-manifest.ps1 `
      -InstallerPath .\publish\installers\NewCosmosAgentSetup_2.0.20261124.exe `
      -Version 2.0.20261124 -Sign
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Notes = '',
    [string]$Url = '',
    [string]$BaseUrl = 'https://cosmos-static.sua17.cn:20000/downloads/windows/',
    [string]$PublishedAt = '',
    [string]$OutputPath = '',
    [switch]$Sign,
    [string]$PfxPath = '',
    [string]$PfxPasswordEnv = 'NC_CODESIGN_PFX_PASSWORD'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $RepoRoot 'deploy\update\latest.json'
}
if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $RepoRoot $OutputPath
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

if ([string]::IsNullOrWhiteSpace($PfxPath)) {
    $PfxPath = Join-Path $RepoRoot 'deploy\keys\NewCosmosCodeSign.pfx'
}
if (-not [System.IO.Path]::IsPathRooted($PfxPath)) {
    $PfxPath = Join-Path $RepoRoot $PfxPath
}
$PfxPath = [System.IO.Path]::GetFullPath($PfxPath)

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

function New-DetachedCmsSignature {
    param(
        [byte[]]$Content,
        [string]$CertificatePath,
        [string]$Password,
        [string]$SignaturePath
    )

    Add-Type -AssemblyName System.Security

    $cert = $null
    try {
        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $CertificatePath, $Password, $flags
    }
    catch {
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $CertificatePath, $Password
    }

    try {
        $contentInfo = New-Object System.Security.Cryptography.Pkcs.ContentInfo -ArgumentList (,$Content)
        $cms = New-Object System.Security.Cryptography.Pkcs.SignedCms -ArgumentList $contentInfo, $true
        $signer = New-Object System.Security.Cryptography.Pkcs.CmsSigner -ArgumentList $cert
        $signer.DigestAlgorithm = New-Object System.Security.Cryptography.Oid -ArgumentList '2.16.840.1.101.3.4.2.1'
        $signer.IncludeOption = [System.Security.Cryptography.X509Certificates.X509IncludeOption]::EndCertOnly
        $cms.ComputeSignature($signer)
        $signature = $cms.Encode()
        [System.IO.File]::WriteAllBytes($SignaturePath, $signature)

        # 本地验签：只验证签名本身，不验证证书链（自签名证书本来就不在公共根里）
        $verifyContentInfo = New-Object System.Security.Cryptography.Pkcs.ContentInfo -ArgumentList (,$Content)
        $verifyCms = New-Object System.Security.Cryptography.Pkcs.SignedCms -ArgumentList $verifyContentInfo, $true
        $verifyCms.Decode($signature)
        $verifyCms.CheckSignature($true)
    }
    finally {
        if ($null -ne $cert) { $cert.Dispose() }
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    Stop-WithError '-Version 不能为空。' 2
}
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    Stop-WithError "安装包不存在：$InstallerPath" 2
}

$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$extension = [System.IO.Path]::GetExtension($installer).ToLowerInvariant()
if ($extension -notin '.exe', '.msi') {
    Stop-WithError "安装包必须是 .exe 或 .msi：$installer" 2
}

if ([string]::IsNullOrWhiteSpace($Url)) {
    $fileName = [System.IO.Path]::GetFileName($installer)
    $Url = $BaseUrl.TrimEnd('/') + '/' + [System.Uri]::EscapeDataString($fileName)
}
if ($Url -notmatch '^https?://') {
    Stop-WithError "下载地址必须是 http(s) URL：$Url" 2
}

if ([string]::IsNullOrWhiteSpace($PublishedAt)) {
    $PublishedAt = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')
}
else {
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($PublishedAt, [ref]$parsed)) {
        Stop-WithError "-PublishedAt 不是有效的 ISO 8601 时间：$PublishedAt" 2
    }
    $PublishedAt = $parsed.ToString('yyyy-MM-ddTHH:mm:sszzz')
}

Write-Step '计算 SHA256'
$sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "  文件：$installer"
Write-Host "  大小：$((Get-Item -LiteralPath $installer).Length) 字节"
Write-Host "  SHA256：$sha256"

$manifest = [ordered]@{
    version     = $Version
    url         = $Url
    sha256      = $sha256
    notes       = $Notes
    publishedAt = $PublishedAt
}

$json = $manifest | ConvertTo-Json -Depth 5
$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

try {
    [System.IO.File]::WriteAllText($OutputPath, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
}
catch {
    Stop-WithError "写清单失败：$($_.Exception.Message)" 3
}

Write-Step '清单已写入'
Write-Host "  $OutputPath"

if ($Sign) {
    if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) {
        Stop-WithError "找不到 PFX：$PfxPath" 4
    }
    $password = [Environment]::GetEnvironmentVariable($PfxPasswordEnv)
    if ([string]::IsNullOrWhiteSpace($password)) {
        $passwordFile = Join-Path $RepoRoot 'deploy\keys\pfx.pass'
        if (Test-Path -LiteralPath $passwordFile -PathType Leaf) {
            $password = [System.IO.File]::ReadAllText($passwordFile, [System.Text.Encoding]::UTF8).Trim()
        }
    }
    if ([string]::IsNullOrWhiteSpace($password)) {
        Stop-WithError "未找到 PFX 密码。请设置环境变量 $PfxPasswordEnv，或写入 deploy\keys\pfx.pass。" 4
    }

    $signaturePath = $OutputPath + '.sig'
    Write-Step '生成 CMS 分离签名'
    try {
        $content = [System.IO.File]::ReadAllBytes($OutputPath)
        New-DetachedCmsSignature -Content $content -CertificatePath $PfxPath -Password $password -SignaturePath $signaturePath
    }
    catch {
        Stop-WithError "签名/验签失败：$($_.Exception.Message)" 5
    }

    Write-Host "  $signaturePath"
    Write-Host '  分发时 latest.json 与 latest.json.sig 必须成对上传。'
}

Write-Host ''
Write-Host '发布：' -ForegroundColor Cyan
Write-Host "  scp `"$installer`" root@10.110.0.2:/www/wwwroot/cosmos-static.sua17.cn/downloads/windows/"
Write-Host "  scp `"$OutputPath`" root@10.110.0.2:/www/wwwroot/cosmos-static.sua17.cn/downloads/windows/latest.json"
exit 0
