/** 文书模板管理：字段与 /api/template-admin 契约保持一致。 */

export interface TemplateCategoryNode {
  label: string
  path: string
  /** 后端分类树附带；叶子节点为 true。 */
  isLeaf?: boolean
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

export type TemplateMoveDirection = 'up' | 'down'
