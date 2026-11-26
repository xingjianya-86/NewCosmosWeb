export interface RoleItem {
  id: number
  name: string
  code: string
  description: string
  level: number
  dataScope: string
  userCount: number
}

export interface RoleSaveInput {
  name: string
  code?: string
  description: string
  level: number
  dataScope: string
}

export interface PermissionDefinition {
  code: string
  name: string
  module: string
  description: string
  sortOrder: number
}

export interface RolePermissions {
  roleId: number
  permissions: string[]
}

export const DATA_SCOPE_OPTIONS = [
  { value: 'SELF', label: '仅本人' },
  { value: 'ORG', label: '本组织' },
  { value: 'ORG_AND_CHILDREN', label: '本组织及下级' },
  { value: 'ALL', label: '全部数据' },
] as const

export function dataScopeLabel(value: string): string {
  return DATA_SCOPE_OPTIONS.find((item) => item.value === value)?.label ?? value
}
