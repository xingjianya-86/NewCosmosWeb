-- 2026-11-26 后补追缴基线（合并 legacy 20261019_recovery_amount_source.sql + 20261101_recovery_code.sql）
--
-- 目的：Server 端「后补追缴」API / 打印底座上线所需的最小结构基线。
--   · nc_biz_recovery_records     追缴记录主表（含金额来源与编号列）
--   · nc_biz_recovery_segments    追缴金额自动分段明细
--   · nc_config_proof_unit_templates 证明接收单位与专属模板映射（证明类文书复用）
--
-- 原则：
--   · 只做 IF NOT EXISTS / 安全 ALTER，不 DROP / TRUNCATE，不改写任何存量数据；
--   · 幂等，可重复执行；已有库执行时建表为 no-op，仅补齐缺失列/索引；
--   · 存量数据回填（如 id=3 的 Manual 说明）不在此脚本内，按业务需要另行人工核对。
--
-- 用法：
--   psql -h <host> -U <user> -d <db> -v ON_ERROR_STOP=1 \
--     -f deploy/database/20261126_recovery_baseline.sql

BEGIN;

-- ───────────────────────────── 1. 追缴记录主表 ─────────────────────────────

CREATE TABLE IF NOT EXISTS nc_biz_recovery_records (
    id                BIGSERIAL     PRIMARY KEY,
    input_mode        VARCHAR(10)   NOT NULL DEFAULT 'Search',
    source_type       VARCHAR(30),
    source_id         BIGINT,
    person_name       VARCHAR(100)  NOT NULL,
    id_card           VARCHAR(18),
    recovery_reason   TEXT,
    monthly_amount    NUMERIC(14,2),
    recovery_months   INTEGER,
    recovery_amount   NUMERIC(14,2),
    recovered_amount  NUMERIC(14,2),
    start_month       VARCHAR(7),
    end_month         VARCHAR(7),
    status            VARCHAR(20)   NOT NULL DEFAULT 'Draft',
    created_by        VARCHAR(100),
    created_at        TIMESTAMP     NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMP     NOT NULL DEFAULT NOW(),
    amount_source     VARCHAR(10)   NOT NULL DEFAULT 'Flat',
    amount_note       TEXT,
    auto_amount       NUMERIC(14,2),
    recovery_code     VARCHAR(20)
);

-- 已有库补齐金额来源/编号列（与 legacy 迁移同口径，幂等）
ALTER TABLE nc_biz_recovery_records ADD COLUMN IF NOT EXISTS amount_source VARCHAR(10) NOT NULL DEFAULT 'Flat';
ALTER TABLE nc_biz_recovery_records ADD COLUMN IF NOT EXISTS amount_note   TEXT;
ALTER TABLE nc_biz_recovery_records ADD COLUMN IF NOT EXISTS auto_amount   NUMERIC(14,2);
ALTER TABLE nc_biz_recovery_records ADD COLUMN IF NOT EXISTS recovery_code VARCHAR(20);

COMMENT ON TABLE  nc_biz_recovery_records IS '后补追缴记录（Search=搜索录入 / Manual=手工录入）';
COMMENT ON COLUMN nc_biz_recovery_records.amount_source IS '金额来源：Segment=变更记录分段累加 / Flat=月标准×月数 / Manual=人工核实填写';
COMMENT ON COLUMN nc_biz_recovery_records.amount_note   IS '手工填写原因（Manual 时必填）或自动计算依据说明';
COMMENT ON COLUMN nc_biz_recovery_records.auto_amount   IS '自动计算的参考值（人工覆盖时保留，便于审计对比）';
COMMENT ON COLUMN nc_biz_recovery_records.recovery_code IS '追缴编号（YYYYMM-NNN，随保存一次生成；打印/补打同号；存量记录首次打印时补分配）';

CREATE INDEX IF NOT EXISTS idx_nc_biz_recovery_records_created_at
    ON nc_biz_recovery_records (created_at DESC);

CREATE INDEX IF NOT EXISTS idx_nc_biz_recovery_records_id_card
    ON nc_biz_recovery_records (id_card);

-- 编号唯一（并发生成由应用层 pg_advisory_xact_lock 串行化，此索引兜底）
CREATE UNIQUE INDEX IF NOT EXISTS ux_nc_biz_recovery_records_recovery_code
    ON nc_biz_recovery_records (recovery_code)
    WHERE recovery_code IS NOT NULL;

-- ───────────────────────────── 2. 追缴分段明细 ─────────────────────────────

CREATE TABLE IF NOT EXISTS nc_biz_recovery_segments (
    id                  BIGSERIAL     PRIMARY KEY,
    recovery_id         BIGINT        NOT NULL,
    segment_no          INTEGER       NOT NULL DEFAULT 1,
    monthly_amount      NUMERIC(14,2),
    segment_start_month VARCHAR(7),
    segment_end_month   VARCHAR(7),
    months              INTEGER,
    segment_amount      NUMERIC(14,2),
    source              VARCHAR(20),
    created_at          TIMESTAMP     NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE  nc_biz_recovery_segments IS '追缴金额自动计算的分段明细（依据来源 + 起止月 + 月标准 + 段金额）';
COMMENT ON COLUMN nc_biz_recovery_segments.source IS '分段依据：ChangeRecord=保障金变更记录 / Ledger=台账现值 / GracePeriod=渐退期原标准';

CREATE INDEX IF NOT EXISTS idx_recovery_segments_recovery_id
    ON nc_biz_recovery_segments (recovery_id);

-- 外键（幂等）：追缴记录删除时级联清理分段
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_recovery_segments_recovery'
    ) THEN
        ALTER TABLE nc_biz_recovery_segments
            ADD CONSTRAINT fk_recovery_segments_recovery
            FOREIGN KEY (recovery_id) REFERENCES nc_biz_recovery_records (id) ON DELETE CASCADE;
    END IF;
END $$;

-- ───────────────────────────── 3. 证明单位模板映射 ─────────────────────────────

CREATE TABLE IF NOT EXISTS nc_config_proof_unit_templates (
    id          BIGSERIAL      PRIMARY KEY,
    unit_name   VARCHAR(200)   NOT NULL,
    template_id BIGINT         NOT NULL,
    created_by  INTEGER,
    updated_by  INTEGER,
    created_at  TIMESTAMP      NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMP
);

COMMENT ON TABLE  nc_config_proof_unit_templates IS '证明接收单位与专属模板映射表（单位名唯一，全局共享）';
COMMENT ON COLUMN nc_config_proof_unit_templates.unit_name   IS '接收单位名称（如 区民政局/市人社局/XX学校）';
COMMENT ON COLUMN nc_config_proof_unit_templates.template_id IS '关联 nc_biz_templates.id（专属证明模板）';

CREATE UNIQUE INDEX IF NOT EXISTS idx_proof_unit_templates_unit_name
    ON nc_config_proof_unit_templates (unit_name);

COMMIT;

-- 回滚（如需，按顺序）：
-- DROP INDEX IF EXISTS ux_nc_biz_recovery_records_recovery_code;
-- DROP TABLE IF EXISTS nc_biz_recovery_segments;
-- DROP TABLE IF EXISTS nc_config_proof_unit_templates;
-- DROP TABLE IF EXISTS nc_biz_recovery_records;
