# 服务清单与迁移评估（SERVICE_INVENTORY）

> 盘点对象：`legacy/NewCosmos.Desktop/Services`（263 个 `.cs`）。
> 对照目标：`src/NewCosmos.Core`（net10.0，无 MAUI）+ `NewCosmos.Server`（Linux）+ `NewCosmos.Agent`（Windows）+ `NewCosmos.LotteryWorker`（Python）。
> 盘点日期：2026-11-24。所有数字来自磁盘，核对命令见文末。

## 0. 基线

| 指标 | 数值 |
|---|---:|
| legacy Services 文件 | 263 |
| legacy Services 总行数 | 68,337 |
| 已复制/改写进 `src/NewCosmos.Core` | 170 个文件（169 个同名 + 新增 `CurrentUserContext.cs`） |
| 仍未迁移 | 94 个文件，22,860 行 |
| 引用 MAUI API（FileSystem/Preferences/SecureStorage/MainThread/DeviceInfo 等） | 8 个文件 |
| 引用 `App.CurrentUserId` 等静态用户上下文 | 18 个文件（均在待迁移侧） |
| 真正 Windows-only（DllImport/ProtectedData/Registry/winspool） | 5 个文件 |
| Office COM 相关（Templates 8 + Printing 13 + Reporting 3 + Duty 2，含接口） | 26 个文件 |
| Python/TensorFlow 相关 | Lottery 7 个文件 |

## 1. 迁移状态总表

| 目录 | 文件 | 行数 | 已进 Core | 待迁移 | 结论 |
|---|---:|---:|---:|---:|---|
| `Core` | 53 | 6,875 | 22 | 31 | 一半是桌面生命周期/更新/对话框，不进 Core |
| `Database` | 6 | 2,704 | 6 | 0 | 已完成，`PostgreSqlDatabaseService` 直接可用 |
| `Domain` | 127 | 39,286 | 89 | 38 | 业务主体；剩档案 20、打印 13、报表 3 等 |
| `Import` | 24 | 4,899 | 24 | 0 | 已完成（EPPlus + ExcelDataReader） |
| `Lottery` | 7 | 1,433 | 0 | 7 | 不迁 Core，改写为 Worker + Server API |
| `Platform` | 11 | 539 | 11 | 0 | 只有接口；实现留在 `Platforms/Windows`、`Platforms/Android` |
| `StateMachine` | 1 | 81 | 1 | 0 | `ApplicationStateMachine` 已完成 |
| `System` | 15 | 7,762 | 9 | 6 | 字典/区划/标准已进 Core；值班/数据库/网络待拆 |
| `Templates` | 8 | 2,791 | 0 | 8 | Office COM 引擎，整体留 Agent |
| `UserManagement` | 5 | 873 | 5 | 0 | 权限/角色/数据范围已完成 |
| `Utilities` | 6 | 1,094 | 2 | 4 | Office 检测/字体/双面打印留 Agent |

`Domain` 子域明细：

| 子域 | 文件 | 行数 | 已进 Core | 待迁移 | 去向 |
|---|---:|---:|---:|---:|---|
| SocialAssistance | 40 | 7,412 | 40 | 0 | Core |
| ArchiveManagement | 20 | 4,654 | 0 | 20 | Core（域逻辑）+ Agent（模板渲染） |
| Printing | 13 | 1,883 | 0 | 13 | 队列/记录进 Core；执行留 Agent |
| ElderlyBenefits | 6 | 3,143 | 6 | 0 | Core |
| ChangeManagement | 9 | 6,507 | 8 | 1 | Core（`DynamicManagementRecordService` 待适配用户上下文） |
| Reporting | 9 | 6,406 | 6 | 3 | 月报/统计进 Core；`PrintService` 拆 Server+Agent |
| Recovery | 5 | 1,422 | 5 | 0 | Core |
| AssetVerification | 4 | 1,437 | 4 | 0 | Core |
| NearRelative | 3 | 967 | 3 | 0 | Core |
| TempRelief | 4 | 2,349 | 4 | 0 | Core |
| UserManagement | 6 | 2,234 | 6 | 0 | Core |
| BankFlow | 2 | 255 | 2 | 0 | Core |
| SevereDisease | 3 | 128 | 3 | 0 | Core |
| Signatures | 2 | 95 | 2 | 0 | Core |
| SpecialApproval | 1 | 394 | 0 | 1 | Core（替换 `App.CurrentUserFullName`） |

