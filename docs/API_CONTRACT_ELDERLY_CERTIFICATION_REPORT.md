# 高龄年度认证报表合同（v2，Excel + PDF）

> 2026-11-27。报表支持 **Excel（.xlsx）与 PDF（.pdf）**；PDF 由 Core 的 PDFsharp 渲染器直出，
> 不依赖 Office COM/Agent。桌面端与 Web 共用同一套列定义、行拼装与 PDF 排版；
> Web Server 只负责数据范围过滤、文件生成与下载。

## 1. 报表 key

| key | 名称 | 数据范围 |
|---|---|---|
| `ROSTER` | 年度认证花名册 | 批次全部任务 |
| `SUMMARY` | 年度认证情况汇总表 | 按乡镇/村聚合 |
| `UNVERIFIED` | 未认证人员名单 | `Pending/Refused/Failed/NotLocated` 且未停发 |

后续（信息变更做完再加）：

| key | 名称 |
|---|---|
| `INFO_CHANGE` | 信息变更汇总表 |

## 2. 花名册 `ROSTER`

列：

```text
序号 | 姓名 | 身份证号 | 性别 | 出生日期 | 年龄
享受类别 | 月标准（元） | 户籍地址 | 家庭住址 | 联系电话
认证方式 | 认证日期 | 认证结果 | 凭证状态 | 停发决定 | 备注
```

排序：乡镇/村、姓名、身份证。

说明：
- 身份证号按导出权限返回完整值；Excel 只落输出目录/下载，不进日志。
- 月标准为空显示空；金额两位小数。
- 停发决定用 `stop_decision` 中文名（未处理/待停发/已停发/暂缓/已排除）。

## 3. 汇总表 `SUMMARY`

列：

```text
乡镇 | 村社区 | 应认证人数
已通过 | 免核查 | 待核查 | 拒绝核查 | 核查不通过
已去世 | 已迁出 | 无法联系 | 已停发
凭证异常人数 | 认证率
```

口径：
- 应认证 = 该村任务总数
- 已认证 = Verified + Exempt
- 认证率 = (Verified + Exempt) / 总人数，保留 1 位小数
- 凭证异常 = `EvidenceStatus in (Missing, Partial, Ambiguous)`
- 已停发 = `cert_status = Stopped`
- 按乡镇、村社区排序；最后一行合计。

## 4. 未认证名单 `UNVERIFIED`

列：

```text
序号 | 姓名 | 身份证号 | 性别 | 年龄 | 乡镇 | 村社区
联系电话 | 认证状态 | 凭证状态 | 建议停发原因
应停日期 | 停发决定 | 跟进日期 | 备注
```

筛选：`cert_status in (Pending, Refused, Failed, NotLocated)` 且 `cert_status <> Stopped`。
排除：`stop_decision = Approved`。

## 5. Web API

### GET `/api/elderly/certifications/campaigns/{id}/reports/{formKey}.xlsx`

- 权限：`ELDERLY_VIEW`
- 数据范围：按当前用户可见任务过滤；汇总表只汇总范围内任务。
- 返回：`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
- `Content-Disposition: attachment; filename*=UTF-8''<encoded>.xlsx`
- 文件名：`{campaignNo}_{formKey}_{yyyyMMddHHmmss}.xlsx`
- 未知 formKey → 400 `VALIDATION_FAILED`
- 无数据 → 仍返回带表头的空表（前端可提示“无数据”）
- 不要用 JSON/base64 返回 Excel。

### GET `/api/elderly/certifications/campaigns/{id}/reports/{formKey}.pdf`

- 权限与数据范围同上；不新增权限码。
- 返回：`application/pdf`
- 文件名：`{campaignNo}_{formKey}_{yyyyMMddHHmmss}.pdf`
- 版式：A4 横向、每页重复标题/表头、自动换行分页、页脚页码；格式同 Excel（金额两位小数、日期 `yyyy-MM-dd`、认证率一位小数）。
- 未知 formKey → 400；生成失败（如中文字体缺失）→ `DOCUMENT_GENERATION_FAILED`，不返回空白 PDF。
- 无数据 → 仍返回一页“本报表无数据”的 PDF。
- 打印：前端在新窗口打开 PDF Blob，由浏览器 PDF 查看器打印；多份报表请分别导出或由桌面端合并打印。

## 6. 桌面端

- 页面：`Pages/ElderlyBenefits/ElderlyCertificationReportPage`
- ViewModel：`ElderlyCertificationReportViewModel`
- 选择批次 + 勾选报表 → 导出 `.xlsx` / `.pdf` 到：
  `OutputPathHelper.GetPeriodDirectory(OutputCategories.ElderlyCertification, campaign.Year.ToString())`
- 文件名同上；导出成功提示路径，可打开输出目录。
- “打印 PDF”：勾选的多份报表合并为一份 PDF（每份新起一页），优先调用系统 PDF 关联的 `print` 动词；未注册（Win32 1155）时打开默认阅读器由人工打印。

## 7. 共用 Core

- `ElderlyCertificationReportFormKeys`：ROSTER/SUMMARY/UNVERIFIED
- `ElderlyCertificationReportService`：查数据、拼列/行（桌面与 Web 共用）
- `ElderlyCertificationExcelExporter`：用 EPPlus 生成 xlsx（Core 已引用 EPPlus 8.6.3）
- `ElderlyCertificationPdfRenderer`：用 PDFsharp 直出 PDF（Core 已引用 PDFsharp 6.1.1）
- `ChinesePdfFontResolver`：中文字体查找/注册；Web Server 内置 `Resources/Fonts/notosansregular.ttf`
- Web Server 如需数据范围过滤，先按 scoped SQL 取任务，再调用共享导出器；不要在 Web 端重写列定义。
