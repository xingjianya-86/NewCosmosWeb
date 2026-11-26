/**
 * 数据中心 · 数据字典管理。
 * 字段与 src/NewCosmos.Server/Contracts/DictionaryContracts.cs 保持一致。
 */

/** 字典分类（种子表 + 更新表合并视图）。 */
export interface DictCategory {
  id: number
  category: string
  displayName: string
  description: string
  sortOrder: number
  isActive: boolean
  status: string
  source: string
  action: string
  updatedAt: string | null
  updatedBy: number | null
}

/** 字典项。 */
export interface DictItem {
  id: number
  category: string
  itemKey: string
  itemValue: string
  sortOrder: number
  isActive: boolean
  description: string
  status: string
  source: string
  action: string
  updatedAt: string | null
  updatedBy: number | null
}

/** 新增/编辑字典分类请求。 */
export interface DictCategoryRequest {
  category: string
  displayName: string
  description: string
  sortOrder: number
  isActive: boolean
}

/** 新增/编辑字典项请求；category 固定为当前分类键。 */
export interface DictItemRequest {
  category: string
  itemKey: string
  itemValue: string
  sortOrder: number
  isActive: boolean
  description: string
}

/** 新增成功后返回服务端分配的 Id。 */
export interface DictionaryCreatedResponse {
  id: number
  message: string
}

/** 编辑/删除/同步/初始化结果。 */
export interface DictionaryActionResult {
  message: string
}
