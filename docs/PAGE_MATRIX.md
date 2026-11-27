# 页面矩阵（PAGE_MATRIX）

> 口径：`legacy/NewCosmos.Desktop`（MAUI 原项目）只读盘点，事实以磁盘为准。
> 盘点日期：2026-11-24。
> 目标形态：Vue 3 + Element Plus，桌面与手机同一套路由 + 响应式；Office/打印只走 Windows Agent。

## 0. 事实基线

| 项目 | 数量 | 核对命令（在 `legacy/NewCosmos.Desktop` 下） |
|---|---:|---|
| 页面 XAML | 113 | `(Get-ChildItem Pages -Recurse -Filter *.xaml).Count` |
| 页面 code-behind | 113 | `(Get-ChildItem Pages -Recurse -Filter *.cs).Count` |
| ViewModel 文件 | 120 | `(Get-ChildItem ViewModels -Recurse -Filter *.cs).Count` |
| 全仓 XAML | 132 | 113 页面 + 15 `Components/` + 4 根/平台/样式 |
| Services 文件 | 263 | `(Get-ChildItem Services -Recurse -Filter *.cs).Count` |
| 移动端页面 | 33 | `Pages/Mobile/*.xaml`（不含 5 个步骤视图） |
| 移动端步骤视图 | 5 | `Pages/Mobile/SocialAssistance/FormSteps/*.xaml` |
| 共享组件 | 15 | `Components/*.xaml` |

`Pages/` 目录页面分布：

| 目录 | 页面数 | XAML 行数 | 说明 |
|---|---:|---:|---|
| SocialAssistance | 16 | 5198 | 11 个页面 + 5 个申请步骤视图 |
| Mobile | 38 | 8271 | 33 个移动页面 + 5 个移动步骤视图 |
| ElderlyBenefits | 9 | 2424 | 高龄津贴 |
| ArchiveManagement | 8 | 2254 | 档案制作/输出/查询/文书/模板 |
| ChangeManagement | 5 | 856 | 动态管理/户主/死亡/渐退 |
| DatabaseManagement | 5 | 1348 | 数据库/导入/字典/区划/标准 |
| Config | 4 | 401 | 设置/配置向导/网络/更新日志 |
| DutyManagement | 4 | 901 | 值班 |
| Lottery | 4 | 415 | 彩票 |
| AssetVerification | 4 | 771 | 经济核对 |
| UserManagement | 4 | 1546 | 用户/组织/在线会话 |
| Recovery | 3 | 866 | 追缴 |
| TempRelief | 2 | 1498 | 临时救助 |
| Auth / BankFlow / Main / Reporting / Reprint / Shared / Splash | 各 1 | — | 登录/流水/首页/月报/补打/加载/启动 |

## 1. 标注规则

**类型**：入口、列表、表单、详情、报表、打印、管理、弹窗/组件。
**Office/打印**：
- `—`：不依赖；
- `打印`：只入打印队列或调打印代理；
- `Office`：需要 Word/Excel 模板或 COM 渲染；
- `Office+打印`：模板渲染并实际打印。

**手机适配难度**：
- 低：列表/详情/单列表单，改栅格即可；
- 中：多分区表单、上传、预览、树形/穿梭；
- 高：五步表单、档案制作/输出、复杂报表、多文件导入、模板管理。

**优先级**（沿用 `docs/MIGRATION_PLAN.md` 批次）：
P0 框架/认证 → P1 社会救助核心 → P2 档案/打印 → P3 其他业务域 → P4 报表/导入 → P5 管理与系统。

**路由约定**：
- 页面入口 `https://cosmos.sua17.cn`；业务 API `https://cosmos-static.sua17.cn:20000`。
- 前缀：`/assist` 社会救助、`/temp-relief` 临时救助、`/recovery` 追缴、`/archives` 档案、`/reprint` 补打、`/reports` 报表、`/elderly` 高龄、`/asset-verification` 核对、`/changes` 变更、`/duty` 值班、`/admin` 管理、`/lottery` 彩票。
- 手机默认复用同一路由做响应式；仅采集/打印任务类使用 `/m/*` 独立路由。

## 2. 业务域汇总

