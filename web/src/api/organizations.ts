import type {
  OrganizationDetail,
  OrganizationImportResult,
  OrganizationOptions,
  OrganizationPreview,
  OrganizationSaveInput,
  OrganizationTreeResponse,
} from '@/types/organizations'
import { apiHttp } from './http'

const BASE = '/system/organizations'

export function getOrganizationTree(): Promise<OrganizationTreeResponse> {
  return apiHttp.get<OrganizationTreeResponse>(`${BASE}/tree`).then((response) => response.data)
}

export function getOrganization(id: number): Promise<OrganizationDetail> {
  return apiHttp.get<OrganizationDetail>(`${BASE}/${id}`).then((response) => response.data)
}

export function listOrganizationOptions(): Promise<OrganizationOptions> {
  return apiHttp.get<OrganizationOptions>(`${BASE}/options`).then((response) => response.data)
}

export function createOrganization(
  input: OrganizationSaveInput,
): Promise<{ id: number; message: string }> {
  return apiHttp
    .post<{ id: number; message: string }>(BASE, input)
    .then((response) => response.data)
}

export function updateOrganization(
  id: number,
  input: OrganizationSaveInput,
): Promise<{ message: string }> {
  return apiHttp
    .put<{ message: string }>(`${BASE}/${id}`, input)
    .then((response) => response.data)
}

export function deleteOrganization(id: number): Promise<{ message: string }> {
  return apiHttp.delete<{ message: string }>(`${BASE}/${id}`).then((response) => response.data)
}

export function previewOrganizationImport(file: File): Promise<OrganizationPreview> {
  const form = new FormData()
  form.append('file', file)
  return apiHttp
    .post<OrganizationPreview>(`${BASE}/preview-import`, form, { timeout: 120_000 })
    .then((response) => response.data)
}

export function importOrganizations(file: File): Promise<OrganizationImportResult> {
  const form = new FormData()
  form.append('file', file)
  return apiHttp
    .post<OrganizationImportResult>(`${BASE}/import`, form, { timeout: 300_000 })
    .then((response) => response.data)
}
