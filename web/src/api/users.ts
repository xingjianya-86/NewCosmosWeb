import type {
  MyPermissions,
  UserCreateInput,
  UserListItem,
  UserOptions,
  UserPage,
  UserQuery,
  UserStats,
  UserUpdateInput,
} from '@/types/users'
import { apiHttp } from './http'

const BASE = '/system/users'

export function listUsers(params: UserQuery = {}): Promise<UserPage> {
  return apiHttp
    .get<UserPage>(BASE, {
      params: {
        keyword: params.keyword?.trim() || undefined,
        status: params.status && params.status !== 'all' ? params.status : undefined,
        role: params.role || undefined,
        page: params.page ?? 1,
        pageSize: params.pageSize ?? 20,
      },
    })
    .then((response) => response.data)
}

export function getUser(id: number): Promise<UserListItem> {
  return apiHttp.get<UserListItem>(`${BASE}/${id}`).then((response) => response.data)
}

export function createUser(input: UserCreateInput): Promise<{ id: number; message: string }> {
  return apiHttp.post<{ id: number; message: string }>(BASE, input).then((response) => response.data)
}

export function updateUser(
  id: number,
  input: UserUpdateInput,
): Promise<{ message: string }> {
  return apiHttp
    .put<{ message: string }>(`${BASE}/${id}`, input)
    .then((response) => response.data)
}

export function deleteUser(id: number): Promise<{ message: string }> {
  return apiHttp.delete<{ message: string }>(`${BASE}/${id}`).then((response) => response.data)
}

export function resetUserPassword(
  id: number,
  newPassword: string,
): Promise<{ message: string }> {
  return apiHttp
    .post<{ message: string }>(`${BASE}/${id}/reset-password`, { newPassword })
    .then((response) => response.data)
}

export function toggleUserStatus(
  id: number,
  isActive: boolean,
): Promise<{ message: string }> {
  return apiHttp
    .post<{ message: string }>(`${BASE}/${id}/toggle-status`, { isActive })
    .then((response) => response.data)
}

export function getUserStats(): Promise<UserStats> {
  return apiHttp.get<UserStats>(`${BASE}/stats`).then((response) => response.data)
}

export function getUserOptions(): Promise<UserOptions> {
  return apiHttp.get<UserOptions>(`${BASE}/options`).then((response) => response.data)
}

export function getMyPermissions(): Promise<MyPermissions> {
  return apiHttp.get<MyPermissions>(`${BASE}/my-permissions`).then((response) => response.data)
}