| 域 | 页面 | VM 文件（域内） | 主要交付批次 | 手机对等 |
|---|---:|---:|---|---|
| 框架/认证 | 4（含启动/加载） | 7 | P0 | 是 |
| 社会救助 | 16 | 22 | P1 | 是（五步表单独立移动布局） |
| 临时救助 | 2 | 5 | P3 | 是 |
| 追缴 | 3 | 4 | P3 | 是 |
| 档案/文书/打印 | 9（含补打） | 26 | P2 | 部分（手机只入队/拍照） |
| 高龄津贴 | 9 | 12 | P3/P4 | 是 |
| 经济核对 | 4 | 5 | P3/P4 | 是（快速核对） |
| 动态管理/变更 | 5 | 5 | P3 | 是 |
| 银行流水 | 1 | 1 | P3 | 查看为主 |
| 值班/节假日 | 4 | 4 | P3/P5 | 值班表只读 |
| 报表/公示/导入 | 4 | 4 | P4 | 桌面为主 |
| 用户/组织/会话 | 4 | 4 | P5 | 否（管理类） |
| 系统配置/数据库 | 9 | 9 | P5 | 否（运维类） |
| 彩票 | 4 | 4 | P5 | 查看为主 |

> `VM 文件（域内）` 只统计 `ViewModels/<同名目录>`；`Reprint/Providers`、`Mobile` 等子目录按其父目录归属。
>
> 跨域页面按主域计数：`MonthlyPublicityPage` 计入档案、`DataImportPage` 计入数据库、`OrganizationImportPage` 计入用户；去重后合计 75 个桌面页面（另有 38 个移动页面/步骤视图，合计 113）。

## 3. 框架与认证（P0）

| 页面 | 类型 | Office/打印 | VM | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|---|
| SplashPage | 入口 | — | — | 低 | 不设路由（首屏骨架/加载态） | P0 |
| LoginPage | 表单 | — | LoginViewModel | 低 | `/login` | P0 |
| MainPage | 入口/导航 | —（含补打入口） | MainViewModel | 中 | `/home` | P0 |
| Shared/LoadingProgressDialog | 组件 | — | LoadingProgressDialogViewModel | 低 | 全局遮罩组件 | P0 |

要点：启动动画/序列登录是桌面/Android 资产（WebView + 音频），Web 不做逐帧迁移，只保留品牌色与骨架屏；登录走 `.1` 同源 `/api/auth/*`，业务 token 只在内存/Pinia，refresh 只存 `.1` Cookie。

## 4. 社会救助（P1/P3）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| SocialAssistanceHomePage | 入口 | — | 低 | `/assist` | P1 |
| ApplicationFormPage（五步容器） | 表单 | — | 高 | `/assist/applications/new`、`/assist/applications/:id/edit` | P1 |
| ApplicationFormStep1View | 表单步骤 | — | 高 | 同上（`step=1`） | P1 |
| ApplicationFormStep2View | 表单步骤 | — | 中 | 同上（`step=2`） | P1 |
| ApplicationFormStep3View | 表单步骤 | — | 高 | 同上（`step=3`） | P1 |
| ApplicationFormStep4View | 表单步骤 | — | 中 | 同上（`step=4`） | P1 |
| ApplicationFormStep5View | 表单步骤 | — | 中 | 同上（`step=5`） | P1 |
| ApplicationWorkflowPage | 列表/详情/审核 | 打印 | 中 | `/assist/applications/:id/workflow` | P1 |
| QuickVerifyPage | 表单/测算 | — | 中 | `/assist/quick-verify` | P1 |
| CapabilityAssessmentPage | 表单/评估 | — | 低 | `/assist/capability/:personId` | P3 |
| CollegeStudentManagementPage | 列表/管理 | — | 中 | `/assist/college-students` | P3 |
| GracePeriodExpiringListPage | 列表 | — | 低 | `/assist/grace-periods` | P3 |
| NearRelativeListPage | 列表 | — | 低 | `/assist/near-relatives` | P3 |
| NearRelativeEntryPage | 表单 | — | 中 | `/assist/near-relatives/new` | P3 |
| SpecialApprovalFormPage | 表单 | — | 中 | `/assist/special-approval/new` | P3 |
| SelectMembersPopup | 弹窗 | — | 低 | 家庭成员选择组件 | P1 |

补充：字典/区划/照片是 P1 依赖（见 `docs/MIGRATION_PLAN.md`），但对应管理界面可延后到 P5。

## 5. 临时救助（P3）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| TempReliefListPage | 列表/补打 | 打印 | 低 | `/temp-relief` | P3 |
| TempReliefFormPage | 表单 | 打印（保存并打印） | 高 | `/temp-relief/new`、`/temp-relief/:id/edit` | P3 |

