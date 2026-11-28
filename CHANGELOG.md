# Changelog

本文件是 NewCosmos Web 发布说明的单一来源。
`C:\Projects\publish-all.ps1` 发版时会按版本号抽取对应段落写入 Windows / Android 更新清单；
找不到对应版本段时回退到 `## [未发布]`。

发版前把待发内容写在 `## [未发布]` 下；发版时把标题改成实际版本号（如 `## [2.0.20261127] - 2026-11-27`）。

## [未发布]

### 新增

- 高龄老人年度认证：认证活动、认证记录、停止预览、报表导出。
- 高龄老人信息变更：变更申请、审核流转、列表查询。
- 认证报表 PDF 支持内置中文字体（Noto Sans），不依赖服务器系统字体包。
- 顶栏右上角新增「北斗终端」入口：点击用系统浏览器打开随包发布的终端序列页（`web/public/terminal/index.html`），覆盖浏览器桌面/移动、Windows 壳、Android 壳。
- 新增**客户端下载页** `https://cosmos.sua17.cn/download.html`：网页端 / Windows 桌面壳 / 打印代理 / Android 移动壳 / 独立应用端 Windows 与 Android，**每个软件一个下载按钮**；版本与地址由发布脚本生成同源 `download-data.json`（`.2` 的 `/downloads/` 无 CORS，页面跨域读不到清单），并内置兜底数据。

### 变更

- 统一发布入口改为 `C:\Projects\publish-all.ps1`；发布工具不再执行数据库 SQL。
- 发布工具支持 `.1` / `.2` 独立 SSH 密钥，并自动解析 `C:\Projects` Junction 的真实路径。
- 依赖升级：EPPlus 8.7.3、NPOI 2.8.1、Npgsql 10.0.4、SkiaSharp 3.119.4、PDFsharp 6.5.0；PDFsharp 6.5.0 起改为 AGPL/商业双许可，已按 AGPL 显式加载（`PdfSharpLicenseBootstrap`，本项目 GPL-3.0 与 AGPLv3 兼容）。
- 发布新增**回环下载校验**：Verify 步骤把刚上传的 Windows 安装包 / Android APK 从服务器拉回，与清单、本地文件三方比对 SHA256（`-SkipDownloadVerify` 可跳过）。
- Agent / Shell 改为**单文件压缩发布**（`PublishSingleFile` + `EnableCompressionInSingleFile` + `IncludeNativeLibrariesForSelfExtract`，并去掉 XML 文档），安装包体积显著下降。
- 发布菜单与计划新增「本次涉及的服务器路径」摘要，确认前即可看到会覆盖哪些主机路径。
- Vite 构建显式使用 esbuild 压缩，并移除第三方 license 注释。
- **字典字段值统一规划**：Core 新增 `DictValueMapper` 作为「字典 `item_key`（英文，落库） ↔ `item_value`（中文，展示）」的**唯一换算点**，`ApplicationFormService` 原先散落的 10 组私有 switch 全部改为委托；覆盖身体状况、残疾类型/等级、就业、婚姻、文化程度、政治面貌、民族、家庭关系、性别、户口性质、申请原因。
- 补齐漏转的展示出口：申请详情（成员身体状况/就业状况）、归档生产侧栏（申请原因/户籍类型/性别/民族/婚姻/文化程度）、档案打印文书（性别/婚姻/身体状况/申请原因/成员关系与户口），此前这些位置直接回显英文 key。
- 存量数据清洗迁移 `Migrations/007_dict_value_cleanup.sql`（已执行）：`nc_biz_applications` / `nc_biz_family_members` 的婚姻、文化程度、政治面貌 3 列共 10 处中文脏值归一为字典 key，备份表 `nc_sys_dictfix_bak_20261128`。
- **家庭关系列（`relationship_to_head`）值统一**：写路径 7 处改为落字典 key（`HouseholdHeadResolver`、`FamilyMemberService`×2、`ApplicationService`、`ChangeService.DeathAndQueries`×2 + 死亡登记写入、`ChangeService.RebuildAndReview` 死亡登记写入），杜绝新脏值；展示出口 8 处补 `DictValueMapper.ToFormRelationship`（申请详情、月报家庭关系、快速核验×2、资产核对历史、死亡证明文书×2 与搜索列表、变更审计 JSON）；比较逻辑两套值都认（`IsHeadMember`、月报与动态管理的 SQL 户主判定）。
- **家庭关系显示词表统一**：全仓只保留一份 `DictValueMapper.ToFormRelationship`（Head→本人/户主、Son→子/婿、Daughter→女/媳、Parent→父母/岳父母/公婆）。删除两份重复 switch —— `TempReliefConstants.GetRelationshipDisplayName`（临时救助 12 处调用）与 `ApplicationWorkflowQueryService.RelationshipText`（业务申请工作流列表）改为委托。同步修掉 3 处**拿显示文本比较户主**的隐患：`TempReliefPrintDataBuilder` 文书去重、`TempReliefEndpoints` 成员过滤、`TempReliefFormView` 前端户主判定，全部改为比较字典 key，避免以后换词表时静默失效。`TempReliefConstants.RelationHead` 明确为展示值并改为「本人/户主」。
- **身体状况显示词表统一到字典口径（B 方案，`nc_dict_items.HealthStatuses.item_value` 不动）**：`Healthy`/`Weak` 统一显示为「健康或良好 / 一般或较弱」。改动 `DictValueMapper.ToFormHealthStatus`（直接引用 `HealthStatusConstants.*_DISPLAY`，与 `DictDisplayHelper.GetHealthStatusDisplay`、`PageDefaultValues.HealthOptions`、`DefaultValuesConstants.HEALTH_STATUS` 同源）与前端 `HEALTH_STATUS_OPTIONS`；三处默认值（`formModel.createMember`、`Step2Members`、`CaregiverList`）同步；`normalizeHealthStatus` 保留历史短词「健康 / 一般」作别名归一。此前同一人在申请表单显示「健康」、在月报显示「健康或良好」，现为同一套词。
- 表单默认值对齐：`formModel.createHeadMember` 与 `Step2Members.ensureHouseholdHead` 原先写 `'户主'`，而 `RELATION_OPTIONS` 的 value 是 `'本人/户主'`，导致户主关系下拉选不中、同一表单两种写法并存；现统一写 `'本人/户主'`（并把历史 `'户主'` 一并归一）。
- 存量清洗迁移 `Migrations/008_relationship_cleanup.sql`（已执行）：`nc_biz_family_members` 89 行 + `nc_biz_death_records` 11 行的 `本人/户主`、`本人` 归一为 `Head`，备份表 `nc_sys_relfix_bak_20261128`。全库字典脏值由 13 类降到 2 类。
- 存量清洗迁移 `Migrations/009_other_difficulty_relationship_cleanup.sql`（已执行）：`nc_biz_other_difficulty_persons.relationship` 剩余 3 行转出台账快照（`本人`×1、`本人/户主`×2，均 `id_card = head_id_card`）归一为 `Head`，备份表 `nc_sys_odrelfix_bak_20261128`；该列现 135 行全为字典 key。90 项字典脏值探针复扫仅剩 2 类共 13 行，均为按设计保留（大学生窄选项列、月报显示文本快照）。

