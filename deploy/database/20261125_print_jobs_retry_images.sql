-- 2026-11-25 打印任务：超时回收/最大重试 + 照片下发
-- 目标表：
--   nc_biz_print_jobs  打印任务队列（retry_count / images_json）
--   nc_biz_templates   模板表（file_hash，Agent 本地模板缓存键；历史库已有则跳过）
--
-- 全部使用 IF NOT EXISTS，可重复执行；执行前请先备份。
--
-- 用法：
--   psql -h 10.110.0.2 -U newcosmos -d new_cosmos \
--     -f deploy/database/20261125_print_jobs_retry_images.sql

BEGIN;

-- 重试计数：Agent 失败（fail）与 Processing 超时回收各 +1；达到 5 次后不再重试。
ALTER TABLE nc_biz_print_jobs
    ADD COLUMN IF NOT EXISTS retry_count integer NOT NULL DEFAULT 0;

-- 照片下发：模板 config 中 PHOTO_* 字段 → 档案照片 Base64（List<PrintImagePayload>）。
ALTER TABLE nc_biz_print_jobs
    ADD COLUMN IF NOT EXISTS images_json jsonb;

-- 模板文件哈希（SHA256）：Agent 以 {templateId}-{hash} 复用本地模板缓存。
-- 历史库一般已存在；此处兜底，缺失时 payload 会按 file_data 现算，不依赖本列。
ALTER TABLE nc_biz_templates
    ADD COLUMN IF NOT EXISTS file_hash character varying(128);

-- 超时回收扫描（status + claimed_at）与重试判断的支撑索引。
CREATE INDEX IF NOT EXISTS idx_nc_biz_print_jobs_processing_claimed
    ON nc_biz_print_jobs (claimed_at)
    WHERE status = 'Processing' AND deleted_at IS NULL;

COMMIT;

-- 回滚（如需）：
-- ALTER TABLE nc_biz_print_jobs DROP COLUMN IF EXISTS retry_count;
-- ALTER TABLE nc_biz_print_jobs DROP COLUMN IF EXISTS images_json;
-- DROP INDEX IF EXISTS idx_nc_biz_print_jobs_processing_claimed;