## 2. 依赖画像

| 依赖 | 文件数 | 具体文件 | 处理方式 |
|---|---:|---|---|
| MAUI：`FileSystem` / `Preferences` / `SecureStorage` / `MainThread` / `DeviceInfo` | 8 | `Core/ConfigService`、`Core/DeviceIdentityProvider`、`Core/DialogService`、`Core/OutputRootMigrationService`、`Core/SessionLifecycleService`、`Core/SessionStore`、`Core/SplashAssetsService`、`System/NetworkAccessService` | Web/Server 用配置中心 + Redis/PG 会话 + 浏览器存储；桌面保留原实现 |
| `App.CurrentUserId/Name/OrganizationId` | 18 | `AddressScopeService`、`SessionLifecycleService`、`ArchivePhotoService`、`DeathStatementService`、`PublicityOutputService`、`PdfVerificationService`、`DynamicManagementRecordService`、`ImportedArchiveService`、`PhotoPrintPushHelper`、`PrintAgentService`、`PrintExecuteService`、`PrintJobFactory`、`MonthlyReportService.SpecialDetail`、`PrintService`、`SpecialApprovalFormService`、`UserService`、`BankFlowImportService`、`ClientVersionService` | legacy 文件计数；已迁移副本（如 `UserService`、`AddressScopeService`）已改用 `CurrentUserContext`，其余迁移时必须替换；Server 禁止保留静态用户 |
| Office COM（Word/Excel/WPS） | 26 | `Templates/*`（8）、`Domain/Printing/*`（13）、`Domain/Reporting/PrintService*`（3）、`System/DutyService*`（2） | 只在 Agent 执行；Server 只生成任务/字段 |
| Windows API（DllImport/ProtectedData/Registry/winspool） | 5 | `Core/ConfigService`、`Templates/OfficeComWorker`、`Utilities/FontInstallChecker`、`Utilities/OfficeProviderDetector`、`Utilities/PrinterDuplexHelper` | 桌面/Agent；Server 不引用 |
| Python / TensorFlow | 7 | `LotteryDataService`、`LotteryPredictService`、`UserPurchaseService`、`PythonScriptRunner`、3 个接口 | `.2` Lottery Worker + Server API |
| EPPlus / ExcelDataReader | 24（Import） | `BaseImportService`、`SheetReaderFactory`、两类 Reader、各域 ImportService | 已在 Core；注意 EPPlus 非商业授权 |
| Npgsql | 3（Database） | `PostgreSqlDatabaseService`、`SchemaService`、`SchemaService.Validation` | 已在 Core |
| PDFsharp | 4 | `PrintService.PdfGeneration`、`PdfVerificationService` 等 | PDF 合并留 Server/Agent，验证留 Core |
| 本地文件系统 / 输出目录 | Core/File、OutputRootMigration、PrintService、Templates | `document_output.yaml`、`{Documents}` 占位 | Web 用对象存储/受控下载；Agent 保留本地输出 |

## 3. 三个去向

### A. 可直接进 Core（已完成或只差用户上下文）

