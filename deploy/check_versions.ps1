# NewCosmos 版本一致性检查（发布前跑一次）
# 用法: powershell -File deploy/check_versions.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Read-JsonVersion([string]$path) {
    if (-not (Test-Path $path)) { throw "缺少文件: $path" }
    return (Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json).version
}

$webVersion = Read-JsonVersion (Join-Path $root 'web/package.json')

$propsPath = Join-Path $root 'Directory.Build.props'
[xml]$props = Get-Content $propsPath -Raw -Encoding UTF8
$msVersion = $props.Project.PropertyGroup.Version | Select-Object -First 1
$informational = $props.Project.PropertyGroup.InformationalVersion | Select-Object -First 1

$serverFallback = (Select-String -Path (Join-Path $root 'src/NewCosmos.Server/Configuration/ServerEnvironmentOptions.cs') `
    -Pattern '2\.0\.\d{8}' | Select-Object -First 1).Matches.Value
$agentFallback = (Select-String -Path (Join-Path $root 'src/NewCosmos.Agent/Services/AgentRuntime.cs') `
    -Pattern '2\.0\.\d{8}' | Select-Object -First 1).Matches.Value

$errors = @()
if ($webVersion -ne $msVersion) { $errors += "web/package.json=$webVersion 与 Directory.Build.props=$msVersion 不一致" }
if ($informational -ne $msVersion) { $errors += "InformationalVersion=$informational 与 Version=$msVersion 不一致" }
if ($serverFallback -and $serverFallback -ne $msVersion) { $errors += "Server 版本兜底=$serverFallback 与 $msVersion 不一致" }
if ($agentFallback -and $agentFallback -ne $msVersion) { $errors += "Agent 版本兜底=$agentFallback 与 $msVersion 不一致" }

# 禁止子项目再写死 <Version>
$csprojWithVersion = Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Recurse -File -Filter *.csproj |
    Select-String -Pattern '<Version>' -SimpleMatch
foreach ($hit in $csprojWithVersion) {
    $errors += "$($hit.Path):$($hit.LineNumber) 仍写死 <Version>，应继承 Directory.Build.props"
}

if ($errors.Count -gt 0) {
    Write-Host '版本一致性检查失败:' -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "版本一致性检查通过: $msVersion" -ForegroundColor Green
