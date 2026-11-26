-- 2026-11-26 彩票助手（Web 移植）数据库迁移
-- 口径：
--   1. 4 张表结构照 legacy/NewCosmos.Desktop/Scripts/Lottery/create_tables.sql；
--   2. nc_lottery_user_purchases 增列 prize_amount（历史库可能缺）与 source_job_id；
--   3. source_job_id 建唯一部分索引，保证同一预测任务重复轮询不重复入库；
--   4. 列表/验证高频查询补索引。
-- 说明：全部 CREATE ... IF NOT EXISTS / ADD COLUMN IF NOT EXISTS，重复执行零影响；
--       不删表、不清数据。

BEGIN;

-- ── 开奖记录表 ──
CREATE TABLE IF NOT EXISTS nc_lottery_draws (
    id                  SERIAL PRIMARY KEY,
    lottery_type        VARCHAR(10) NOT NULL,
    draw_number         VARCHAR(20) NOT NULL,
    draw_date           DATE NOT NULL,
    red_numbers_json    TEXT NOT NULL DEFAULT '[]',
    blue_numbers_json   TEXT NOT NULL DEFAULT '[]',
    sales_amount        DECIMAL(18,2) DEFAULT 0,
    pool_money          DECIMAL(18,2) DEFAULT 0,
    prize_grades_json   TEXT DEFAULT '[]',
    prize_description   TEXT,
    created_at          TIMESTAMP DEFAULT NOW(),
    UNIQUE(lottery_type, draw_number)
);

-- ── 预测记录表（历史表；新代码不读写，仅保留结构，不 DROP） ──
CREATE TABLE IF NOT EXISTS nc_lottery_predictions (
    id                  SERIAL PRIMARY KEY,
    lottery_type        VARCHAR(10) NOT NULL,
    draw_number         VARCHAR(20) NOT NULL,
    red_numbers_json    TEXT NOT NULL DEFAULT '[]',
    blue_numbers_json   TEXT NOT NULL DEFAULT '[]',
    algorithm_version   VARCHAR(50),
    algorithm_name      VARCHAR(100),
    confidence_score    DECIMAL(5,2) DEFAULT 0,
    is_hit_red          BOOLEAN,
    is_hit_blue         BOOLEAN,
    red_hit_count       INTEGER,
    blue_hit_count      INTEGER,
    prize_level         INTEGER,
    prize_amount        DECIMAL(12,2),
    analysis_json       TEXT,
    created_at          TIMESTAMP DEFAULT NOW()
);

-- ── 用户购彩记录表 ──
CREATE TABLE IF NOT EXISTS nc_lottery_user_purchases (
    id                  SERIAL PRIMARY KEY,
    lottery_type        VARCHAR(10) NOT NULL,
    draw_number         VARCHAR(20),
    red_numbers         TEXT NOT NULL DEFAULT '[]',
    blue_numbers        TEXT NOT NULL DEFAULT '[]',
    algorithm           VARCHAR(50),
    purchase_date       DATE NOT NULL DEFAULT CURRENT_DATE,
    is_verified         BOOLEAN DEFAULT FALSE,
    is_hit              BOOLEAN DEFAULT FALSE,
    hit_red_count       INTEGER DEFAULT 0,
    hit_blue_count      INTEGER DEFAULT 0,
    prize_level         INTEGER DEFAULT 0,
    prize_amount        DECIMAL(12,2) DEFAULT 0,
    created_at          TIMESTAMP DEFAULT NOW()
);

-- ── 统计缓存表（历史表；新代码不读写，仅保留结构，不 DROP） ──
CREATE TABLE IF NOT EXISTS nc_lottery_statistics (
    id                  SERIAL PRIMARY KEY,
    lottery_type        VARCHAR(10) NOT NULL,
    stat_type           VARCHAR(50) NOT NULL,
    number_value        INTEGER NOT NULL,
    period_count        INTEGER,
    frequency           INTEGER DEFAULT 0,
    frequency_rate      DECIMAL(5,2) DEFAULT 0,
    current_missing     INTEGER DEFAULT 0,
    average_missing     DECIMAL(8,2) DEFAULT 0,
    max_missing         INTEGER DEFAULT 0,
    last_appear_period  VARCHAR(20),
    recent_frequency    INTEGER DEFAULT 0,
    updated_at          TIMESTAMP DEFAULT NOW(),
    UNIQUE(lottery_type, stat_type, number_value, period_count)
);

-- ── 购彩表补列（幂等） ──
ALTER TABLE nc_lottery_user_purchases
    ADD COLUMN IF NOT EXISTS prize_amount DECIMAL(12,2) DEFAULT 0;

ALTER TABLE nc_lottery_user_purchases
    ADD COLUMN IF NOT EXISTS source_job_id VARCHAR(64);

-- 同一预测任务（jobId/派生键）只入一次；NULL（机选/手工）不受约束。
CREATE UNIQUE INDEX IF NOT EXISTS ux_lottery_user_purchases_source_job
    ON nc_lottery_user_purchases (source_job_id)
    WHERE source_job_id IS NOT NULL;

-- ── 索引（沿用桌面端索引名，避免同列重复索引） ──
CREATE INDEX IF NOT EXISTS idx_draws_type_date
    ON nc_lottery_draws (lottery_type, draw_date DESC);
CREATE INDEX IF NOT EXISTS idx_draws_type_number
    ON nc_lottery_draws (lottery_type, draw_number);

CREATE INDEX IF NOT EXISTS idx_predictions_type
    ON nc_lottery_predictions (lottery_type, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_predictions_draw
    ON nc_lottery_predictions (lottery_type, draw_number);

CREATE INDEX IF NOT EXISTS idx_statistics_type
    ON nc_lottery_statistics (lottery_type, stat_type);

CREATE INDEX IF NOT EXISTS idx_user_purchases_type_date
    ON nc_lottery_user_purchases (lottery_type, purchase_date DESC);
CREATE INDEX IF NOT EXISTS idx_user_purchases_unverified
    ON nc_lottery_user_purchases (lottery_type, is_verified)
    WHERE is_verified = false;

COMMENT ON TABLE nc_lottery_draws IS '彩票开奖记录表';
COMMENT ON TABLE nc_lottery_predictions IS '彩票预测记录表（历史保留）';
COMMENT ON TABLE nc_lottery_statistics IS '彩票统计缓存表（历史保留）';
COMMENT ON TABLE nc_lottery_user_purchases IS '用户购彩记录表（预测/机选入库，开奖后验证）';
COMMENT ON COLUMN nc_lottery_user_purchases.source_job_id IS '来源预测任务标识（幂等入库；多注任务第一注=jobId，其余为 jobId#序号）';

COMMIT;
