-- NewCosmos P0：Agent 打印设备与按账户绑定
-- 幂等脚本：可重复执行（NC_AUTO_MIGRATE=1 时服务启动自动执行）。

CREATE TABLE IF NOT EXISTS nc_sys_print_devices (
    id            BIGSERIAL PRIMARY KEY,
    device_key    UUID        NOT NULL,
    machine_name  TEXT        NOT NULL,
    agent_version TEXT        NOT NULL DEFAULT '',
    printers_json JSONB       NOT NULL DEFAULT '[]'::jsonb,
    last_seen_at  TIMESTAMPTZ NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_nc_sys_print_devices_device_key UNIQUE (device_key)
);

CREATE INDEX IF NOT EXISTS ix_nc_sys_print_devices_last_seen
    ON nc_sys_print_devices (last_seen_at DESC);

CREATE TABLE IF NOT EXISTS nc_sys_printer_bindings (
    id           BIGSERIAL PRIMARY KEY,
    user_id      INTEGER     NOT NULL,
    device_id    BIGINT      NOT NULL
        REFERENCES nc_sys_print_devices (id) ON DELETE CASCADE,
    printer_name TEXT        NOT NULL,
    is_default   BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_nc_sys_printer_bindings_user_device_printer
        UNIQUE (user_id, device_id, printer_name)
);

-- 同一账号最多一台默认打印机；并发下由数据库兜底。
CREATE UNIQUE INDEX IF NOT EXISTS ux_nc_sys_printer_bindings_default
    ON nc_sys_printer_bindings (user_id)
    WHERE is_default;

CREATE INDEX IF NOT EXISTS ix_nc_sys_printer_bindings_user_created
    ON nc_sys_printer_bindings (user_id, created_at, id);