| 类别 | 服务 | 状态 |
|---|---|---|
| 数据访问 | `IDatabaseService`/`PostgreSqlDatabaseService`、`ITransactionScope`、`SchemaService`、`SchemaSyncService`、`SeedMergeService` | 已完成 |
| 业务域 | `ApplicationService`、`IncomeCalculationService`、`ClassificationService`、`GuaranteeAmountService`、`GracePeriodService`、`QuickVerifyService`、`PersonSearchService`、`HouseholdSurveyService`、`SupporterService`、`CaregiverService`、`SubsidyDataService`、`VisitRecordService`、`LandContractService`、`OtherDifficultyLedgerService` | 已完成 |
| 变更/追缴/高龄 | `ChangeService`（5 分片）、`ImportedArchiveService`、`RecoveryService`、`RecoveryAmountCalculator`、`ElderlyApplicationService`、`ElderlyPaybackCalculator`、`ElderlyCategoryService` | 已完成 |
| 临时救助/核对/重病 | `TempReliefService`、`TempReliefCandidateRanker`、`AssetVerificationService`、`PdfVerificationService`、`SevereDiseaseAdviceService`、`SevereDiseaseByCostRule` | 已完成（`PdfVerificationService` 需换用户上下文） |
| 报表/统计 | `MonthlyReportService`、`StatisticsService`、`TempReliefSummaryGrouper` | 已完成 |
| 导入 | 全部 24 个文件（含 6 类组合导入 + 银行流水/土地/高龄/种植补贴） | 已完成 |
| 权限 | `NewPermissionService`、`RoleService`、`DataScopeService`、`UserService`、`OrganizationService`、`UserSessionService` | 已完成 |
| 系统 | `DictionaryService`、`RegionService`、`StandardConfigService`、`HolidayManageService`、`HolidayService`、`BusinessTimelineService`、`ApplicationStatusService`、`DictCacheService`、`DataMasker` | 已完成 |
| 状态机 | `ApplicationStateMachine` | 已完成 |
| 打印数据（不含执行） | `PrintQueueService`、`PrintRecordService`、`PrintJobFactory`、`PhotoPrintPushHelper` | 待迁移；队列表 `nc_biz_print_jobs` 已在库 |
| 档案域逻辑 | `ArchiveService`、`DeathStatementService`（5 分片）、`ArchivePhotoService`、`ProofUnitTemplateService`、`TemplateService`、`PublicityOutputService` | 待迁移；模板渲染留 Agent |
| 一事一议 | `SpecialApprovalFormService` | 待迁移，工作量小 |

### B. 需要适配后进 Core / Server

| 服务 | 现状问题 | 适配方式 |
|---|---|---|
| `ConfigService` + `PublicityOutputService` 等 | MAUI `FileSystem`/`Preferences`/`SecureStorage`，密码 `enc:` 由 DPAPI 加密 | Server 只读环境变量/appsettings；桌面/Agent 保留 DPAPI；Web 不再读取 ini |
| `SessionStore` / `SessionLifecycleService` / `SessionHeartbeatService` | `SecureStorage` + `MainThread` + 桌面生命周期 | 改写为 PG `nc_sys_user_sessions` + Bearer + 前端心跳；Web 不用 SecureStorage |
| `DatabaseBackupService` / `DatabaseManagementService` | 调 `pg_dump`/`psql`，恢复含 `DROP SCHEMA public CASCADE` | Server 后台任务 + 维护模式 + 二次确认；下载走 `.2` 受限路径 |
| `FileService` / `OutputRootMigrationService` | 本地目录、用户文档目录 | Server 用存储抽象（受控目录/S3 兼容）；Agent 保留本地输出迁移 |
| `ArchivePhotoService` / `ArchivePhotoWebPMigrationService` | bytea 存照片，SkiaSharp 压缩，无缩略图 | 保留 bytea 兼容；新增缩略图列/表 + 服务端 WebP 转码；列表只出元数据 |
| `PrintService` / `PrintService.PdfGeneration` | 同时做 HTML 预览、Office COM 打印、PDFsharp 合并 | 拆三段：Server 数据/HTML、Agent COM 打印、Server/Agent PDF 合并 |
| `DutyService` / `DutyService.Schedule` | EPPlus 读写 + Office COM 打印/导出 | 排班数据进 Core；Excel 解析可进 Core；打印/PDF 留 Agent |
| `ClientVersionService` | 读 `App.CurrentUserFullName`，服务端无意义 | 改为 Server `/info` + Agent 上报版本；桌面版保留 |
| `AddressScopeService`、`DeathStatementService`、`PublicityOutputService`、`DynamicManagementRecordService`、`BankFlowImportService`、`MonthlyReportService.SpecialDetail`、`UserService`、`PrintJobFactory`、`PrintAgentService`、`PrintExecuteService`、`PhotoPrintPushHelper` | 读静态 `App.CurrentUserId` 等 | 统一替换 `CurrentUserContext`；Server 每请求 `Begin(snapshot)` |
| `LotteryDataService` / `LotteryPredictService` / `UserPurchaseService` / `PythonScriptRunner` | 本地 Python 进程、嵌入式运行时、训练 10–40 分钟 | 改写为 Worker 任务 API + 结果表；Server 只入队/查询 |

### C. 留在 Agent / Desktop（不迁 Linux）

