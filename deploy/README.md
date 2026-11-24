# NewCosmos Web 部署说明（.1 边缘 + .2 应用）

> 适用范围：`deploy/edge-1`、`deploy/app-2`、`deploy/scripts`、`deploy/database`。
> `deploy/lottery` 不在本说明内，后续单独维护。
> 所有密码/Token 都是占位符，真实值只放服务器环境文件，禁止提交 Git。

---

## 1. 文件清单

```text
deploy/
├─ .gitignore                              # 忽略 server.env/backup.env/publish 等
├─ edge-1/
│  └─ nginx-cosmos.sua17.cn.conf          # .1：index + /api/auth 反代，其它 404
├─ app-2/
│  ├─ nginx-cosmos-static.sua17.cn.conf   # .2：assets/downloads/api
│  ├─ brotli.conf.example                 # 可选 ngx_brotli 片段
│  ├─ Dockerfile.server                   # 运行镜像（只接收 publish/）
│  ├─ docker-compose.yml                  # server 容器
│  ├─ .dockerignore                       # 防止 server.env 进构建上下文
│  ├─ server.env.example                  # 运行时环境变量模板
│  └─ newcosmos-server.service            # 不用 Docker 时的 systemd 单元
├─ scripts/
│  ├─ deploy-web-split.ps1                # index -> .1，assets -> .2
│  └─ deploy-server.ps1                   # publish -> .2，Docker/systemd
├─ database/
│  ├─ backup.sh                           # pg_dump -Fc + 校验 + 保留天数
│  └─ backup.env.example                  # 备份账号/目录/保留天数模板
└─ README.md
```

---

## 2. 真实拓扑与端口

```text
浏览器
  ├─ https://cosmos.sua17.cn/              -> 10.110.0.1:443（2C/1.6G/3Mbps）
  │    └─ /api/auth/*                     -> 10.110.0.2:8787（内网 HTTP）
  └─ https://cosmos-static.sua17.cn:20000 -> 公网:20000 DNAT -> 10.110.0.2:443
       ├─ /assets/*                       -> 磁盘静态
       ├─ /downloads/*                    -> 磁盘静态 + 限速
       └─ /api/*                          -> 127.0.0.1:8787（ASP.NET Core）
```

关键点：

- **`.1` 只有 3Mbps**：只发 `index.html` 和认证小 JSON。`/assets`、`/downloads`、照片、导入导出、备份一律不得经过 `.1`。
- **`.2` 才承载大流量**：100Mbps 出口，assets 用 hash + `immutable`，下载限速。
- **API 必须同时监听两处**：
  - `127.0.0.1:8787` 给 `.2` 本机 nginx；
  - `10.110.0.2:8787` 给 `.1` 边缘认证反代。
  Docker 用两个 `ports` 映射，systemd 用两个 Kestrel URL。**防火墙必须只允许 `10.110.0.1` 访问 `10.110.0.2:8787`**，公网不得直连 8787。
  注意：Docker 发布的端口会绕过 `ufw`，必须在 `DOCKER-USER` 链按源地址限制（示例见 §10 第 4 条）。
- **认证同源**：登录/刷新/登出走 `https://cosmos.sua17.cn/api/auth/*`，refresh cookie 只存在 `.1`。
- **业务跨源**：业务 API 直连 `.2`，前端带 `Authorization: Bearer <sessionId>` 和 `X-NC-Device`，不依赖跨站 Cookie。

---

## 3. 前置条件

### 3.1 DNS 与防火墙

- `cosmos.sua17.cn` A 记录指向公网入口（`.1`）。
- `cosmos-static.sua17.cn` A 记录指向同一公网入口（或 `.2` 的公网地址）。
- 公网 `TCP/20000` DNAT 到 `10.110.0.2:443`，**不要**转发到 `.2:20000`，也**不要**把 `.1` 当静态出口。
- `.1` 到 `.2:8787` 内网放行；`:8787` 对公网与其它内网主机拒绝。
- `.2` 的 PostgreSQL `5432` 只对 `127.0.0.1`、Docker 网桥或 `.2` 内网地址开放，禁止公网。

