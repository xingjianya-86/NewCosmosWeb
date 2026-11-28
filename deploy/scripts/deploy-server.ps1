<#
.SYNOPSIS
    发布 NewCosmos.Server 到 10.110.0.2（Docker Compose 或 systemd）。

.DESCRIPTION
    服务器布局固定为：
      /srv/newcosmos/server/
        publish/            # 本次发布产物（旧版本保留为 publish.prev）
        publish.prev/       # 上一次发布（回滚用）
        Dockerfile.server
        docker-compose.yml
        .dockerignore
        server.env          # 手工维护，脚本绝不上传/覆盖

    流程：
      1. 本机 dotnet publish -c Release -r linux-x64（框架依赖）；
      2. 上传到 publish.new，校验后原子切换 publish；
      3. Docker：构建带时间戳 tag 的镜像并重启容器；systemd：装 unit 并重启服务；
      4. 轮询 /health，失败自动尝试回滚并打印日志。

    本脚本不会在服务器上编译源码，也不会改 server.env。

.PARAMETER Mode
    docker（默认）或 systemd。

.PARAMETER SshKeyPath
    占位参数：改成实际部署私钥路径。默认值不可用，必须显式覆盖。

.PARAMETER SkipUpload
    只在本机 publish，打印产物路径，不连服务器。

.PARAMETER SkipPublish
    跳过 dotnet publish，配合 -PublishDir 使用已有产物。

.EXAMPLE
    .\deploy-server.ps1 -SshKeyPath C:\keys\newcosmos_deploy_ed25519

.EXAMPLE
    .\deploy-server.ps1 -Mode systemd -InstallCjkFonts -SshKeyPath C:\keys\newcosmos_deploy_ed25519
#>
[CmdletBinding()]
param(
    [ValidateSet("docker", "systemd")]
    [string]$Mode = "docker",

    [string]$AppHost = "10.110.0.2",
    [string]$RemoteUser = "root",
    [string]$RemoteRoot = "/srv/newcosmos/server",
    [string]$SshPort = "22",
    [string]$SshKeyPath = "C:\path\to\newcosmos_deploy_ed25519",

    [string]$RuntimeIdentifier = "linux-x64",
    [string]$PublishDir = "",
    [string]$ImageTag = "",

    [switch]$SkipPublish,
    [switch]$SkipUpload,
    [switch]$InstallCjkFonts
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
    # SSH 横幅写到 stderr，Stop 模式会误判；临时放宽，用退出码判定。
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        & ssh @sshArgs
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldEap
    }
    if ($exitCode -ne 0) {
        throw "远程命令失败（host=$TargetHost, exit=$exitCode）。"
    }
}

function Invoke-RemoteCapture {
    param(
        [string]$TargetHost,
        [string]$Command
    )
    $sshArgs = Get-SshBaseArgs -TargetHost $TargetHost
    $sshArgs += $Command
    $errFile = [IO.Path]::GetTempFileName()
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        $output = & ssh @sshArgs 2> $errFile
        $code = $LASTEXITCODE
        $text = ($output | Out-String).Trim()
        $errText = ''
        if (Test-Path -LiteralPath $errFile) {
            $errText = [IO.File]::ReadAllText($errFile, [Text.Encoding]::Default).Trim()
        }
    }
    finally {
        $ErrorActionPreference = $oldEap
        Remove-Item -LiteralPath $errFile -Force -ErrorAction SilentlyContinue
    }
    if ($code -ne 0) {
        throw "远程命令失败（host=$TargetHost, exit=$code）：$Command`n$errText"
    }
    # 成功时只返回 stdout；SSH 登录横幅在 stderr，不能混进哈希/输出。
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
        "-i", $SshKeyPath,
        "-P", $SshPort,
        "-o", "BatchMode=yes",
        "-o", "IdentitiesOnly=yes",
        "-o", "ConnectTimeout=10"
    )
    if ($Recurse) { $scpArgs += "-r" }
    $scpArgs += @($LocalPath, "$RemoteUser@${TargetHost}:$RemotePath")
    # SSH/scp 的服务器横幅写在 stderr，PowerShell Stop 模式会误判为终止错误；
    # 临时放宽，真正失败由 $LASTEXITCODE 判定。
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
        & scp @scpArgs
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldEap
    }
    if ($exitCode -ne 0) {
        throw "上传失败（host=$TargetHost, path=$RemotePath, exit=$exitCode）。"
    }
}

function Expand-RemoteTemplate {
    param(
        [string]$Template,
        [hashtable]$Tokens
    )
    $result = $Template
    foreach ($key in $Tokens.Keys) {
        $result = $result.Replace("__${key}__", [string]$Tokens[$key])
    }
    return $result
}

