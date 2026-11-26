import type {
  ArchivePhotoCategory,
  ArchivePhotoDetail,
  ArchivePhotoExportParams,
  ArchivePhotoListResponse,
  ArchivePhotoQueryParams,
  ArchivePhotoUploadInput,
} from '@/types/archivePhoto'
import { apiHttp } from './http'

/** 档案照片分页列表（读权限 ARCHIVE_VIEW）。 */
export function listArchivePhotos(
  params: ArchivePhotoQueryParams = {},
): Promise<ArchivePhotoListResponse> {
  return apiHttp
    .get<ArchivePhotoListResponse>('/archives/photos', { params })
    .then((response) => response.data)
}

/** 照片详情（含 Base64 原图）。 */
export function getArchivePhoto(
  id: number | string,
  signal?: AbortSignal,
): Promise<ArchivePhotoDetail> {
  return apiHttp
    .get<ArchivePhotoDetail>(`/archives/photos/${encodeURIComponent(String(id))}`, { signal })
    .then((response) => response.data)
}

/** 照片类别字典。 */
export function getArchivePhotoCategories(): Promise<ArchivePhotoCategory[]> {
  return apiHttp
    .get<ArchivePhotoCategory[]>('/archives/photos/categories')
    .then((response) => response.data ?? [])
}

/** 插入照片（multipart：idCard / category / file）。 */
export function uploadArchivePhoto(input: ArchivePhotoUploadInput): Promise<{ id: number }> {
  const form = new FormData()
  form.append('idCard', input.idCard)
  form.append('category', input.category)
  form.append('file', input.file)
  return apiHttp
    .post<{ id: number }>('/archives/photos', form, { timeout: 120_000 })
    .then((response) => response.data)
}

/** 删除照片（写权限 ARCHIVE_MANAGE）。 */
export function deleteArchivePhoto(id: number | string): Promise<void> {
  return apiHttp
    .delete(`/archives/photos/${encodeURIComponent(String(id))}`)
    .then(() => undefined)
}

/** 导出 ZIP（导出全部 / 勾选照片）。 */
export function exportArchivePhotos(params: ArchivePhotoExportParams = {}): Promise<Blob> {
  return apiHttp
    .get<Blob>('/archives/photos/export', {
      params,
      responseType: 'blob',
      timeout: 120_000,
    })
    .then((response) => response.data)
}
