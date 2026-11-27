-- 20261127 本机操作指令通道（LocalOp）：Agent 设备能力上报列
-- 心跳 POST /api/agent/heartbeat 新增 capabilities 字段，服务端原样落到本列，
-- 供前端判断「该设备是否支持字体安装 / ZeroTier 接入」等本机操作。
-- 幂等脚本：可重复执行（NC_AUTO_MIGRATE=1 时服务启动自动执行，按文件名顺序排在 001 之后）。
-- 表由 001_printer_binding.sql 创建，这里只做列级 IF NOT EXISTS 增量。
-- 未上报 capabilities 的老 Agent 保持默认空数组，心跳解析侧对非数组 JSON 也做了容错。

ALTER TABLE nc_sys_print_devices
    ADD COLUMN IF NOT EXISTS capabilities_json JSONB NOT NULL DEFAULT '[]'::jsonb;