事实：表单 XAML 949 行、VM 2115 行；分区为「0 类型与申请人 → 1 申请信息 → 2 金额（小额）与公示核实 → 3 家庭成员」；申请人只能从导入台账/低保申请库选择，不能自由录入。

## 6. 追缴（P3）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| RecoverySearchPage | 列表/搜索 | 打印 | 低 | `/recovery` | P3 |
| RecoveryManualPage | 表单 | 打印 | 中 | `/recovery/manual/:personId?` | P3 |
| RecoveryFormPage | 表单 | 打印 | 中 | `/recovery/:personId` | P3 |

事实：3 页 XAML 866 行、VM 2101 行；金额由 `RecoveryAmountCalculator` 分段计算，算不出必须人工填写并写原因（`recovery_amount` 为唯一事实来源）；打印字段由 `RecoveryPrintFieldBuilder` 构建。

## 7. 档案、文书、打印（P2）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| ArchiveProductionPage | 表单/详情（字段制作） | 间接（产出打印字段） | 高 | `/archives/production/:businessType/:id` | P2 |
| ArchiveOutputPage | 打印/预览 | Office+打印 | 高 | `/archives/output/:businessType/:id` | P2 |
| ArchiveQueryPage | 列表/详情 | 打印（补打） | 中 | `/archives/query` | P2 |
| ArchivePhotoQueryPage | 列表/详情 | —（照片导出） | 中 | `/archives/photos` | P2 |
| DeathStatementPage | 表单/文书 | 打印 | 高 | `/archives/death-statements` | P2 |
| MonthlyPublicityPage | 报表/公示 | Office（模板输出） | 中 | `/archives/publicity` | P4 |
| TemplateManagementPage | 管理 | Office（docx/xlsx 模板） | 高 | `/archives/templates` | P2 |
| ProofUnitTemplateManagePage | 管理 | Office（单位模板） | 中 | `/archives/proof-templates` | P2 |
| UnifiedReprintPage | 打印/预览 | Office+打印 | 高 | `/reprint` | P2 |

事实：档案制作 VM 4 个分片共 4774 行、输出 VM 2313 行、死亡说明 5 个分片 2050 行、补打 9 个 VM 文件 2812 行；输出页含 WebView2 PDF 预览、批量打印、照片打印入队、完成工单。

## 8. 高龄津贴（P3/P4）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| ElderlyBenefitsHomePage | 入口 | — | 低 | `/elderly` | P3 |
| ElderlyApplicationListPage | 列表 | 打印 | 低 | `/elderly/applications` | P3 |
| ElderlyApplicationFormPage | 表单 | 打印（申请表） | 高 | `/elderly/applications/new` | P3 |
| ElderlyPendingListPage | 列表 | — | 低 | `/elderly/pending` | P3 |
| ElderlyReviewListPage | 列表 | — | 低 | `/elderly/reviews` | P3 |
| ElderlyReviewPage | 表单/复核 | 打印（三表） | 中 | `/elderly/reviews/:id` | P3 |
| ElderlyStopListPage | 列表 | — | 低 | `/elderly/stops` | P3 |
| ElderlyStopPage | 表单/停发 | 打印 | 中 | `/elderly/stops/new` | P3 |
| ElderlyReportPage | 报表 | 打印 | 中 | `/elderly/report` | P4 |

事实：9 页 2424 行 XAML、12 个 VM 4706 行；含 80/90 岁年龄复核（`ElderlyApplicationService.ReviewAge90`）、停发、下月待办、补发计算（`ElderlyPaybackCalculator`）、打印数据构建（`ElderlyPrintDataBuilder`）。

## 9. 经济核对 / 资产核查（P3/P4）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| AssetVerificationHomePage | 入口 | — | 低 | `/asset-verification` | P3 |
| QuickAssetVerificationPage | 表单/核对 | —（OCR） | 高 | `/asset-verification/quick` | P3 |
| MonthlyAssetAuditPage | 列表/审核 | —（PDF 报告上传） | 中 | `/asset-verification/audit` | P3 |
| MonthlyReportPage | 报表 | —（导出） | 中 | `/asset-verification/monthly-report` | P4 |

事实：快速核对 VM 1117 行、月度审核 1083 行、月报 1135 行；月度审核按年月统计提交/有报告/已归档/驳回，并批量上传核对报告 PDF；PDF 校验走 `PdfVerificationService`（哈希 + 内容校验）。

