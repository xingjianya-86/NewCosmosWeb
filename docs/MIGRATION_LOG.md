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

## 待办

- P1 起：社会救助、档案/文书、报表/导入、其他业务域、管理与系统页面。
- 业务 API 按域扩展；Web 页面按 `docs/PAGE_MATRIX.md` 推进。
- 把 P0/P1 列回写 YAML（`Resources/Schema`），让 YAML 重新成为完整权威。
- 打印任务认领协议：Web 按 `target_device_id/target_printer` 定向认领；补 `PrintRelay` 常量或过滤。
- 土地字典标准/别名归一（禁止批量改历史值）。
- `.2` 证书 SAN、CORS、nginx `nginx -t`、真实部署与端到端冒烟。
