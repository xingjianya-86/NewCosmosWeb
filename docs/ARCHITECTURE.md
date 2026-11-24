# NewCosmos Web — 架构与部署

## 拓扑

```text
浏览器/手机
  ├─ 页面：https://cosmos.sua17.cn                → 10.110.0.1 (2C/1.6G/3Mbps)
  ├─ 登录：https://cosmos.sua17.cn/api/auth/*     → .1 nginx → .2:8787
  └─ 业务：https://cosmos-static.sua17.cn:20000/* → 10.110.0.2 (4C/7.7G/100Mbps)

.1：只发 index.html + 反代 /api/auth，禁止业务大流量
.2：nginx（assets/downloads/api）+ ASP.NET Core API + PostgreSQL + OCR + Lottery Worker
Windows 固定 PC：NewCosmos.Agent，出站连 .2，Office COM/打印
```

## 认证

- 登录/刷新/登出走 `.1` 同源，refresh cookie 只存在 `.1`。
- 业务 API 直连 `.2`，使用 `Authorization: Bearer <sessionId>`，不用跨站 Cookie。
- `.2` CORS 只允许 `https://cosmos.sua17.cn`。
- 会话权威：`nc_sys_user_sessions`；Web 端 `deviceKind=web`。
- 每个请求 `X-NC-Device` 与 session 的设备绑定校验。

## 打印机按账户绑定

- `nc_sys_print_devices`：Agent 设备与上报的打印机。
- `nc_sys_printer_bindings`：`user_id + device_id + printer_name`，每账号可多绑、一台默认。
- 打印任务入队时固化 `target_user_id/target_device_id/target_printer`，只有对应 Agent 能认领。
- 手机端只入队；实际打印在绑定的 Windows PC 执行。

## 性能预算

### .1

- nginx only；index gzip < 50KB；`/api/auth/*` 小 JSON。
- 禁止 `/assets`、照片、导入、导出、备份经过 `.1`。

### .2

- nginx：assets Brotli/gzip + immutable；API gzip；`client_max_body_size 128m`。
- ASP.NET Core：工作站 GC，堆上限 3GB；响应压缩；分页。
- PostgreSQL：`shared_buffers=1GB`，连接池 20–30，`work_mem=8MB`。
- Lottery Worker：`--memory=2g --cpus=1.5`，训练错峰。
- 备份：`/srv/newcosmos/backups`，不公开静态下载。

### Web

- Vue 3 + Element Plus，按需引入；首屏 gzip ≤ 300KB。
- 桌面/手机路由级分包；静态资源 hash + immutable。
- 照片列表只出缩略图；PDF 预览按需加载 pdf.js。
