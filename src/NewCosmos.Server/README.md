# NewCosmos.Server

NewCosmos Web 的 ASP.NET Core 业务 API。P0 包含：认证、会话、心跳、登出、打印机按账户绑定、Agent 注册/心跳、健康检查。
P1 起增加社会救助申请只读查询（列表/详情，见下文「P1 申请查询」）。

- 目标框架：`net10.0`
- 领域服务：引用 `src/NewCosmos.Core/NewCosmos.Core.csproj`
- 默认监听：`http://0.0.0.0:8787`（可用 `ASPNETCORE_URLS` 覆盖）
- 默认 CORS：`https://cosmos.sua17.cn`

## 启动

```powershell
dotnet run --project src/NewCosmos.Server/NewCosmos.Server.csproj
```

生产发布：

```powershell
dotnet publish src/NewCosmos.Server/NewCosmos.Server.csproj -c Release -o publish/server
```

数据库密码与 Agent 注册密钥只从环境变量读取，`appsettings.json` 不保存任何密码。

```bash
export NC_DB_HOST=127.0.0.1
export NC_DB_PORT=5432
export NC_DB_NAME=newcosmos
export NC_DB_USER=newcosmos
export NC_DB_PASS='<数据库密码>'
# 兼容别名：NC_DB_PASSWORD
# OCR 识别与 AI 重病判定共用同一服务地址（留空=未启用，两者均降级为人工）
export NC_OCR_SERVICE_URL='http://127.0.0.1:11434'
export NC_AGENT_REGISTER_TOKEN='<一次性注册密钥>'
export NC_AUTO_MIGRATE=1
export NC_MAX_BODY_MB=128
export ASPNETCORE_URLS=http://0.0.0.0:8787
```

## 环境变量

| 变量 | 必填 | 默认 | 说明 |
|---|---|---|---|
| `NC_DB_HOST` | 是 | - | PostgreSQL 主机 |
| `NC_DB_PORT` | 否 | `5432` | PostgreSQL 端口 |
| `NC_DB_NAME` | 是 | - | 数据库名 |
| `NC_DB_USER` | 是 | - | 数据库用户 |
| `NC_DB_PASS` | 是 | - | 数据库密码；也接受 `NC_DB_PASSWORD` |
| `NC_DB_SSL_MODE` | 否 | `Prefer` | Npgsql `SSL Mode` |
| `NC_DB_TRUST_CERT` | 否 | `true` | 内网自签证书 |
| `NC_DB_MAX_POOL` / `NC_DB_MIN_POOL` | 否 | `30` / `2` | 连接池 |
| `NC_CORS_ORIGIN` | 否 | `https://cosmos.sua17.cn` | 多个用逗号分隔；`*` 会被忽略 |
| `NC_OCR_SERVICE_URL` | 否 | 空 | OCR 识别与 AI 重病判定服务地址（**同一服务两条路由，同一个地址**，如 `http://127.0.0.1:11434`）；为空时两者均降级为人工/无建议，不影响表单保存 |
| `NC_AGENT_REGISTER_TOKEN` | 建议 | 空 | Agent 注册头 `X-NC-Agent-Register`；为空时注册返回 503 |
| `NC_AUTO_MIGRATE` | 否 | `false` | `1/true/yes/on` 时启动执行 `Migrations/*.sql` |
| `NC_MAX_BODY_MB` | 否 | `4` | 请求体上限；上传场景建议 `128` |
| `NC_MODE` | 否 | `prod` | `test` / `prod`，用于 `/info` |
| `NC_APP_VERSION` | 否 | 程序集版本 | `/info` 与日志 |
| `NC_LOG_DIR` | 否 | `logs` | Core Serilog 日志目录 |
| `NC_AGENT_HEARTBEAT_SECONDS` | 否 | `30` | 注册响应中的心跳间隔 |
| `ASPNETCORE_URLS` | 否 | `http://0.0.0.0:8787` | 监听地址 |

## 数据库迁移

`Migrations/001_printer_binding.sql` 创建：

- `nc_sys_print_devices`：Agent 设备、`device_key`、机器名、版本、上报打印机 JSON、最后心跳时间。
- `nc_sys_printer_bindings`：`user_id + device_id + printer_name` 账户绑定；部分唯一索引保证每账号最多一个默认打印机。

```bash
# 自动（幂等）
NC_AUTO_MIGRATE=1 dotnet NewCosmos.Server.dll

# 手动
psql -h 127.0.0.1 -U newcosmos -d newcosmos -f Migrations/001_printer_binding.sql
```

`NC_AUTO_MIGRATE=1` 执行失败只写日志、不阻止启动；生产建议先手动执行并验证，再关掉自动迁移。

## API

统一错误格式：

```json
{ "code": "AUTH_REQUIRED", "message": "请先登录", "traceId": "00-..." }
```

