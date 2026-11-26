-- 2026-11-26 死亡情况说明（Web 移植）数据库补齐
-- 口径：不新建表、不改数据；仅对既有 nc_biz_death_declarations / nc_biz_death_linkage_tasks
--       幂等补齐 Web 领域服务读取的列与范围查询索引。
-- 表结构权威来源：legacy/NewCosmos.Desktop/Resources/Schema/death_declarations/database.yaml
-- 说明：ADD COLUMN IF NOT EXISTS 对已存在列零影响；索引用 to_regclass 守卫，表缺失时跳过而不是报错。

BEGIN;

-- ── 死亡申报台账：规范化格式五条目 / 核实说明 / 软删与归属列 ──
ALTER TABLE IF EXISTS nc_biz_death_declarations
    ADD COLUMN IF NOT EXISTS deceased_relation      character varying(50),
    ADD COLUMN IF NOT EXISTS death_place            character varying(200),
    ADD COLUMN IF NOT EXISTS declared_by_id_card    character varying(18),
    ADD COLUMN IF NOT EXISTS declared_by_phone      character varying(20),
    ADD COLUMN IF NOT EXISTS reporter_name          character varying(100),
    ADD COLUMN IF NOT EXISTS reporter_relation      character varying(50),
    ADD COLUMN IF NOT EXISTS reporter_phone         character varying(20),
    ADD COLUMN IF NOT EXISTS situation_desc         text,
    ADD COLUMN IF NOT EXISTS remains_arrangement    text,
    ADD COLUMN IF NOT EXISTS business_agent         character varying(100),
    ADD COLUMN IF NOT EXISTS business_matter        character varying(200),
    ADD COLUMN IF NOT EXISTS cert_type              character varying(50),
    ADD COLUMN IF NOT EXISTS cert_issue_org         character varying(200),
    ADD COLUMN IF NOT EXISTS cremation_date         date,
    ADD COLUMN IF NOT EXISTS cremation_place        character varying(200),
    ADD COLUMN IF NOT EXISTS cremation_cert_no      character varying(50),
    ADD COLUMN IF NOT EXISTS burial_date            date,
    ADD COLUMN IF NOT EXISTS burial_place           character varying(200),
    ADD COLUMN IF NOT EXISTS family_situation       text,
    ADD COLUMN IF NOT EXISTS dispute_status         character varying(20),
    ADD COLUMN IF NOT EXISTS dispute_desc           text,
    ADD COLUMN IF NOT EXISTS death_record_id        bigint,
    ADD COLUMN IF NOT EXISTS village_id             integer,
    ADD COLUMN IF NOT EXISTS town_id                integer,
    ADD COLUMN IF NOT EXISTS village_name           character varying(100),
    ADD COLUMN IF NOT EXISTS operator_name          character varying(100),
    ADD COLUMN IF NOT EXISTS remark                 text,
    ADD COLUMN IF NOT EXISTS created_by             character varying(100),
    ADD COLUMN IF NOT EXISTS updated_at             timestamp without time zone,
    ADD COLUMN IF NOT EXISTS deleted_at             timestamp without time zone;

-- ── 死亡联动待办：派活表列（申报作废/归档不删行，靠 JOIN 台账状态退出）──
ALTER TABLE IF EXISTS nc_biz_death_linkage_tasks
    ADD COLUMN IF NOT EXISTS ref_application_id     bigint,
    ADD COLUMN IF NOT EXISTS ref_elderly_id         bigint,
    ADD COLUMN IF NOT EXISTS permission_code        character varying(50),
    ADD COLUMN IF NOT EXISTS village_id             integer,
    ADD COLUMN IF NOT EXISTS town_id                integer,
    ADD COLUMN IF NOT EXISTS completed_at           timestamp without time zone;

-- ── 索引：本村/本镇范围查询 + 申报台账去重/日期 + 联动待办读取 ──
DO $$
BEGIN
    IF to_regclass('public.nc_biz_death_declarations') IS NOT NULL THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS idx_biz_decl_id_card ON nc_biz_death_declarations (deceased_id_card)';
        EXECUTE 'CREATE INDEX IF NOT EXISTS idx_biz_decl_death_date ON nc_biz_death_declarations (death_date)';
        EXECUTE 'CREATE INDEX IF NOT EXISTS idx_biz_decl_village ON nc_biz_death_declarations (village_id)';
        EXECUTE 'CREATE INDEX IF NOT EXISTS idx_biz_decl_town ON nc_biz_death_declarations (town_id)';
        EXECUTE 'CREATE INDEX IF NOT EXISTS idx_biz_decl_declared_at ON nc_biz_death_declarations (declared_at)';
    END IF;

    IF to_regclass('public.nc_biz_death_linkage_tasks') IS NOT NULL THEN
        EXECUTE 'CREATE INDEX IF NOT EXISTS idx_biz_death_task_pending ON nc_biz_death_linkage_tasks (status, task_domain)';
        EXECUTE 'CREATE INDEX IF NOT EXISTS idx_biz_death_task_id_card ON nc_biz_death_linkage_tasks (deceased_id_card)';
        EXECUTE 'CREATE INDEX IF NOT EXISTS idx_biz_death_task_declaration ON nc_biz_death_linkage_tasks (declaration_id)';
        EXECUTE 'CREATE INDEX IF NOT EXISTS idx_biz_death_task_scope ON nc_biz_death_linkage_tasks (village_id, town_id)';
    END IF;
END $$;

COMMIT;
