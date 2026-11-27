# Schema 对账报告（NewCosmos Web ↔ RustCosmos）

> 对账日期：2026-11-24。只读对账，未改任何代码/数据库。
> 权威口径：`legacy/NewCosmos.Desktop/Resources/Schema/**/*.yaml`（下称 YAML）。
> 现状基线：`D:\Projects\RustCosmosCode\backups\RustCosmos_backup_20261124_063400.sql`（下称“备份库”）。
> Rust 增量：`D:\Projects\RustCosmosCode\deploy\sql\*.sql`（仅 7 个文件，无全量 DDL）。
> NewCosmos Web 增量：`src/NewCosmos.Server/Migrations/001_printer_binding.sql`。

## 0. 结论（先看这 5 条）

1. **P0 阻塞**：YAML 的 `nc_sys_users.position` 在备份库中不存在，而 Web 的 `UserService` 新建/编辑用户会写该列，会直接报 `42703 column does not exist`。登录查询未显式引用该列，但用户管理写入必失败。同型缺列还有 `nc_biz_caregivers/guardians/supporters/near_relative_staffs.position`。
2. **P1 阻塞**：`nc_biz_applications` 有 4 列、`nc_biz_family_members` 有 3 列在**备份库已存在且 Web 代码实际读写**，但 YAML 未收录：`chain_type`、`medical_self_paid_12m`、`total_annual_income`、`per_capita_annual_income`；`person_type`、`monthly_income_capacity`、`medical_self_paid_12m`。必须回写 YAML，否则“YAML 权威”与运行库继续分叉。
3. **Rust 已改库、YAML 未收**：临时救助 11 列、一事一议申报表 3 列 + 1 个部分唯一索引、照片临时池 1 列 + 允许身份证为空。前两组备份库已生效，照片池/OCR 尚未生效。三项都属于**必须合并**。
4. **打印是最大协议冲突**：Rust 用 `Printing`，YAML/Web 用 `Processing`；Rust 按 `agent_machine=device_id` 定向认领，Web 的 `ClaimPendingAsync` 不按目标过滤；设备注册还有 Rust `nc_biz_print_agents` 与 Web `nc_sys_print_devices + nc_sys_printer_bindings` 两套表。共库运行会互相抢任务，必须二选一。
5. **土地字典同名不同义**：`LandUsage`（耕地/园地/林地/草地/其他）与 Rust 新增 `LandUseTypes`（自耕/转包/承包）都落在 `land_usage` 列；实际数据已出现 `自行种植/自行耕种/转包他人/转包经营/承包土地/承包经营` 六种写法。必须先定标准与别名，**禁止批量改历史值**。

---

## 1. 对账口径

| 来源 | 磁盘事实 | 说明 |
|---|---|---|
| YAML | 133 张去重表 | 权威结构；但已落后于实际库（见 §5.1） |
| Rust `deploy/sql` | 7 个增量脚本：3 个建表、3 个加列、1 个字典数据 | 无全量 schema，不改旧表定义 |
| 备份库 | 195 个 `public` 表，其中 51 个 `backup_*`/`*_bak_*` 临时表 | 用于判断“迁移是否已执行” |
| NewCosmos Web | 仅 1 个迁移（打印设备/绑定） | `src/NewCosmos.Server/README.md:121` 明确 `ISchemaSyncService` 为 no-op，缺表/缺列不会自动补 |

**判断规则**：YAML 是权威；Rust/Web 新增对象不得删除；实际库已有但 YAML 没有的，回写 YAML；YAML 有但实际库没有的，补 DDL。

---

## 2. 按域总览（仅 NewCosmos 有定义 / Rust 无自有 DDL）

