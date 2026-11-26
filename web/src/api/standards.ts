import type {
  StandardActionResult,
  StandardConfig,
  StandardConfigRequest,
  StandardCreatedResponse,
  StandardOverview,
} from '@/types/standards'
import { apiHttp } from './http'

// apiHttp 的 baseURL 已包含 `/api`；后端路由为 `/api/system/standards/`。
const BASE = '/system/standards'

export function listStandards(type?: string): Promise<StandardConfig[]> {
  return apiHttp
    .get<StandardConfig[]>(`${BASE}/`, { params: type ? { type } : undefined })
    .then((response) => response.data ?? [])
}

export function getStandardOverview(): Promise<StandardOverview> {
  return apiHttp.get<StandardOverview>(`${BASE}/overview`).then((response) => response.data)
}

export function createStandard(
  request: StandardConfigRequest,
): Promise<StandardCreatedResponse> {
  return apiHttp.post<StandardCreatedResponse>(`${BASE}/`, request).then((response) => response.data)
}

export function updateStandard(
  id: number,
  request: StandardConfigRequest,
): Promise<StandardActionResult> {
  return apiHttp
    .put<StandardActionResult>(`${BASE}/${encodeURIComponent(String(id))}`, request)
    .then((response) => response.data)
}

export function deleteStandard(id: number): Promise<StandardActionResult> {
  return apiHttp
    .delete<StandardActionResult>(`${BASE}/${encodeURIComponent(String(id))}`)
    .then((response) => response.data)
}

export function syncStandardSchema(): Promise<StandardActionResult> {
  return apiHttp
    .post<StandardActionResult>(`${BASE}/sync-schema`)
    .then((response) => response.data)
}

export function seedStandardsFromYaml(): Promise<StandardActionResult> {
  return apiHttp.post<StandardActionResult>(`${BASE}/seed`).then((response) => response.data)
}
