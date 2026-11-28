<#
.SYNOPSIS
    构建 NewCosmos Agent / Shell 的 Windows 按用户安装包（Inno Setup）。

.DESCRIPTION
    1. dotnet publish Agent、Shell：Release / win-x64 / self-contained
    2. 用 ISCC 编译 deploy\installer\NewCosmosAgent.iss、NewCosmosShell.iss
    3. 输出 publish\installers\NewCosmosAgentSetup_<版本>.exe
             publish\installers\NewCosmosShellSetup_<版本>.exe
    4. 可选 -Sign：先签发布目录里的 exe，再签最终安装包

    Inno 探测顺序：
      -IsccPath 参数
      %LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe（本机已安装路径）
      %LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
      C:\Program Files\Inno Setup 7\ISCC.exe
      C:\Program Files (x86)\Inno Setup 7\ISCC.exe
      C:\Program Files\Inno Setup 6\ISCC.exe
      C:\Program Files (x86)\Inno Setup 6\ISCC.exe
      PATH 中的 ISCC.exe

    退出码：
      0  成功
      2  缺少 dotnet / Inno / 项目文件 / 参数错误
      3  dotnet publish 失败
      4  ISCC 编译失败
      5  签名失败

.PARAMETER IsccPath
    指定 ISCC.exe；不传则自动探测。

.PARAMETER Version
    覆盖 Directory.Build.props 中的 Version（只影响安装包显示名和文件名）。

.PARAMETER SkipPublish
    跳过 dotnet publish，直接使用 publish\agent、publish\shell 现有产物。

.PARAMETER Sign
    构建完成后调用 sign-windows.ps1 签名 exe 和安装包。

.EXAMPLE
    powershell -File deploy\scripts\build-installers.ps1

.EXAMPLE
    $env:NC_CODESIGN_PFX_PASSWORD = '<强密码>'
    powershell -File deploy\scripts\build-installers.ps1 -Sign

