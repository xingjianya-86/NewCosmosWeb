-- 20261128 其他困难台账人员表关系列统一：历史中文关系值 → FamilyRelationships.item_key
--
-- 背景：nc_biz_other_difficulty_persons.relationship 的存储口径是 item_key
--       （135 行里 132 行已是 Head/Spouse/Son/Daughter/Grandchild/Parent），
--       仅剩 3 行是 2026-11 转出台账快照（复制自当时仍为中文的 nc_biz_family_members）。
--       该 3 行 id_card = head_id_card，语义均为户主 → Head。
--
-- 展示出口已验证两套值都兼容，改前改后显示一致：
--   - ImportedArchiveService.RelationshipDisplaySql：命中 item_key 走字典，未命中原样回退
--   - AssetVerificationService → 前端 relationshipText：key 查表，中文原样透传
--   - OtherDifficultyLedgerService / ApplicationService / CollegeStudentService /
--     TempReliefService：只用 id_card、COUNT 或列名，不读本列
--
-- 幂等：条件命中才 UPDATE，重复执行影响 0 行。

BEGIN;

UPDATE nc_biz_other_difficulty_persons
SET relationship = 'Head', updated_at = NOW()
WHERE relationship IN ('本人/户主', '本人', '户主');

COMMIT;
