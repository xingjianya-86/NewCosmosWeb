-- 2026-11-26 历史补打中心：打印任务批次号
-- 目标表：nc_biz_print_jobs
--   batch_no  批量补打批次号（同一次「按月逐户补打」的所有任务共享）
--   Agent 完成回传时按 batch_no 写入 nc_biz_print_records，补打中心据此展示留痕。
-- 全部使用 IF NOT EXISTS，可重复执行；执行前请先备份。
--
-- 用法：
--   psql -h 10.110.0.2 -U newcosmos -d new_cosmos \
--     -f deploy/database/20261126_reprint_batch.sql

BEGIN;

ALTER TABLE nc_biz_print_jobs
    ADD COLUMN IF NOT EXISTS batch_no varchar(64);

-- 批次进度查询（WHERE batch_no = $1）
CREATE INDEX IF NOT EXISTS idx_nc_biz_print_jobs_batch_no
    ON nc_biz_print_jobs (batch_no)
    WHERE batch_no IS NOT NULL AND deleted_at IS NULL;

-- 业务留痕反查（补打历史 / 原样补打）
CREATE INDEX IF NOT EXISTS idx_nc_biz_print_jobs_business_type_id
    ON nc_biz_print_jobs (business_type, business_id);

COMMIT;

-- 回滚（如需）：
-- DROP INDEX IF EXISTS idx_nc_biz_print_jobs_business_type_id;
-- DROP INDEX IF EXISTS idx_nc_biz_print_jobs_batch_no;
-- ALTER TABLE nc_biz_print_jobs DROP COLUMN IF EXISTS batch_no;