.EXAMPLE
    powershell -File deploy\scripts\build-installers.ps1 `
      -IsccPath 'C:\Users\me\AppData\Local\Programs\Inno Setup 7\ISCC.exe'
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$IsccPath = '',
    [string]$Version = '',
    [string]$OutputDir = '',
    [string]$PfxPath = '',
    [string]$PfxPasswordEnv = 'NC_CODESIGN_PFX_PASSWORD',
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$SkipPublish,
    [switch]$Sign
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$AgentProj = Join-Path $RepoRoot 'src\NewCosmos.Agent\NewCosmos.Agent.csproj'
$ShellProj = Join-Path $RepoRoot 'src\NewCosmos.Shell\NewCosmos.Shell.csproj'
$AgentIss = Join-Path $RepoRoot 'deploy\installer\NewCosmosAgent.iss'
$ShellIss = Join-Path $RepoRoot 'deploy\installer\NewCosmosShell.iss'
$AgentPublishDir = Join-Path $RepoRoot 'publish\agent'
$ShellPublishDir = Join-Path $RepoRoot 'publish\shell'
$SignScript = Join-Path $PSScriptRoot 'sign-windows.ps1'
$WebView2Cache = Join-Path $RepoRoot 'deploy\installer\cache\MicrosoftEdgeWebview2Setup.exe'

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot 'publish\installers'
}
if (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot $OutputDir
}
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

if ([string]::IsNullOrWhiteSpace($PfxPath)) {
    $PfxPath = Join-Path $RepoRoot 'deploy\keys\NewCosmosCodeSign.pfx'
}
if (-not [System.IO.Path]::IsPathRooted($PfxPath)) {
    $PfxPath = Join-Path $RepoRoot $PfxPath
}
$PfxPath = [System.IO.Path]::GetFullPath($PfxPath)

if (-not [string]::IsNullOrWhiteSpace($IsccPath) -and -not [System.IO.Path]::IsPathRooted($IsccPath)) {
    $IsccPath = Join-Path (Get-Location).Path $IsccPath
}

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

function Read-BuildVersion {
    $propsPath = Join-Path $RepoRoot 'Directory.Build.props'
    if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
        Stop-WithError "找不到 $propsPath，无法确定版本。" 2
    }

    [xml]$props = Get-Content -LiteralPath $propsPath -Raw -Encoding UTF8
    $propertyGroup = @($props.Project.PropertyGroup)[0]
    $v = [string]$propertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($v)) {
        Stop-WithError 'Directory.Build.props 缺少 <Version>。' 2
    }

    $numeric = [string]$propertyGroup.AssemblyVersion
    if ($numeric -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        $numeric = '2.0.0.0'
    }
    return [pscustomobject]@{ Version = $v; NumericVersion = $numeric }
}

function Resolve-Iscc {
    if (-not [string]::IsNullOrWhiteSpace($IsccPath)) {
        if (Test-Path -LiteralPath $IsccPath -PathType Leaf) {
            return (Resolve-Path -LiteralPath $IsccPath).Path
        }
        Stop-WithError "指定的 ISCC 不存在：$IsccPath" 2
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'))
        $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
    }
    $candidates.Add('C:\Program Files\Inno Setup 7\ISCC.exe')
    $candidates.Add('C:\Program Files (x86)\Inno Setup 7\ISCC.exe')
    $candidates.Add('C:\Program Files\Inno Setup 6\ISCC.exe')
    $candidates.Add('C:\Program Files (x86)\Inno Setup 6\ISCC.exe')

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }

    Write-Host '未找到 Inno Setup 编译器 ISCC.exe。' -ForegroundColor Red
    Write-Host '请安装 Inno Setup 7（或 6），或显式指定路径：' -ForegroundColor Yellow
    Write-Host '  powershell -File deploy\scripts\build-installers.ps1 -IsccPath "C:\Users\<你>\AppData\Local\Programs\Inno Setup 7\ISCC.exe"'
    Write-Host '本机已探测过：'
    foreach ($candidate in $candidates) { Write-Host "  $candidate" }
    Write-Host '下载：https://jrsoftware.org/isdl.php'
    exit 2
}

function Invoke-Publish {
    param([string]$Project, [string]$OutDir, [string]$Label)

    if (-not (Test-Path -LiteralPath $Project -PathType Leaf)) {
        Stop-WithError "找不到项目：$Project" 2
    }

    Write-Step "发布 $Label -> $OutDir"
    if (Test-Path -LiteralPath $OutDir) {
        Remove-Item -LiteralPath $OutDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

    $publishArgs = @(
        'publish', $Project,
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', 'true',
        # 不用 PublishSingleFile：.NET 单文件自带 deflate 压缩，Inno 的 lzma2 再压不动，
        # 实测安装包反而变大（Shell 98.6→127.1MB、Agent 49.3→61.8MB）。
        # 保持散文件夹，让 lzma2 直接压原始 IL，压缩率最高。
        '-p:PublishDocumentationFiles=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $OutDir
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "dotnet publish 失败（$Label，exit=$LASTEXITCODE）。" 3
    }
}

function Invoke-SignFile {
    param([string]$File)

    Write-Host "签名：$File"
    $signParams = @{
        Path = $File
        PfxPath = $PfxPath
        PfxPasswordEnv = $PfxPasswordEnv
        TimestampUrl = $TimestampUrl
    }
    & $SignScript @signParams
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "签名失败（exit=$LASTEXITCODE）：$File" 5
    }
}

# ---------------------------------------------------------------------------
# 0. 前置检查
# ---------------------------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-WithError '找不到 dotnet，请安装 .NET 10 SDK。' 2
}
foreach ($file in @($AgentIss, $ShellIss, $SignScript)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        Stop-WithError "缺少构建文件：$file" 2
    }
}
if ($Sign) {
    if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) {
        Stop-WithError "启用 -Sign 但找不到 PFX：$PfxPath`n请先运行 deploy\scripts\new-codesign-cert.ps1，或用 -PfxPath 指定。" 2
    }
}

$iscc = Resolve-Iscc
Write-Step "使用 ISCC：$iscc"

$buildVersion = Read-BuildVersion
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $buildVersion.Version = $Version
}
Write-Step "版本：$($buildVersion.Version)（文件版本 $($buildVersion.NumericVersion)）"

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# ---------------------------------------------------------------------------
# 1. dotnet publish
# ---------------------------------------------------------------------------
if (-not $SkipPublish) {
    Invoke-Publish -Project $AgentProj -OutDir $AgentPublishDir -Label 'NewCosmos.Agent'
    Invoke-Publish -Project $ShellProj -OutDir $ShellPublishDir -Label 'NewCosmos.Shell'
}
else {
    Write-Step '跳过 dotnet publish（-SkipPublish）'
}