| 服务/目录 | 原因 | 归属 |
|---|---|---|
| `Templates/*`（`WordEngine`、`ExcelEngine`、`OfficeComWorker`、`TemplateEngineFactory`、`TemplateFieldBuilder/Resolver`、`TemplateEngineHelpers`） | Office/WPS COM、STA 单线程 | Agent（Windows） |
| `Domain/Printing/PrintAgentService`、`PrintExecuteService`、`PhotoPrintService` | 打印机、COM、照片排版 | Agent |
| `Domain/Reporting/PrintService` 的 COM 分支 | 月报打印/双面/逐份 | Agent |
| `Utilities/OfficeProviderDetector`、`OfficeProviderInfo`、`FontInstallChecker`、`PrinterDuplexHelper` | 注册表/winspool/字体 | Agent |
| `Core/UpdateService`、`AppUpdateCoordinator`、`UpdateHistoryService`、`StartupIntent` | 桌面安装/增量补丁/启动意图 | Desktop（后续 MAUI） |
| `Core/SplashGate`、`SplashAssetsService` | 启动动画 WebView 资产 | Desktop |
| `Core/DialogService`、`LoadingProgressService`、`NavigationService` | MAUI 页面/弹窗/导航 | 由 Vue 组件替代 |
| `Core/DeviceIdentityProvider` | Preferences + 设备型号 | Agent 生成 deviceKey；Web 用 `X-NC-Device` |
| `System/NetworkAccessService` | ZeroTier CLI（Windows 路径） | Agent/Desktop 运维工具 |
| `Platform` 实现（`Platforms/Windows/*`、`Platforms/Android/*`） | 文件选择、相册、安装包、系统栏、打印 | 各端原生；Web 用 `<input type=file>`/`capture` |

## 4. 重点服务事实

### 4.1 打印链路（13 个文件，1,883 行）

| 服务 | 职责 | 事实 |
|---|---|---|
| `PrintQueueService` | 入队/认领/完成 | 表 `nc_biz_print_jobs`；`fields_json`/`table_rows_json`/`supporter_table_data_json` 为 JSONB；状态 `Pending/Processing/Completed/Failed` |
| `PrintJobFactory` | 组装打印任务 | 模板分类复用 `ArchiveCategoryResolver`；`RequestedBy ??= App.CurrentUserId` |
| `PrintAgentService` | PC 常驻轮询 | 启动时取 Pending，调 `PrintExecuteService`；权限按当前 PC 登录用户判断 |
| `PrintExecuteService` | 实际打印 | 业务类型 → 权限码映射；Excel 超行分片；PDF 合并 |
| `PhotoPrintService` | 照片打印 | WebP/JPEG/PNG 统一转 JPEG；Word COM 排版；版式 `FULL_PAGE/HALF_PAGE/ID_CARD` |
| `PrintRecordService` | 打印留痕 | 记录打印历史 |
| `PhotoPrintPushHelper` | 手机照片入队 | 鉴权取图 → `PrintJobRequest` → 入队 |
| `nc_biz_print_jobs` 现有列 | `printer_name`、`agent_machine`、`requested_by` | **缺** `target_user_id/target_device_id/target_printer`，与 ARCHITECTURE 的“按账户绑定打印机”有差距，需要迁移加列 |

### 4.2 会话与用户上下文

- 会话权威表：`nc_sys_user_sessions`；`UserSessionService` 已在 Core。
- 桌面侧 `SessionStore` 用 `SecureStorage` 存 sessionId；Web 改为 HttpOnly refresh Cookie（`.1`）+ 内存 Bearer。
- 18 个服务读 `App.CurrentUserId` 等静态属性；Core 已提供 `CurrentUserContext`（`AsyncLocal` + 默认快照），迁移时必须替换，否则 Server 并发下会串用户。

### 4.3 照片与文件

- `ArchivePhotoService`：照片存 `nc_biz_archive_photos.photo_data`（bytea）；列表只取元数据；预览/打印按 id 单取。
- 压缩常量：长边 2000、目标 350KB、硬上限 500KB、WebP 质量 80→50；历史 JPEG/PNG 有迁移服务（批量 20）。
- 无档案照片缩略图实现（`ArchivePhotoService` 与照片表均未见缩略图字段/生成逻辑；`OcrConstants.ThumbnailMaxEdge` 只用于 OCR 结果卡片）；照片列表在 Web 必须新增缩略图，不能直接下发 350–500KB 原图。