### 3.2 证书

- `.1`：证书必须覆盖 `cosmos.sua17.cn`。
- `.2`：证书 **SAN 必须包含 `cosmos-static.sua17.cn`**。浏览器 TLS 握手发生在 `.2:443`（经 20000 DNAT），SAN 不匹配会直接证书错误。
- 可以一张多 SAN 证书同时包含两个域名，两台机器各放一份；也可以各用各的证书。
- 默认路径（可改配置）：
  - `.1`：`/etc/nginx/ssl/cosmos.sua17.cn/{fullchain.pem,privkey.pem}`
  - `.2`：`/etc/nginx/ssl/cosmos-static.sua17.cn/{fullchain.pem,privkey.pem}`
- 续期后执行 `nginx -t && systemctl reload nginx`；建议在 certbot deploy hook 里做。
- `.2` 不监听 80，无法用 HTTP-01 在本机续期；请用 DNS-01，或在 `.1` 申请后把证书复制到 `.2` 并 reload。`.1` 的 80 只用于 ACME 与跳转。
- HSTS 默认 `max-age=31536000`，**没有** `includeSubDomains`。确认所有子域都强制 HTTPS 后再加，否则会把自己锁死。
- 当前未启用 CSP（Element Plus 有内联样式、业务 API 跨源）。需要时用 edge 配置里的注释模板，并完整回归登录、打印预览、PDF 预览。

### 3.3 软件

`.1`：

- nginx（`limit_req`/`limit_conn`/`gzip` 模块；发行版默认即可）。
- `www-data` 用户（Debian/Ubuntu 默认）。

`.2`：

- nginx（同上；Brotli 可选）。
- Docker Engine + `docker compose` v2，**或** .NET 10 ASP.NET Core Runtime + systemd。
- PostgreSQL 客户端 `pg_dump`/`pg_restore`（备份脚本）。
- `curl`（部署脚本健康检查）。

---

## 4. 目录与权限

### 4.1 `.1`

```bash
sudo install -d -m 0755 -o www-data -g www-data /srv/newcosmos/web
sudo install -d -m 0755 /etc/nginx/ssl/cosmos.sua17.cn
sudo install -d -m 0755 /var/www/certbot
```

`/srv/newcosmos/web` 里只应有 `index.html`（以及回滚用的 `index.html.bak.*`）。**不要**放 assets。

### 4.2 `.2`

```bash
# 静态
sudo install -d -m 0755 -o www-data -g www-data \
  /srv/newcosmos/web /srv/newcosmos/web/assets /srv/newcosmos/web/downloads

# API（Docker 固定 UID/GID 10001；systemd 用 newcosmos 用户）
sudo install -d -m 0750 -o 10001 -g 10001 /srv/newcosmos/backups /srv/newcosmos/logs
sudo install -d -m 0700 /srv/newcosmos/backups/postgres
sudo install -d -m 0755 /srv/newcosmos/server
```

如果使用 systemd：

```bash
sudo groupadd --system newcosmos || true
sudo useradd --system --gid newcosmos --home-dir /srv/newcosmos \
     --shell /usr/sbin/nologin newcosmos || true
sudo chown -R newcosmos:newcosmos /srv/newcosmos/backups /srv/newcosmos/logs
```

> Docker 卷会覆盖镜像内目录权限。宿主机 `/srv/newcosmos/backups` 必须能被容器内 UID 10001 写入：
> `sudo chown -R 10001:10001 /srv/newcosmos/backups`
> 备份脚本以 root 运行，不受影响。

---

## 5. 部署顺序（首次）

### 第 1 步：`.1` nginx

```bash
sudo install -m 0644 deploy/edge-1/nginx-cosmos.sua17.cn.conf \
     /etc/nginx/conf.d/cosmos.sua17.cn.conf
sudo install -m 0644 /path/to/index.html /srv/newcosmos/web/index.html
sudo chown www-data:www-data /srv/newcosmos/web/index.html
sudo nginx -t
sudo systemctl reload nginx
```