$agentExe = Join-Path $AgentPublishDir 'NewCosmos.Agent.exe'
$shellExe = Join-Path $ShellPublishDir 'NewCosmos.Shell.exe'
if (-not (Test-Path -LiteralPath $agentExe -PathType Leaf)) {
    Stop-WithError "缺少 $agentExe。请去掉 -SkipPublish 重新构建，或先执行 dotnet publish。" 2
}
if (-not (Test-Path -LiteralPath $shellExe -PathType Leaf)) {
    Stop-WithError "缺少 $shellExe。请去掉 -SkipPublish 重新构建，或先执行 dotnet publish。" 2
}

if (-not (Test-Path -LiteralPath $WebView2Cache -PathType Leaf)) {
    Write-Warning "未找到 WebView2 离线安装包：$WebView2Cache"
    Write-Warning '安装包仍可编译，但目标机缺少 WebView2 时只会提示下载：https://go.microsoft.com/fwlink/p/?LinkId=2124703'
}

# ---------------------------------------------------------------------------
# 2. 可选：先签发布目录里的 exe（这样安装包内也是已签名文件）
# ---------------------------------------------------------------------------
if ($Sign) {
    Invoke-SignFile -File $agentExe
    Invoke-SignFile -File $shellExe
}

# ---------------------------------------------------------------------------
# 3. ISCC 编译
# ---------------------------------------------------------------------------
$envNames = @(
    'NC_APP_VERSION',
    'NC_APP_NUMERIC_VERSION',
    'NC_AGENT_PUBLISH_DIR',
    'NC_SHELL_PUBLISH_DIR',
    'NC_OUTPUT_DIR'
)
$savedEnv = @{}
foreach ($name in $envNames) {
    $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name, [EnvironmentVariableTarget]::Process)
}

try {
    $env:NC_APP_VERSION = $buildVersion.Version
    $env:NC_APP_NUMERIC_VERSION = $buildVersion.NumericVersion
    $env:NC_AGENT_PUBLISH_DIR = $AgentPublishDir
    $env:NC_SHELL_PUBLISH_DIR = $ShellPublishDir
    $env:NC_OUTPUT_DIR = $OutputDir

    Write-Step '编译 NewCosmos Agent 安装包'
    & $iscc $AgentIss
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "Agent 安装包编译失败（ISCC exit=$LASTEXITCODE）。" 4
    }

    Write-Step '编译 NewCosmos Shell 安装包'
    & $iscc $ShellIss
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "Shell 安装包编译失败（ISCC exit=$LASTEXITCODE）。" 4
    }
}
finally {
    foreach ($name in $envNames) {
        [Environment]::SetEnvironmentVariable($name, $savedEnv[$name], [EnvironmentVariableTarget]::Process)
    }
}

# ---------------------------------------------------------------------------
# 4. 结果检查 / 可选签名
# ---------------------------------------------------------------------------
$agentSetup = Join-Path $OutputDir "NewCosmosAgentSetup_$($buildVersion.Version).exe"
$shellSetup = Join-Path $OutputDir "NewCosmosShellSetup_$($buildVersion.Version).exe"

if (-not (Test-Path -LiteralPath $agentSetup -PathType Leaf)) {
    $agentSetup = @(Get-ChildItem -LiteralPath $OutputDir -Filter 'NewCosmosAgentSetup_*.exe' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
if (-not (Test-Path -LiteralPath $shellSetup -PathType Leaf)) {
    $shellSetup = @(Get-ChildItem -LiteralPath $OutputDir -Filter 'NewCosmosShellSetup_*.exe' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

if (-not (Test-Path -LiteralPath $agentSetup -PathType Leaf)) {
    Stop-WithError "编译结束但找不到 Agent 安装包：$OutputDir" 4
}
if (-not (Test-Path -LiteralPath $shellSetup -PathType Leaf)) {
    Stop-WithError "编译结束但找不到 Shell 安装包：$OutputDir" 4
}

if ($Sign) {
    Invoke-SignFile -File $agentSetup
    Invoke-SignFile -File $shellSetup
}

Write-Step '构建完成'
foreach ($setup in @($agentSetup, $shellSetup)) {
    $item = Get-Item -LiteralPath $setup
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ("  {0}" -f $item.FullName)
    Write-Host ("    {0:N1} MB  SHA256={1}" -f ($item.Length / 1MB), $hash)
}

Write-Host ''
Write-Host '下一步：' -ForegroundColor Cyan
Write-Host "  powershell -File deploy\scripts\write-update-manifest.ps1 -InstallerPath '$shellSetup' -Version '$($buildVersion.Version)'"
Write-Host '  把安装包和 latest.json 上传到 .2 的 /www/wwwroot/cosmos-static.sua17.cn/downloads/windows/'
exit 0