| 域 | 代表表 | 数量 | Rust 关系 |
|---|---|---:|---|
| 用户/权限 | `nc_sys_users`、`nc_sys_roles`、`nc_sys_organizations`、`nc_sys_permissions`、`nc_sys_user_roles`、`nc_perm_*` | 8 | 读写；无 DDL |
| 会话 | `nc_sys_user_sessions` | 1 | 读写；列完全一致 |
| 申请/家庭经济 | `nc_biz_applications`、`nc_biz_family_members`、收入/财产/土地/供养/渐退期等 | 30+ | 读写；无 DDL |
| 档案/照片/打印 | `nc_biz_archives`、`nc_biz_archive_files`、`nc_biz_archive_photos`、`nc_biz_print_records`、`nc_biz_print_jobs`、`nc_biz_templates` | 6 | 读写；照片/打印有差异 |
| 临时救助 | `nc_biz_temp_relief_applications` + members/diseases/accidents/educations | 5 | 读写；Rust 加 11 列 |
| 一事一议 | `nc_biz_special_approvals`、`nc_biz_special_approval_forms` | 2 | 读写；Rust 加 3 列 |
| 低保/特困/边缘/城乡/其他困难/刚性支出 | `nc_biz_low_income_*`、`nc_biz_destitute_*`、`nc_biz_*_subsistence_*`、`nc_biz_other_difficulty_*`、`nc_biz_rigid_expenditure_*` | 13 | 部分读写 |
| 高龄/大学生/死亡/变更/资产/银行流水/月报/值班/近亲属/签名/模板/配置/地区/字典/日志/AI | 对应 `Resources/Schema/<域>/` | 60+ | 部分读写；个别列 YAML 落后 |
| 彩票 | `nc_lottery_draws`、`nc_lottery_user_purchases` 等 4 张 | 4 | Rust 不建表；Web Worker 读 draws/purchases |

> 完整 133 表清单以 `Resources/Schema/**/*.yaml` 去重为准；本报告只展开有差异的域。

---

## 3. 仅 Rust 有 DDL（YAML 无）

| 对象 | 关键列 | 备份库 | 处置 | 说明/建议 |
|---|---|---|---|---|
| `nc_biz_import_batches` | 17 列：`import_type/mode/file_hash/status/family_imported/...` | 已存在 | **可忽略（保留）** | 只存文件名/哈希/计数，无 PII；Web 导入暂不写。建议补 YAML 或标注为“Rust 审计表”。 |
| `nc_biz_print_agents` | 10 列：`device_id/device_name/secret_hash/user_id/status/last_seen_at/bound_at` | 已存在 | **需人工确认** | 与 Web 的 `nc_sys_print_devices`/`nc_sys_printer_bindings` 功能重叠；保留表本身，但设备主数据只能选一套。`user_id bigint` 无外键，`users.id` 为 integer。 |
| `nc_biz_ocr_jobs` | 14 列：`status/category/photo_id/image_data bytea/attempts/result jsonb/...` | **未创建** | **可忽略（Web 不用）** | Web 走远程 OCR（`RemoteOcrRecognizeClient`），无队列表。若 Rust OCR 仍上线再执行 `deploy/sql/20261124_ocr_jobs.sql`；`image_data` 属敏感数据，Rust 仅保留 1 天。 |

---

## 4. 仅 NewCosmos Web 有 DDL（YAML/Rust 无）

| 对象 | 关键列 | 备份库 | 处置 | 说明/建议 |
|---|---|---|---|---|
| `nc_sys_print_devices` | `id/device_key uuid/machine_name/agent_version/printers_json/last_seen_at/...` | **未创建** | **必须合并** | 来源 `src/NewCosmos.Server/Migrations/001_printer_binding.sql`。P0 打印绑定依赖，上线前必须执行。 |
| `nc_sys_printer_bindings` | `id/user_id/device_id/printer_name/is_default/...` | **未创建** | **必须合并** | 唯一键 `(user_id, device_id, printer_name)`；每账号最多一个默认打印机。Web AGENTS 铁律 6 的落点。 |
| `nc_sys_print_devices.is_active` | 无 | — | **可忽略（后续）** | Web README 已注明缺 `is_active`，当前心跳只能判断“存在”。 |
| `nc_sys_ai_judgments` | `input_key varchar(300) PK/verdict/matched_item/confidence real/reason text/source/policy_version varchar(16)/model/created_at/updated_at` | **未创建** | **必须合并** | 来源 `src/NewCosmos.Server/Migrations/005_ai_judgment_cache.sql`（抄桌面 `docs/migrations/20261112_ai_judgment_cache.sql`，幂等）。AI 重病判定 LLM 结果缓存，`AiJudgmentCacheService` 读写；缺表时判定降级走服务端 LLM，不阻断业务。 |

> 这两张表应补进 YAML（建议放 `Resources/Schema/printing/`），否则 YAML 无法作为完整权威。