# ---------------------------------------------------------------------------
# 0. 路径
# ---------------------------------------------------------------------------
$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
$AppDir = Join-Path $RepoRoot "deploy\app-2"
$ServerProj = Join-Path $RepoRoot "src\NewCosmos.Server\NewCosmos.Server.csproj"
$Dockerfile = Join-Path $AppDir "Dockerfile.server"
$ComposeFile = Join-Path $AppDir "docker-compose.yml"
$DockerIgnore = Join-Path $AppDir ".dockerignore"
$UnitFile = Join-Path $AppDir "newcosmos-server.service"

foreach ($f in @($Dockerfile, $ComposeFile, $DockerIgnore, $UnitFile)) {
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) {
        throw "缺少部署文件：$f"
    }
}

$RemoteRoot = $RemoteRoot.TrimEnd("/")
$Stamp = Get-Date -Format "yyyyMMddHHmmss"
if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    throw "RuntimeIdentifier 不能为空（默认 linux-x64）。"
}
if ([string]::IsNullOrWhiteSpace($ImageTag)) {
    $ImageTag = $Stamp
}
if ($ImageTag -notmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$') {
    throw "非法 Docker tag：$ImageTag（只允许字母、数字、_ . -）"
}

$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nc-server-deploy-" + $Stamp)
New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null
$StagePublish = Join-Path $TempRoot "publish"

# ---------------------------------------------------------------------------
# 1. dotnet publish
# ---------------------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Step "发布 NewCosmos.Server（$RuntimeIdentifier，框架依赖）"
    Assert-Command "dotnet"

    if (-not (Test-Path -LiteralPath $ServerProj -PathType Leaf)) {
        throw "找不到 $ServerProj。NewCosmos.Server 项目尚未创建/路径不一致，无法发布。"
    }

    New-Item -ItemType Directory -Path $StagePublish -Force | Out-Null
    $publishArgs = @(
        "publish", $ServerProj,
        "-c", "Release",
        "-r", $RuntimeIdentifier,
        "--self-contained", "false",
        "-p:UseAppHost=false",
        "-o", $StagePublish
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败（exit=$LASTEXITCODE）。"
    }
    $PublishDir = $StagePublish
}
else {
    if ([string]::IsNullOrWhiteSpace($PublishDir)) {
        throw "-SkipPublish 必须同时指定 -PublishDir（已有 publish 产物目录）。"
    }
    if (-not (Test-Path -LiteralPath $PublishDir -PathType Container)) {
        throw "PublishDir 不存在：$PublishDir"
    }
    $PublishDir = (Resolve-Path -LiteralPath $PublishDir).Path
}

$serverDll = Join-Path $PublishDir "NewCosmos.Server.dll"
if (-not (Test-Path -LiteralPath $serverDll -PathType Leaf)) {
    throw "发布产物缺少 NewCosmos.Server.dll：$PublishDir"
}
if (-not (Test-Path -LiteralPath (Join-Path $PublishDir "NewCosmos.Server.runtimeconfig.json") -PathType Leaf)) {
    Write-Warning "缺少 NewCosmos.Server.runtimeconfig.json，确认这是完整 publish 产物。"
}

$publishFiles = @(Get-ChildItem -LiteralPath $PublishDir -Recurse -File -Force)
$publishSize = ($publishFiles | Measure-Object -Property Length -Sum).Sum
Write-Host ("产物：{0} 个文件，{1:N1} MB" -f $publishFiles.Count, ($publishSize / 1MB))

# 防止把运行时密钥误上传。
$leakedEnv = @($publishFiles | Where-Object { $_.Name -eq "server.env" -or $_.Name -like "*.local.ps1" })
if ($leakedEnv.Count -gt 0) {
    throw "发布产物里出现密钥/本机文件，拒绝上传：$($leakedEnv.FullName -join ', ')"
}

if ($SkipUpload) {
    Write-Step "已跳过上传（-SkipUpload）"
    Write-Host "publish 产物：$PublishDir"
    Write-Host "时间戳/镜像 tag：$ImageTag"
    return
}

# ---------------------------------------------------------------------------
# 2. SSH 前置检查
# ---------------------------------------------------------------------------
Write-Step "检查 SSH 参数"
if ([string]::IsNullOrWhiteSpace($SshKeyPath) -or -not (Test-Path -LiteralPath $SshKeyPath -PathType Leaf)) {
    throw "SSH 私钥不存在：$SshKeyPath。请用 -SshKeyPath 指定实际部署私钥（占位默认值不可用）。"
}
Assert-Command "ssh"
Assert-Command "scp"

# 先确认远端 server.env 已人工创建，避免 publish 已切换但服务起不来。
$checkEnvCmd = "test -s '$RemoteRoot/server.env' || { echo 'ERROR: server.env missing or empty. Copy server.env.example, fill it, chmod 600, then retry.' >&2; exit 20; }"
Invoke-Remote -TargetHost $AppHost -Command $checkEnvCmd

