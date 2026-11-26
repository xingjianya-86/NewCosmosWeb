<#
.SYNOPSIS
    用 signtool.exe 对 Windows 发布物（exe/msi）做 Authenticode 签名。

.DESCRIPTION
    自动查找：
      1. PATH 中的 signtool.exe
      2. C:\Program Files (x86)\Windows Kits\10\bin\<版本>\x64\signtool.exe
      3. C:\Program Files\Windows Kits\10\bin\<版本>\x64\signtool.exe
    优先使用最新 Windows SDK 版本。

    PFX 密码从环境变量读取（默认 NC_CODESIGN_PFX_PASSWORD），不写在命令行历史里。
    默认使用 DigiCert 时间戳，保证证书过期后旧安装包仍可验证。

    退出码：
      0  全部成功
      2  参数/文件错误
      3  找不到 signtool.exe
      4  PFX 或密码不可用
      5  signtool 签名失败
      6  签名后校验失败（没有签名者/哈希不匹配等）

.PARAMETER Path
    要签名的文件或目录；目录会递归收集 *.exe / *.msi。

.PARAMETER PfxPath
    代码签名 PFX。默认 <仓库根>\deploy\keys\NewCosmosCodeSign.pfx。

.PARAMETER PfxPasswordEnv
    PFX 密码环境变量名，默认 NC_CODESIGN_PFX_PASSWORD。

.PARAMETER TimestampUrl
    时间戳服务，默认 http://timestamp.digicert.com。传空字符串可跳过（不推荐）。

.EXAMPLE
    $env:NC_CODESIGN_PFX_PASSWORD = '<强密码>'
    powershell -File deploy\scripts\sign-windows.ps1 -Path publish\installers

.EXAMPLE
    powershell -File deploy\scripts\sign-windows.ps1 `
      -Path .\publish\agent\NewCosmos.Agent.exe,.\publish\installers\NewCosmosAgentSetup_2.0.20261124.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,

    [string]$PfxPath = '',
    [string]$PfxPasswordEnv = 'NC_CODESIGN_PFX_PASSWORD',
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($PfxPath)) {
    $PfxPath = Join-Path $RepoRoot 'deploy\keys\NewCosmosCodeSign.pfx'
}
if (-not [System.IO.Path]::IsPathRooted($PfxPath)) {
    $PfxPath = Join-Path (Get-Location).Path $PfxPath
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

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    $roots = New-Object System.Collections.Generic.List[string]
    $programFilesX86 = ${env:ProgramFiles(x86)}
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $roots.Add((Join-Path $programFilesX86 'Windows Kits\10\bin'))
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $roots.Add((Join-Path $env:ProgramFiles 'Windows Kits\10\bin'))
    }

    foreach ($root in $roots) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root -PathType Container)) { continue }

        $versionDirs = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue | Sort-Object {
            try { [version]$_.Name } catch { [version]'0.0.0.0' }
        } -Descending)

        foreach ($versionDir in $versionDirs) {
            foreach ($arch in @('x64', 'x86', 'arm64')) {
                $candidate = Join-Path $versionDir.FullName "$arch\signtool.exe"
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
            }
        }
    }
    return $null
}

$resolvedFiles = New-Object System.Collections.Generic.List[string]
foreach ($item in $Path) {
    if ([string]::IsNullOrWhiteSpace($item)) { continue }

    if (Test-Path -LiteralPath $item -PathType Leaf) {
        $resolved = (Resolve-Path -LiteralPath $item).Path
        if ($resolved -notmatch '\.(exe|msi)$') {
            Write-Warning "跳过非 exe/msi 文件：$resolved"
            continue
        }
        $resolvedFiles.Add($resolved)
        continue
    }

    if (Test-Path -LiteralPath $item -PathType Container) {
        $children = @(Get-ChildItem -LiteralPath $item -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in '.exe', '.msi' })
        foreach ($child in $children) { $resolvedFiles.Add($child.FullName) }
        continue
    }

    Write-Warning "路径不存在：$item"
}

$files = @($resolvedFiles | Sort-Object -Unique)
if ($files.Count -eq 0) {
    Stop-WithError '没有找到可签名的 .exe / .msi 文件。请检查 -Path。' 2
}

if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) {
    Stop-WithError "找不到 PFX：$PfxPath`n请先运行 deploy\scripts\new-codesign-cert.ps1，或用 -PfxPath 指定。" 4
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

$signTool = Find-SignTool
if ($null -eq $signTool) {
    Stop-WithError @'
找不到 signtool.exe。
请安装 Windows 10/11 SDK（勾选 “Windows SDK Signing Tools for Desktop Apps”），
或把 signtool.exe 加入 PATH。常见路径：
  C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe
'@ 3
}

Write-Step "使用 signtool：$signTool"
Write-Step "待签名文件：$($files.Count) 个"

$failed = 0
foreach ($file in $files) {
    Write-Host "签名：$file"
    $signArgs = @(
        'sign',
        '/fd', 'SHA256',
        '/f', $PfxPath,
        '/p', $password
    )
    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256')
    }
    else {
        Write-Warning '未指定时间戳 URL；证书过期后签名将失效。'
    }
    $signArgs += $file

    & $signTool @signArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "签名失败（exit=$LASTEXITCODE）：$file" -ForegroundColor Red
        $failed++
        continue
    }

    $auth = Get-AuthenticodeSignature -FilePath $file
    $status = [string]$auth.Status
    if ($null -eq $auth.SignerCertificate) {
        Write-Host "签名后校验失败：没有签名者证书（$file）" -ForegroundColor Red
        $failed++
        continue
    }

    if ($status -in @('HashMismatch', 'NotSigned', 'NotSupported', 'Incompatible')) {
        Write-Host "签名后校验失败：状态 $status（$file）" -ForegroundColor Red
        $failed++
        continue
    }

    if ($status -in @('NotTrusted', 'UnknownError')) {
        Write-Host "  已签名；本机尚未信任该自签名证书（Status=$status）。" -ForegroundColor Yellow
        Write-Host '  客户端需导入 NewCosmosCodeSign.cer 到 Root/TrustedPublisher。' -ForegroundColor Yellow
    }
    else {
        Write-Host "  已签名；Status=$status" -ForegroundColor Green
    }
    Write-Host "  签名者：$($auth.SignerCertificate.Subject)"
}

if ($failed -gt 0) {
    Stop-WithError "$failed 个文件签名或校验失败。" 5
}

Write-Step '全部签名成功'
exit 0
