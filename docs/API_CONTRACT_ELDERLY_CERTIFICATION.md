# 高龄年度认证 Web API 合同（v1）

> 2026-11-26。桌面端已按同一数据模型实现第一批；本文件是 Web Server 与 Web 前端、后续桌面端共同遵守的字段/接口合同。
> 状态 key、字段名、接口路径不得由前端自行发明；新增字段先改本文件。

## 0. 通用

- 业务 API Base：`https://cosmos-static.sua17.cn:20000`（开发环境按实际配置）
- 认证：`Authorization: Bearer <sessionId>`；设备头 `X-NC-Device`
- JSON：camelCase（ASP.NET Core Web 默认）；时间 ISO 8601
- 权限：
  - 查看：`ELDERLY_VIEW`
  - 新建批次/生成名单/导入/改结果/停发：`ELDERLY_EDIT`
- Server 必须按当前登录用户的数据范围过滤批次/任务，不信任前端 `userId/orgId`。
- 分页统一：`page` 从 1 开始，`pageSize` 默认 20，最大 100。

## 1. 状态字典

### 认证状态 `certStatus`

| key | 含义 |
|---|---|
| `Pending` | 待核查 |
| `Verified` | 已通过 |
| `Refused` | 拒绝核查 |
| `Failed` | 核查不通过 |
| `Deceased` | 已去世 |
| `MovedOut` | 已迁出 |
| `NotLocated` | 无法联系 |
| `Exempt` | 免核查 |
| `Stopped` | 其他原因已停发 |
| `Invalid` | 数据无效 |

### 认证方式 `verifyMethod`

`Face` 人脸识别 / `Onsite` 上门 / `Video` 视频 / `Photo` 照片 / `Other` 其他

### 凭证状态 `evidenceStatus`

`None` 无凭证 / `Complete` 齐全 / `Partial` 部分缺失 / `Missing` 全部缺失 / `Ambiguous` 文件名歧义

### 停发决定 `stopDecision`

`None` 未处理 / `Pending` 待停发 / `Approved` 已停发 / `Deferred` 暂缓 / `Excluded` 已排除

### 批次状态 `status`

`Draft` 草稿 / `Collecting` 收集中 / `Reviewing` 复核中 / `Closed` 已关闭

## 2. 接口清单

### 2.1 批次

#### GET `/api/elderly/certifications/campaigns`

返回当前用户可见批次，按年度/轮次倒序。

```json
[
  {
    "id": 1,
    "campaignNo": "RZ2026",
    "year": 2026,
    "round": 1,
    "name": "2026年度高龄津贴领取资格认证",
    "displayName": "2026年度高龄津贴领取资格认证",
    "periodStart": "2026-01-01",
    "periodEnd": "2026-12-31",
    "validUntil": "2026-12-31",
    "status": "Collecting",
    "statusText": "收集中",
    "requireEvidence": true,
    "deadlineStopOffset": 1,
    "rosterGeneratedAt": "2026-11-26T10:00:00",
    "closedAt": null,
    "remark": ""
  }
]
```

#### POST `/api/elderly/certifications/campaigns`

```json
{
  "year": 2026,
  "round": 1,
  "name": "2026年度高龄津贴领取资格认证",
  "periodStart": "2026-01-01",
  "periodEnd": "2026-12-31",
  "requireEvidence": true
}
```

返回 `{ "id": 1 }`。同年度同轮次重复返回 `VALIDATION_FAILED`。

#### POST `/api/elderly/certifications/campaigns/{id}/roster`

生成/刷新应认证名单快照。幂等，只补新增人员。

```json
{ "added": 123 }
```

#### GET `/api/elderly/certifications/campaigns/{id}/stats`

```json
{
  "total": 1200,
  "pending": 300,
  "verified": 850,
  "refused": 5,
  "failed": 10,
  "deceased": 8,
  "movedOut": 2,
  "notLocated": 5,
  "exempt": 20,
  "stopped": 0,
  "invalid": 0,
  "evidenceIssue": 45,
  "stopCandidate": 330,
  "completionRate": 72.5
}
```

