-- 20261128 字典字段值统一：历史中文脏值 → 字典 item_key（权威见 nc_dict_items）
--
-- 背景：字典类字段的统一存储口径是 item_key（英文，如 Married/Primary/Masses），
--       展示口径是 item_value（中文），两者唯一换算点在 Core DictValueMapper。
--       存量数据里混进了少量直接写入的中文显示值，导致同一列两套词表。
--
-- 范围（仅已确认「读取出口全部经过 DictValueMapper.ToForm*」的两表三列）：
--   nc_biz_applications.marital_status / education_level / political_status
--   nc_biz_family_members.marital_status / education_level / political_status
--
-- 明确不动（需先改代码，见 docs/MIGRATION_LOG.md 同批说明）：
--   - relationship_to_head：HouseholdHeadResolver / ChangeService 仍有写中文的 SQL，
--     月报与变更页按原值直接展示，改库会把中文变英文；
--   - nc_biz_college_students.education_level：该域用窄选项「本科/专科」，非 EducationLevels 字典；
--   - nc_biz_death_records.relationship_to_head：读取路径未逐一审计。
--
-- 幂等：条件命中才 UPDATE，重复执行影响 0 行。

BEGIN;

UPDATE nc_biz_applications SET marital_status  = 'Married'  WHERE marital_status  = '已婚';
UPDATE nc_biz_applications SET education_level = 'Primary'  WHERE education_level = '小学';
UPDATE nc_biz_applications SET political_status = 'Masses'   WHERE political_status = '群众';

UPDATE nc_biz_family_members SET marital_status  = 'Married'  WHERE marital_status  = '已婚';
UPDATE nc_biz_family_members SET education_level = 'Primary'  WHERE education_level = '小学';
UPDATE nc_biz_family_members SET political_status = 'Masses'   WHERE political_status = '群众';

COMMIT;
