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
    默认部署私钥；.1/.2 未单独指定时使用它。

.PARAMETER EdgeSshKeyPath
    .1（edge）专用私钥；留空回退 -SshKeyPath。

.PARAMETER AppSshKeyPath
    .2（app）专用私钥；留空回退 -SshKeyPath。

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
    [string]$EdgeSshKeyPath = "",
    [string]$AppSshKeyPath = "",

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

# .1 与 .2 可用不同部署密钥；未单独指定时回退到 -SshKeyPath
function Get-KeyForHost {
    param([string]$TargetHost)
    if ($TargetHost -eq $EdgeHost -and -not [string]::IsNullOrWhiteSpace($EdgeSshKeyPath)) {
        return $EdgeSshKeyPath
    }
    if ($TargetHost -eq $AppHost -and -not [string]::IsNullOrWhiteSpace($AppSshKeyPath)) {
        return $AppSshKeyPath
    }
    return $SshKeyPath
}

function Get-SshBaseArgs {
    param([string]$TargetHost)
    return @(
        "-i", (Get-KeyForHost -TargetHost $TargetHost),
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
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        # 2>&1 把 SSH 登录横幅并进输出流：PS5.1 下仅用 2> 时，横幅可能在 EAP 还原后
        # 仍被当 NativeCommandError 刷到控制台（发布日志里的 "Debian GNU/Linux 13"）。
        $raw = & ssh @sshArgs 2>&1
        $code = $LASTEXITCODE
        $errText = (($raw | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } |
            ForEach-Object { $_.ToString() }) -join [Environment]::NewLine).Trim()
    }
    finally {
        $ErrorActionPreference = $oldEap
    }
    if ($code -ne 0) {
        throw "远程命令失败（host=$TargetHost, exit=$code）：$Command`n$errText"
    }
}

function Invoke-RemoteCapture {
    param(
        [string]$TargetHost,
        [string]$Command
    )
    $sshArgs = Get-SshBaseArgs -TargetHost $TargetHost
    $sshArgs += $Command
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        $raw = & ssh @sshArgs 2>&1
        $code = $LASTEXITCODE
        $errText = (($raw | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } |
            ForEach-Object { $_.ToString() }) -join [Environment]::NewLine).Trim()
        $text = (($raw | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) | Out-String).Trim()
    }
    finally {
        $ErrorActionPreference = $oldEap
    }
    if ($code -ne 0) {
        throw "远程命令失败（host=$TargetHost, exit=$code）：$Command`n$errText"
    }
    # 成功时只返回 stdout；SSH 登录横幅已在上面按 ErrorRecord 过滤掉，不会混进哈希/输出。
    return $text
}

function Copy-ToRemote {
    param(
        [string]$LocalPath,
        [string]$TargetHost,
        [string]$RemotePath,
        [switch]$Recurse
    )
    $scpArgs = @(
        "-i", (Get-KeyForHost -TargetHost $TargetHost),
        "-P", $SshPort,
        "-o", "BatchMode=yes",
        "-o", "IdentitiesOnly=yes",
        "-o", "ConnectTimeout=10"
    )
    if ($Recurse) { $scpArgs += "-r" }
    $scpArgs += @($LocalPath, "$RemoteUser@${TargetHost}:$RemotePath")
    # SSH/scp 会把服务器横幅写到 stderr；用 2>&1 并进输出流，避免 PS5.1 在 EAP 还原后
    # 仍把它当 NativeCommandError 刷到控制台。真正的失败由 $LASTEXITCODE 判定。
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        $raw = & scp @scpArgs 2>&1
        $exitCode = $LASTEXITCODE
        $errText = (($raw | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } |
            ForEach-Object { $_.ToString() }) -join [Environment]::NewLine).Trim()
    } finally {
        $ErrorActionPreference = $oldEap
    }
    if ($exitCode -ne 0) {
        $detail = if ([string]::IsNullOrWhiteSpace($errText)) { '' } else { "`n$errText" }
        throw "上传失败（host=$TargetHost, path=$RemotePath, exit=$exitCode）。$detail"
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

# .1：根目录所有文件 + 需同源访问的目录。
# 服务器策略：
#   .1 的 nginx 对 ^/根级静态文件(非 index.html) 一律 404 → .json 必须放子目录，
#                    且子目录路径(带斜杠)才走 location / 的 try_files；
#   .2 的 nginx 只放行 /assets/ /images/ /splash/ /downloads/ /api/，
#                    所以 terminal/ 页面与 data/ 数据必须发到 .1。
$EdgeDirNames = @('terminal', 'data')

$edgeFiles = @(Get-ChildItem -LiteralPath $DistDir -File -Force)
if ($edgeFiles.Count -eq 0) { throw "dist 根目录没有文件，停止发布。" }
foreach ($f in $edgeFiles) {
    Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $LocalEdgeStage $f.Name) -Force
}

