-- 2026-11-26 打印设备启用状态
-- 目的：允许停用 Agent 设备而不删除历史绑定/任务；心跳/认领/绑定查询只认 is_active=true。
ALTER TABLE nc_sys_print_devices
    ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;

CREATE INDEX IF NOT EXISTS idx_sys_print_devices_active
    ON nc_sys_print_devices(is_active);