---

## 5. 两边都有，但列/约束不同

### 5.1 备份库已有、YAML 缺列（必须回写 YAML）

| 表 | 备份库有、YAML 无 | 备份库列数 / YAML 列数 | 处置 | 影响 |
|---|---|---|---:|---|
| `nc_biz_applications` | `chain_type`、`medical_self_paid_12m`、`total_annual_income`、`per_capita_annual_income` | 98 / 94 | **必须合并** | Web P1 读写这 4 列；`ApplicationColumns.cs` 与年值口径依赖 |
| `nc_biz_family_members` | `person_type`、`monthly_income_capacity`、`medical_self_paid_12m` | 55 / 52 | **必须合并** | Web `FamilyMemberService` 插入/更新直接用 |
| `nc_biz_temp_relief_applications` | `relief_person_count`、`standard_tier`、`standard_multiplier`、`accepted_at`、`investigation_done_at`、`county_confirmed_at/by`、`filing_status`、`reject_reason`、`reject_notified_at`、`reject_notice_method` | 70 / 59 | **必须合并** | Rust 政策迁移已生效；Web 暂未读写，但 YAML 必须收口 |
| `nc_biz_special_approval_forms` | `case_type`、`submitted_at`、`filed_at` | 19 / 16 | **必须合并** | Rust 流程依赖；唯一索引已存在 |
| `nc_biz_archives` | `related_archive_id`、`relation_type`、`relation_description` | 13 / 10 | **需人工确认** | 备份库有；Web/legacy 磁盘未找到读写 SQL，疑似历史预留。先补 YAML，不动库 |
| `nc_biz_household_surveys` | `application_reason`、`application_reason_detail`、`survey_conclusion` | 14 / 11 | **必须合并** | Web 变更/月报查询使用 `survey_conclusion` |
| `nc_biz_change_records` | `new_application_id` | 23 / 22 | **必须合并** | 迁移 `20260911_add_change_records_new_application_id.sql` |
| `nc_biz_templates` | `sort_order` | 11 / 10 | **可忽略（补 YAML 即可）** | P2 模板排序 |
| `nc_biz_asset_report_history` | `period_type`、`week_start`、`week_end`、`week_number` | 15 / 11 | **可忽略（补 YAML 即可）** | P3 资产报告周维度 |
| `nc_lottery_draws` / `_predictions` / `_user_purchases` / `_statistics` | YAML 无表；仅 `legacy/.../Scripts/Lottery/create_tables.sql` | 11/16/14/13 | **必须合并（文档层）** | 建表 SQL 与备份库一致；`predictions/statistics` 已无代码读写，保留不 DROP |
| `nc_biz_recovery_records` / `_segments` | YAML 无表；来源 `docs/migrations/20261019_recovery_amount_source.sql` | 21/10 | **必须合并（文档层）** | P3 追缴依赖；当前 `Schema` 目录未覆盖 |
| `nc_sys_migrations` | YAML 无表 | 4 | **可忽略** | 迁移登记表，非业务 |

### 5.2 YAML 有、备份库缺列（必须补 DDL）

| 表 | 缺失列 | 处置 | 影响 |
|---|---|---|---|
| `nc_sys_users` | `position varchar(100)` | **必须合并（P0 前）** | Web 新建/编辑用户写 `position`；缺列即失败 |
| `nc_biz_caregivers` | `position varchar(100)` | **必须合并** | Web 照料人新增/编辑写 `position` |
| `nc_biz_near_relative_staffs` | `position varchar(100)` | **必须合并** | 近亲属经办人新增/查询写 `position` |
| `nc_biz_guardians` | `position varchar(100)` | **必须合并（低风险）** | YAML 权威要求；Web 当前未写 |
| `nc_biz_supporters` | `position varchar(100)` | **必须合并（低风险）** | 同上 |

### 5.3 Rust 待执行、备份库未生效

