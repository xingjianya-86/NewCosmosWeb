-- 一次性维护脚本：清空旧打印注册数据
-- 目标：
--   nc_sys_printer_bindings  账户 ↔ 设备 ↔ 打印机绑定
--   nc_sys_print_devices     Agent 设备注册
-- 不删除 nc_biz_print_jobs（历史打印任务）与打印留痕。
-- 执行前请先备份。
--
-- 用法：
--   psql -h 10.110.0.2 -U newcosmos -d new_cosmos -f deploy/database/20261124_clear_printer_registration.sql

BEGIN;

TRUNCATE TABLE nc_sys_printer_bindings, nc_sys_print_devices
    RESTART IDENTITY CASCADE;

COMMIT;

-- 可选：如果确认旧打印任务也不需要，再单独执行：
-- TRUNCATE TABLE nc_biz_print_jobs RESTART IDENTITY CASCADE;