# .2：静态资源目录（服务器只放行 /assets/ /images/ /splash/ /downloads/ /api/）
$edgeDirs = @()
$staticDirs = @()
foreach ($d in @(Get-ChildItem -LiteralPath $DistDir -Directory)) {
    if ($EdgeDirNames -contains $d.Name) { $edgeDirs += $d } else { $staticDirs += $d }
}
if ($staticDirs.Count -eq 0) { throw "dist 下没有静态资源目录，停止发布。" }

$staticNames = @()
foreach ($d in $staticDirs) {
    $dest = Join-Path $LocalAssetsStage $d.Name
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item -Path (Join-Path $d.FullName "*") -Destination $dest -Recurse -Force
    $staticNames += $d.Name
}
foreach ($d in $edgeDirs) {
    $dest = Join-Path $LocalEdgeStage $d.Name
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item -Path (Join-Path $d.FullName "*") -Destination $dest -Recurse -Force
}

$indexSize = (Get-Item -LiteralPath (Join-Path $LocalEdgeStage "index.html")).Length
$assetFiles = @(Get-ChildItem -LiteralPath $LocalAssetsStage -Recurse -File)
if ($assetFiles.Count -eq 0) { throw "静态资源 staging 为空，停止发布。" }

Write-Host ("入口文件   : {0} 个 → .1（{1}）" -f `
    $edgeFiles.Count, (($edgeFiles | Select-Object -ExpandProperty Name) -join ", "))
Write-Host ("入口目录   : {0} → .1（{1}）" -f `
    $edgeDirs.Count, (($edgeDirs | Select-Object -ExpandProperty Name) -join ", "))
Write-Host ("静态资源   : {0} 个文件，{1:N1} MB → .2/{2}" -f `
    $assetFiles.Count, (($assetFiles | Measure-Object -Property Length -Sum).Sum / 1MB), `
    (($staticNames | ForEach-Object { $_ }) -join " / "))