| 方法 | 路径 | 认证 |
|---|---|---|
| POST | `/api/auth/login` | 匿名 |
| POST | `/api/auth/heartbeat` | Bearer + X-NC-Device |
| POST | `/api/auth/logout` | Bearer + X-NC-Device |
| GET | `/api/auth/me` | Bearer + X-NC-Device |
| GET | `/api/printers` | Bearer + X-NC-Device |
| GET | `/api/printers/devices` | Bearer + X-NC-Device |
| POST | `/api/printers/bind` | Bearer + X-NC-Device |
| DELETE | `/api/printers/{id}` | Bearer + X-NC-Device |
| POST | `/api/printers/{id}/default` | Bearer + X-NC-Device |
| POST | `/api/agent/register` | `X-NC-Agent-Register` |
| POST | `/api/agent/heartbeat` | `X-NC-Device-Key` |
| GET | `/api/applications` | Bearer + X-NC-Device |
| GET | `/api/applications/{id}` | Bearer + X-NC-Device |
| GET | `/health` | 匿名 |
| GET | `/info` | 匿名 |

认证头：`Authorization: Bearer <sessionId>`；除匿名端点外还必须携带 `X-NC-Device`，并与创建会话时的 `deviceId` 一致。

## P1 申请查询（只读）

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/applications?page=1&pageSize=20&keyword=&status=` | 分页列表；`page` 从 1 开始，`pageSize` 1–100（越界按边界收敛），`keyword` 匹配申请人/家庭成员姓名与身份证，`status` 精确匹配状态码 |
| GET | `/api/applications/{id}` | 详情（含家庭成员）；不存在返回 404 `NOT_FOUND` |

字段与示例见 `docs/API_CONTRACT_APPLICATIONS.md`。实现约定：

- 列表统一脱敏：`applicantIdCardMasked` 与 `applicantPhone` 走 Core `DataMasker`；完整身份证仅在详情返回。
- `statusText` 中文映射：`Draft` 草稿、`Submitted` 已提交、`Approved` 已审批、`Completed` 已完成、`Stopped` 已停保、`Refused` 已驳回；其他状态回显原值。
- 家庭成员 `age` 由 `birth_date` 实时计算周岁；`birthDate` 只输出日期（无时间部分）。
- 查询只读 `nc_biz_applications` / `nc_biz_family_members`，均带 `deleted_at IS NULL`；列表 SQL 使用 `ApplicationColumns.Full` 显式列，禁止 `SELECT *`，全部值为 `$1..$n` 参数。
- 未登录返回 401 `AUTH_REQUIRED`；数据库错误返回 500 `DB_ERROR`，未预期异常由统一中间件返回 500 `INTERNAL_ERROR`。
- **暂无权限码校验**：P1 只校验登录会话，任何登录用户都可查询全部申请的列表与详情；按组织/乡镇的数据范围过滤与 403 权限码待权限中心接入后补齐（`ApplicationEndpoints` 已留 TODO）。
- 尚未实现保存/提交等写接口（P1 只读阶段）。

## 打印机绑定规则

- 只操作当前登录账号；服务端不接受前端传 `userId`。
- 浏览器用 `deviceId` 绑定（`GET /api/printers/devices` 获取）；`deviceKey` 只用于 Agent 心跳，不暴露给前端。
- `printerName` 必须存在于该设备最近一次上报的 `printers_json`（忽略大小写，落库用上报名称）。
- 账号第一条绑定自动默认；删除默认后剩余最早一条自动提升。
- 同一账号默认唯一，由数据库部分唯一索引兜底。

## 安全与性能

- 仓库不保存数据库密码/Token；日志不打印密码、Bearer、设备密钥。
- CORS 只允许页面源；允许 `Authorization`、`Content-Type`、`X-NC-Device`、`X-NC-Device-Key`。
- 启用 Brotli/Gzip 响应压缩；Kestrel 请求体上限可配。
- 仅信任 loopback 代理的 `X-Forwarded-For`；`.1` 通过 ZeroTier 反代时如需要真实 IP，可把 `.1` 的 ZT 地址加入 KnownProxies。
- `nc_sys_user_sessions` 是会话权威；每个请求设置 `CurrentUserContext` 并启用 `ISessionGuard`。

## 已知限制（P0）

- 未实现 refresh token；Web 端会话过期后重新登录。
- `nc_sys_print_devices` 暂无 `is_active` 字段；禁用设备需后续迁移。
- `ISchemaSyncService` 为 no-op：`nc_sys_user_sessions`、`nc_sys_users` 等既有表必须提前存在。
- `web` / `mobile-web` 目前按桌面端配额计算；需要独立移动配额时再改 Core。
- 登录接口未加 IP 限流；建议在 `.1` nginx 用 `limit_req` 补。
