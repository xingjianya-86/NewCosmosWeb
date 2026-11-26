import type {
  PermissionDefinition,
  RoleItem,
  RolePermissions,
  RoleSaveInput,
} from '@/types/roles'
import { apiHttp } from './http'

const BASE = '/system/roles'

export function listRoles(): Promise<RoleItem[]> {
  return apiHttp.get<RoleItem[]>(BASE).then((response) => response.data ?? [])
}

export function createRole(input: RoleSaveInput): Promise<{ id: number; message: string }> {
  return apiHttp
    .post<{ id: number; message: string }>(BASE, input)
    .then((response) => response.data)
}

export function updateRole(
  id: number,
  input: RoleSaveInput,
): Promise<{ message: string }> {
  return apiHttp
    .put<{ message: string }>(`${BASE}/${id}`, input)
    .then((response) => response.data)
}

export function deleteRole(id: number): Promise<{ message: string }> {
  return apiHttp.delete<{ message: string }>(`${BASE}/${id}`).then((response) => response.data)
}

export function getRolePermissions(id: number): Promise<RolePermissions> {
  return apiHttp
    .get<RolePermissions>(`${BASE}/${id}/permissions`)
    .then((response) => response.data)
}

export function saveRolePermissions(
  id: number,
  permissions: string[],
): Promise<{ message: string }> {
  return apiHttp
    .put<{ message: string }>(`${BASE}/${id}/permissions`, { permissions })
    .then((response) => response.data)
}

export function listPermissionDefinitions(): Promise<PermissionDefinition[]> {
  return apiHttp
    .get<PermissionDefinition[]>('/system/permissions')
    .then((response) => response.data ?? [])
}
