/**
 * 全局照片中心：字段与 /api/photos 契约保持一致。
 *
 * 界面展示用字段（categoryLabel / personName / hasData）在接口缺失时由前端兜底，
 * 不在类型里做可选，避免每个调用点重复判空。
 */

/** 补传业务域过滤；空字符串 = 全部。 */
export type PhotoDomain =
  | ''
  | 'ElderlyBenefits'
  | 'SocialAssistance'
  | 'TempRelief'
  | 'AssetVerification'

/** 照片补全状态过滤；空字符串 = 全部。 */
export type PhotoBackfillStatus = '' | 'unuploaded' | 'partial' | 'uploaded'

/** 照片打印版式；空字符串 = 按默认（身份证原寸，其余整页）。 */
export type PhotoPrintLayout = '' | 'FULL_PAGE' | 'HALF_PAGE' | 'ID_CARD'

export interface PhotoCategory {
  code: string
  label: string
}

/* ───────────────────────── 人员补全 ───────────────────────── */

export interface PhotoBackfillItem {
  idCard: string
  personName: string
  /** 业务域代码（一个人员可同时属于多个域）。 */
  businessTypes: string[]
  /** 代表业务 ID，上传照片时写入 application_id 追溯。 */
  businessId: number | null
  /** 已归档照片张数（全类别合计）。 */
  uploadedCount: number
  /** 应传张数；接口未提供时为 null。 */
  requiredCount: number | null
  /** 未传张数；接口未提供且无法由应传数推算时为 null。 */
  missingCount: number | null
  /** 服务端状态原值（uploaded / partial / unuploaded …）。 */
  status: string
}

export interface PhotoBackfillQuery {
  domain?: PhotoDomain
  keyword?: string
  status?: PhotoBackfillStatus
  page?: number
  pageSize?: number
}

export interface PhotoBackfillPage {
  items: PhotoBackfillItem[]
  total: number
  page: number
  pageSize: number
  /** 数据范围受限/扫描截断时的说明（服务端可选返回）。 */
  scopeNote?: string
}

/* ───────────────────────── 照片查询 ───────────────────────── */

export interface PhotoRecord {
  id: number
  applicantIdCard: string
  applicationId: number | null
  category: string
  categoryLabel: string
  photoName: string
  fileSize: number
  imageFormat: string
  createdAt: string
  /** false = 服务端明确表示无图；null = 未知（按有图处理）。 */
  hasData: boolean | null
  personName: string
}

export interface PhotoQueryParams {
  keyword?: string
  category?: string
  page?: number
  pageSize?: number
}

export interface PhotoPage {
  items: PhotoRecord[]
  total: number
  page: number
  pageSize: number
}

export interface PhotoUploadInput {
  file: File
  category: string
  applicationId?: number | null
  businessType?: string
}

export interface PhotoUploadResult {
  id: number
}

export interface PhotoOcrResult {
  category: string
  name: string
  idCard: string
  gender: string
  birthDate: string
  address: string
  fields: Record<string, string>
}

/* ───────────────────────── 打印 / 导出 ───────────────────────── */

export interface PhotoPrintInput {
  photoIds: number[]
  layout?: PhotoPrintLayout
  bindingId?: number | null
  printerName?: string
}

export interface PhotoPrintFailure {
  photoId: number | null
  message: string
}

export interface PhotoPrintResult {
  id: number | null
  jobNo: string
  status: string
  successCount: number
  failures: PhotoPrintFailure[]
  message: string
}

/* ───────────────────────── 采集会话 ───────────────────────── */

export interface PhotoCapturePerson {
  idCard: string
  name: string
  /** 业务域代码数组；上传时取第一个作为 businessType。 */
  businessTypes: string[]
  businessId: number | null
}

export type PhotoCaptureOutcome = 'success' | 'skipped' | 'failed'

export interface PhotoCaptureSummary {
  totalPeople: number
  succeeded: number
  skipped: number
  failed: number
  uploadedPhotos: number
  failedPhotos: number
  skippedNames: string[]
  failedNames: string[]
}
