<#
.SYNOPSIS
    将 Web 构建产物拆分发布到 .1（index.html）与 .2（assets）。

.DESCRIPTION
    拓扑：
      10.110.0.1  /www/wwwroot/cosmos.sua17.cn/index.html     只发入口，绝不发资源
      10.110.0.2  /www/wwwroot/cosmos-static.sua17.cn/assets/**      hash 静态资源，只新增不删除

    行为：
      1. 可选构建 web（-Build），默认使用现有 web\dist；
      2. 上传前在 .1 备份旧 index.html 为 index.html.bak.<时间戳>；
      3. index.html 先传临时文件再 mv，避免半截文件；
      4. assets 用 cp -a 覆盖/新增，绝不删除旧 hash 文件（回滚依赖旧资源仍在）；
      5. chown 到 nginx 用户并 nginx -t + reload。

    前提：
      - 本机有 ssh/scp，已把部署公钥加入两台服务器的 authorized_keys；
      - 服务器已安装 deploy/edge-1、deploy/app-2 的 nginx 配置；
      - 服务器有 /srv/newcosmos/web 目录。

.PARAMETER Build
    构建 web（npm ci / npm install + npm run build）。不传则直接使用 web\dist。

.PARAMETER SshKeyPath
    占位参数：改成实际部署私钥路径。默认值不可用，必须显式覆盖。

.EXAMPLE
    .\deploy-web-split.ps1 -Build -SshKeyPath C:\keys\newcosmos_deploy_ed25519

.EXAMPLE
    .\deploy-web-split.ps1 -SkipUpload
    只构建/整理本地产物，打印 staging 路径，不连服务器。
#>
[CmdletBinding()]
param(
    [switch]$Build,

    [string]$WebDir = "",

    [string]$EdgeHost = "10.110.0.1",
    [string]$EdgeRoot = "/www/wwwroot/cosmos.sua17.cn",

    [string]$AppHost = "10.110.0.2",
    [string]$AppWebRoot = "/www/wwwroot/cosmos-static.sua17.cn",

    [string]$RemoteUser = "root",
    [string]$WebUser = "www-data",
    [string]$SshPort = "22",
    [string]$SshKeyPath = "C:\path\to\newcosmos_deploy_ed25519",

    [switch]$SkipNpmCi,
    [switch]$SkipUpload
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "找不到命令 '$Name'，请先安装或加入 PATH。"
    }
}

function Get-SshBaseArgs {
    param([string]$TargetHost)
    return @(
        "-i", $SshKeyPath,
        "-p", $SshPort,
        "-o", "BatchMode=yes",
        "-o", "IdentitiesOnly=yes",
        "-o", "ConnectTimeout=10",
        "$RemoteUser@$TargetHost"
    )
}

function Invoke-Remote {
    param(
        [string]$TargetHost,
        [string]$Command
    )
    $sshArgs = Get-SshBaseArgs -TargetHost $TargetHost
    $sshArgs += $Command
    & ssh @sshArgs
    if ($LASTEXITCODE -ne 0) {
        throw "远程命令失败（host=$TargetHost, exit=$LASTEXITCODE）：$Command"
    }
}

function Invoke-RemoteCapture {
    param(
        [string]$TargetHost,
        [string]$Command
    )
    $sshArgs = Get-SshBaseArgs -TargetHost $TargetHost
    $sshArgs += $Command
    $output = & ssh @sshArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "远程命令失败（host=$TargetHost, exit=$LASTEXITCODE）：$Command`n$output"
    }
    return (($output | Out-String).Trim())
}

function Copy-ToRemote {
    param(
        [string]$LocalPath,
        [string]$TargetHost,
        [string]$RemotePath,
        [switch]$Recurse
    )
    $scpArgs = @(
        "-i", $SshKeyPath,
        "-P", $SshPort,
        "-o", "BatchMode=yes",
        "-o", "IdentitiesOnly=yes",
        "-o", "ConnectTimeout=10"
    )
    if ($Recurse) { $scpArgs += "-r" }
    $scpArgs += @($LocalPath, "$RemoteUser@${TargetHost}:$RemotePath")
    & scp @scpArgs
    if ($LASTEXITCODE -ne 0) {
        throw "上传失败（host=$TargetHost, path=$RemotePath, exit=$LASTEXITCODE）。"
    }
}

# ---------------------------------------------------------------------------
# 0. 路径与参数
# ---------------------------------------------------------------------------
$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($WebDir)) {
    $WebDir = Join-Path $RepoRoot "web"
}
if (-not (Test-Path -LiteralPath $WebDir -PathType Container)) {
    throw "Web 目录不存在：$WebDir"
}
$WebDir = (Resolve-Path -LiteralPath $WebDir).Path
$DistDir = Join-Path $WebDir "dist"

$EdgeRoot = $EdgeRoot.TrimEnd("/")
$AppWebRoot = $AppWebRoot.TrimEnd("/")

$Stamp = Get-Date -Format "yyyyMMddHHmmss"
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nc-web-deploy-" + $Stamp)
$LocalEdgeStage = Join-Path $TempRoot "edge"
$LocalAssetsStage = Join-Path $TempRoot "assets"
New-Item -ItemType Directory -Path $LocalEdgeStage, $LocalAssetsStage -Force | Out-Null

# ---------------------------------------------------------------------------
# 1. 可选构建
# ---------------------------------------------------------------------------
if ($Build) {
    Write-Step "构建 Web（$WebDir）"
    # Windows 上优先用 npm.cmd，避免 npm.ps1 被执行策略拦截。
    $npmCommand = Get-Command "npm.cmd" -ErrorAction SilentlyContinue
    if (-not $npmCommand) {
        $npmCommand = Get-Command "npm" -ErrorAction SilentlyContinue
    }
    if (-not $npmCommand) {
        throw "找不到 npm，请先安装 Node.js 并加入 PATH。"
    }
    $npm = $npmCommand.Source

    Push-Location $WebDir
    try {
        if (-not $SkipNpmCi) {
            if (Test-Path -LiteralPath (Join-Path $WebDir "package-lock.json")) {
                Write-Host "npm ci"
                & $npm ci
            }
            else {
                Write-Host "npm install（未找到 package-lock.json）"
                & $npm install
            }
            if ($LASTEXITCODE -ne 0) {
                throw "npm 安装依赖失败（exit=$LASTEXITCODE）。"
            }
        }

        Write-Host "npm run build"
        & $npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "npm run build 失败（exit=$LASTEXITCODE）。"
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $DistDir "index.html") -PathType Leaf)) {
    throw "找不到 $DistDir\index.html。请先执行 npm run build，或加 -Build 让脚本构建。"
}
if (-not (Test-Path -LiteralPath (Join-Path $DistDir "assets") -PathType Container)) {
    throw "找不到 $DistDir\assets。当前 Web 构建没有 assets 目录，请检查 Vite 输出配置。"
}

# ---------------------------------------------------------------------------
# 2. 本地 staging
# ---------------------------------------------------------------------------
Write-Step "整理本地发布产物"

Copy-Item -LiteralPath (Join-Path $DistDir "index.html") `
          -Destination (Join-Path $LocalEdgeStage "index.html") -Force

Copy-Item -Path (Join-Path $DistDir "assets\*") `
          -Destination $LocalAssetsStage -Recurse -Force

$indexSize = (Get-Item -LiteralPath (Join-Path $LocalEdgeStage "index.html")).Length
$assetFiles = @(Get-ChildItem -LiteralPath $LocalAssetsStage -Recurse -File)
if ($assetFiles.Count -eq 0) {
    throw "assets staging 为空，停止发布。"
}

Write-Host ("index.html : {0:N1} KB" -f ($indexSize / 1KB))
Write-Host ("assets     : {0} 个文件，{1:N1} MB" -f `
    $assetFiles.Count, (($assetFiles | Measure-Object -Property Length -Sum).Sum / 1MB))

if ($indexSize -gt (50 * 1KB)) {
    Write-Warning ("index.html 超过 50KB（{0:N1} KB），会直接占满 .1 的 3Mbps 首屏预算。" -f ($indexSize / 1KB))
}

# dist 根目录下除 index.html / assets 外的文件不会上传。
# 常见坑：favicon.ico、manifest.webmanifest、robots.txt 被页面以 /xxx 引用，
# 但 .1 只发 index、.2 只发 /assets 与 /downloads，这些文件会 404。
$extraFiles = @(Get-ChildItem -LiteralPath $DistDir -Force |
    Where-Object { $_.Name -notin @("index.html", "assets") })
if ($extraFiles.Count -gt 0) {
    Write-Warning ("dist 根目录还有 {0} 项未上传：{1}" -f `
        $extraFiles.Count, (($extraFiles | Select-Object -ExpandProperty Name) -join ", "))
    Write-Warning "请把它们放到会被构建进 /assets 的位置（或内联），否则线上会 404。"
}

$indexText = Get-Content -LiteralPath (Join-Path $DistDir "index.html") -Raw -Encoding UTF8
if ($indexText -match '(?:src|href)\s*=\s*["'']\./') {
    Write-Warning "index.html 里出现相对路径 ./，请确认 Vite base 配置为 '/'。"
}

# ---------------------------------------------------------------------------
# 3. 上传
# ---------------------------------------------------------------------------
if ($SkipUpload) {
    Write-Step "已跳过上传（-SkipUpload）"
    Write-Host "本地 index staging : $(Join-Path $LocalEdgeStage 'index.html')"
    Write-Host "本地 assets staging: $LocalAssetsStage"
    Write-Host "时间戳              : $Stamp"
    return
}

Write-Step "检查 SSH 参数"
if ([string]::IsNullOrWhiteSpace($SshKeyPath) -or -not (Test-Path -LiteralPath $SshKeyPath -PathType Leaf)) {
    throw "SSH 私钥不存在：$SshKeyPath。请用 -SshKeyPath 指定实际部署私钥（占位默认值不可用）。"
}
Assert-Command "ssh"
Assert-Command "scp"

# ---------------------------------------------------------------------------
# 3.1 发布 index 到 .1
# ---------------------------------------------------------------------------
Write-Step "发布 index.html 到 .1（$EdgeHost）"

$remoteIndexTmp = "/tmp/nc-index-$Stamp.html"

Invoke-Remote -TargetHost $EdgeHost -Command (
    "install -d -m 0755 -o '$WebUser' -g '$WebUser' '$EdgeRoot' && " +
    "if [ -f '$EdgeRoot/index.html' ]; then " +
    "cp -a '$EdgeRoot/index.html' '$EdgeRoot/index.html.bak.$Stamp'; " +
    "fi"
)

Copy-ToRemote -LocalPath (Join-Path $LocalEdgeStage "index.html") `
    -TargetHost $EdgeHost -RemotePath $remoteIndexTmp

$publishIndexCmd =
    "install -m 0644 -o '$WebUser' -g '$WebUser' '$remoteIndexTmp' '$EdgeRoot/index.html.new' && " +
    "mv -f '$EdgeRoot/index.html.new' '$EdgeRoot/index.html' && " +
    "rm -f '$remoteIndexTmp' && " +
    "chown '$WebUser':'$WebUser' '$EdgeRoot/index.html' && " +
    "chmod 0644 '$EdgeRoot/index.html' && " +
    "nginx -t && systemctl reload nginx"

Invoke-Remote -TargetHost $EdgeHost -Command $publishIndexCmd

$localHash = (Get-FileHash -LiteralPath (Join-Path $LocalEdgeStage "index.html") -Algorithm SHA256).Hash.ToLowerInvariant()
$remoteHash = (Invoke-RemoteCapture -TargetHost $EdgeHost `
    -Command "sha256sum '$EdgeRoot/index.html' | cut -d' ' -f1").ToLowerInvariant()
if ($localHash -ne $remoteHash) {
    throw "index.html 校验失败：local=$localHash remote=$remoteHash"
}
Write-Host "index.html SHA256 校验通过：$localHash"

# ---------------------------------------------------------------------------
# 3.2 发布 assets 到 .2（只新增/覆盖，不删除）
# ---------------------------------------------------------------------------
Write-Step "发布 assets 到 .2（$AppHost）"

$remoteAssetsTmp = "/tmp/nc-assets-$Stamp"

Invoke-Remote -TargetHost $AppHost -Command (
    "install -d -m 0755 -o '$WebUser' -g '$WebUser' '$AppWebRoot' '$AppWebRoot/assets' && " +
    "rm -rf '$remoteAssetsTmp'"
)

Copy-ToRemote -LocalPath $LocalAssetsStage -TargetHost $AppHost `
    -RemotePath $remoteAssetsTmp -Recurse

$publishAssetsCmd =
    "cp -a '$remoteAssetsTmp/.' '$AppWebRoot/assets/' && " +
    "chown -R '$WebUser':'$WebUser' '$AppWebRoot/assets' && " +
    "chmod -R u=rwX,go=rX '$AppWebRoot/assets' && " +
    "rm -rf '$remoteAssetsTmp' && " +
    "nginx -t && systemctl reload nginx"

Invoke-Remote -TargetHost $AppHost -Command $publishAssetsCmd

Write-Host ("assets 已发布：{0} 个文件（旧 hash 文件保留，未删除）" -f $assetFiles.Count)

# ---------------------------------------------------------------------------
# 4. 完成
# ---------------------------------------------------------------------------
Write-Step "发布完成"
Write-Host "index : $EdgeHost`:$EdgeRoot/index.html"
Write-Host "assets: $AppHost`:$AppWebRoot/assets/"
Write-Host "备份  : $EdgeHost`:$EdgeRoot/index.html.bak.$Stamp"
Write-Host ""
Write-Host "回滚 index（示例）：" -ForegroundColor Yellow
Write-Host "  ssh -i `"$SshKeyPath`" $RemoteUser@$EdgeHost `"cp -a '$EdgeRoot/index.html.bak.$Stamp' '$EdgeRoot/index.html' && nginx -t && systemctl reload nginx`""
Write-Host "本地 staging 保留在：$TempRoot"