### 2.2 名单/任务

#### GET `/api/elderly/certifications/campaigns/{id}/tasks`

Query：`page`、`pageSize`、`keyword`（姓名/身份证）、`certStatus`、`evidenceStatus`。

```json
{
  "items": [
    {
      "id": 1001,
      "campaignId": 1,
      "idCard": "110101195001011234",
      "name": "张三",
      "gender": "男",
      "birthDate": "1950-01-01",
      "ageSnapshot": 76,
      "category": "CAT2",
      "categoryText": "80-89周岁其他老年人",
      "issueAmount": 25.00,
      "applicationId": 88,
      "applicationNo": "GA20260101001",
      "sourceKind": "Application",
      "hukouVillage": "XX村",
      "hukouAddress": "XX镇XX村",
      "familyAddress": "XX小区1号",
      "phone": "138****0000",
      "certStatus": "Pending",
      "certStatusText": "待核查",
      "verifyMethod": "Face",
      "verifyMethodText": "人脸识别",
      "verifiedAt": null,
      "verifiedBy": "",
      "resultNote": "",
      "deathDate": null,
      "expectedEvidenceCount": 2,
      "evidenceCount": 2,
      "evidenceStatus": "Complete",
      "evidenceStatusText": "齐全",
      "stopDecision": "None",
      "stopDecisionText": "未处理",
      "followUpDate": null
    }
  ],
  "pageIndex": 1,
  "pageSize": 20,
  "totalCount": 1200,
  "totalPages": 60
}
```

#### GET `/api/elderly/certifications/tasks/{id}`

返回单条任务，字段同上，另含完整地址/代办人/银行等快照字段（可按需裁剪，但字段名固定）。

#### PUT `/api/elderly/certifications/tasks/{id}/result`

```json
{
  "certStatus": "Verified",
  "verifyMethod": "Face",
  "verifiedAt": "2026-11-26",
  "verifiedBy": "张三",
  "resultNote": "",
  "deathDate": null
}
```

返回 204。状态/方式非法返回 `VALIDATION_FAILED`。

#### GET `/api/elderly/certifications/tasks/{id}/evidence`

```json
[
  {
    "id": 9001,
    "taskId": 1001,
    "seq": 1,
    "evidenceType": "FACE",
    "evidenceTypeText": "人脸认证",
    "originalName": "110101195001011234_1.jpg",
    "displayName": "2026年度_XX村_张三_1234_01.jpg",
    "imageFormat": "jpg",
    "fileSize": 123456,
    "sha256": "...",
    "matchStatus": "Matched",
    "matchMethod": "ExcelName"
  }
]
```

#### GET `/api/elderly/certifications/evidence/{id}/content`

返回图片二进制，`Content-Type` 按 `imageFormat`。权限同查看；不要 base64 塞 JSON。

### 2.3 批量导入（Excel + ZIP）

采用两段式，避免 2GB ZIP 在一个请求里同步解析：

#### POST `/api/elderly/certifications/import/preview`

`multipart/form-data`：

- `excel`：`.xlsx/.xls`，≤ 20MB
- `zip`：`.zip`，单个 ≤ 200MB；当前**一次预览只接受一个 Excel + 一个 ZIP**，多 ZIP 追加接口后续再加
- `campaignId`：long

服务端：
- 校验扩展名/文件头/大小/ZIP 条目数/解压总量；
- 落临时目录 `Path.GetTempPath()/nc-elderly-cert/{previewId}`，不写 Web 根目录；
- **预览阶段只落内存会话 + 临时文件，不写 `import_batches`**；apply 成功时才由 Core `ApplyAsync` 创建 `nc_biz_elderly_cert_import_batches` 并写 `status=Applied/PartiallyApplied`；
- 预览会话 24h 过期；服务重启后 apply 返回 `PREVIEW_EXPIRED`，需重新上传；
- 解析 Excel + ZIP，返回预览。

