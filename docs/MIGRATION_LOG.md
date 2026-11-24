# 迁移执行记录

> 2026-11-24

## 已完成

### 新工程

- 创建 `D:\Projects\NewCosmosWeb`，git init。
- `legacy/NewCosmos.Desktop` 只读参考副本（已去敏，默认不入库）。
- `src/NewCosmos.Core`：`net10.0`，可编译，0 警告 0 错误。
- `src/NewCosmos.Server`：ASP.NET Core P0，认证/会话/心跳/登出、打印机账户绑定、Agent 注册/心跳、健康检查、迁移。
- `web`：Vue 3 + Element Plus，登录、主框架、桌面/手机响应式、打印机绑定页；生产资源指向 `.2`。
- `src/NewCosmos.Office`：Word/Excel/PDF/打印引擎，0 警告 0 错误。
- `src/NewCosmos.Agent`：Windows 设备注册/DPAPI/心跳/打印机探测/Office 探测/`--render`。
- `src/NewCosmos.Shell`：WPF + WebView2 壳（P6 骨架），可编译。
- `src/NewCosmos.LotteryWorker`：Python Worker + Linux 容器骨架。
- `deploy`：`.1/.2` nginx、Docker/systemd、发布/备份脚本。
- `docs`：架构、API 契约、页面矩阵、服务盘点、性能预算、Schema 对账、WebView2 壳、打印机绑定。

### 验证

- `dotnet build NewCosmosWeb.slnx`：0 错误。
- `dotnet test tests/NewCosmos.Core.Tests`：588/588 通过。
- `npm run build`：通过；首屏 JS gzip ≈192–200KB、CSS ≈20KB。
- Agent：真实 Office DOCX→PDF 冒烟通过；注册/心跳 mock 通过。
- Server：临时 PostgreSQL 冒烟通过（登录/心跳/互踢/登出/绑定/默认/解绑）。

### 数据库（10.110.0.2 / new_cosmos）

- 执行 `src/NewCosmos.Server/Migrations/001_printer_binding.sql`：新表 `nc_sys_print_devices`、`nc_sys_printer_bindings` 已创建，当前均为 0 行。
- 执行 `deploy/database/002_schema_reconciliation.sql`：P0/P1 列与打印目标列已补齐（多数 `IF NOT EXISTS` 跳过）。
- 清空旧 Rust 打印注册表 `nc_biz_print_agents`（备份：
  `deploy/database/backups/print_agents_20261124_200425.sql`），当前 0 行。
- 停止本机 `RustCosmosPrintAgent` 进程，并移除 `HKCU\...\Run` 自启动项，避免再次注册。
- 保留历史打印任务 `nc_biz_print_jobs`（195 行），未删除。

## 生产切换（2026-11-24 20:2x）

> ⚠️ 本次上线的是 **P0**：登录、首页、打印机绑定、Agent 注册/心跳。社会救助/档案/报表等业务页面尚未迁移。
> 旧 Rust 业务页已随 `.1` index 替换和 `nc-server` 停止而下线；如需继续使用旧业务页，按下方回滚步骤恢复。

### 已部署

- `.2`：`newcosmos-server:local` 容器已启动，`/health` 200，`/info` database=ok；旧 `nc-server` 容器已停止但保留，可回滚。
- `.2` nginx：`/www/server/panel/vhost/nginx/cosmos-static.sua17.cn.conf` 已切换为 NewCosmos 版本；备份：
  - `cosmos-static.sua17.cn.conf.bak_`（原始）
  - `cosmos-static.sua17.cn.conf.bak_20261124_2026`
- `.1` nginx：`/www/server/panel/vhost/nginx/cosmos.sua17.cn.conf` 已切换；备份：
  - `cosmos.sua17.cn.conf.bak_20261124_2026`
- `.1` index：`/www/wwwroot/cosmos.sua17.cn/index.html` 已更新；备份：
  - `index.html.bak_20261124_2026`
- `.2` assets：`web/dist/assets/*` 已上传到 `/www/wwwroot/cosmos-static.sua17.cn/assets/`，旧 hash 文件保留。
- `.2` 服务端目录：`/srv/newcosmos/server/`（publish、Dockerfile、compose、server.env）。

### 验证

- `https://cosmos.sua17.cn/` 200，index 指向 `.2` assets。
- `https://cosmos-static.sua17.cn:20000/assets/index-*.js` 200，CORS `*`，immutable。
- `https://cosmos.sua17.cn/api/auth/login` 错误密码返回 401（边缘反代 + DB 正常）。
- `https://cosmos-static.sua17.cn:20000/api/auth/login` 401，CORS 允许 `.1`。
- `OPTIONS /api/auth/login` 204，允许 Authorization/Content-Type/X-NC-Device。
- Agent 注册/心跳在 `.2:8787` 冒烟通过，测试设备已清理。

### 回滚

- `.2` 服务：
  ```bash
  cd /srv/newcosmos/server && docker compose down
  docker start nc-server
  ```
- `.2` nginx：恢复 `cosmos-static.sua17.cn.conf.bak_20261124_2026` 后 `nginx -t && nginx -s reload`。
- `.1` nginx/index：恢复 `cosmos.sua17.cn.conf.bak_20261124_2026` 与 `index.html.bak_20261124_2026` 后 reload。

## 画风与启动动画（2026-11-24 21:0x）

