# NewCosmos Web — P0 API Contract

> 本文件是前后端唯一契约。P0 只实现认证、会话、打印机绑定、Agent 注册/心跳、健康检查。
> 业务 API 后续按域追加，但认证头、错误格式、分页格式保持不变。

## 0. 通用

- Base URL（业务 API）：`https://cosmos-static.sua17.cn:20000`
- 认证 API（同源）：`https://cosmos.sua17.cn/api/auth/*`
- 认证头：`Authorization: Bearer <sessionId>`
- 设备头：`X-NC-Device: <deviceId>`
- 请求/响应：`application/json; charset=utf-8`
- 时间：ISO 8601 UTC（`2026-11-24T10:00:00Z`）
- 错误：

```json
{
  "code": "AUTH_INVALID",
  "message": "用户名或密码错误",
  "traceId": "00-..."
}
```

## 1. 认证

### POST /api/auth/login

```json
{
  "username": "admin",
  "password": "***",
  "deviceId": "web-4f2c...",
  "deviceKind": "web",
  "deviceName": "Chrome / Windows",
  "appVersion": "2.0.0"
}
```

响应：

```json
{
  "sessionId": "8f1c...",
  "expiresAt": "2026-12-24T10:00:00Z",
  "user": {
    "id": 1,
    "username": "admin",
    "fullName": "张三",
    "phone": "138****0000",
    "organizationId": 10,
    "organizationName": "XX镇"
  }
}
```

- `deviceKind`：`web` / `mobile-web` / `desktop` / `android`
- 会话策略沿用 `nc_sys_user_sessions`；Web 端计入 `web` 配额。
- 登录失败返回 401，不区分“用户不存在/密码错误”。

### POST /api/auth/heartbeat

Header：`Authorization`, `X-NC-Device`

```json
{ "active": true, "expiresAt": "2026-12-24T10:00:00Z" }
```

失效返回 401，body `{ "code": "SESSION_REPLACED" | "SESSION_EXPIRED" | ... }`。

### POST /api/auth/logout

Header：`Authorization`

返回 204。

### GET /api/auth/me

Header：`Authorization`, `X-NC-Device`

返回 `user` 对象（同登录响应）。

## 2. 打印机绑定（按账户）

### GET /api/printers/devices

返回已注册的 Agent 设备（不暴露 deviceKey）：

```json
[
  {
    "id": 3,
    "machineName": "民政办-01",
    "online": true,
    "lastSeenAt": "2026-11-24T09:59:00Z",
    "printers": ["HP LaserJet M404", "Microsoft Print to PDF"]
  }
]
```

### GET /api/printers

返回当前账号的绑定：

```json
[
  {
    "id": 12,
    "deviceId": 3,
    "deviceName": "民政办-01",
    "printerName": "HP LaserJet M404",
    "isDefault": true,
    "online": true,
    "lastSeenAt": "2026-11-24T09:59:00Z"
  }
]
```

### POST /api/printers/bind

```json
{ "deviceId": 3, "printerName": "HP LaserJet M404", "isDefault": true }
```

- 只能绑定当前账号；`deviceId` 必须存在且曾上线。
- 浏览器端只使用公开的 `deviceId`；不接收也不暴露 `deviceKey`。
- 同一账号首次绑定自动设为默认。
- 返回绑定对象。

### DELETE /api/printers/{id}

只能删自己的绑定；删默认后若还有绑定，自动把最早一条设为默认。

### POST /api/printers/{id}/default

设为当前账号默认打印机。

## 3. Agent 注册/心跳

### POST /api/agent/register

无需用户会话，使用一次性注册密钥头 `X-NC-Agent-Register: <NC_AGENT_REGISTER_TOKEN>`。

```json
{
  "machineName": "MZ-01",
  "agentVersion": "2.0.0",
  "printers": [
    { "name": "HP LaserJet M404", "isDefault": true },
    { "name": "Microsoft Print to PDF", "isDefault": false }
  ]
}
```

响应：

```json
{ "deviceId": 3, "deviceKey": "a1b2...", "heartbeatSeconds": 30 }
```

`deviceKey` 只返回一次，Agent 保存在本机 DPAPI 加密配置中。

### POST /api/agent/heartbeat

Header：`X-NC-Device-Key: <deviceKey>`

```json
{
  "agentVersion": "2.0.0",
  "printers": [
    { "name": "HP LaserJet M404", "isDefault": true }
  ]
}
```

响应 204；设备不存在/被禁用返回 401。

## 4. 健康检查

- `GET /health` → `{ "status": "ok", "time": "..." }`
- `GET /info` → `{ "service": "NewCosmos.Server", "version": "...", "database": "...", "mode": "test|prod" }`

## 5. 错误码（P0）

| code | HTTP | 说明 |
|---|---|---|
| `AUTH_INVALID` | 401 | 用户名或密码错误 |
| `AUTH_REQUIRED` | 401 | 未带/无效会话 |
| `SESSION_REPLACED` | 401 | 被顶号 |
| `SESSION_EXPIRED` | 401 | 会话过期 |
| `FORBIDDEN` | 403 | 无权限 |
| `PRINTER_NOT_FOUND` | 404 | 绑定不存在 |
| `PRINTER_DEVICE_NOT_FOUND` | 404 | Agent 设备不存在 |
| `PRINTER_NAME_NOT_REPORTED` | 400 | 打印机不在 Agent 上报列表 |
| `VALIDATION_ERROR` | 400 | 参数校验失败 |
| `DB_ERROR` | 500 | 数据库错误 |
