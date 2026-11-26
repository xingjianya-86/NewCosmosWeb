-- 2026-11-26 文书模板自定义分类节点
-- 口径：内置分类树（TemplateCategoryProvider）保持不变，本表仅存页面新增的自定义节点；
--       节点 path 为完整路径（如「低收入人口/业务操作/自定义」），parent_path 为父路径（根为空串）。
-- 说明：
--   1. CREATE TABLE / INDEX IF NOT EXISTS，重复执行零影响。
--   2. 与 nc_biz_templates.categories（text[] 路径数组）通过 path 字符串关联，改名/删除时联动维护。

BEGIN;

CREATE TABLE IF NOT EXISTS nc_biz_template_category_nodes (
  id BIGSERIAL PRIMARY KEY,
  parent_path VARCHAR(300) NOT NULL DEFAULT '',
  label VARCHAR(120) NOT NULL,
  path VARCHAR(400) NOT NULL UNIQUE,
  sort_order INT NOT NULL DEFAULT 0,
  created_at TIMESTAMP NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_nc_template_cat_parent ON nc_biz_template_category_nodes(parent_path);

COMMIT;
