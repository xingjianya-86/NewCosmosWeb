# 高龄年度认证·信息变更合同（v1）

> 2026-11-26。认证/导入过程中发现电话、地址、银行卡、代办人变化时，不直接覆盖申请主表；
> 先落变更单，审批通过后更新 `nc_biz_elderly_applications`，保留旧值、来源、审批人。

## 1. 允许变更的字段

| 字段 key | 中文 | 风险 | 说明 |
|---|---|---|---|
| `phone` | 联系电话 | LOW | 可直接审批 |
| `family_address` | 家庭住址 | LOW | 现居住地址，不改变户籍资格 |
| `detail_address` | 家庭详细地址 | LOW | 门牌号 |
| `bank_name` | 社保卡开户行 | HIGH | 需凭证/复核 |
| `bank_account` | 社保卡账号 | HIGH | 需凭证/复核；记录生效月 |
| `agent_name` | 代办人姓名 | HIGH | 需审批 |
| `agent_relation` | 代办人关系 | HIGH | 需审批 |
| `agent_receive_name` | 代领人姓名 | HIGH | 需审批 |
| `agent_receive_relation` | 代领人关系 | HIGH | 需审批 |
| `agent_receive_bank_name` | 代领人开户行 | HIGH | 需审批 |
| `agent_receive_bank_account` | 代领人账号 | HIGH | 需审批 |

不允许直接改：
- `name` / `id_card`：身份信息不一致走人工核实；身份证/户籍变化走停旧建新或迁出流程。
- `hukou_*`：户籍变化可能影响资格，不能当普通信息更新。
- `category` / `identity_flag`：走高龄复核（`nc_biz_elderly_reviews`）。

## 2. 数据表

### `nc_biz_elderly_info_change_records`

```text
id BIGSERIAL
change_no VARCHAR(40) NOT NULL UNIQUE
campaign_id BIGINT NULL
task_id BIGINT NULL
application_id BIGINT NULL
id_card VARCHAR(18) NOT NULL
name VARCHAR(50) NOT NULL
change_type VARCHAR(20) NOT NULL   -- LOW / HIGH
reason VARCHAR(500)
status VARCHAR(20) NOT NULL DEFAULT 'Pending' -- Pending/Approved/Rejected/Cancelled
source_type VARCHAR(30)            -- Manual/CertificationImport/Certification
source_file VARCHAR(200)
source_row INT
effective_month VARCHAR(7)         -- 银行卡等生效月，可空
applied_at TIMESTAMP
applied_by VARCHAR(50)
reviewed_at TIMESTAMP
reviewed_by VARCHAR(50)
review_opinion VARCHAR(500)
created_at TIMESTAMP DEFAULT NOW()
created_by VARCHAR(50)
updated_at TIMESTAMP DEFAULT NOW()
deleted_at TIMESTAMP
```

### `nc_biz_elderly_info_change_details`

```text
id BIGSERIAL
change_id BIGINT NOT NULL
field_key VARCHAR(50) NOT NULL
field_label VARCHAR(100)
old_value TEXT
new_value TEXT
created_at TIMESTAMP DEFAULT NOW()
```

一次提交多个字段共用同一个 `change_no`。

索引：
- `records (id_card)`
- `records (campaign_id, status)`
- `records (application_id)`
- `details (change_id)`

## 3. 状态

| status | 含义 |
|---|---|
| `Pending` | 待审批 |
| `Approved` | 已通过并写入申请主表 |
| `Rejected` | 已驳回 |
| `Cancelled` | 被新提交覆盖/作废 |

## 4. 审批规则

1. 只有 `nc_biz_elderly_applications.status = Confirmed` 且未删除的档案可写；名册未建档人员先补建档。
2. 审批事务内：
   - `SELECT ... FOR UPDATE` 锁申请行；
   - 校验当前值仍等于 `old_value`（防止并发覆盖）；
   - 更新申请主表字段；
   - 变更单置 `Approved`，写 `applied_at/applied_by/reviewed_at/reviewed_by`；
   - 银行/代办人变更写安全日志（脱敏旧/新值）。
3. 银行卡变更默认 `effective_month = 下个月`，审批人可改；电话/地址立即生效。
4. 同人同字段已有 `Pending` 时，新提交覆盖旧单：旧单置 `Cancelled`，新单保留完整 old/new。
5. 已停发/已去世任务不允许再建信息变更；先处理停发。
6. 已通过变更后，认证任务快照不回溯改；报表如需“当前信息”，另按申请主表读取。

## 5. Web API

### GET `/api/elderly/certifications/tasks/{taskId}/info-changes`

权限 `ELDERLY_VIEW`；返回该任务/身份证的变更单（含 details）。

### GET `/api/elderly/certifications/info-changes?status=Pending&page=&pageSize=&keyword=`

权限 `ELDERLY_VIEW`；数据范围按任务/身份证过滤。

### POST `/api/elderly/certifications/tasks/{taskId}/info-changes`

权限 `ELDERLY_EDIT`：

```json
{
  "reason": "老人更换社保卡",
  "sourceType": "Manual",
  "effectiveMonth": "2026-12",
  "details": [
    { "fieldKey": "bank_account", "oldValue": "6222...", "newValue": "6228..." }
  ]
}
```

服务端校验字段白名单、风险级别、旧值（可空则取当前值）、不能改身份字段。

### POST `/api/elderly/certifications/info-changes/{id}/approve`

```json
{ "reviewOpinion": "已核对新卡照片", "effectiveMonth": "2026-12" }
```

### POST `/api/elderly/certifications/info-changes/{id}/reject`

```json
{ "reviewOpinion": "新卡号与照片不一致" }
```

## 6. 桌面端

- 认证名单页/任务行增加「信息变更」入口。
- 变更弹窗：显示当前值，填写新值 + 原因；高风险字段提示需复核。
- 独立「信息变更待审批」页：Pending 列表、查看 old/new、通过/驳回、备注。
- 审批通过后刷新任务/申请档案；不自动重打文书，后续可接“变更备案表”。

## 7. 导入联动（第二阶段）

- 腾讯 Excel 中如带 `联系电话/现居住地址/新社保卡号/新开户行/新代办人`，导入预览时生成 `Pending` 变更单，而不是直接改主表。
- 导入结果摘要增加“生成信息变更 N 条”。
- 本阶段先做手工变更 + 审批，导入联动后补。
