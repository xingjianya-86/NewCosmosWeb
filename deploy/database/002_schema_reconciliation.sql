-- NewCosmos Web：Schema 对账增量（P0/P1）
-- 来源：docs/SCHEMA_RECONCILIATION.md §8
-- 全部可重复执行；执行前先备份。建议低峰窗口执行。

BEGIN;

-- 1) P0：用户/照料人/近亲属写 position（YAML 有、旧库缺）
ALTER TABLE nc_sys_users               ADD COLUMN IF NOT EXISTS position varchar(100);
ALTER TABLE nc_biz_caregivers           ADD COLUMN IF NOT EXISTS position varchar(100);
ALTER TABLE nc_biz_guardians            ADD COLUMN IF NOT EXISTS position varchar(100);
ALTER TABLE nc_biz_supporters           ADD COLUMN IF NOT EXISTS position varchar(100);
ALTER TABLE nc_biz_near_relative_staffs ADD COLUMN IF NOT EXISTS position varchar(100);

-- 2) P1：申请/家庭成员（旧库已有，空库兜底）
ALTER TABLE nc_biz_applications
    ADD COLUMN IF NOT EXISTS chain_type varchar(30),
    ADD COLUMN IF NOT EXISTS medical_self_paid_12m numeric(18,2),
    ADD COLUMN IF NOT EXISTS total_annual_income numeric(12,2) DEFAULT 0,
    ADD COLUMN IF NOT EXISTS per_capita_annual_income numeric(12,2) DEFAULT 0;

ALTER TABLE nc_biz_family_members
    ADD COLUMN IF NOT EXISTS person_type varchar(50),
    ADD COLUMN IF NOT EXISTS monthly_income_capacity numeric(18,2) DEFAULT 0,
    ADD COLUMN IF NOT EXISTS medical_self_paid_12m numeric(18,2);

-- 3) 档案/问卷/变更（旧库已有，空库兜底）
ALTER TABLE nc_biz_archives
    ADD COLUMN IF NOT EXISTS related_archive_id bigint,
    ADD COLUMN IF NOT EXISTS relation_type varchar(50),
    ADD COLUMN IF NOT EXISTS relation_description text;

ALTER TABLE nc_biz_household_surveys
    ADD COLUMN IF NOT EXISTS application_reason varchar(200),
    ADD COLUMN IF NOT EXISTS application_reason_detail text,
    ADD COLUMN IF NOT EXISTS survey_conclusion text;

ALTER TABLE nc_biz_change_records
    ADD COLUMN IF NOT EXISTS new_application_id bigint;

-- 4) 照片临时池（Rust 方案；Web 照片绑定前可空身份证）
ALTER TABLE nc_biz_archive_photos ADD COLUMN IF NOT EXISTS temp_token uuid;
ALTER TABLE nc_biz_archive_photos ALTER COLUMN applicant_id_card DROP NOT NULL;
CREATE INDEX IF NOT EXISTS idx_nc_biz_archive_photos_temp_token
    ON nc_biz_archive_photos (temp_token) WHERE temp_token IS NOT NULL;

-- 5) 打印任务：按账户/设备定向认领（Web 新协议）
ALTER TABLE nc_biz_print_jobs
    ADD COLUMN IF NOT EXISTS target_user_id integer,
    ADD COLUMN IF NOT EXISTS target_device_id bigint,
    ADD COLUMN IF NOT EXISTS target_printer text;

CREATE INDEX IF NOT EXISTS idx_print_jobs_target_pending
    ON nc_biz_print_jobs (target_device_id, created_at)
    WHERE status = 'Pending' AND deleted_at IS NULL;

-- 6) 打印状态归一：Rust 的 Printing -> Web 的 Processing
--    旧库当前只有 Completed/Failed 时无影响。
UPDATE nc_biz_print_jobs SET status = 'Processing' WHERE status = 'Printing';

COMMIT;

-- 注意：
-- 1. 临时救助 11 列、一事一议 3 列由 Rust deploy/sql 脚本负责，已生效则跳过；
--    空库环境请按 docs/SCHEMA_RECONCILIATION.md §8 顺序执行。
-- 2. 土地字典只加别名，禁止批量改历史值。
