# 迁移已知问题 / 待办

## Core 抽取后遗留

1. `Services/Domain/ArchiveManagement`、`Services/Domain/Printing`、`Services/Domain/Reporting/PrintService`、`Services/Templates`、`Services/Lottery` 未进入 Core：
   - Office/打印 → `NewCosmos.Office` / `NewCosmos.Agent`
   - 彩票 → `NewCosmos.LotteryWorker`
   - 档案制作依赖 Office，待 Office 项目稳定后再拆服务层。
2. `ConfigService`、`LoggerService` 的桌面实现未进 Core：
   - Server 使用自己的配置/日志适配；
   - 桌面端后续继续用 legacy 实现，或抽 `NewCosmos.Desktop` 专用配置。
3. `OcrRecognizedRecord.ThumbnailImage`（MAUI ImageSource）已移除，保留 `Result.Thumbnail` 字节：
   - Web 用 Blob URL 渲染；
   - 桌面迁移时在 UI 层做 `ImageSource.FromStream`。
4. `DeathDeclarationConstants.ResolveChannel` 从 MAUI `DeviceInfo` 改为 `OperatingSystem.IsAndroid()`。
5. `ImageCompressor` 移除了 MAUI 图像兜底，统一 SkiaSharp 解码。
6. `SessionGuard` 仍是单进程单会话实现；Server 使用请求级 `ISessionGuard`，桌面端后续保持单用户语义。
7. `App.Current*` 已替换为 `CurrentUserContext`；Server 中间件负责设置，桌面端登录后需 `SetDefault`。

## 待迁移

- legacy `NewCosmos.Tests` 纯逻辑测试逐步迁入 `tests/NewCosmos.Core.Tests`。
- 档案制作/月报打印/照片打印/批量补打依赖 Office，等 Agent/Office 项目。
- 数据库 schema 与 Rust 期间新增表/列对账（`nc_sys_user_sessions`、`nc_biz_print_jobs`、`nc_lottery_*` 等）。
- Web 业务页面（申请、档案、月报、导入、管理、彩票）按 P1–P5 批次迁移。
- `.1/.2` 证书 SAN 确认：`cosmos-static.sua17.cn` 必须在证书内。