确认：

- `https://cosmos.sua17.cn/` 返回 index，响应头含 `Cache-Control: no-store`、HSTS。
- `https://cosmos.sua17.cn/assets/anything.js` 返回 404。
- `https://cosmos.sua17.cn/api/printers` 返回 404 JSON。
- `/api/auth/*` 能到 `.2`（先完成第 4/5 步再验）。

### 第 2 步：`.2` nginx

```bash
sudo install -m 0644 deploy/app-2/nginx-cosmos-static.sua17.cn.conf \
     /etc/nginx/conf.d/cosmos-static.sua17.cn.conf
sudo nginx -t
sudo systemctl reload nginx
```

注意：

- 该文件顶部有 `limit_req_zone`、`limit_conn_zone`、`map`、`upstream`，**必须被 `http {}` 包含**（`conf.d` 默认如此），不能整体塞进某个 `server {}`。
- 如果发行版 nginx ≥ 1.25.1，`listen 443 ssl http2;` 会有弃用告警，可改成 `listen 443 ssl;` + `http2 on;`，功能不变。
- `listen 20000` 默认注释；只有没做 20000→443 DNAT 时才需要打开。
- Brotli 默认关闭。确认 `nginx -V` 含 brotli 后，按 `brotli.conf.example` 头部说明启用；没有模块时 include 会导致 nginx 启动失败。

### 第 3 步：PostgreSQL

```bash
sudo -u postgres createuser --login --pwprompt newcosmos_app
sudo -u postgres createdb -O newcosmos_app newcosmos

# 备份只读账号（推荐）
sudo -u postgres psql -d newcosmos <<'SQL'
CREATE ROLE newcosmos_backup LOGIN PASSWORD '__REPLACE_ME__';
GRANT CONNECT ON DATABASE newcosmos TO newcosmos_backup;
GRANT USAGE ON SCHEMA public TO newcosmos_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO newcosmos_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO newcosmos_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO newcosmos_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON SEQUENCES TO newcosmos_backup;
SQL
```

Docker 模式需要让容器访问宿主机 PG：

- compose 已加 `host.docker.internal:host-gateway`；
- `postgresql.conf` 的 `listen_addresses` 需包含 Docker 网桥地址（或 `10.110.0.2`）；
- `pg_hba.conf` 允许 `172.17.0.0/16`（或 `10.110.0.0/24`）用 `scram-sha-256`；
- 也可以用 `NC_DB_HOST=10.110.0.2`，此时只需放行 `.2` 自身/内网。

性能基线（按 ARCHITECTURE）：

```conf
shared_buffers = 1GB
work_mem = 8MB
max_connections = 100          # 应用连接池 20-30
```

首次部署把 `NC_AUTO_MIGRATE=true`，迁移成功后改回 `false`。

### 第 4 步：`.2` 环境文件

```bash
cd /srv/newcosmos/server
sudo cp /path/to/deploy/app-2/server.env.example server.env
sudo chmod 600 server.env
sudo vi server.env
```

必填：

- `NC_DB_*`：数据库地址、库名、账号、密码；
- `NC_CORS_ORIGIN=https://cosmos.sua17.cn`（只允许这一个）；
- `NC_AGENT_REGISTER_TOKEN`：`openssl rand -hex 32` 生成；
- `NC_AUTO_MIGRATE`：首次 `true`，之后 `false`；
- `TZ=Asia/Shanghai`。

`server.env`：

- **绝不上传/覆盖**，部署脚本只检查它存在；
- 不写 `ASPNETCORE_URLS`，监听地址由 Dockerfile/unit 控制；
- systemd 模式把 `NC_DB_HOST` 从 `host.docker.internal` 改成 `127.0.0.1`。

### 第 5 步：部署 API

Docker（推荐）：

```powershell
.\deploy\scripts\deploy-server.ps1 -SshKeyPath C:\keys\newcosmos_deploy_ed25519
```

systemd：