- Web 设计令牌改为旧 NewCosmos 纸墨档案风：暖纸、暖墨、朱砂、松绿、赭金、1–3px 圆角；Element Plus 默认蓝/大圆角已覆盖。
- P0 三页重绘：登录（档案封面/印章）、首页（纸墨信息卡/快捷入口）、打印机绑定（纸墨表格/手机卡片/设备选择）。
- 启动动画 `Resources/Splash` 已复制到 `web/public/splash`，浏览器 bridge 改为 `postMessage`；`splash-intent` 立即返回 `full`，动画完整播放。
- `SplashOverlay`：每会话一次、`?nosplash=1` 跳过、60 秒硬兜底、iframe 跨域 message 校验、音频受浏览器自动播放策略限制。
- `.2` nginx 新增 `/splash/`：`frame-ancestors https://cosmos.sua17.cn`，不再使用 `X-Frame-Options: DENY`。
- 已上传 `splash/**` 与新 assets 到 `.2`，`.1` index 已替换；`/splash/splash.html` 200，assets 200 + CORS + immutable，登录接口 401 正常。
- 当前生产页面已带新画风与启动动画；业务页面仍为 P0。
- 未登录启动改为 `login.html` 序列登录：动画第 2 步输入账号密码，成功继续 3–5 步/终幕后进首页；失败留在第 2 步；动画自带“切换经典登录”才回落 Vue 登录页。已移除自定义右上角跳过按钮。
- 首页按 `Pages/Main/MainPage.xaml` 1:1 重构：76px 顶栏 + 6px 朱砂条、200px 墨黑侧栏、数据概览 4 卡、救助业务 7 卡/管理工具 4 卡/娱乐 1 卡/链接 2 卡、看板娘立绘栏、44px 状态栏；未实现模块走 `/coming-soon/:module`。
- 首页素材已上传 `.2:/www/wwwroot/cosmos-static.sua17.cn/images/`；`.2` nginx 增加 `/images/` 静态 location。
- 已部署新 assets/index/splash，`/`、`/login`、`/applications`、`/coming-soon/*` 均 200。
- 修复 URL 拼接：Vue Router 改用 `VITE_ROUTER_BASE=/`，不再把 `VITE_ASSET_BASE`（`.2` 绝对地址）当 Router base；`/login`、`/printers` 直接刷新 200。
- 启动闸门：已登录不播动画，直接进应用；未登录才播动画，结束后挂载登录页。`?nosplash=1` 或本会话已播则跳过。
- 对外名称恢复为 `帝皇权杖δ-me13 社会救助管理系统`；`APP_NAME`、页面标题、登录/侧栏品牌、Shell 标题同步。

## 待办

- P1 起：社会救助、档案/文书、报表/导入、其他业务域、管理与系统页面。
- 业务 API 按域扩展；Web 页面按 `docs/PAGE_MATRIX.md` 推进。
- 把 P0/P1 列回写 YAML（`Resources/Schema`），让 YAML 重新成为完整权威。
- 打印任务认领协议：Web 按 `target_device_id/target_printer` 定向认领；补 `PrintRelay` 常量或过滤。
- 土地字典标准/别名归一（禁止批量改历史值）。
- `.2` 证书 SAN、CORS、nginx `nginx -t`、真实部署与端到端冒烟。

## P2 模块查询、权限与数据范围（2026-11-24/25）

- 全栈版本统一为 `2.0.20261124`：`Directory.Build.props` 为 .NET 单一源，`web/package.json` 为 Web 单一源（Vite 注入 `__APP_VERSION__`）；Server `/info`、Agent 心跳、登录页与状态栏同源。新增 `deploy/check_versions.ps1`。
- 首页 `/api/dashboard/summary` 接入真实统计：本月新增/退出、新申请经济核对、本月新增高龄；渐退期、高龄待办、大学生毕业提醒按真实计数显示；单块失败返回 null，前端显示「—」，不再假 0。
- 新增低收入模块首页 `/social-assistance` 与渐退期列表 `/social-assistance/grace-period`；申请列表 `/api/applications` 保留，补 `INCOME_VIEW` 权限码与数据范围。
- 新增高龄模块：`/elderly`、`/elderly/applications`、`/elderly/applications/:id`、`/elderly/pending`、`/elderly/reviews`；接口 `/api/elderly/*` 全部 `ELDERLY_VIEW` 权限。
- 新增经济核对模块：`/asset-verification`、`/asset-verification/tasks`、`/asset-verification/monthly`；接口 `/api/asset-verification/*` 全部 `ASSET_VIEW` 权限。
- 数据范围：新增 `DataScopeFilterBuilder`，按当前用户角色 `data_scope` + 机构 `town_id/village_id` 映射到业务表：
  - 低收入申请：乡镇名/村名/ID 兜底（历史数据 town_id/village_id 为空）；
  - 高龄档案：`hukou_town_id/hukou_village_id` 精确过滤；
  - 经济核对：村区域代码 `community` 过滤；
  - SELF 范围按 `created_by`；经济核对无 created_by，按拒绝处理。
- 待办列表在 Server 侧按数据范围二次过滤，避免跨村姓名/身份证泄露；概览统计仍使用 Core 全局口径（跨机构统计下一批改 Core 服务）。
- 权限：`EndpointSecurity.RequirePermissionAsync` 统一 401/403；P1 `/api/applications` 同步收口。
- 生产验证（2026-11-24 23:5x）：
  - `dotnet build NewCosmosWeb.slnx` 0 错误；Core 588 测试全过；
  - `npm run build` 通过；
  - `/api/dashboard/summary`、`/api/social-assistance/*`、`/api/elderly/*`、`/api/asset-verification/*` 登录态冒烟 200；
  - 村经办人（role=VILLAGE_CLERK，org village_id=910470）数据范围实测：申请 4、 高龄 6、核对 27，与库内该村数量一致；
  - 三个模块首页与列表已截图走查。
- 部署：`.2` 服务器镜像 `newcosmos-server:20261124235325`，`/info` 返回 `2.0.20261124`；Web 新 assets 与 `.1` index 已更新（`index-Xyi0EXhz.js`）。