## 10. 动态管理 / 变更（P3）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| ChangePage | 列表/管理 | 间接（跳档案输出） | 中 | `/changes` | P3 |
| HeadChangePage | 表单 | — | 中 | `/changes/head/:id` | P3 |
| HouseholdDeathChangePage | 表单 | 打印 | 中 | `/changes/death/:id` | P3 |
| GracePeriodConfirmPage | 表单 | — | 低 | `/changes/grace/:id` | P3 |
| MemberChangeReasonPopup | 弹窗 | — | 低 | 成员变更原因组件 | P3 |

> 2026-11-27：补齐桌面版变更页的**双源搜索**——当前库在享对象之外，新增
> `GET /api/social-assistance/change-management/imported-search`（6 个历史导入库检索，CHANGE_VIEW）
> 与 `POST .../imported/{sourceTable}/{sourceId}/migrate`（建档迁移，CHANGE_MANAGE，数据范围校验后调
> Core `IImportedArchiveService.MigrateToCurrentAsync`）。Web 页面在关键词搜索时把导入库记录
> **并入「在享对象」同一张列表**（状态列标"待建档/已建档"），可一键建档并进入五步表单补全，
> 与桌面版"点变更时自动建档"对齐。

## 11. 银行流水 / 值班（P3/P5）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| BankFlowViewPage | 列表/详情 | —（Excel 导入） | 中 | `/bank-flow` | P3 |
| DutySchedulePage | 报表/排班 | 打印+Office（月度值班表导出） | 中 | `/duty` | P3 |
| DutyMemberPage | 管理 | — | 低 | `/duty/members` | P5 |
| DutyAdjustPage | 表单/调整 | — | 中 | `/duty/adjust` | P5 |
| HolidayPage | 管理 | — | 低 | `/duty/holidays` | P5 |

## 12. 报表与导入（P4）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| Reporting/MonthlyReportPage | 报表/打印 | 打印+Office | 中 | `/reports/monthly` | P4 |
| ArchiveManagement/MonthlyPublicityPage | 报表/公示 | Office | 中 | `/archives/publicity` | P4 |
| DatabaseManagement/DataImportPage | 导入 | —（Excel） | 高 | `/admin/import` | P4 |
| UserManagement/OrganizationImportPage | 导入 | —（Excel） | 高 | `/admin/organizations/import` | P4 |

## 13. 用户、配置、数据库（P5）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| UserManagementPage | 管理/权限 | — | 高 | `/admin/users` | P5 |
| OrganizationPage | 管理/树 | — | 中 | `/admin/organizations` | P5 |
| OnlineSessionsPage | 管理 | — | 低 | `/admin/sessions` | P5 |
| DictionaryManagementPage | 管理 | — | 低 | `/admin/dictionaries` | P1（数据）/P5（界面） |
| RegionManagementPage | 管理 | — | 低 | `/admin/regions` | P1（数据）/P5（界面） |
| StandardConfigManagementPage | 管理 | — | 中 | `/admin/standards` | P5 |
| DatabaseManagementPage | 管理/备份恢复 | — | 高（仅桌面） | `/admin/database` | P5 |
| SettingsPage | 管理 | Office（输出根/打印设置） | 低 | `/settings` | P5 |
| ConfigWizardPage | 管理 | — | 低 | 不迁（部署脚本替代） | P5 |
| NetworkAccessPage | 管理 | — | 中 | 已迁 `/system/network-access`（Web 页面 + Agent 执行器） | P5 |
| UpdateLogPage | 管理 | — | 低 | `/settings/changelog` | P5 |

> 2026-11-27：`NetworkAccessPage` 已迁（网络接入页 `/system/network-access` + Agent `LocalOp` 执行器，
> ZeroTier 检测/安装/接入/退出走任务队列）；公文字体检查/一键安装未单独成页，并入 `/settings`
> 的 `FontInstallCard`（同样由目标设备 Agent 执行）。配置向导 `ConfigWizardPage` 仍不迁（部署脚本替代）。

## 14. 彩票（P5）

| 页面 | 类型 | Office/打印 | 手机 | 建议路由 | 优先级 |
|---|---|---|---|---|---|
| LotteryHomePage | 入口 | — | 低 | `/lottery` | P5 |
| LotteryPredictionPage | 表单/任务 | — | 低 | `/lottery/predict` | P5 |
| LotteryHistoryPage | 列表 | — | 低 | `/lottery/history` | P5 |
| LotteryResultPage | 报表/详情 | — | 低 | `/lottery/results` | P5 |

