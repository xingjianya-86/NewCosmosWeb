# legacy 测试迁移说明

迁移日期：2026-11-24
来源：`legacy/NewCosmos.Desktop/NewCosmos.Tests`（xUnit，目标框架 MAUI/Windows）
目标：`tests/NewCosmos.Core.Tests`（xUnit，`net10.0`，引用 `src/NewCosmos.Core`）

## 结果概览

| 项目 | 数量 |
| --- | --- |
| legacy 测试文件 | 48 |
| 已迁移测试文件 | 45 |
| 整文件跳过 | 3 |
| 文件内跳过用例 | 5 个方法 |
| legacy 测试方法（`[Fact]` + `[Theory]`） | 428 |
| 已迁移测试方法 | 385（336 `[Fact]` + 49 `[Theory]`） |
| 已迁移展开用例 | 586 |
| 本轮 `dotnet test` 总计 | 588（含既有 `CurrentUserContextTests` 2 例） |
| 通过 / 失败 / xUnit 跳过 | 588 / 0 / 0 |

迁移时仅做两类机械调整：

1. 命名空间统一为 `NewCosmos.Core.Tests`（原 `NewCosmos.Tests.*` 子命名空间一并收敛）。
2. 对依赖文件系统或 Desktop 宿主资源的用例，删除测试方法并在原位置留注释（见下）。

所有已迁移用例的断言、边界值和测试数据保持原样，未修改 `src/NewCosmos.Core`。

## 整文件跳过

| legacy 文件 | 跳过用例数 | 原因 |
| --- | ---: | --- |
| `Core/ConfigServiceIniMergeTests.cs` | 8 | Core 只有 `IConfigService` 接口，没有 Desktop 的 `ConfigService.MergeMissingIniKeys` 静态自愈实现（`Services/Core/ConfigService.cs` 未迁移）。属于“Core 缺类型”，非 MAUI/DB/UI 依赖。 |
| `Domain/DeathDeclareValidatorTests.cs` | 24 | Core 缺 `NewCosmos.Services.Domain.ArchiveManagement.DeathDeclareValidator` 及其 `Input` 模型（死亡申报校验器整体尚未迁移）。属于“Core 缺类型”。 |
| `Helpers/OutputPathHelperDirectoryTests.cs` | 7 | 直接调用 `OutputPathHelper.Configure` / `GetCategoryDirectory` / `GetPeriodDirectory`，会创建、删除真实临时目录，并断言 `Directory.Exists`；按任务要求不迁移文件系统测试。`OutputPathHelper` 本身在 Core 中存在，但静态 API 与磁盘强耦合。 |

## 文件内跳过用例

| legacy 文件 | 已迁移 | 跳过用例 | 原因 |
| --- | ---: | --- | --- |
| `Helpers/ChangelogMarkdownParserTests.cs` | 5 | `Parse_真实CHANGELOG结构_能解析出已发布版本` | 从 `AppContext.BaseDirectory` 逐级向上遍历磁盘查找仓库根 `CHANGELOG.md`。 |
| 同上 |  | `随包内置CHANGELOG资源_存在且可解析` | 依赖 Desktop 宿主程序集 `UpdateHistoryService` 及嵌入资源 `NewCosmos.CHANGELOG.md`，Core 无该类型/资源。 |
| `Helpers/FontFamilyReaderTests.cs` | 5 个方法 / 8 个用例 | `ReadFamilyNames_坏文件_回退文件名不抛异常` | 写入并删除临时字体文件。 |
| 同上 |  | `ReadFamilyNames_真实字体文件_能解析族名` | 读取系统字体目录 `simsun.ttc`。 |
| `Helpers/SevereDiseaseJudgeTests.cs` | 其余全部 + `真实清单文件_可加载且含关键业务裁定`（2026-11-27 恢复） | 0 | 已恢复：测试工程以 `None ... Link="Resources\Icd10\severe_disease_catalog.json" CopyToOutputDirectory` 复制随包清单到输出目录（见下方第 4 条）。 |

## 未迁移原因分类说明

- **MAUI / UI**：legacy 测试集中没有直接依赖 MAUI 控件、页面或 ViewModel 的用例；本轮未因 UI 跳过。
- **数据库**：没有用例连接真实 PostgreSQL；涉及 DB 的服务逻辑均通过纯函数/常量/快照测试覆盖，或本身已被拆为静态纯逻辑（如 `RigidExpenditureRule`、`RecoveryAmountCalculator`）。
- **HTTP**：`NominatimGeocoder`、`HolidayApiParser` 测试只覆盖 URL 组装与响应解析，均为纯逻辑，已迁移；没有发起真实网络请求。
- **文件系统 / 宿主资源**：见上表，均为明确跳过，未强行改写。

## Core 需要后续修的点

1. **迁移 `ConfigService.MergeMissingIniKeys`**（Desktop `Services/Core/ConfigService.cs`）到 Core，或抽成独立纯静态辅助类；补齐后可直接启用 `ConfigServiceIniMergeTests` 的 8 个用例。
2. **迁移 `DeathDeclareValidator`**（含 `Input` 模型）到 `Services/Domain/ArchiveManagement`；这是 24 个申报前置校验用例无法迁移的唯一原因。
3. **`ChangelogMarkdownParser` 资源宿主**：Core 只有解析器，`UpdateHistoryService` 与随包 `CHANGELOG.md` 嵌入资源仍在 Desktop。若希望覆盖“随包资源可解析”，需要在 Core 侧明确资源归属（或把资源复制逻辑放到测试项目并由测试工程显式链接）。
4. **重病清单测试资源**：✅ 已完成（2026-11-27）——测试项目以 `None`/`Link` + `CopyToOutputDirectory` 方式复制 `Resources/Icd10/severe_disease_catalog.json` 到输出目录，“真实清单 ≥80 条 + 业务裁定”防回归用例已恢复，Core 未改。
5. **`OutputPathHelper` 可测性**：当前静态类直接 `Directory.CreateDirectory`。若要覆盖目录 API，建议抽出纯路径组合函数（输入输出根参数化），磁盘断言留在集成测试。
6. **`FontFamilyReader` 文件重载**：文件回退与系统字体用例属于文件系统/环境依赖，建议保留为 Desktop 集成测试，或在 Core 侧提供可注入的文件读取抽象。