| 对象 | DDL | 备份库 | 处置 | 说明 |
|---|---|---|---|---|
| `nc_biz_archive_photos.temp_token` | `uuid` + 部分索引 | 无此列 | **必须合并** | `deploy/sql/20261124_photo_temp_pool.sql`；同时 `applicant_id_card DROP NOT NULL` |
| `nc_biz_archive_photos.applicant_id_card` | YAML `NOT NULL`；Rust 改为可空 | 当前 `NOT NULL` | **需人工确认** | 临时池绑定前无身份证；Web 现有查询按身份证过滤，不会列出临时照片，但需确认没有代码假设非空 |
| `nc_biz_ocr_jobs` | 建表 | 无此表 | **可忽略（按需）** | 仅 Rust OCR 队列使用 |
| `nc_sys_print_devices` / `nc_sys_printer_bindings` | 建表 | 无表 | **必须合并** | Web P0 打印绑定 |

### 5.4 已对齐（无需动作）

| 表 | 结论 |
|---|---|
| `nc_sys_user_sessions` | 16 列与 YAML 完全一致；备份库状态值 `active/replaced/kicked/logout/expired` 与 Rust/YAML 一致 |
| `nc_biz_print_jobs` | 28 列与 YAML 完全一致（含 `photo_*`、`job_kind`） |
| `nc_biz_print_records` | 与 YAML 一致（含 2026-10 漂移列） |
| 权限 8 表 | 列与 YAML 一致；`nc_perm_user_permissions`/`nc_perm_cache_version` 的 PK 只在拆分 YAML 中声明，见 §6.9 |
| `nc_config_standards`、`nc_regions_*`、`nc_dict_*` 结构 | 列一致；土地字典“数据/命名”冲突见 §6.5 |

---

## 6. 冲突与命名不一致

| # | 主题 | 冲突事实 | 处置 | 建议 |
|---|---|---|---|---|
| 6.1 | 打印任务状态 | Rust `JOB_PRINTING="Printing"`；YAML/Web 常量均为 `Pending/Processing/Completed/Failed` | **必须合并** | 统一为 `Processing`；备份库共 190 行（189 Completed / 1 Failed），无 Printing 存量，切换窗口安全 |
| 6.2 | 打印任务认领 | Rust：`agent_machine=device_id` 且只认领本机；Web：`ClaimPendingAsync` 不按目标过滤，认领后才写 `agent_machine` | **必须合并** | 采用“入队即固化目标”的 Rust 语义；Web 认领增加 `agent_machine`/绑定用户过滤；否则 Web Agent 会抢走 Rust 中继任务 |
| 6.3 | 打印设备主数据 | Rust `nc_biz_print_agents`（device_id + secret + user）；Web `nc_sys_print_devices` + `nc_sys_printer_bindings`（device_key + 每账户每打印机） | **需人工确认** | 建议 Web 表为 Web 端唯一权威；Rust 表仅供 Rust 客户端过渡，禁止双写。任务表只保留一个认领协议 |
| 6.4 | 打印业务枚举 | Rust 写 `business_type='PrintRelay'`、`job_kind in relay_render/relay_print/relay_pdf`；Web `PrintBusinessTypes`/`PrintJobConstants` 无这些值 | **需人工确认** | 要么补 Web 常量与展示，要么 Web 列表按 `business_type` 过滤中继任务 |
| 6.5 | 土地字典 | `LandUsage`（耕地/园地/林地/草地/其他）与 `LandUseTypes`（SelfFarm/Sublease/Contract）混用；实际 `land_usage` 值有 6 种写法 | **必须合并 + 需人工确认** | 不合并 category；把 Rust 新类重命名为经营方式（建议 `LandOperationTypes`）；别名补 `承包土地`（`转包经营` 已有）；`income_write` 的 CASE 认全 6 种历史写法 |
| 6.6 | 临时救助状态 | YAML/legacy 仅 `Draft/Confirmed/Stopped`；Rust 另写 `Submitted/Returned/Rejected` | **必须合并** | YAML 注释/常量补全；Web 读取未知状态时映射为“处理中”，禁止直接显示英文码 |
| 6.7 | 一事一议状态 | YAML/legacy 仅 `Draft/Submitted/Approved/Rejected`；Rust 另写 `Returned/NotPassed/Filed/ReturnedForCorrection` | **必须合并** | 同上；`Filed` 是备案终态，不能落到 legacy 的 `Approved` |
| 6.8 | 字典项 description 语义 | YAML 定义 `description=字典项说明`；Rust 用它存 `nc_config_standards.standard_type` 键 | **需人工确认** | 建议新增独立列或改用 `nc_dict_items` 的保留字段；短期在文档标注 |
| 6.9 | YAML 内部重复定义 | 17 张表在两个文件重复（如 `social_assistance/*.yaml` 与 `family_economy/database.yaml`；权限拆分文件另补 PK） | **需人工确认** | 不影响运行，但会让对账/补列误报；建议收口为单文件 |
| 6.10 | 主键序列 | YAML 中 `nc_sys_users/roles/organizations` 的 `id integer` 未声明 SERIAL；备份库实际有序列 | **需人工确认** | 用 `pg_get_serial_sequence` 核对；新建空库时必须补序列，否则 Web 插入失败 |