if ($indexSize -gt (50 * 1KB)) {
    Write-Warning ("index.html 超过 50KB（{0:N1} KB），会直接占满 .1 的 3Mbps 首屏预算。" -f ($indexSize / 1KB))
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
foreach ($keyInfo in @(
        @{ Label = '.1'; Path = (Get-KeyForHost -TargetHost $EdgeHost) },
        @{ Label = '.2'; Path = (Get-KeyForHost -TargetHost $AppHost) }
    )) {
    if ([string]::IsNullOrWhiteSpace($keyInfo.Path) -or -not (Test-Path -LiteralPath $keyInfo.Path -PathType Leaf)) {
        throw "SSH 私钥不存在（$($keyInfo.Label)）：$($keyInfo.Path)。请用 -SshKeyPath / -EdgeSshKeyPath / -AppSshKeyPath 指定实际部署私钥。"
    }
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

# 其余入口文件（download.html / download-data.json 等）：备份 → 上传 → 原子替换 → SHA256 校验
$otherFiles = @($edgeFiles | Where-Object { $_.Name -ne "index.html" })
foreach ($f in $otherFiles) {
    $name = $f.Name
    $remoteTmp = "/tmp/nc-page-$Stamp-$name"

    Invoke-Remote -TargetHost $EdgeHost -Command (
        "if [ -f '$EdgeRoot/$name' ]; then " +
        "cp -a '$EdgeRoot/$name' '$EdgeRoot/$name.bak.$Stamp'; fi"
    )

    Copy-ToRemote -LocalPath $f.FullName -TargetHost $EdgeHost -RemotePath $remoteTmp

    Invoke-Remote -TargetHost $EdgeHost -Command (
        "install -m 0644 -o '$WebUser' -g '$WebUser' '$remoteTmp' '$EdgeRoot/$name.new' && " +
        "mv -f '$EdgeRoot/$name.new' '$EdgeRoot/$name' && " +
        "rm -f '$remoteTmp' && " +
        "chown '$WebUser':'$WebUser' '$EdgeRoot/$name' && " +
        "chmod 0644 '$EdgeRoot/$name'"
    )

    $pageLocal = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $pageRemote = (Invoke-RemoteCapture -TargetHost $EdgeHost `
        -Command "sha256sum '$EdgeRoot/$name' | cut -d' ' -f1").ToLowerInvariant()
    if ($pageLocal -ne $pageRemote) {
        throw "$name 校验失败：local=$pageLocal remote=$pageRemote"
    }
    Write-Host "$name SHA256 校验通过：$pageLocal"
}

# 页面目录（terminal/ 等）：整体发到 .1；先删后传避免 scp 嵌套目录
foreach ($d in $edgeDirs) {
    $name = $d.Name
    if ($name -notmatch '^[A-Za-z0-9_-]+$') {
        throw "非法页面目录名：$name"
    }
    $localDir = Join-Path $LocalEdgeStage $name

    Invoke-Remote -TargetHost $EdgeHost -Command ("rm -rf '$EdgeRoot/$name'")
    Copy-ToRemote -LocalPath $localDir -TargetHost $EdgeHost -RemotePath "$EdgeRoot/$name" -Recurse

    $idxLocal = Join-Path $localDir 'index.html'
    if (Test-Path -LiteralPath $idxLocal -PathType Leaf) {
        $lh = (Get-FileHash -LiteralPath $idxLocal -Algorithm SHA256).Hash.ToLowerInvariant()
        $rh = (Invoke-RemoteCapture -TargetHost $EdgeHost `
            -Command "sha256sum '$EdgeRoot/$name/index.html' | cut -d' ' -f1").ToLowerInvariant()
        if ($lh -ne $rh) { throw "$name/index.html 校验失败：local=$lh remote=$rh" }
        Write-Host "$name/index.html SHA256 校验通过：$lh"
    }
    Invoke-Remote -TargetHost $EdgeHost -Command (
        "chown -R '$WebUser':'$WebUser' '$EdgeRoot/$name' && " +
        "find '$EdgeRoot/$name' -type d -exec chmod 0755 {} + && " +
        "find '$EdgeRoot/$name' -type f -exec chmod 0644 {} +"
    )
}

# ---------------------------------------------------------------------------
# 3.2 发布静态资源到 .2（只新增/覆盖，不删除）
# ---------------------------------------------------------------------------
Write-Step ("发布静态资源到 .2（$AppHost）：{0}" -f ($staticNames -join " / "))

$remoteAssetsTmp = "/tmp/nc-assets-$Stamp"

# 只创建各静态资源子目录；**绝不对 $AppWebRoot 本身做 install/cp -a**——
# 曾用 `cp -a tmp/. AppWebRoot/` 把根目录权限改成 700，nginx 进不去导致整站 404。
$mkdirCmds = @()
foreach ($n in $staticNames) {
    $mkdirCmds += "install -d -m 0755 -o '$WebUser' -g '$WebUser' '$AppWebRoot/$n'"
}
$mkdirCmds += "rm -rf '$remoteAssetsTmp'"

Invoke-Remote -TargetHost $AppHost -Command ($mkdirCmds -join " && ")

Copy-ToRemote -LocalPath $LocalAssetsStage -TargetHost $AppHost `
    -RemotePath $remoteAssetsTmp -Recurse

# 逐目录覆盖/新增，绝不删除（回滚依赖旧 hash 文件仍在）
$publishCmds = @()
foreach ($n in $staticNames) {
    $publishCmds += "cp -a '$remoteAssetsTmp/$n/.' '$AppWebRoot/$n/'"
    $publishCmds += "chown -R '$WebUser':'$WebUser' '$AppWebRoot/$n'"
    $publishCmds += "chmod -R u=rwX,go=rX '$AppWebRoot/$n'"
}
$publishCmds += "rm -rf '$remoteAssetsTmp'"
$publishCmds += "nginx -t"
$publishCmds += "systemctl reload nginx"

Invoke-Remote -TargetHost $AppHost -Command ($publishCmds -join " && ")

Write-Host ("静态资源已发布：{0} 个文件 → .2/{1}（旧 hash 文件保留，未删除）" -f `
    $assetFiles.Count, (($staticNames | ForEach-Object { $_ }) -join " / "))

# ---------------------------------------------------------------------------
# 4. 完成
# ---------------------------------------------------------------------------
Write-Step "发布完成"
Write-Host "入口  : $EdgeHost`:$EdgeRoot/  （$(($edgeFiles | ForEach-Object { $_.Name }) -join ', ')）"
Write-Host "静态  : $AppHost`:$AppWebRoot/  （$(($staticNames | ForEach-Object { $_ }) -join ' / ')）"
Write-Host "备份  : $EdgeHost`:$EdgeRoot/*.bak.$Stamp"
Write-Host ""
Write-Host "回滚 index（示例）：" -ForegroundColor Yellow
Write-Host "  ssh -i `"$(Get-KeyForHost -TargetHost $EdgeHost)`" $RemoteUser@$EdgeHost `"cp -a '$EdgeRoot/index.html.bak.$Stamp' '$EdgeRoot/index.html' && nginx -t && systemctl reload nginx`""
Write-Host "本地 staging 保留在：$TempRoot"