```powershell
.\deploy\scripts\deploy-server.ps1 -Mode systemd `
  -InstallCjkFonts -SshKeyPath C:\keys\newcosmos_deploy_ed25519
```

脚本行为：

1. 本机 `dotnet publish -c Release -r linux-x64 --self-contained false`；
2. 上传到 `/srv/newcosmos/server/publish.new`，校验 DLL 后原子切换 `publish`；
3. Docker：备份 Compose/Dockerfile，构建 `newcosmos-server:<时间戳>`，重启容器；
   systemd：安装 unit、创建 `newcosmos` 用户、`systemctl restart`；
4. 轮询 `http://127.0.0.1:8787/health`，失败时打印日志并尝试回滚到上一个镜像/`publish.prev`。

辅助参数：

- `-SkipUpload`：只在本机 publish，打印产物路径；
- `-SkipPublish -PublishDir <目录>`：用已有 publish 产物；
- `-ImageTag`：指定镜像 tag；
- `-InstallCjkFonts`：构建镜像时装 `fonts-noto-cjk`（体积大，仅在服务端需要中文文书/图片时开）。

> 当前仓库若还没有 `src/NewCosmos.Server/NewCosmos.Server.csproj`，脚本会明确报错。不要用空目录糊弄构建。

### 第 6 步：部署 Web（拆分）

```powershell
.\deploy\scripts\deploy-web-split.ps1 -Build `
  -SshKeyPath C:\keys\newcosmos_deploy_ed25519
```

或只发布已有 `web\dist`（不重新构建）：

```powershell
.\deploy\scripts\deploy-web-split.ps1 `
  -SshKeyPath C:\keys\newcosmos_deploy_ed25519
```

行为：

- `index.html` → `.1:/srv/newcosmos/web/`；
- `assets/**` → `.2:/srv/newcosmos/web/assets/`；
- **只新增/覆盖，不删除**旧 hash 文件：回滚旧 index 时旧资源仍在；
- 上传前备份 `.1` 的旧 index 为 `index.html.bak.<时间戳>`；
- 先传临时文件再 `mv`，避免半截 index；
- `chown www-data`、`nginx -t && systemctl reload nginx`；
- 对 index 做 SHA256 校验。

注意：

- Vite 的 `base` 必须是 `/`，资源输出在 `dist/assets/`。
- `dist` 根目录除 `index.html`/`assets` 外的文件（`favicon.ico`、`manifest.webmanifest`、`robots.txt` 等）**不会上传**，脚本会告警。请把它们放进会被构建进 `/assets` 的位置，或内联到 index；否则 `.1` 会按 SPA 回退返回 HTML、`.2` 会 404。
- `-SkipUpload` 可只在本机整理产物并打印 staging 路径。

### 第 7 步：数据库备份

```bash
sudo install -m 0755 deploy/database/backup.sh /usr/local/sbin/newcosmos-pg-backup
sudo install -d -m 0755 /etc/newcosmos
sudo install -m 0600 deploy/database/backup.env.example /etc/newcosmos/backup.env
sudo vi /etc/newcosmos/backup.env       # 填 newcosmos_backup 密码
sudo /usr/local/sbin/newcosmos-pg-backup
```

cron（`/etc/cron.d/newcosmos-pg-backup`，文件末尾留空行）：

```cron
15 2 * * * root /usr/local/sbin/newcosmos-pg-backup >> /var/log/newcosmos-pg-backup.log 2>&1
```

脚本会：

- `pg_dump -Fc` 到 `/srv/newcosmos/backups/postgres`；
- 先写 `.partial`，成功后原子改名；
- `pg_restore --list` 校验归档；
- 生成 `.sha256`；
- 删除超过 `NC_BACKUP_RETENTION_DAYS`（默认 14 天）的旧备份；
- 拒绝把备份写入 `/srv/newcosmos/web` 等公开目录。

**备份必须再异地复制一份**（`rsync` 到另一台机器/对象存储），本机磁盘坏了本地备份也一起没。
恢复演练：先 `createdb newcosmos_restore`，`pg_restore` 到临时库验证，再决定是否切生产；不要直接 `--clean` 覆盖生产库。