### 4.4 备份与数据库管理

- `DatabaseBackupService` 调 `pg_dump` 备份、`psql` 恢复；恢复脚本包含 `DROP SCHEMA IF EXISTS public CASCADE`，属破坏性操作。
- `DatabaseManagementService` 提供库状态、表大小、备份文件列表、日志清理；权限码 `SYSTEM_BACKUP`。
- 备份目录在桌面为 `StorageOptions.GetBackupPath()`；Server 需固定到 `/srv/newcosmos/backups`，不公开静态下载。

### 4.5 导入

- 10 种导入类型：农村低保、城市低保、低保边缘、刚性支出、特困、其他困难（6 类组合导入）+ 高龄补贴、年度种植补贴、土地承包、银行流水核对。
- 两种读取器：EPPlus（xlsx）与 ExcelDataReader（xls）；`SheetReaderFactory` 选择。
- 组合导入按文件名匹配家庭表/人员表（`ImportTypeCodes` 中定义匹配模式），失败要给出缺失文件明细。

### 4.6 彩票

- `PythonScriptRunner` 启动 `python-embed`，注入 `PGPASSWORD`，超时 Kill。
- `LotteryPredictService` 调 `Scripts/Lottery/lstm_entry.py`，模型未训练时拒绝预测；训练 10–40 分钟。
- 数据源 `nc_lottery_draws`；奖级判定 `UserPurchaseService` + `LotteryPrizeResolver`。

### 4.7 值班

- `DutyService` 用 EPPlus 解析值班 Excel、校验成员、生成排班；`PrintMonthAsync`/`ExportMonthPdfAsync` 走 Office COM。
- 迁移拆分：排班算法/校验进 Core；Excel 解析可进 Core（EPPlus）；打印/PDF 留 Agent。

## 5. 迁移风险清单

| # | 风险 | 证据 | 影响 | 处置 |
|---|---|---|---|---|
| R1 | Office COM 不能上 Linux | `Templates/*`、`Printing/*`、`PrintService`、`DutyService` 共 26 文件（含接口） | 文书/打印全部不可用 | 所有 COM 只在 Agent；Server 只生成任务；Agent 单 STA 串行 |
| R2 | 静态用户上下文串号 | legacy 18 个服务读 `App.CurrentUserId`；Core 已提供 `CurrentUserContext` | Server 并发下写错经办人/越权 | 迁移时统一替换；请求入口 `Begin`，禁止静态 |
| R3 | DPAPI 密文无法跨平台 | `ConfigService` 的 `enc:` + `ProtectedData` | 复制 ini 到 Linux 解不开 | Server 用环境变量；ini/DPAPI 只留桌面 |
| R4 | 照片 bytea 撑大库与备份 | `nc_biz_archive_photos.photo_data`，单张 ≤500KB | 库/备份膨胀、列表卡顿 | 缩略图表 + 独立取图接口 + 备份窗口/保留策略 |
| R5 | 打印任务缺账户/设备归属 | `nc_biz_print_jobs` 仅 `printer_name/agent_machine` | 可能打到别人打印机 | 迁移加 `target_user_id/target_device_id` + 认领过滤 |
| R6 | 恢复操作破坏性 | `RestoreDatabaseAsync` 先 `DROP SCHEMA public CASCADE` | 误操作整库丢失 | 维护模式 + 二次确认 + 恢复前自动备份 |
| R6b | 备份工具是 Windows 二进制 | `DatabaseBackupService` 优先用随包 `Resources/PostgreSQL/pg_dump.exe` / 同目录 `psql.exe` | Linux Server 无法执行 | Server 用系统 `pg_dump/psql` 或 PG 容器内工具；路径可配 |
| R7 | Schema 自动同步与双端并存 | `SchemaSyncService` + 67 个 YAML；旧桌面端仍在写库 | 新旧端 DDL 竞争/漂移 | Server 关闭自动 DDL；迁移脚本单点；旧端只读兼容 |
| R8 | 导入大文件内存峰值 | EPPlus 全量加载；组合导入多文件 | `.2` 7.7G 内存被吃满 | 限流 2 个并发；流式读取；>5 万行转后台任务 |
| R9 | EPPlus 授权 | `ExcelPackage.License.SetNonCommercialOrganization(...)` | 商用合规风险 | 评估授权或换 ClosedXML/NPOI（Core 已引用 NPOI） |
| R10 | 月报 HTML 注入 | `PrintService`/`MonthlyReportHtmlBuilder` 拼 HTML | 在 Web 渲染时 XSS | 服务端转义；前端只渲染受控模板/PDF |
| R11 | PDF 预览依赖 WebView2 | `Controls/PdfPreviewView` + PdfJs | 浏览器不可用 | Web 用 pdf.js 动态加载；大文件分页/按需 |
| R12 | 彩票训练抢占资源 | Python + TensorFlow，1.8GB 运行时，10–40 分钟 | API/PG 抖动 | 独立容器 2G/1.5CPU；错峰；训练时降 API 堆 |
| R13 | 输出目录与用户文档耦合 | `document_output.yaml`、`OutputRootMigrationService` | Server 无“我的文档” | Agent 落本地；Server 落受控目录 |
| R14 | 数据范围只在客户端校验的风险 | `DataScopeService` 在 Core，但旧 VM 也过滤 | Web 越权 | Server 每个查询强制注入组织/村范围；不信任前端 |
| R15 | 94 个待迁移文件体量大 | 22,860 行，其中 Core 31、Archive 20、Printing 13 | 工期/回归风险 | 按 P0–P5 切批；每批 build + 冒烟；先接口后实现 |