### 6.5 土地字典证据（备份库实测）

| 表/来源 | 值分布 |
|---|---|
| `nc_biz_land_registrations.land_usage` | 自行耕种 29、转包他人 7、转包经营 1 |
| `nc_biz_land_confirmation_records.land_usage` | 转包他人 215、自行种植 12、自行耕种 5、承包土地 2、承包经营 2 |
| NewCosmos 常量 | `自行种植 / 转包他人 / 承包土地`（`Constants/DictionaryConstants.cs`） |
| Rust 标准 | `自行耕种 / 转包他人 / 承包经营`；别名缺 `承包土地` |
| `nc_dict_items` 现状 | `LandUsage` 仅 5 条（耕地/园地/林地/草地/其他）；无 `LandUseTypes`/`LandUseTypeAliases` |

> 结论：`LandUsage` 更像“土地类型”，不应作为 `land_usage` 经营方式的字典。新增字典必须先命名清楚，再做别名归一。

---

## 7. NewCosmos Web P0/P1 依赖表字段核对

| 优先级 | 表 | 字段是否齐全 | 缺口/风险 | 动作 |
|---|---|---|---|---|
| P0 | `nc_sys_users` | **不齐全** | 备份库缺 `position`；登录查询未显式引用该列，但新建/编辑用户会写该列并失败 | 上线前 `ADD COLUMN IF NOT EXISTS position varchar(100)` |
| P0 | `nc_sys_organizations` | 齐全 | `province_id` 已在 YAML/库；唯一索引 `ux_nc_sys_organizations_name` 存在 | 无需改；先查重名再建唯一索引 |
| P0 | 权限 8 表 | 齐全 | 列一致；拆分 YAML 才含 `nc_perm_user_permissions`/`nc_perm_cache_version` 的 PK | 以实际库约束为准，补 YAML |
| P0 | `nc_sys_user_sessions` | 齐全 | 16 列一致；无缺列 | 无需改 |
| P0 | `nc_sys_print_devices` / `nc_sys_printer_bindings` | **表不存在** | 备份库未执行 001 | 上线前执行 `001_printer_binding.sql` |
| P1 | `nc_sys_ai_judgments` | **表不存在** | 备份库未执行 005（AI 重病判定缓存） | 上线前执行 `005_ai_judgment_cache.sql`；缺表只降级为每次走服务端 LLM |
| P0/P2 | `nc_biz_print_jobs` | 结构齐全 | 状态值/认领协议冲突；`PrintRelay` 枚举 Web 不识别 | 统一 `Processing` + 定向认领；补 Web 常量 |
| P1 | `nc_biz_applications` | **YAML 不齐全 / 库齐全** | 4 列只在库/YAML 之外：`chain_type`、`medical_self_paid_12m`、`total_annual_income`、`per_capita_annual_income` | 回写 YAML；库用 `IF NOT EXISTS` 兜底 |
| P1 | `nc_biz_family_members` | **YAML 不齐全 / 库齐全** | 3 列：`person_type`、`monthly_income_capacity`、`medical_self_paid_12m` | 回写 YAML；库用 `IF NOT EXISTS` 兜底 |
| P1 | `nc_biz_archives` / `nc_biz_archive_files` | 基本齐全 | YAML 缺 `related_archive_id/relation_type/relation_description`（库有，代码未用） | 补 YAML；不动库 |
| P1 | `nc_biz_archive_photos` | **有未决差异** | `temp_token` 未建；`applicant_id_card` YAML/库 NOT NULL，Rust 要求可空 | 执行照片池迁移；确认 Web 查询能容忍空身份证 |
| P2 | `nc_biz_print_records` | 齐全 | 列与 YAML 一致 | 无需改 |
| P1 | `nc_dict_*` / `nc_config_standards` | 结构齐全 | 土地字典分类/取值冲突；Rust 字典分类行未建 | 见 §6.5；补分类行与别名 |
| P1 | `nc_regions_*` | 齐全 | `province_id`、经纬度已在 YAML/库 | 无需改 |
| P5 | `nc_lottery_*` | 结构齐全（非 YAML） | 仅 legacy SQL 定义 | 补 YAML；Worker 只依赖 draws/purchases |