---

## 6. 上线验证清单

```bash
# 1) .1 入口：no-store + HSTS
curl -sSI https://cosmos.sua17.cn/ | grep -Ei 'HTTP/|cache-control|strict-transport|x-content-type'

# 2) .1 不提供静态资源/业务 API
curl -sSI https://cosmos.sua17.cn/assets/app.js          # 期望 404
curl -sS  https://cosmos.sua17.cn/api/printers          # 期望 404 JSON

# 3) 认证反代（不要用真实密码刷屏；登录接口可用占位请求看 400/401）
curl -sS -o /dev/null -w '%{http_code}\n' \
  -X POST https://cosmos.sua17.cn/api/auth/login \
  -H 'Content-Type: application/json' -d '{}'

# 4) .2 静态：immutable + CORS *
curl -sSI https://cosmos-static.sua17.cn:20000/assets/<实际hash文件名>.js \
  | grep -Ei 'HTTP/|cache-control|access-control-allow-origin'

# 5) .2 敏感路径
curl -sSI https://cosmos-static.sua17.cn:20000/.env       # 404
curl -sSI https://cosmos-static.sua17.cn:20000/backups/   # 404
curl -sSI https://cosmos-static.sua17.cn:20000/           # 404

# 6) CORS 预检：允许源
curl -sSI -X OPTIONS https://cosmos-static.sua17.cn:20000/api/printers \
  -H 'Origin: https://cosmos.sua17.cn' \
  -H 'Access-Control-Request-Method: GET' \
  -H 'Access-Control-Request-Headers: authorization,x-nc-device'
# 期望 204 + Access-Control-Allow-Origin: https://cosmos.sua17.cn
# 且绝对没有 Access-Control-Allow-Credentials

# 7) CORS 预检：非白名单源
curl -sSI -X OPTIONS https://cosmos-static.sua17.cn:20000/api/printers \
  -H 'Origin: https://evil.example' \
  -H 'Access-Control-Request-Method: GET'
# 期望没有 Access-Control-Allow-Origin

# 8) API 健康（在 .2 本机）
curl -fsS http://127.0.0.1:8787/health
curl -fsS http://10.110.0.2:8787/health
```

再检查：

- 浏览器 Network：index 无缓存，assets 来自 `cosmos-static...:20000`，字体无 CORS 报错；
- 登录 → 心跳 → 登出正常，被顶号/过期返回契约错误码；
- `.1` 的 `access_log` 里没有 assets/大文件；
- `.2` 的 `nginx -T` 无重复 `add_header` 导致的丢失（本配置在每个带 `add_header` 的 location 重复了安全头）。

---

## 7. 回滚

### 7.1 Web index

```bash
ssh -i <key> root@10.110.0.1
cd /srv/newcosmos/web
cp -a index.html.bak.<时间戳> index.html
chown www-data:www-data index.html
nginx -t && systemctl reload nginx
```

assets 只新增不删除，旧 index 引用的旧 hash 文件仍在，回滚不需要动 `.2`。

### 7.2 API（Docker）

```bash
cd /srv/newcosmos/server
docker images 'newcosmos-server'          # 找上一个 tag
NC_IMAGE_TAG=<上一个tag> docker compose up -d --no-deps server
curl -fsS http://127.0.0.1:8787/health
```

`publish.prev` 保留上一次产物；失败发布可能留下 `publish.failed` 供排查。部署脚本健康检查失败会自动尝试回滚。

### 7.3 API（systemd）

```bash
cd /srv/newcosmos/server
sudo mv publish publish.failed
sudo mv publish.prev publish
sudo chown -R newcosmos:newcosmos publish
sudo systemctl restart newcosmos-server
curl -fsS http://127.0.0.1:8787/health
```

unit 文件旧版本会备份到 `/etc/systemd/system/newcosmos-server.service.bak.<时间戳>`，必要时先恢复 unit 再 `daemon-reload`。

### 7.4 nginx

修改前保留 `/etc/nginx/conf.d/*.conf.bak.<时间戳>`；回滚后 `nginx -t && systemctl reload nginx`。

