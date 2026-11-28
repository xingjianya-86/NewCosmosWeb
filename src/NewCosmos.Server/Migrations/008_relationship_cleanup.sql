-- 20261128 家庭关系列值统一：历史中文关系值 → FamilyRelationships.item_key（权威见 nc_dict_items）
--
-- 背景：relationship_to_head 的统一存储口径是 item_key（Head/Spouse/Son…），
--       展示口径是 item_value（本人/户主、配偶、子/婿…），唯一换算点 Core DictValueMapper。
--       存量里混着直接写入的中文值（旧代码 HouseholdHeadResolver / ChangeService 的 SQL 曾写中文），
--       同一列两套词表，导致部分出口回显英文、部分回显中文。
--
-- 前置（本批已改代码，缺一不可）：
--   写路径统一写 key：HouseholdHeadResolver、FamilyMemberService、ApplicationService、
--                     ChangeService.DeathAndQueries、ChangeService.RebuildAndReview
--   展示出口统一过 DictValueMapper.ToFormRelationship：申请详情、月报家庭关系、快速核验、
--                     资产核对历史、死亡证明文书与搜索、变更审计 JSON、死亡登记写入
--   比较逻辑两套值都认：ChangeManagementQueryService.IsHeadMember、
--                     MonthlyReportService / DynamicManagementRecordService 的 SQL 户主判定
--
-- 范围：仅 nc_biz_family_members 与 nc_biz_death_records。
--       明确不动：nc_biz_college_students.education_level（窄选项域）、
--                 nc_biz_other_difficulty_persons.relationship（展示路径未审计）、
--                 nc_biz_monthly_classified_adds.health_status（月报历史快照）。
--
-- 幂等：条件命中才 UPDATE，重复执行影响 0 行。

BEGIN;

UPDATE nc_biz_family_members
SET relationship_to_head = 'Head'
WHERE relationship_to_head IN ('本人/户主', '本人', '户主');

UPDATE nc_biz_death_records
SET relationship_to_head = 'Head'
WHERE relationship_to_head IN ('本人/户主', '本人', '户主');

COMMIT;