事实：4 页 415 行 XAML、4 个 VM 895 行、7 个服务 1433 行；预测通过 `PythonScriptRunner` 调用嵌入式 Python + TensorFlow LSTM，模型训练 10–40 分钟；迁移后只保留 `.2` Worker + 查询/入队 API。

## 15. 移动端 33 页归并

| 模块 | 页数 | 页面 | Web 策略 | 难度 |
|---|---:|---|---|---|
| 工作台 | 1 | MobileMainPage | 复用 `/home`，大卡片 + 底部 Tab | 中 |
| 社会救助 | 2 + 5 步 | MobileApplicationFormPage、MobileApplicationWorkflowPage、MobileApplicationFormStep1–5View | 复用 `/assist/applications/*`，`MobileStepperBar` 改 Steps 吸底 | 高 |
| 动态管理 | 5 | MobileChangePage、MobileHeadChangePage、MobileHouseholdDeathChangePage、MobileGracePeriodConfirmPage、MobileGracePeriodExpiringListPage | 复用 `/changes/*`，表单单列 | 中 |
| 高龄 | 8 | MobileElderlyBenefitsHomePage、MobileElderlyApplicationListPage、MobileElderlyApplicationFormPage、MobileElderlyPendingListPage、MobileElderlyReviewListPage、MobileElderlyReviewPage、MobileElderlyStopListPage、MobileElderlyStopPage | 复用 `/elderly/*` | 高（申请表单） |
| 临时救助 | 2 | MobileTempReliefFormPage、MobileTempReliefListPage | 复用 `/temp-relief/*` | 高 |
| 追缴 | 3 | MobileRecoverySearchPage、MobileRecoveryManualPage、MobileRecoveryFormPage | 复用 `/recovery/*` | 中 |
| 经济核对 | 3 | MobileAssetVerificationHomePage、MobileMonthlyAssetAuditPage、MobileQuickAssetVerificationPage | 复用 `/asset-verification/*`；拍照/OCR 用原生 `capture` | 高 |
| 档案/文书 | 4 | MobileArchiveQueryPage、MobileArchivePhotoPage、MobileArchiveOutputPage、MobileDeathStatementPage | 查询/照片响应式；输出只入打印队列 | 中 |
| 值班 | 1 | MobileDutySchedulePage | 复用 `/duty`，只读日历 | 低 |
| 采集/打印工具 | 4 | MobileOcrScanPage、OcrAssignPage、MobilePhotoBackfillPage、MobilePrintJobsPage | 独立 `/m/scan`、`/m/ocr-assign`、`/m/photos/backfill`、`/m/print-jobs` | 中 |

> 移动端不需要迁移 `PrintAgentService` 之类的后台轮询；手机只负责采集、入队、看状态。

## 16. 重点页面拆解

### 16.1 申请五步表单（P1，难度高）

| 项 | 事实 |
|---|---|
| 文件 | `ApplicationFormPage.xaml` 650 行 + Step1–5 共 1837 行；VM 11 个分片共 9322 行 |
| 步骤 | 1 基本信息/户籍与居住地址；2 家庭成员；3 经济状况；4 入户调查；5 分类认定与提交 |
| 模式 | 新建 + 补全、复核、家庭信息修正、编辑家庭信息、成员变更（共 6 种 `FormOperationMode`） |
| 关键逻辑 | 年值口径收入核算、分类判定、AI 建议、银行流水、OCR 分配、入户记录、分步校验、全量校验、草稿保存 |
| Web 方案 | 路由 `/assist/applications/new?step=n`；Pinia 草稿 + 服务端自动存草稿；步骤组件懒加载；第 3 步拆「收入/财产/支出/补贴」子页签；校验以服务端 `Core` 为准 |
| 风险 | 第 3 步单文件 890 行；`nc_biz_applications` 380+ 列；模式分支多；金额口径不可在 Vue 重算 |

### 16.2 档案制作中心（P2，难度高）

