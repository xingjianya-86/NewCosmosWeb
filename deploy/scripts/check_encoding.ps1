<#
.SYNOPSIS
    检查 deploy 下 PowerShell / Inno Setup 脚本是否为 UTF-8 with BOM。

.DESCRIPTION
    Windows PowerShell 5.1 会把“无 BOM 的 UTF-8 中文”按系统 ANSI（GBK/CP936）读取，
    导致脚本里的中文乱码甚至语法错误。项目规范要求含中文的 .ps1 / .iss 使用
    UTF-8 with BOM。

    检查范围：deploy\ 下 *.ps1 / *.psm1 / *.psd1 / *.iss
    退出码：0 通过；1 存在 BOM/编码问题。

.EXAMPLE
    powershell -File deploy\scripts\check_encoding.ps1

.EXAMPLE
    powershell -File deploy\scripts\check_encoding.ps1 -Root D:\Projects\NewCosmosWeb\deploy
#>
[CmdletBinding()]
param(
    [string]$Root = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
}
if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
    Write-Host "错误：目录不存在：$Root" -ForegroundColor Red
    exit 2
}
$Root = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')

$extensions = @('.ps1', '.psm1', '.psd1', '.iss')
$excludePattern = '\\(node_modules|\.git|bin|obj|publish|dist|\.opencode)\\'

$files = @(Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
        $extensions -contains $_.Extension.ToLowerInvariant() -and
        $_.FullName -notmatch $excludePattern
    })

$utf8Strict = New-Object System.Text.UTF8Encoding($false, $true)
$issues = New-Object System.Collections.Generic.List[string]

foreach ($file in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $nonAscii = $false
    foreach ($b in $bytes) {
        if ($b -gt 0x7F) { $nonAscii = $true; break }
    }

    $relative = $file.FullName.Substring($Root.Length).TrimStart('\')

    if (-not $hasBom -and $nonAscii) {
        try {
            [void]$utf8Strict.GetString($bytes, 0, $bytes.Length)
            $issues.Add("[BOM] $relative ：含非 ASCII 且是 UTF-8 无 BOM -> 请另存为 UTF-8 with BOM")
        }
        catch {
            $issues.Add("[BOM] $relative ：含非 ASCII 且不是合法 UTF-8（可能是 GBK/ANSI）-> 请转为 UTF-8 with BOM")
        }
        continue
    }

    if ($hasBom -and $bytes.Length -gt 3) {
        try {
            [void]$utf8Strict.GetString($bytes, 3, $bytes.Length - 3)
        }
        catch {
            $issues.Add("[BOM] $relative ：有 BOM 但内容不是合法 UTF-8 -> 请检查并重新保存")
        }
    }
}

if ($issues.Count -gt 0) {
    Write-Host '编码检查失败：' -ForegroundColor Red
    foreach ($issue in $issues) { Write-Host "  - $issue" -ForegroundColor Red }
    Write-Host ''
    Write-Host "共检查 $($files.Count) 个文件，发现 $($issues.Count) 个问题。" -ForegroundColor Red
    exit 1
}

Write-Host "编码检查通过：$($files.Count) 个脚本均为 UTF-8 with BOM（或纯 ASCII）。" -ForegroundColor Green
exit 0
