export interface UserRoleRef {
  id: number
  name: string
  code: string
}

export interface UserListItem {
  id: number
  username: string
  fullName: string
  phone: string
  identityCard: string
  position: string
  organizationId: number | null
  organizationName: string
  isActive: boolean
  roles: UserRoleRef[]
  canBeManaged: boolean
  createdAt: string
}

export interface UserPage {
  items: UserListItem[]
  total: number
  pageIndex: number
  pageSize: number
}

export interface UserQuery {
  keyword?: string
  status?: 'all' | 'active' | 'inactive'
  role?: string
  page?: number
  pageSize?: number
}

export interface UserCreateInput {
  username: string
  password: string
  fullName: string
  phone: string
  identityCard: string
  position: string
  organizationId: number | null
  roleIds: number[]
}

export interface UserUpdateInput {
  fullName: string
  phone: string
  identityCard: string
  position: string
  organizationId: number | null
  isActive: boolean
  roleIds: number[]
}

export interface UserStats {
  activeUsers: number
  roleCount: number
}

export interface UserOptions {
  roles: UserRoleRef[]
  organizations: { id: number; name: string }[]
  positions: string[]
}

export interface MyPermissions {
  permissions: string[]
  isSuperAdmin: boolean
}