| 项 | 事实 |
|---|---|
| 文件 | `ArchiveProductionPage.xaml` 270 行；VM 4 分片 4774 行 |
| 功能 | 按业务来源（申请/核对/临时救助/变更等）装载数据；构建模板字段；右侧信息边栏；文书分类；批量归档；跳转输出 |
| 依赖 | `ITemplateService`（模板元数据）、`IAssetVerificationService`、`IChangeService`、字典/机构/地址 |
| Web 方案 | `/archives/production/:businessType/:id`；字段字典接口 + 虚拟滚动侧栏；输出页拆分「模板选择 / PDF 预览 / 打印入队」 |
| 风险 | 字段来源跨 11 个服务；字段口径必须与模板系统一致；不要在前端拼 Word |

### 16.3 档案输出与补打（P2，难度高）

| 项 | 事实 |
|---|---|
| 文件 | `ArchiveOutputPage.xaml` 269 行 + VM 3 分片 2313 行；`UnifiedReprintPage.xaml` 481 行 + VM 9 文件 2812 行 |
| 功能 | 模板集勾选、单份/批量渲染、PDF 预览、保存文件、打印、照片打印、完成工单；补打支持跨域按人聚合 + 批量打印 |
| 依赖 | Office COM、PDFsharp 合并、WebView2 PDF 预览、打印机 |
| Web 方案 | 浏览器只做「选择 + 预览 + 入队 + 轮询」；渲染/合并/打印全部由 Agent 执行；补打路由 `/reprint?tab=person`（或 `batch`） |
| 风险 | 批量 100+ 份的进度与失败重试；PDF 预览流量；打印任务归属（见打印重点页） |

### 16.4 月报表（P4，难度中高）

| 项 | 事实 |
|---|---|
| 文件 | `MonthlyReportPage.xaml` 239 行；VM 1135 行；`PrintService` 2 文件（含 PDF 合并） |
| 功能 | 年月/乡镇筛选、汇总卡片、表单勾选列表（替代原 14 个按钮）、会议记录、生成历史、预览/打印/导出 |
| 分类 | 城乡低保、低保边缘、特困、刚性支出、其他困难（`MonthlyReportCategoryConstants`） |
| Web 方案 | `/reports/monthly`；服务端生成 HTML/PDF（或 Office 模板交给 Agent）；前端只做参数、预览、下载、打印入队 |
| 风险 | 打印与预览必须同源；跨月口径与 `BusinessTimeline` 扣减；大批量 PDF 合并内存 |

### 16.5 高龄津贴（P3，难度高）

| 项 | 事实 |
|---|---|
| 文件 | 9 页 2424 行；12 个 VM 4706 行；表单 2556 行 |
| 功能 | 申请登记、复核（80/90 岁重评、停旧增新、打印三表）、停发、下月待办、月报、补发计算 |
| Web 方案 | 申请表单复用分步容器；复核/停发做成“列表 + 抽屉/详情”；打印统一入队 |
| 风险 | 年龄边界与补发金额规则；复核会生成新档案并停旧档案；三表打印字段多 |

### 16.6 临时救助（P3，难度高）

| 项 | 事实 |
|---|---|
| 文件 | 2 页 1498 行（表单 949 行）；5 个 VM 2583 行（表单 2115 行） |
| 功能 | 急难型/支出型、申请人选择（台账/低保库）、小额金额、公示核实、家庭成员、候选排序（`TempReliefCandidateRanker`）、保存并打印 |
| Web 方案 | `/temp-relief/new` 单页 4 分区；金额与公示日期由服务端计算/校验；打印入队 |
| 风险 | 申请人来源受限，不能自由输入；公示日期与金额联动；单据模板多 |

### 16.7 追缴（P3，难度中）

| 项 | 事实 |
|---|---|
| 文件 | 3 页 866 行；VM 4 个 2101 行 |
| 功能 | 搜索已停止人员、手工录入、分段计算应追缴金额、历史时间轴、保存并打印 |
| Web 方案 | `/recovery` 列表 + `/recovery/:personId` 表单；金额计算只调 `Core` 接口；打印入队 |
| 风险 | `recovery_amount` 唯一事实来源；算不出必须人工填写原因；历史分段取数跨表 |

### 16.8 资产核查（P3/P4，难度高）

| 项 | 事实 |
|---|---|
| 文件 | 4 页 771 行；5 个 VM 2714 行 |
| 功能 | 快速核对（身份证 OCR + 经济测算）、月度审核（报告 PDF 上传/复核/批量）、核对月报、首页 |
| Web 方案 | 快速核对表单 + 拍照上传；月度审核用表格 + 批量上传；PDF 校验在服务端 |
| 风险 | OCR 单槽串行（客户端超时 360s、单张约 30s、大图可达 2 分钟）；PDF 报告哈希校验；批量上传内存；手机相机方向/压缩 |