---

## 8. 建议迁移 SQL（按顺序，全部可重复执行）

> 目标：先补 P0/P1 缺列，再补 Rust/Web 新对象；所有 DDL 均为可空或带默认值，不改旧数据。

```sql
BEGIN;

-- 1) P0：Web 用户/照料人/近亲属写 position（YAML 有、备份库无）
ALTER TABLE nc_sys_users                ADD COLUMN IF NOT EXISTS position varchar(100);
ALTER TABLE nc_biz_caregivers            ADD COLUMN IF NOT EXISTS position varchar(100);
ALTER TABLE nc_biz_guardians             ADD COLUMN IF NOT EXISTS position varchar(100);
ALTER TABLE nc_biz_supporters            ADD COLUMN IF NOT EXISTS position varchar(100);
ALTER TABLE nc_biz_near_relative_staffs  ADD COLUMN IF NOT EXISTS position varchar(100);

-- 2) P1：Web 代码实际读写的申请/家庭成员列（备份库已有，空库兜底）
ALTER TABLE nc_biz_applications
    ADD COLUMN IF NOT EXISTS chain_type varchar(30),
    ADD COLUMN IF NOT EXISTS medical_self_paid_12m numeric(18,2),
    ADD COLUMN IF NOT EXISTS total_annual_income numeric(12,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS per_capita_annual_income numeric(12,2) NOT NULL DEFAULT 0;

ALTER TABLE nc_biz_family_members
    ADD COLUMN IF NOT EXISTS person_type varchar(50),
    ADD COLUMN IF NOT EXISTS monthly_income_capacity numeric(18,2) DEFAULT 0,
    ADD COLUMN IF NOT EXISTS medical_self_paid_12m numeric(18,2);

-- 3) 照片临时池（Rust；当前备份库未执行）
ALTER TABLE nc_biz_archive_photos ADD COLUMN IF NOT EXISTS temp_token uuid;
ALTER TABLE nc_biz_archive_photos ALTER COLUMN applicant_id_card DROP NOT NULL;
CREATE INDEX IF NOT EXISTS idx_nc_biz_archive_photos_temp_token
    ON nc_biz_archive_photos (temp_token) WHERE temp_token IS NOT NULL;

COMMIT;
```

按文件执行（不要手工重抄）：

| 顺序 | 文件 | 说明 |
|---:|---|---|
| 1 | `deploy/sql/20261123_temp_relief_policy.sql` | 已生效则幂等跳过 |
| 2 | `deploy/sql/20261123_special_approval_policy.sql` | 唯一索引已存在，先查重 |
| 3 | `deploy/sql/20261124_land_use_dict.sql` | 只加字典数据；执行前补 `承包土地` 别名（`转包经营` 已有） |
| 4 | `deploy/sql/20261124_photo_temp_pool.sql` | 当前未执行 |
| 5 | `deploy/sql/20261124_ocr_jobs.sql` | 仅 Rust OCR 需要；Web 可跳过 |
| 6 | `src/NewCosmos.Server/Migrations/001_printer_binding.sql` | Web P0 必须 |
| 7 | `deploy/sql/20261123_import_batches.sql` / `20261123_print_agents.sql` | 已存在则跳过 |

打印状态归一（在 Rust/Web 代码都改为 `Processing` 之后执行）：

```sql
UPDATE nc_biz_print_jobs SET status = 'Processing' WHERE status = 'Printing';
CREATE INDEX IF NOT EXISTS idx_print_jobs_agent_pending
    ON nc_biz_print_jobs (agent_machine, created_at)
    WHERE status = 'Pending' AND deleted_at IS NULL;
```

---

## 9. 上线前 schema 检查清单（先备份 → 再迁移 → 再验证）