## 6. 建议迁移批次

| 批次 | 服务 | 产出 |
|---|---|---|
| P0 | Database、Session*（改写）、CurrentUserContext 接入、Logger/DataMasker、Print 队列表迁移 | Server 可认证/会话/健康检查 |
| P1 | SocialAssistance 40、Import 24、Permission 5、System 字典/区划/标准 | 申请/收入/分类/导入可用 |
| P2 | ArchiveManagement 20、Printing 队列 7、Reporting 月报、Templates 8（Agent） | 档案/文书/打印闭环 |
| P3 | Elderly 6、TempRelief 4、Recovery 5、AssetVerification 4、Change 9、BankFlow 2、SpecialApproval 1、Duty | 全业务域 |
| P4 | Reporting 打印/公示、Import 组织导入 | 报表/公示/导入 |
| P5 | DatabaseManagement/Backup、Config、Lottery Worker、更新/桌面生命周期 | 运维/彩票/桌面 |

## 7. 核对命令

```powershell
cd D:\Projects\NewCosmosWeb
# 总量
(Get-ChildItem legacy\NewCosmos.Desktop\Services -Recurse -Filter *.cs).Count          # 263
# 已进 Core 的同名文件
$core = Get-ChildItem src\NewCosmos.Core\Services -Recurse -Filter *.cs | % Name
(Get-ChildItem legacy\NewCosmos.Desktop\Services -Recurse -Filter *.cs | ? { $_.Name -in $core }).Count
# 待迁移
(Get-ChildItem legacy\NewCosmos.Desktop\Services -Recurse -Filter *.cs | ? { $_.Name -notin $core }).Count  # 94
# 静态用户上下文
Get-ChildItem legacy\NewCosmos.Desktop\Services -Recurse -Filter *.cs |
  ? { (Get-Content $_.FullName -Raw) -cmatch 'App\.Current(User|Organization|Tenant)' } | % Name
# Office COM
Get-ChildItem legacy\NewCosmos.Desktop\Services -Recurse -Filter *.cs |
  ? { (Get-Content $_.FullName -Raw) -cmatch 'Interop|ProgID|OfficeComWorker' } | % Name
```

## 8. 验收口径

- `src/NewCosmos.Core` 不出现 `Microsoft.Maui`、`App.CurrentUser*`、`DllImport("winspool")`、`ProtectedData`。
- Server 端所有业务查询都带 `CurrentUserContext` 数据范围；跨账号打印任务无法认领。
- Agent 是唯一 Office COM 宿主；Server 进程内无 WINWORD/EXCEL 子进程。
- 94 个待迁移文件每个都有明确去向（Core / Server / Agent / Desktop / 废弃），无“悬空服务”。
