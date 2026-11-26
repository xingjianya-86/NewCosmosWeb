/** 文书模板管理：字段与 /api/template-admin 契约保持一致。 */

export interface TemplateCategoryNode {
  /** 自定义分类 id；内置分类为 null（后端旧版本可能不返回）。 */
  id: number | null
  label: string
  path: string
  /** 后端分类树附带；叶子节点为 true。 */
  isLeaf?: boolean
  /** 内置分类不可改名 / 删除；自定义分类为 false。 */
  isBuiltIn: boolean
  children?: TemplateCategoryNode[]
}

export interface TemplateAdminListItem {
  id: number
  name: string
  fileType: string
  docType: string
  categories: string[]
  sortOrder: number
  createdAt: string
  updatedAt: string | null
}

export interface TemplateAdminDetail extends TemplateAdminListItem {
  configJson: string
  /** 模板级默认份数（后端可能返回 copies 或 defaultCopies，前端两者兼容）。 */
  copies?: number
  defaultCopies?: number
  /** 分类级份数（config_json.copiesByClassification 的投影），分类命中时优先。 */
  copiesByClassification?: Record<string, number> | null
  /** 配置校验提示，非阻断。 */
  configWarnings?: string[] | null
}

export interface TemplateAdminCreateInput {
  file: File
  name: string
  docType: string
  categories: string[]
  configJson?: string
}

export interface TemplateAdminUpdateInput {
  file?: File
  name?: string
  docType?: string
  categories?: string[]
  configJson?: string
  copies?: number
}

export interface TemplateAdminCreateResult {
  id: number
  configWarnings?: string[] | null
}

export interface TemplateAdminMutationResult {
  configWarnings?: string[] | null
}

export interface TemplateCategoryCreateInput {
  parentPath: string | null
  label: string
}

export interface TemplateCategoryUpdateInput {
  label: string
}

export interface TemplateCategoryMutationResult {
  id: number
  path: string
}

export type TemplateMoveDirection = 'up' | 'down'