### 7.5 数据库

- 恢复到临时库验证后再切；
- 恢复前先对当前生产库再做一次备份；
- 不要把 `pg_restore --clean` 直接打到生产库。

---

## 8. CORS / 安全要点

- `.2` CORS 只回显 `https://cosmos.sua17.cn`；其它 Origin 不下发 `Access-Control-Allow-Origin`。
- 允许请求头：`Authorization`、`Content-Type`、`X-NC-Device`、`X-NC-Device-Key`（另带 `X-NC-Agent-Register` 给 Agent 注册）。
- **不发送** `Access-Control-Allow-Credentials`；上游若设置也被 `proxy_hide_header` 丢掉。
- 业务 API 用 Bearer，不依赖跨站 Cookie；refresh cookie 只在 `.1` 同源。
- `.1` 对 `/api/auth/*` 强制 `no-store`，并隐藏上游 `Cache-Control`/`Server`。
- `.1` 其它 `/api/*`、`/assets/*`、`/downloads/*`、`/backups/*` 全部 404。
- `.2` `autoindex off`；隐藏文件、`.env/.sql/.bak/.log/.dump/.map` 全部 404。
- 下载：10 req/s/IP + 20 突发，每 IP 最多 4 条并发，单连接 8MiB/s（前 1MiB 不限速）。
- 上传：`.2` `client_max_body_size 128m`；`.1` 只有 2m。
- TLS 1.2/1.3，HSTS 一年，`X-Content-Type-Options`、`X-Frame-Options`、`Referrer-Policy`、`Permissions-Policy` 全站。
- 备份在 `/srv/newcosmos/backups`，不在 nginx root 下，且 `/backups/` 显式 404。
- 所有服务文件 `chmod 600`，部署私钥只给发布机；建议后续改成非 root + sudo 白名单。

---

## 9. 性能注意

- `.1` 的 3Mbps 是硬瓶颈：只允许 index + `/api/auth/*`。index gzip 目标 < 50KB；任何资源经 `.1` 都会拖垮页面。
- `.2`：
  - assets 文件名 hash，`Cache-Control: public, max-age=31536000, immutable`；
  - `Access-Control-Allow-Origin: *` 让字体/JS/CSS 跨域加载；
  - HTTP/2、`sendfile`、`tcp_nopush`、`open_file_cache`；
  - gzip 默认开启；Brotli 需模块，启用后优先；
  - 下载限速/限连接，避免单个用户打满 100Mbps；
  - API 反代 keepalive 32，超时 300s（导入/导出/报表），静态不走 API。
- ASP.NET Core：
  - 工作站 GC（`DOTNET_gcServer=0`），堆上限 3GB（`DOTNET_GCHeapHardLimit=0xC0000000`）；
  - 容器 128m 上传由 nginx 先缓冲，后端按分页/流式处理；
  - 图片只出缩略图，PDF 预览按需加载。
- PostgreSQL：`shared_buffers=1GB`、连接池 20-30、`work_mem=8MB`；备份在凌晨 `nice`/`ionice` 低优先级。
- OCR（8788）与 Lottery Worker 不在本配置内；注意 7.7G 内存预算，Lottery Worker 建议 `--memory=2g --cpus=1.5` 并错峰训练。

---

## 10. 需要人工确认的点

