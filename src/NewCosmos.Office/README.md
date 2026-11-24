# NewCosmos.Office

Windows 固定 PC 上的 Office/打印核心库。服务端（Linux）**不**运行 Office COM；
Word/Excel 模板填充、PDF 导出、双面设置与打印队列执行都集中在本项目，由
`NewCosmos.Agent` 调用。

- 目标框架：`net10.0-windows`
- 项目引用：`..\NewCosmos.Core\NewCosmos.Core.csproj`
- 命名空间保持 `NewCosmos.*`（`NewCosmos.Services.Templates`、`NewCosmos.Services.Domain.Printing` 等）
- 源码来源于 `legacy/NewCosmos.Desktop`，按 Web 架构裁剪；不复制 ArchiveManagement 全量

## 1. 构建

```powershell
dotnet build src\NewCosmos.Office\NewCosmos.Office.csproj
```

依赖的 EPPlus / NPOI / PDFsharp / Serilog 通过 `NewCosmos.Core` 的项目引用传递，无需在本项目重复声明版本。

## 2. 目录

```text
Templates/
  ITemplateEngine.cs          模板引擎契约 + TemplateConfig/TemplateFieldMapping
  TemplateEngineHelpers.cs    临时文件/分块工具
  TemplateFieldBuilder.cs     RawFieldData → 统一字段字典（地址/成员/编号字段）
  TemplateFieldResolver.cs    indexShifts 索引槽投影
  OfficeComWorker.cs          全局唯一 STA 线程 + Excel/Word 应用池 + 超时/进程跟踪
  WordEngine.cs               Word COM：填充/表格/签名图/保存/PDF/打印
  ExcelEngine.cs              EPPlus 填充 + Office COM 导出 PDF/打印
  TemplateEngineFactory.cs    按 docx/xlsx 创建引擎（可选按 templateId 拉取）
Utilities/
  OfficeProviderDetector.cs   MS Office / WPS ProgID 探测、残留进程清理
  OfficeProviderInfo.cs       探测结果
  PrinterDuplexHelper.cs      DEVMODE.dmDuplex 双面设置（失败降级单面）
Printing/
  IPrintQueueService.cs / PrintQueueService.cs         nc_biz_print_jobs 入队/认领/回写
  IPrintExecuteService.cs / PrintExecuteService.cs     模板 → 源文件 → 打印/PDF → 留痕
  IPrintJobFactory.cs / PrintJobFactory.cs             推送打印任务构建（模板解析/字段快照）
  IPrintRecordService.cs / PrintRecordService.cs       nc_biz_print_records 留痕
  IPhotoPrintService.cs / PhotoPrintService.cs         照片直接打印（Word 排版 → 打印）
  IPrintAgentService.cs / PrintAgentService.cs         PC 代理轮询认领（Windows）
  PhotoPrintPushHelper.cs                              照片推送打印共用逻辑
Helpers/
  TemplateCopiesResolver.cs   模板份数唯一解析入口（TemplateConfig.ResolveCopies）
ArchiveManagement/
  ITemplateService.cs         只读模板契约（GetById/GetFileData/GetByCategories）
  IArchivePhotoService.cs     只读照片契约（按人查/按 Id 取字节）
Reporting/
  IPrintService.cs            预览 PDF 生成所需的最小契约
```

> `ArchiveManagement` / `Reporting` 下只有**接口裁剪**，没有数据库实现。
> 需要库外提供实现（服务端 API 客户端、DB 仓储或测试替身）后再装配打印编排服务。

## 3. 主要 API

### 模板渲染（最常用）

```csharp
using NewCosmos.Models.Options;
using NewCosmos.Services.Templates;

var storage = new StorageOptions
{
    ApplicationName = "NewCosmos",
    UseAppDataDirectory = true,
    LogDirectoryName = "Logs",
    BackupDirectoryName = "Backup",
    TempDirectoryName = "Temp"
};
var perf = new PerformanceOptions { /* 超时/重试字段，见 AgentRuntime */ };

using var engine = new WordEngine(logger, perf, storage);
engine.LoadFromFile(@"C:\templates\低保审核审批表.docx");
engine.ReplaceFields(new Dictionary<string, string>
{
    ["{户主姓名}"] = "张三",
    ["{家庭住址}"] = "XX镇XX村"
});
engine.ReplaceTableByPlaceholder("{成员姓名}", "{成员姓名}", rows);
var pdf = await engine.ExportPdfAsync();
```

- `WordEngine`/`ExcelEngine` 的 COM 调用全部封送到 `OfficeComWorker` 的 STA 线程串行执行；
  Word/Excel 应用实例池化复用，空闲 5 分钟自动退出，单次 COM 操作默认 120 秒超时。
- `TemplateEngineFactory.CreateFromTemplateIdAsync` 需要外部提供 `ITemplateService`（服务端/API 客户端）。
- `TemplateFieldBuilder` 依赖 `AddressResolver`、`IDictCacheService`、`ITemplateService`、`IBusinessTimelineService`；
  适合在能拿到 Core 服务实现的宿主中构建字段，不由 Office 库自行查库。