```json
{
  "previewId": 5001,
  "campaignId": 1,
  "batchNo": "IMP20261126120000123",
  "totalRows": 1200,
  "matchedRows": 1150,
  "unknownRows": 20,
  "duplicateRows": 10,
  "nameMismatchRows": 5,
  "invalidRows": 5,
  "conflictRows": 10,
  "evidenceTotal": 2400,
  "evidenceMatched": 1200,
  "evidenceMissing": 1100,
  "evidenceUnmatched": 100,
  "summary": "共 1200 行：...",
  "rows": [
    {
      "rowNo": 3,
      "idCard": "110101195001011234",
      "name": "张三",
      "village": "XX村",
      "certStatus": "Verified",
      "certStatusText": "已通过",
      "verifyMethod": "Face",
      "verifiedAt": "2026-11-20",
      "expectedEvidenceCount": 2,
      "matchedEvidenceCount": 2,
      "evidenceStatus": "Complete",
      "evidenceStatusText": "齐全",
      "matchStatus": "Matched",
      "matchStatusText": "已匹配",
      "errorMessage": "",
      "taskId": 1001
    }
  ],
  "unmatchedEntries": [
    { "entryName": "extra/1.jpg", "fileName": "1.jpg", "length": 12345, "extension": ".jpg" }
  ],
  "warnings": [],
  "errors": []
}
```

#### POST `/api/elderly/certifications/import/apply`

```json
{
  "previewId": 5001,
  "allowAdverseOverwrite": false,
  "rowNumbers": null
}
```

- `rowNumbers` 为空表示应用全部可应用行；否则只应用指定 Excel 行号。
- 服务端校验预览文件哈希；预览过期（临时目录不存在/超过 24h）返回 `PREVIEW_EXPIRED`，前端提示重新上传。
- 返回：

```json
{
  "batchId": 5001,
  "batchNo": "IMP20261126120000123",
  "appliedRows": 1140,
  "skippedRows": 50,
  "errorRows": 10,
  "evidenceInserted": 2200,
  "evidenceSkipped": 20,
  "evidenceUnmatched": 80,
  "infoChangesCreated": 35,
  "infoChangesSkipped": 8,
  "infoChangeErrors": 1,
  "summary": "应用 1140 行，跳过 50 行，失败 10 行；...；信息变更生成 35 条，跳过 8 条，失败 1 条"
}
```

- 导入联动信息变更：Excel 额外列 `联系电话/现居住地址/社保卡开户行/社保卡账号/代办人姓名/代办人关系/代领人姓名/代领人关系/代领人开户行/代领人账号` 会与申请主表当前值比对，**只对已应用且非不利结果的行走**生成 `Pending` 变更单（`source_type=CertificationImport`，记录来源文件与行号）。
- 值未变化的行计入 `infoChangesSkipped`；申请主表缺失/未确认、银行卡号无法识别（科学计数法/非 12–30 位数字）、超长等计入 `infoChangeErrors` 并写入 `summary` 末尾，**不回滚已完成的导入**。
- 变更单仍需在“信息变更审批”逐笔审批，审批通过后才写申请主表；导入不会直接改电话/地址/银行卡。

### 2.4 停发预览（第二批）

#### GET `/api/elderly/certifications/campaigns/{id}/stop-preview`

Query：`page`、`pageSize`、`keyword`、`certStatus`。

返回任务分页，筛选 `Pending/Refused/Failed/Deceased/MovedOut/NotLocated`，并带：

- `suggestedStopReason` / `suggestedStopReasonText`
- `suggestedStopNote`（`NotLocated` 等需要备注的情形，前端预填到停发备注）
- `dueStopDate`（去世优先取死亡日期，其余按批次截止日 + `deadlineStopOffset`）
- `stopDecision`
- `applicationId`（为空表示名册未建档，前端禁用停发）

原因口径：
- `Pending / Refused / Failed / NotLocated` → `CERTIFICATION_FAIL`
- `Deceased` → `DECEASED`（预填死亡日期）
- `MovedOut` → `MOVED_OUT`

> `NotLocated` 不直接写 `NOT_LOCATED`：现有取消备案表停发原因五项里没有“查找不到本人”，统一归到 `CERTIFICATION_FAIL`，备注写“无法联系/查找不到本人”。

