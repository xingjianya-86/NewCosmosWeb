/** 档案照片查询：字段与 /api/archives/photos 契约保持一致。 */

export interface ArchivePhotoCategory {
  code: string
  label: string
}

export interface ArchivePhotoListItem {
  id: number
  applicantIdCard: string
  photoCategory: string
  photoCategoryDisplay: string
  photoName: string
  fileSize: number
  imageFormat: string
  createdAt: string
  /** 列表接口附带：photo_data 是否非空；缺失时按有图处理。 */
  hasData?: boolean
}

export interface ArchivePhotoDetail extends ArchivePhotoListItem {
  /** 原图 Base64（可能不带 data: 前缀）。 */
  photoBase64: string
}

export interface ArchivePhotoListResponse {
  items: ArchivePhotoListItem[]
  total: number
  page: number
  pageSize: number
}

export interface ArchivePhotoQueryParams {
  keyword?: string
  category?: string
  page?: number
  pageSize?: number
}

export interface ArchivePhotoExportParams {
  keyword?: string
  category?: string
  /** 逗号分隔的照片 ID；为空表示导出当前查询全部。 */
  ids?: string
}

export interface ArchivePhotoUploadInput {
  idCard: string
  category: string
  file: File
}