### 16.9 数据导入（P4，难度高）

| 项 | 事实 |
|---|---|
| 文件 | `DataImportPage.xaml` 262 行；VM 648 行；`Services/Import` 24 文件 4899 行（已全部进 Core） |
| 导入类型 | 农村低保、城市低保、低保边缘、刚性支出、特困、其他困难（6 类组合导入）+ 高龄补贴、年度种植补贴、土地承包、银行流水核对 |
| 功能 | 选择类型、多文件匹配、模板下载、逐行校验、批次结果、失败明细 |
| Web 方案 | `/admin/import` 上传 → 服务端后台任务 → 进度/结果轮询；大文件直传 `.2`，不经过 `.1` |
| 风险 | EPPlus 非商业授权；多文件命名匹配；10 万行级内存；导入失败回滚与重试 |

### 16.10 备份与恢复（P5，难度高，仅桌面/运维）

| 项 | 事实 |
|---|---|
| 文件 | `DatabaseManagementPage.xaml` 371 行；VM 660 行；`DatabaseBackupService`、`DatabaseManagementService` |
| 功能 | 库状态/表大小、备份文件列表、`pg_dump` 备份、`psql` 恢复（DROP SCHEMA public CASCADE 后重放）、日志清理、权限 `SYSTEM_BACKUP` |
| Web 方案 | `/admin/database` 只做只读监控 + 触发异步备份；恢复必须维护模式 + 二次确认；下载走 `.2` 受限路径 |
| 风险 | 恢复是破坏性操作；备份含照片 bytea，体积大；绝不允许 `.1` 转发备份文件 |

### 16.11 彩票（P5，难度低，独立 Worker）

| 项 | 事实 |
|---|---|
| 文件 | 4 页 415 行；4 VM 895 行；`Services/Lottery` 7 文件 1433 行 |
| 功能 | 同步历史开奖、机选、LSTM 预测、训练模型、历史/中奖结果 |
| 依赖 | 嵌入式 Python + TensorFlow；模型数据在用户目录；训练 10–40 分钟 |
| Web 方案 | `/lottery` 只做查询/触发；`.2` Lottery Worker（2G/1.5 CPU）+ 任务表；训练错峰 |
| 风险 | 训练占满 CPU/内存；GPL-3.0 组件；与民政业务无耦合，最低优先级 |

## 17. 组件复用（15 个）

| 组件 | Web 对应 |
|---|---|
| BackButton / PageHeaderView | 布局头部 + 返回 |
| StepFlowBar / MobileStepperBar | `el-steps`（桌面横向 / 手机吸底） |
| PaginationBarView | `el-pagination`（服务端分页） |
| LoadingOverlay / ProgressPopupView | 全局 loading + 任务进度 |
| SnackBarView | `ElMessage` |
| FamilyMemberCardView / IncomeRowView | 业务卡片组件 |
| DateTimeEntryView / DateTimePickerPopupView | `el-date-picker` |
| SignaturePopupView | 签名画布组件（保存 PNG/WebP） |
| SelectMembersPopup | `el-dialog` + 成员表格 |
| FeatureCard / StatCapsule | 首页卡片 / 指标胶囊 |

## 18. 迁移顺序建议（页面视角）

1. **P0**：登录、主框架、首页、全局 loading；先打通 `.1` 登录 + `.2` Bearer。
2. **P1**：五步表单 + 工作流 + 档案查询详情 + 快速核验；同步落地区划/字典只读接口。
3. **P2**：档案制作/输出/补打 + 模板管理 + 打印任务页；Agent 先支持一条 Word → PDF → 打印。
4. **P3**：高龄、临时救助、追缴、资产核查、变更、银行流水、值班。
5. **P4**：月报、公示、各类导入。
6. **P5**：用户/组织/会话、配置、数据库/备份、彩票。

## 19. 验收口径

- 113 个页面全部有归属（本文件 75 个桌面页面 + 38 个移动页面/步骤视图），无遗漏。
- 每个 P0–P2 页面在 Web 上有对应路由，手机宽度 375px 可用。
- 所有 `Office`/`打印` 页面在 Web 上不直接调用 COM；只通过 Agent 队列。
- 页面清单变动时同步更新本文件，并在 PR 说明中给出 `Pages` 目录 diff。