1. **`NewCosmos.Server` 项目路径/程序集名**：脚本与 Dockerfile 假定 `src/NewCosmos.Server/NewCosmos.Server.csproj`、入口 `NewCosmos.Server.dll`、健康检查 `/health`。若实际不同，三处一起改。
2. **环境变量名**：`NC_DB_HOST/PORT/NAME/USER/PASSWORD`、`NC_CORS_ORIGIN`、`NC_AGENT_REGISTER_TOKEN`、`NC_AUTO_MIGRATE` 是否就是服务端读取的键；若用 `ConnectionStrings__*` 需改 env 模板。
3. **Linux 可发布性**：`NewCosmos.Server` 不能引用 `net10.0-windows` 的 Office/COM 项目；服务端不得跑 Office。若引用了，Linux publish 会失败。若服务端用 `System.Drawing.Common` 或本地字体渲染，还需要 `libgdiplus`/`fontconfig`（当前 Dockerfile 仅把 `fonts-noto-cjk` 做成可选）。
4. **`.1` 到 `.2:8787` 的绑定**：compose 同时映射 `127.0.0.1` 与 `10.110.0.2`；systemd unit 绑定两个 Kestrel URL。若 `.2` 内网 IP 不是 `10.110.0.2`，三处（edge nginx、compose、unit）一起改，并限制防火墙来源。
   如果 Docker 不允许同一容器端口做两个主机 IP 映射，退化为 `0.0.0.0:8787:8787`，但必须用 `DOCKER-USER` 链只放行 `10.110.0.1` 与本机：
   ```bash
   # 示例：eth0 为内网网卡；按实际网卡名调整
   sudo iptables -I DOCKER-USER -i eth0 -p tcp --dport 8787 ! -s 10.110.0.1 -j DROP
   ```
   `ufw allow 8787` 对 Docker 发布端口通常无效，必须用 `DOCKER-USER`。
5. **证书 SAN**：`cosmos-static.sua17.cn` 必须进证书；20000→443 DNAT 必须生效。用 `openssl s_client -connect cosmos-static.sua17.cn:20000 -servername cosmos-static.sua17.cn` 检查实际下发的证书。
6. **Docker vs systemd**：二选一。Docker 需要 compose v2；systemd 需要 .NET 10 runtime 和 `newcosmos` 用户。不要把两种同时启用抢 8787。
7. **PostgreSQL 可达性**：Docker 的 `host.docker.internal` 需要 `host-gateway` 与 `listen_addresses`/`pg_hba` 配合；systemd 用 `127.0.0.1`。备份账号只读权限是否足够。
8. **目录权限**：Docker UID/GID 固定 10001，宿主机备份/日志目录要对齐；systemd 用 `newcosmos:newcosmos`。
9. **Web 构建布局**：`web/dist/index.html` + `web/dist/assets/**`；Vite `base: '/'`。`dist` 根部的 favicon/manifest 不会上传，需改放到 assets 或内联。
10. **nginx 版本/模块**：`listen ... http2` 在 ≥1.25.1 有弃用告警；Brotli 默认无模块，未安装不要 include。
11. **SSH**：脚本默认 `root` + 指定私钥，`BatchMode=yes`。先把主机公钥加入发布机的 known_hosts；私钥若有口令，需先加载 ssh-agent（BatchMode 不会弹密码）。建议后续改成专用部署账号 + sudo 白名单。
12. **`server.env` 人工维护**：脚本不会上传、不会覆盖；部署前必须存在且 `chmod 600`。
13. **备份策略**：保留 14 天只是本机；异地副本、恢复演练、监控（备份失败告警）需另行落地。
14. **`deploy/lottery`**：本次未触碰，后续单独负责。
15. **ASP.NET DataProtection（如使用）**：若 refresh cookie 由 DataProtection 加密，需要把密钥环持久化（Docker 挂载 `/app/.aspnet/DataProtection-Keys`，systemd 用 `StateDirectory=`），否则每次重启/重建容器后旧 cookie 失效。会话权威仍在 `nc_sys_user_sessions`。

---

## 11. 常用运维命令

```bash
# nginx
sudo nginx -t
sudo nginx -T | less
sudo systemctl reload nginx
sudo tail -f /var/log/nginx/cosmos.sua17.cn.error.log
sudo tail -f /var/log/nginx/cosmos-static.sua17.cn.error.log

# Docker
cd /srv/newcosmos/server
docker compose ps
docker compose logs -f --tail=200 server
docker inspect --format '{{.State.Health.Status}}' newcosmos-server

# systemd
systemctl status newcosmos-server
journalctl -u newcosmos-server -f

# 磁盘/备份
df -h /srv
ls -lh /srv/newcosmos/backups/postgres | tail
```