### 修复

- 修复从 `C:\Projects` Junction 构建 Vue 时 Vite 报 `index.html` 绝对路径的问题。
- 消除 NuGet 漏洞审计警告 NU1902/NU1903：显式 pin `System.Security.Cryptography.Pkcs` / `System.Security.Cryptography.Xml` 10.0.15、`Microsoft.Build.Tasks.Git` 10.0.404；移除 Agent 多余 PackageReference 消除 NU1510。
- **修复发布脚本漏传静态文件**：原逻辑只把 `index.html` 发到 `.1`、只把 `assets/` 发到 `.2`，导致 `download.html`、`terminal/`、`images/`、`splash/` 线上 404 或被 `.1` 的 SPA 兜底吞成 `index.html`；现改为 `.1` 收根目录所有文件、`.2` 收所有静态资源目录，并逐文件 SHA256 校验。
- **回退 Agent/Shell 单文件压缩**：`PublishSingleFile + EnableCompressionInSingleFile` 反而使安装包变大（Shell 98.6→127.1MB、Agent 49.3→61.8MB，.NET 单文件自带压缩后 lzma2 已压不动），恢复散文件夹 + Inno `lzma2/max`，仅保留 `PublishDocumentationFiles=false`。
- **消除编译警告**：删除 Office 侧重复的 `IArchivePhotoService`（CS0436 ×3，且同名类型分叉会让 Agent 未来触发 CS0433 硬错误）、`HukouType.Normalize` 参数改可空（CS8604）、`RemoteAssistanceRules.Resolve` 调用点补空值（CS8620）；Web 构建现为 **0 警告 0 错误**。
- **低收入人口认定申请「身体状况」显示英文且联动失效**：根因是表单值（中文档位）与库内 Core key（`Healthy`/`Weak`/`SevereIllness`…）两套词表，任一出口漏转就回显英文，且前端派生覆盖守卫只认中文、把英文当历史值拒绝覆盖。现前端 `form/healthStatus.ts` 新增 `normalizeHealthStatus`/`normalizeDisabilityType`/`normalizeDisabilityLevel` 归一（英文 Core key、旧字典显示值一并识别），`deriveHealthStatus` 先归一再走守卫，`formModel.normalizeForm` 在加载时对申请人/成员/赡养人/照料人统一归一；后端 `HealthStatusDeriver` 覆盖守卫改按 Core key 白名单。

---

## [2.0.20261126] - 2026-11-26

### 新增

- 纳入统一发布入口 `C:\Projects\publish-all.ps1`（构建、上传、校验、可选清理）。

### 变更

- 数据库迁移 SQL 不再随发布工具执行；Schema 改由开发期 `src/NewCosmos.Server/Migrations` 与 `Resources/Schema` 维护。

### 修复

- 版本一致性检查补充 Inno Setup 与 package-lock 根版本校验。
