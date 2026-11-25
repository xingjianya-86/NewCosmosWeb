-- 2026-11-25 打印任务：批量模板任务（高龄月报等多明细表一次入队）
-- 目标表：
--   nc_biz_print_jobs  打印任务队列（batch_json：PrintBatchDefinition 快照）
--
-- 批量任务不绑定单个模板（template_id 为空），批次内每个 part 自带 template_id；
-- PC Agent 领取后按 batch_json 下发的模板集合 + 逐 part 字段渲染。
-- 全部使用 IF NOT EXISTS，可重复执行；执行前请先备份。
--
-- 用法：
--   psql -h 10.110.0.2 -U newcosmos -d new_cosmos \
--     -f deploy/database/20261125_print_jobs_batch.sql

BEGIN;

ALTER TABLE nc_biz_print_jobs
    ADD COLUMN IF NOT EXISTS batch_json jsonb;

COMMIT;

-- 回滚（如需）：
-- ALTER TABLE nc_biz_print_jobs DROP COLUMN IF EXISTS batch_json;