### Office 探测

```csharp
var info = OfficeProviderDetector.Detect();
// info.Provider: None / WPS / Office / Both
// info.HasWord / info.HasExcel / info.WordProgId / info.ExcelProgId
```

`WordEngine` / `ExcelEngine` 的打印与 PDF 路径使用 `OfficeProviderDetector.GetWordProgId()` /
`GetExcelProgId()`，**只接受 Microsoft Office 的 ProgID**；WPS 虽然能被探测到，但不在本链路支持范围。

### 打印领域

- `PrintQueueService` / `PrintRecordService`：直接使用 Core 的 `IDatabaseService`（PostgreSQL），
  与 Web/服务端共享同一张 `nc_biz_print_jobs` / `nc_biz_print_records`。
- `PrintExecuteService`：模板填充 → 保存源文件 → 打印 → 导出 PDF → 写留痕。
  需要 `IPrintService`、`ITemplateService`、`ITemplateEngineFactory`、`IPrintRecordService`、
  `INewPermissionService`、`ISignatureService`、`IArchivePhotoService` 注入。
- `PrintAgentService`：Windows 上每 5 秒认领 Pending 任务；当前 Agent P0 **尚未**启动它
  （服务端任务认领 API 仍未落地）。
- `PhotoPrintService`：照片（WebP/JPEG/PNG）→ JPEG → Word 空白文档排版 → 复用打印管线。

## 4. Windows 运行要求

1. **Microsoft Office 桌面版**
   - Office 2016 / 2019 / 2021 / Microsoft 365 均可；必须是与 Agent 进程位数匹配的桌面版（推荐 64 位）。
   - 需要 Word + Excel；仅装 WPS 不够（本链路不走 WPS ProgID）。
   - 首次运行建议手动启动一次 Word/Excel，完成激活/首启向导，避免 COM 启动时弹窗卡住。
   - 不要安装“Microsoft Store 版 Office”的纯 UWP 变体；需要传统 Win32 COM 自动化支持。
2. **运行账户**
   - 必须在固定 PC 的**交互式登录用户**下运行（不是 Windows 服务/Session 0）。
   - 该账户需要对模板/输出目录有读写权限，并能访问本机打印队列。
   - 同一台 PC 不要多个 Agent 实例同时跑（`OfficeComWorker` 是进程内单例，多进程会争抢 Office）。
3. **字体**
   - 公文模板通常依赖：`仿宋_GB2312`、`方正小标宋简体`、`黑体`、`楷体_GB2312`、`宋体`。
   - 缺字体时 Office 会替换字体，导致分页/盖章位置漂移；打印前用 Word 打开模板确认无替换提示。
   - 字体安装到**运行 Agent 的 Windows 账户**（当前用户字体或本机字体均可）。
4. **打印机**
   - 安装厂商驱动（不要只用 “Microsoft Print to PDF” 代替真实打印机做验收）。
   - 确认默认打印机、纸张尺寸（A4）、双面能力；`PrinterDuplexHelper` 设置失败会降级为打印机当前设置。
   - 网络打印机需保证运行账户能访问打印服务器；离线/暂停队列会导致打印任务堆积。
5. **Office 自动化稳定性**
   - 关闭 Office 的“受保护的视图/首次启动向导/登录提示”，或由管理员用组策略预配置。
   - `OfficeComWorker` 只终止自己跟踪的 PID；启动时会清理“无主窗口且超过 30 分钟”的残留 Office 进程，
     不会误杀用户自己打开的文档。

## 5. 账户绑定（重要）

- 打印任务按 `user_id + device_id + printer_name` 绑定；任务入队时固化目标，只有对应 Agent 能认领。
- `NewCosmos.Agent` 的 `deviceKey` 默认用 **DPAPI CurrentUser** 加密，因此：
  - 换 Windows 登录账户后必须重新 `--register`；
  - 复制 `agent.json` 到另一台机器/另一个账户无法解密，这是预期行为。
- 打印队列、Office COM、字体、默认打印机都属于**当前 Windows 账户**；
  用 A 账户注册、用 B 账户运行会表现为“能心跳但打印到错误队列/找不到打印机”。
- 建议固定 PC 使用专用的 `NewCosmosAgent` 本地账户自动登录，并只给该账户安装所需打印机。

## 6. 手工验证清单（Office/打印）

- [ ] `dotnet build src\NewCosmos.Office\NewCosmos.Office.csproj` 0 错误。
- [ ] `NewCosmos.Agent.exe --probe-office` 显示 Word/Excel 可用。
- [ ] `--render` 用真实 `.docx` 模板（含 `{占位符}`）生成 PDF，中文/表格/分页与 Word 手工打印一致。
- [ ] 用 A4 真实打印机打印一份，确认份数、单/双面、纸张、边距。
- [ ] 模板含签名/照片占位符时，确认图片内嵌且未拉伸。
- [ ] 打印任务失败时，`nc_biz_print_records.remark` / `nc_biz_print_jobs.error_message` 有明确原因。
- [ ] 连续打印 10+ 份后任务管理器无残留 WINWORD/EXCEL 进程。
