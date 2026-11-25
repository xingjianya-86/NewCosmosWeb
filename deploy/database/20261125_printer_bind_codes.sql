-- 2026-11-25 打印机绑定码 + 打印任务定向到设备
-- 目标表：
--   nc_sys_printer_bind_codes  一次性绑定码（6 位数字，5 分钟有效）
--   nc_biz_print_jobs          打印任务队列（回填历史任务 target_*）
--
-- 全部使用 IF NOT EXISTS，可重复执行；执行前请先备份。
--
-- 用法：
--   psql -h 10.110.0.2 -U newcosmos -d new_cosmos \
--     -f deploy/database/20261125_printer_bind_codes.sql

BEGIN;

-- 1) 绑定码：网页端生成，Agent 用 X-NC-Device-Key 按码兑换成打印机绑定。
--    code 不建唯一约束：只要求「未使用且未过期」的码唯一，由应用层生成时避让。
CREATE TABLE IF NOT EXISTS nc_sys_printer_bind_codes (
  id bigserial PRIMARY KEY,
  code varchar(6) NOT NULL,
  user_id int NOT NULL,
  expires_at timestamptz NOT NULL,
  used_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_printer_bind_codes_code
    ON nc_sys_printer_bind_codes (code) WHERE used_at IS NULL;

-- 2) 任务定向列兜底（历史库可能已由 002_schema_reconciliation.sql 添加）。
ALTER TABLE nc_biz_print_jobs
    ADD COLUMN IF NOT EXISTS target_user_id integer,
    ADD COLUMN IF NOT EXISTS target_device_id bigint,
    ADD COLUMN IF NOT EXISTS target_printer text;

-- 3) 历史 Pending 任务补目标：按 requested_by 的默认绑定补 device/printer。
--    补不到的保持 NULL（新认领协议不再领取，需人工处理）；不删除任何历史任务。
UPDATE nc_biz_print_jobs j
SET target_user_id   = j.requested_by,
    target_device_id = b.device_id,
    target_printer   = b.printer_name
FROM nc_sys_printer_bindings b
WHERE j.status = 'Pending'
  AND j.deleted_at IS NULL
  AND j.target_device_id IS NULL
  AND j.requested_by IS NOT NULL
  AND b.user_id = j.requested_by
  AND b.is_default;

-- 4) 定向认领扫描索引（与 002 同名同定义，缺失时兜底）。
CREATE INDEX IF NOT EXISTS idx_print_jobs_target_pending
    ON nc_biz_print_jobs (target_device_id, created_at)
    WHERE status = 'Pending' AND deleted_at IS NULL;

COMMIT;

-- 回滚（如需）：
-- DROP TABLE IF EXISTS nc_sys_printer_bind_codes;
-- 回填的 target_* 不回滚（历史任务只能人工核对）。