#### POST `/api/elderly/certifications/tasks/{id}/stop`

逐人停发（不做批量）。Web 停发预览页使用弹窗调用本接口，不再跳现有停发页；请求同现有高龄停发：

```json
{
  "stopReason": "CERTIFICATION_FAIL",
  "dueStopDate": "2026-12-31",
  "deathDate": null,
  "isRecover": false,
  "recoverStartMonth": "",
  "recoverEndMonth": "",
  "recoverAmount": 0,
  "remark": ""
}
```

服务端调用现有 `ElderlyApplicationService.StopAsync`，成功后回写 `stop_decision=Approved`、`stop_application_id`、`stopped_at`，并把认证任务 `cert_status` 置为 `Stopped`（原始认证结果仍由 `stop_reason`/`stop_note`/凭证保留）。

#### POST `/api/elderly/certifications/tasks/{id}/stop-decision`

暂缓/排除/重新进入待停发：

```json
{
  "decision": "Deferred",
  "note": "已联系家属，12月补认证",
  "followUpDate": "2026-12-15"
}
```

### 2.5 报表（Excel / PDF）

- `GET /api/elderly/certifications/campaigns/{id}/reports/{formKey}.xlsx`：下载 Excel
- `GET /api/elderly/certifications/campaigns/{id}/reports/{formKey}.pdf`：下载 PDF（Core PDFsharp 直出）
- 权限均为 `ELDERLY_VIEW`，数据范围按当前用户可见任务过滤。
- 文件名：`{campaignNo}_{formKey}_{yyyyMMddHHmmss}.{xlsx|pdf}`。
- 打印：Web 前端在新窗口打开 PDF Blob，由浏览器内置查看器打印；不走 Print Job/Agent。
- 详细列定义、口径与版式见 `API_CONTRACT_ELDERLY_CERTIFICATION_REPORT.md`。

表单 key：

- `ROSTER` 年度认证花名册
- `SUMMARY` 年度认证情况汇总表
- `UNVERIFIED` 未认证人员名单
- `INFO_CHANGE` 信息变更汇总表（后续）

## 3. 错误码

| code | 场景 |
|---|---|
| `VALIDATION_FAILED` | 参数/状态/日期不合法 |
| `NOT_FOUND` | 批次/任务不存在或不在数据范围 |
| `FORBIDDEN` | 无权限 |
| `PREVIEW_EXPIRED` | 预览临时文件不存在/过期 |
| `IMPORT_FILE_CHANGED` | 预览后 Excel/ZIP 哈希变化 |
| `CONFLICT` | 名单状态已变化，需要重新预览/人工确认 |
| `DB_QUERY_ERROR` | 数据库错误 |

## 4. Web 路由与页面

```text
/elderly/certifications                 认证批次/名单工作台
/elderly/certifications/import          批量导入向导（Excel+ZIP）
/elderly/certifications/:campaignId     批次详情/名单
/elderly/certifications/stop-preview    停发预览（第二批）
/elderly/certifications/reports         报表（Excel / PDF / 浏览器打印）
```

首页 `ElderlyHomeView.vue` 增加“年度身份认证”卡片，权限沿用 `ELDERLY_VIEW`；写操作按钮按 `ELDERLY_EDIT` 控制。

## 5. 文件/目录约定

- 上传临时目录：`Path.GetTempPath()/nc-elderly-cert/{previewId}`，24h 清理，不对外静态暴露。
- 证据图片：存 `nc_biz_elderly_cert_evidence.photo_data`，单图 ≤ 10MB，SHA256 去重。
- Schema：`src/NewCosmos.Server/Resources/Schema/elderly_certification/database.yaml`（复制自桌面权威文件）。
- 迁移：`deploy/database/20261126_elderly_certification.sql`；如启用自动迁移，放 `src/NewCosmos.Server/Migrations/002_elderly_certification.sql`。
- 前端类型：`web/src/types/elderlyCertification.ts`
- 前端 API：`web/src/api/elderlyCertification.ts`