# systemd 模式下 publish 会 chown 到 newcosmos，必须先确保用户/组已存在。
if ($Mode -eq "systemd") {
    Write-Step "检查远端 newcosmos 用户与目录"
    $userPrepCmd = @'
set -e
if ! getent group newcosmos >/dev/null 2>&1; then
  groupadd --system newcosmos
fi
if ! id -u newcosmos >/dev/null 2>&1; then
  useradd --system --gid newcosmos --home-dir /srv/newcosmos \
          --shell /usr/sbin/nologin newcosmos
fi
install -d -o newcosmos -g newcosmos -m 0750 /srv/newcosmos/backups /srv/newcosmos/logs
'@
    Invoke-Remote -TargetHost $AppHost -Command $userPrepCmd
}

# ---------------------------------------------------------------------------
# 3. 上传 publish 并原子切换
# ---------------------------------------------------------------------------
Write-Step "上传发布产物到 $AppHost`:$RemoteRoot"

$owner = if ($Mode -eq "docker") { "10001:10001" } else { "newcosmos:newcosmos" }

$prepareCmd = Expand-RemoteTemplate -Template @'
set -e
install -d -m 0755 '__ROOT__'
rm -rf '__ROOT__/publish.new'
'@ -Tokens @{ ROOT = $RemoteRoot }

Invoke-Remote -TargetHost $AppHost -Command $prepareCmd

Copy-ToRemote -LocalPath $PublishDir -TargetHost $AppHost `
    -RemotePath "$RemoteRoot/publish.new" -Recurse

$swapCmd = Expand-RemoteTemplate -Template @'
set -e
cd '__ROOT__'
if [ -d publish ]; then
  rm -rf publish.prev
  mv publish publish.prev
fi
mv publish.new publish
chown -R '__OWNER__' publish
chmod -R u=rwX,go=rX publish
'@ -Tokens @{ ROOT = $RemoteRoot; OWNER = $owner }

Invoke-Remote -TargetHost $AppHost -Command $swapCmd

# ---------------------------------------------------------------------------
# 4. Docker 模式
# ---------------------------------------------------------------------------
if ($Mode -eq "docker") {
    Write-Step "上传 Compose/Dockerfile 配置"

    $configBackupCmd = Expand-RemoteTemplate -Template @'
set -e
umask 077
install -d -m 0700 '/srv/newcosmos/backups/server-config-__STAMP__'
for f in docker-compose.yml Dockerfile.server .dockerignore; do
  if [ -f '__ROOT__/'$f ]; then
    cp -a '__ROOT__/'$f '/srv/newcosmos/backups/server-config-__STAMP__/'$f
  fi
done
'@ -Tokens @{ ROOT = $RemoteRoot; STAMP = $Stamp }

    Invoke-Remote -TargetHost $AppHost -Command $configBackupCmd

    Copy-ToRemote -LocalPath $ComposeFile -TargetHost $AppHost -RemotePath "$RemoteRoot/docker-compose.yml.new"
    Copy-ToRemote -LocalPath $Dockerfile -TargetHost $AppHost -RemotePath "$RemoteRoot/Dockerfile.server.new"
    Copy-ToRemote -LocalPath $DockerIgnore -TargetHost $AppHost -RemotePath "$RemoteRoot/.dockerignore.new"

    $installConfigCmd = Expand-RemoteTemplate -Template @'
set -e
cd '__ROOT__'
if [ ! -s server.env ]; then
  echo "ERROR: __ROOT__/server.env 不存在或为空。请先 cp server.env.example server.env 并填值。" >&2
  exit 20
fi
chmod 600 server.env
mv -f docker-compose.yml.new docker-compose.yml
mv -f Dockerfile.server.new Dockerfile.server
mv -f .dockerignore.new .dockerignore
command -v docker >/dev/null 2>&1 || { echo "ERROR: docker 未安装" >&2; exit 22; }
docker compose version >/dev/null 2>&1 || { echo "ERROR: 需要 docker compose v2" >&2; exit 23; }
command -v curl >/dev/null 2>&1 || { echo "ERROR: 需要 curl 做健康检查" >&2; exit 24; }
'@ -Tokens @{ ROOT = $RemoteRoot }

    Invoke-Remote -TargetHost $AppHost -Command $installConfigCmd

    # 记录当前镜像 tag，供健康检查失败时回滚。
    $prevImage = Invoke-RemoteCapture -TargetHost $AppHost `
        -Command "docker inspect --format '{{.Config.Image}}' newcosmos-server 2>/dev/null || true"
    $prevTag = ""
    if ($prevImage -match '^newcosmos-server:(.+)$') {
        $prevTag = $Matches[1]
    }

    Write-Step "构建并重启容器（image tag: $ImageTag）"

    $fonts = if ($InstallCjkFonts) { "true" } else { "false" }

    $dockerDeployCmd = Expand-RemoteTemplate -Template @'
