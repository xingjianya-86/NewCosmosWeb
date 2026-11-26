-- 2026-11-26 值班管理（Web 移植）数据库迁移
-- 口径：新建 7 张值班表（成员/分组/请假/轮转游标/月度班次/设置/班务调整），
--       幂等执行；表结构权威来源 src/NewCosmos.Server/Resources/Schema/duty_management/database.yaml。
-- 说明：
--   1. 全部使用 CREATE TABLE IF NOT EXISTS / CREATE INDEX IF NOT EXISTS，重复执行零影响。
--   2. nc_sys_holidays 已由节假日模块迁移/建表脚本维护，本迁移不重建、不改结构。
--   3. 设置种子仅在键不存在时写入，不覆盖现场已配置的值。

BEGIN;

-- ── 值班人员表 ──
CREATE TABLE IF NOT EXISTS nc_duty_members (
    id              BIGSERIAL      PRIMARY KEY,
    name            character varying(50) NOT NULL,
    phone           character varying(20),
    is_active       boolean         DEFAULT TRUE,
    join_date       date,
    exit_date       date,
    sort_order      integer         DEFAULT 0,
    remark          character varying(200),
    created_at      timestamp without time zone DEFAULT NOW(),
    updated_at      timestamp without time zone
);

CREATE INDEX IF NOT EXISTS idx_duty_members_name ON nc_duty_members (name);

-- ── 成员分组表：一人可属多组；组内 sort_order 即轮转顺序 ──
CREATE TABLE IF NOT EXISTS nc_duty_member_groups (
    id              BIGSERIAL      PRIMARY KEY,
    member_id       BIGINT         NOT NULL,
    group_code      character varying(20) NOT NULL,
    sort_order      integer        DEFAULT 0,
    CONSTRAINT uq_duty_member_groups UNIQUE (member_id, group_code),
    CONSTRAINT fk_duty_member_groups_member FOREIGN KEY (member_id)
        REFERENCES nc_duty_members (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_duty_member_groups_member ON nc_duty_member_groups (member_id);
CREATE INDEX IF NOT EXISTS idx_duty_member_groups_group ON nc_duty_member_groups (group_code);

-- ── 请假记录表 ──
CREATE TABLE IF NOT EXISTS nc_duty_leaves (
    id              BIGSERIAL      PRIMARY KEY,
    member_id       BIGINT         NOT NULL,
    start_date      date           NOT NULL,
    end_date        date           NOT NULL,
    reason          character varying(200),
    created_at      timestamp without time zone DEFAULT NOW(),
    CONSTRAINT fk_duty_leaves_member FOREIGN KEY (member_id)
        REFERENCES nc_duty_members (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_duty_leaves_member ON nc_duty_leaves (member_id);
CREATE INDEX IF NOT EXISTS idx_duty_leaves_start ON nc_duty_leaves (start_date);

-- ── 轮转游标表：每组 × 每日期类型一条轮转线 ──
CREATE TABLE IF NOT EXISTS nc_duty_rotation_state (
    id              BIGSERIAL      PRIMARY KEY,
    group_code      character varying(20) NOT NULL,
    date_type       character varying(20) NOT NULL,
    last_member_id  BIGINT,
    updated_at      timestamp without time zone,
    CONSTRAINT uq_duty_rotation_state UNIQUE (group_code, date_type),
    CONSTRAINT fk_duty_rotation_state_member FOREIGN KEY (last_member_id)
        REFERENCES nc_duty_members (id) ON DELETE SET NULL
);

-- ── 值班班次表：每天每组一条；rotation_member_id 为轮转消耗事实 ──
CREATE TABLE IF NOT EXISTS nc_duty_schedules (
    id                  BIGSERIAL      PRIMARY KEY,
    duty_date           date           NOT NULL,
    date_type           character varying(20) NOT NULL,
    group_code          character varying(20) NOT NULL,
    member_id           BIGINT,
    rotation_member_id  BIGINT,
    member_name         character varying(50) NOT NULL,
    year                integer        NOT NULL,
    month               integer        NOT NULL,
    is_adjusted         boolean        DEFAULT FALSE,
    remark              character varying(200),
    created_at          timestamp without time zone DEFAULT NOW(),
    CONSTRAINT uq_duty_schedules UNIQUE (duty_date, group_code),
    CONSTRAINT fk_duty_schedules_member FOREIGN KEY (member_id)
        REFERENCES nc_duty_members (id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_duty_schedules_year_month ON nc_duty_schedules (year, month);
CREATE INDEX IF NOT EXISTS idx_duty_schedules_line_date ON nc_duty_schedules (group_code, date_type, duty_date);

-- ── 值班设置表：key-value ──
CREATE TABLE IF NOT EXISTS nc_duty_settings (
    id              BIGSERIAL      PRIMARY KEY,
    setting_key     character varying(50) NOT NULL,
    setting_value   character varying(200),
    remark          character varying(200),
    updated_at      timestamp without time zone,
    CONSTRAINT uq_duty_settings_key UNIQUE (setting_key)
);

-- ── 班务调整表：串班(SWAP=相互对调)/代班(SUBSTITUTE=单向顶班) ──
CREATE TABLE IF NOT EXISTS nc_duty_shift_changes (
    id                  BIGSERIAL      PRIMARY KEY,
    change_type         character varying(20) NOT NULL,
    from_member_id      BIGINT,
    from_member_name    character varying(50) NOT NULL,
    duty_date           date           NOT NULL,
    to_member_id        BIGINT,
    to_member_name      character varying(50) NOT NULL,
    to_duty_date        date,
    group_code          character varying(20) NOT NULL,
    reason              character varying(200),
    is_cancelled        boolean        DEFAULT FALSE,
    cancelled_at        timestamp without time zone,
    created_at          timestamp without time zone DEFAULT NOW(),
    CONSTRAINT fk_duty_shift_changes_from FOREIGN KEY (from_member_id)
        REFERENCES nc_duty_members (id) ON DELETE SET NULL,
    CONSTRAINT fk_duty_shift_changes_to FOREIGN KEY (to_member_id)
        REFERENCES nc_duty_members (id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_duty_shift_changes_date ON nc_duty_shift_changes (duty_date);
CREATE INDEX IF NOT EXISTS idx_duty_shift_changes_to_date ON nc_duty_shift_changes (to_duty_date);
CREATE INDEX IF NOT EXISTS idx_duty_shift_changes_type ON nc_duty_shift_changes (change_type, is_cancelled);

-- ── 设置种子（仅补缺失键）──
INSERT INTO nc_duty_settings (setting_key, setting_value, remark, updated_at)
VALUES
    ('restday_leader_same_pair', '0',
     '休息日（周六/周日）带班领导两天同人：1=开启，0=关闭', NOW()),
    ('holiday_api_url', 'https://timor.tech/api/holiday/year/{year}',
     '法定节假日主接口地址（{year} 为年份占位符）', NOW()),
    ('holiday_fallback_api_url', 'https://cdn.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{year}.json',
     '法定节假日备用接口地址（{year} 为年份占位符）', NOW())
ON CONFLICT (setting_key) DO NOTHING;

COMMIT;