### 9.1 执行顺序

1. 公告停写/低峰窗口；确认 Rust 服务是否仍在线（决定 OCR/打印代理策略）。
2. **备份**：`pg_dump -Fc --no-owner --no-acl`，记录大小 + SHA256；至少保留一周。
3. **只读预检**：重复数据、状态分布、缺列、序列、照片空身份证。
4. **补 DDL**：§8 第 1–7 步；先 P0 用户 `position`，再 P1 申请/家庭，再 Rust/Web 新对象。
5. **回写 YAML**：把 §5.1/§5.2/§5.3 的列与表补进 `Resources/Schema`，提交评审。
6. **数据归一**：打印状态切换；土地字典只加别名，不批量改历史值。
7. **验证**：列/索引/约束/行数/状态分布全部符合预期。
8. **部署应用 + 冒烟**：登录、建用户、组织、会话互踢、申请保存、照片上传/绑定、打印一张。
9. **观察/回滚**：DDL 均为增量，通常无需回滚；异常时停服务按备份恢复。

### 9.2 备份

```powershell
$env:PGPASSWORD = $env:NEWCOSMOS_DB_PASSWORD
& 'C:\pgsql\bin\pg_dump.exe' -h <host> -p 5432 -U new_cosmos -d new_cosmos `
  -Fc --no-owner --no-acl -f "C:\backup\new_cosmos_20261124_before_web.dump"
Get-FileHash "C:\backup\new_cosmos_20261124_before_web.dump" -Algorithm SHA256
```

### 9.3 预检 SQL（只读）

```sql
-- 9.3.1 一事一议重复（建唯一索引前必须为 0 行）
SELECT application_id, count(*)
FROM nc_biz_special_approval_forms
WHERE deleted_at IS NULL
GROUP BY application_id HAVING count(*) > 1;

-- 9.3.2 打印/临时救助状态分布
SELECT status, count(*) FROM nc_biz_print_jobs GROUP BY 1 ORDER BY 2 DESC;
SELECT status, count(*) FROM nc_biz_temp_relief_applications GROUP BY 1 ORDER BY 2 DESC;

-- 9.3.3 土地取值分布（只统计，不改）
SELECT land_usage, count(*) FROM nc_biz_land_registrations GROUP BY 1 ORDER BY 2 DESC;
SELECT land_usage, count(*) FROM nc_biz_land_confirmation_records GROUP BY 1 ORDER BY 2 DESC;

-- 9.3.4 照片空身份证（DROP NOT NULL 前后都应可解释）
SELECT count(*) FROM nc_biz_archive_photos WHERE applicant_id_card IS NULL;

-- 9.3.5 缺失列核对（迁移前会列出缺列；迁移后应返回 0 行）
SELECT * FROM (VALUES
  ('nc_sys_users','position'),
  ('nc_biz_applications','chain_type'),
  ('nc_biz_applications','medical_self_paid_12m'),
  ('nc_biz_applications','total_annual_income'),
  ('nc_biz_applications','per_capita_annual_income'),
  ('nc_biz_family_members','person_type'),
  ('nc_biz_family_members','monthly_income_capacity'),
  ('nc_biz_family_members','medical_self_paid_12m')
) AS e(t,c)
WHERE NOT EXISTS (
  SELECT 1 FROM information_schema.columns x
  WHERE x.table_schema='public'
    AND x.table_name::text = e.t
    AND x.column_name::text = e.c);

-- 9.3.6 序列与 max(id) 对账（示例）
SELECT last_value, (SELECT max(id) FROM nc_sys_users) AS max_id FROM nc_sys_users_id_seq;
SELECT last_value, (SELECT max(id) FROM nc_sys_organizations) AS max_id FROM nc_sys_organizations_id_seq;
```

### 9.4 迁移后验证

```sql
-- 关键列应全部存在
SELECT table_name, column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_schema='public' AND (
  (table_name='nc_sys_users' AND column_name='position') OR
  (table_name='nc_biz_applications' AND column_name IN
     ('chain_type','medical_self_paid_12m','total_annual_income','per_capita_annual_income')) OR
  (table_name='nc_biz_family_members' AND column_name IN
     ('person_type','monthly_income_capacity','medical_self_paid_12m')) OR
  (table_name='nc_biz_archive_photos' AND column_name IN ('temp_token','applicant_id_card')) OR
  (table_name='nc_biz_temp_relief_applications' AND column_name IN
     ('relief_person_count','standard_tier','standard_multiplier','accepted_at',
      'investigation_done_at','county_confirmed_at','county_confirmed_by',
      'filing_status','reject_reason','reject_notified_at','reject_notice_method')) OR
  (table_name='nc_biz_special_approval_forms' AND column_name IN ('case_type','submitted_at','filed_at'))
);

