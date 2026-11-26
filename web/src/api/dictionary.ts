import type {
  DictCategory,
  DictCategoryRequest,
  DictItem,
  DictItemRequest,
  DictionaryActionResult,
  DictionaryCreatedResponse,
} from '@/types/dictionary'
import { apiHttp } from './http'

// apiHttp 的 baseURL 已包含 `/api`，这里只写剩余路径。
const BASE = '/system/dictionary'

export function listDictCategories(): Promise<DictCategory[]> {
  return apiHttp
    .get<DictCategory[]>(`${BASE}/categories`)
    .then((response) => response.data ?? [])
}

export function createDictCategory(
  request: DictCategoryRequest,
): Promise<DictionaryCreatedResponse> {
  return apiHttp
    .post<DictionaryCreatedResponse>(`${BASE}/categories`, request)
    .then((response) => response.data)
}

export function updateDictCategory(
  id: number,
  request: DictCategoryRequest,
): Promise<DictionaryActionResult> {
  return apiHttp
    .put<DictionaryActionResult>(
      `${BASE}/categories/${encodeURIComponent(String(id))}`,
      request,
    )
    .then((response) => response.data)
}

export function deleteDictCategory(id: number): Promise<DictionaryActionResult> {
  return apiHttp
    .delete<DictionaryActionResult>(`${BASE}/categories/${encodeURIComponent(String(id))}`)
    .then((response) => response.data)
}

export function listDictItems(category: string): Promise<DictItem[]> {
  return apiHttp
    .get<DictItem[]>(`${BASE}/items`, { params: { category } })
    .then((response) => response.data ?? [])
}

export function createDictItem(request: DictItemRequest): Promise<DictionaryCreatedResponse> {
  return apiHttp
    .post<DictionaryCreatedResponse>(`${BASE}/items`, request)
    .then((response) => response.data)
}

export function updateDictItem(
  id: number,
  request: DictItemRequest,
): Promise<DictionaryActionResult> {
  return apiHttp
    .put<DictionaryActionResult>(`${BASE}/items/${encodeURIComponent(String(id))}`, request)
    .then((response) => response.data)
}

export function deleteDictItem(id: number): Promise<DictionaryActionResult> {
  return apiHttp
    .delete<DictionaryActionResult>(`${BASE}/items/${encodeURIComponent(String(id))}`)
    .then((response) => response.data)
}

export function syncDictSchema(): Promise<DictionaryActionResult> {
  return apiHttp.post<DictionaryActionResult>(`${BASE}/sync-schema`).then((response) => response.data)
}

export function seedDictFromYaml(): Promise<DictionaryActionResult> {
  return apiHttp.post<DictionaryActionResult>(`${BASE}/seed`).then((response) => response.data)
}