set -e
cd '__ROOT__'
export INSTALL_CJK_FONTS='__FONTS__'
export NC_IMAGE_TAG='__TAG__'
docker compose build --pull server
docker compose up -d --no-deps server

for i in $(seq 1 30); do
  if curl -fsS --max-time 3 http://127.0.0.1:8787/health >/dev/null 2>&1; then
    echo "health: ok"
    docker compose ps
    exit 0
  fi
  sleep 2
done

echo "health: FAILED" >&2
docker compose logs --tail=200 server >&2 || true

if [ -n '__PREV_TAG__' ]; then
  echo "rollback: try previous image __PREV_TAG__" >&2
  if [ -d publish.prev ]; then
    rm -rf publish.failed
    mv publish publish.failed
    mv publish.prev publish
    chown -R '__OWNER__' publish
  fi
  export NC_IMAGE_TAG='__PREV_TAG__'
  docker compose up -d --no-deps server || true
fi
exit 30
'@ -Tokens @{
        ROOT     = $RemoteRoot
        FONTS    = $fonts
        TAG      = $ImageTag
        PREV_TAG = $prevTag
        OWNER    = $owner
    }

    Invoke-Remote -TargetHost $AppHost -Command $dockerDeployCmd

    Write-Step "Docker 发布成功"
    Write-Host "镜像：newcosmos-server:$ImageTag"
    if ($prevTag -ne "") {
        Write-Host "回滚命令：cd $RemoteRoot && NC_IMAGE_TAG=$prevTag docker compose up -d --no-deps server" -ForegroundColor Yellow
    }
    Write-Host "旧产物：$RemoteRoot/publish.prev"
    return
}

# ---------------------------------------------------------------------------
# 5. systemd 模式
# ---------------------------------------------------------------------------
Write-Step "安装 systemd 单元并重启服务"

$remoteUnitTmp = "/tmp/newcosmos-server.service.$Stamp"
Copy-ToRemote -LocalPath $UnitFile -TargetHost $AppHost -RemotePath $remoteUnitTmp

$systemdDeployCmd = Expand-RemoteTemplate -Template @'
set -e
cd '__ROOT__'
if [ ! -s server.env ]; then
  echo "ERROR: __ROOT__/server.env 不存在或为空。请先 cp server.env.example server.env 并填值。" >&2
  exit 20
fi
chmod 600 server.env

if ! id -u newcosmos >/dev/null 2>&1; then
  if ! getent group newcosmos >/dev/null 2>&1; then
    groupadd --system newcosmos
  fi
  useradd --system --gid newcosmos --home-dir /srv/newcosmos --shell /usr/sbin/nologin newcosmos
fi
install -d -o newcosmos -g newcosmos -m 0750 /srv/newcosmos/backups /srv/newcosmos/logs

chown -R newcosmos:newcosmos publish
chmod -R u=rwX,go=rX publish

if [ -f /etc/systemd/system/newcosmos-server.service ]; then
  cp -a /etc/systemd/system/newcosmos-server.service /etc/systemd/system/newcosmos-server.service.bak.__STAMP__
fi
install -m 0644 '__UNIT_TMP__' /etc/systemd/system/newcosmos-server.service
rm -f '__UNIT_TMP__'
systemctl daemon-reload
systemctl enable newcosmos-server >/dev/null
systemctl restart newcosmos-server

command -v curl >/dev/null 2>&1 || { echo "ERROR: 需要 curl 做健康检查" >&2; exit 24; }

for i in $(seq 1 30); do
  if curl -fsS --max-time 3 http://127.0.0.1:8787/health >/dev/null 2>&1; then
    echo "health: ok"
    systemctl --no-pager --full status newcosmos-server | head -n 20
    exit 0
  fi
  sleep 2
done

echo "health: FAILED" >&2
journalctl -u newcosmos-server -n 120 --no-pager >&2 || true

if [ -d publish.prev ]; then
  echo "rollback: restore publish.prev" >&2
  rm -rf publish.failed
  mv publish publish.failed
  mv publish.prev publish
  chown -R newcosmos:newcosmos publish
  systemctl restart newcosmos-server || true
fi
exit 31
'@ -Tokens @{ ROOT = $RemoteRoot; UNIT_TMP = $remoteUnitTmp; STAMP = $Stamp }

Invoke-Remote -TargetHost $AppHost -Command $systemdDeployCmd

Write-Step "systemd 发布成功"
Write-Host "服务：newcosmos-server"
Write-Host "发布目录：$RemoteRoot/publish"
Write-Host "旧产物：$RemoteRoot/publish.prev"
Write-Host "回滚：先 mv publish publish.failed && mv publish.prev publish，再 systemctl restart newcosmos-server" -ForegroundColor Yellow