SELECT indexname FROM pg_indexes
WHERE schemaname='public' AND indexname IN (
  'ux_special_approval_forms_application',
  'idx_nc_biz_archive_photos_temp_token',
  'ux_nc_sys_printer_bindings_default',
  'uq_nc_sys_printer_bindings_user_device_printer'
);
```

### 9.5 冒烟（必须真实走）

- 登录 / 心跳 / 互踢 / 登出；
- 新建用户（含 `position`）→ 编辑 → 停用；
- 新建组织（含 `province_id`）；
- P1 申请保存/读取 4 个年值/医保列；家庭成员保存 3 列；
- 照片上传 → 临时池（若启用）→ 绑定身份证 → 按申请查询；
- 打印设备绑定 → 入队 → Agent 认领 → 回传 Completed。

### 9.6 回滚

| 层 | 方式 |
|---|---|
| 数据库 | 停服务 → 用备份 `pg_restore --clean --if-exists`；或只回滚本次新增列（需人工确认无业务写入） |
| 应用 | 回退上一版本二进制/镜像 |
| 数据 | 打印状态/土地值归一前先留 CSV 快照；禁止无备份批量 UPDATE |

---

## 10. 未在磁盘找到 / 不在本次范围

| 对象 | 结论 |
|---|---|
| `nc_biz_temp_relief_installments` | 未在磁盘找到 DDL，仅见 Rust 分析报告的建议（P1-2） |
| `nc_biz_special_approval_forms.received_at/review_level/case_name`、`nc_biz_special_approvals.result` | 未在磁盘找到 DDL，仅见分析报告建议 |
| 独立“照片临时池”表 | 未在磁盘找到；实际是 `nc_biz_archive_photos.temp_token` 列 |
| Rust 全量 schema 目录 | 未在磁盘找到；只有 `deploy/sql` 7 个增量脚本 |
| `nc_lottery_*` 的 YAML 定义 | 未在磁盘找到；只有 `legacy/.../Scripts/Lottery/create_tables.sql` |
| Web OCR 队列表 | 未在磁盘找到；Web 走远程 OCR HTTP |
| `nc_biz_print_jobs` 的目标设备列 | 未在磁盘找到；Rust 复用 `agent_machine`，Web 认领时回写 |
| `nc_biz_ocr_jobs`、`nc_sys_print_devices`、`nc_sys_printer_bindings` 在备份库 | 未找到（未创建/未执行迁移） |

> `backup_*`/`*_bak_*` 共 51 张临时备份表不属于 schema 权威，上线迁移时应排除；保留或清理需单独确认。

---

## 11. 证据文件（磁盘）

- YAML：`legacy/NewCosmos.Desktop/Resources/Schema/**/*.yaml`
- 旧库迁移：`legacy/NewCosmos.Desktop/docs/migrations/*.sql`
- 彩票建表：`legacy/NewCosmos.Desktop/Scripts/Lottery/create_tables.sql`
- Rust 增量：`D:\Projects\RustCosmosCode\deploy\sql\*.sql`
- Rust 代码：`crates/nc-service/src/{print_agent,session,photo,ocr_queue,land_use,income_write,temp_relief_write,special_approval}.rs`
- Web 打印：`src/NewCosmos.Server/Migrations/001_printer_binding.sql`、`src/NewCosmos.Server/Services/PrinterBindingService.cs`、`src/NewCosmos.Office/Printing/PrintQueueService.cs`
- Web 查询：`src/NewCosmos.Core/Constants/ApplicationColumns.cs`、`Services/Domain/SocialAssistance/{ApplicationService,FamilyMemberService}.cs`、`Services/Domain/UserManagement/UserService.cs`
- 备份库：`D:\Projects\RustCosmosCode\backups\RustCosmos_backup_20261124_063400.sql`
