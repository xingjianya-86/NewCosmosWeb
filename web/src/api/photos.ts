import type {
  PhotoBackfillItem,
  PhotoBackfillPage,
  PhotoBackfillQuery,
  PhotoCategory,
  PhotoOcrResult,
  PhotoPage,
  PhotoPendingAssignInput,
  PhotoPendingAssignResult,
  PhotoPendingItem,
  PhotoPendingPage,
  PhotoPendingQuery,
  PhotoPrintFailure,
  PhotoPrintInput,
  PhotoPrintResult,
  PhotoQueryParams,
  PhotoRecord,
  PhotoUploadInput,
  PhotoUploadResult,
} from '@/types/photos'
import { apiHttp } from './http'

/* ───────────────────────── 通用归一化 ───────────────────────── */

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function toText(value: unknown): string {
  return typeof value === 'string'
    ? value
    : value === null || value === undefined
      ? ''
      : String(value)
}

function toNumber(value: unknown, fallback = 0): number {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function toNullableNumber(value: unknown): number | null {
  if (value === null || value === undefined || value === '') return null
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

function toBooleanOrNull(value: unknown): boolean | null {
  if (value === null || value === undefined) return null
  if (typeof value === 'boolean') return value
  if (typeof value === 'number') return value !== 0
  const text = toText(value).trim().toLowerCase()
  if (!text) return null
  if (['true', '1', 'yes', 'y'].includes(text)) return true
  if (['false', '0', 'no', 'n'].includes(text)) return false
  return null
}

function unwrapItems(data: unknown): unknown[] {
  if (Array.isArray(data)) return data
  const root = asRecord(data)
  if (!root) return []
  // 兼容服务端不同集合字段：items / photos / categories / 嵌套 data。
  for (const key of ['items', 'Items', 'photos', 'categories'] as const) {
    const value = root[key]
    if (Array.isArray(value)) return value
  }
  const nested = asRecord(root.data)
  if (nested) {
    for (const key of ['items', 'Items', 'photos', 'categories'] as const) {
      const value = nested[key]
      if (Array.isArray(value)) return value
    }
  }
  return []
}

/** 前端状态筛选值 → 服务端状态码。 */
function toServerStatus(status: string | null | undefined): string | undefined {
  switch ((status ?? '').trim().toLowerCase()) {
    case 'unuploaded':
    case 'missing':
      return 'missing'
    case 'partial':
      return 'partial'
    case 'uploaded':
    case 'complete':
      return 'complete'
    default:
      return undefined
  }
}

function unwrapPage(data: unknown, fallbackPage: number, fallbackSize: number) {
  const root = asRecord(data)
  const nested = root ? asRecord(root.data) : null
  const source = nested ?? root
  const items = unwrapItems(data)
  const total = source ? toNumber(source.totalCount ?? source.total, items.length) : items.length
  const page = source ? toNumber(source.page ?? source.pageIndex, fallbackPage) : fallbackPage
  const pageSize = source ? toNumber(source.pageSize, fallbackSize) : fallbackSize
  return { items, total, page: page > 0 ? page : fallbackPage, pageSize: pageSize > 0 ? pageSize : fallbackSize }
}

function splitBusinessTypes(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value.map((item) => toText(item).trim()).filter(Boolean)
  }
  const text = toText(value).trim()
  if (!text) return []
  return text
    .split(/[/,，、;；|]/)
    .map((item) => item.trim())
    .filter(Boolean)
}

/* ───────────────────────── 人员补全 ───────────────────────── */

function normalizeBackfillItem(value: unknown): PhotoBackfillItem | null {
  const raw = asRecord(value)
  if (!raw) return null

  const idCard = toText(raw.idCard ?? raw.id_card ?? raw.applicantIdCard).trim()
  const personName = toText(
    raw.personName ?? raw.person_name ?? raw.name ?? raw.applicantName,
  ).trim()
  if (!idCard && !personName) return null

  const uploadedCount = Math.max(
    0,
    toNumber(
      raw.uploadedCount ??
        raw.uploadedPhotoCount ??
        raw.photoCount ??
        raw.photo_count ??
        raw.uploaded,
    ),
  )
  const requiredCount = toNullableNumber(
    raw.requiredCount ?? raw.requiredPhotoCount ?? raw.expectedCount ?? raw.required,
  )
  const explicitMissing = toNullableNumber(
    raw.missingCount ?? raw.unuploadedCount ?? raw.pendingCount ?? raw.missing,
  )
  const missingCount =
    explicitMissing ?? (requiredCount !== null ? Math.max(0, requiredCount - uploadedCount) : null)

  return {
    idCard,
    personName: personName || '—',
    businessTypes: splitBusinessTypes(
      raw.businessTypes ?? raw.businessType ?? raw.businessTypeNames ?? raw.domains ?? raw.domain,
    ),
    businessId: toNullableNumber(raw.businessId ?? raw.applicationId ?? raw.business_id),
    uploadedCount,
    requiredCount,
    missingCount,
    status: toText(raw.status ?? raw.photoStatus ?? raw.uploadStatus).trim().toLowerCase(),
  }
}

/** 补传候选人员分页（域名/关键词/状态过滤）。 */
export function listPhotoBackfill(params: PhotoBackfillQuery = {}): Promise<PhotoBackfillPage> {
  const page = params.page ?? 1
  const pageSize = params.pageSize ?? 20
  return apiHttp
    .get<unknown>('/photos/backfill', {
      params: {
        domain: params.domain || undefined,
        keyword: params.keyword?.trim() || undefined,
        status: toServerStatus(params.status),
        page,
        pageSize,
      },
    })
    .then((response) => {
      const pageData = unwrapPage(response.data, page, pageSize)
      const root = asRecord(response.data)
      return {
        items: pageData.items
          .map(normalizeBackfillItem)
          .filter((item): item is PhotoBackfillItem => item !== null),
        total: pageData.total,
        page: pageData.page,
        pageSize: pageData.pageSize,
        scopeNote: toText(root?.scopeNote ?? root?.note).trim() || undefined,
      }
    })
}

/* ───────────────────────── 照片查询 ───────────────────────── */

function normalizePhoto(value: unknown): PhotoRecord | null {
  const raw = asRecord(value)
  if (!raw) return null

  const id = toNullableNumber(raw.id ?? raw.photoId ?? raw.photo_id)
  if (id === null) return null

  const category = toText(raw.photoCategory ?? raw.category ?? raw.categoryCode).trim()
  return {
    id,
    applicantIdCard: toText(raw.applicantIdCard ?? raw.idCard ?? raw.applicant_id_card).trim(),
    applicationId: toNullableNumber(raw.applicationId ?? raw.businessId ?? raw.application_id),
    category,
    categoryLabel: toText(
      raw.photoCategoryDisplay ??
        raw.categoryLabel ??
        raw.photoCategoryName ??
        raw.categoryName,
    ).trim(),
    photoName: toText(raw.photoName ?? raw.name ?? raw.fileName).trim(),
    fileSize: Math.max(0, toNumber(raw.fileSize ?? raw.size ?? raw.file_size)),
    imageFormat: toText(raw.imageFormat ?? raw.format ?? raw.contentType).trim(),
    createdAt: toText(raw.createdAt ?? raw.created_at ?? raw.uploadedAt).trim(),
    hasData: toBooleanOrNull(raw.hasData ?? raw.dataAvailable),
    personName: toText(raw.personName ?? raw.applicantName ?? raw.person_name).trim(),
  }
}

/** 全量照片分页查询（关键词 / 类别）。 */
export function listPhotos(params: PhotoQueryParams = {}): Promise<PhotoPage> {
  const page = params.page ?? 1
  const pageSize = params.pageSize ?? 20
  return apiHttp
    .get<unknown>('/photos/query', {
      params: {
        keyword: params.keyword?.trim() || undefined,
        category: params.category || undefined,
        page,
        pageSize,
      },
    })
    .then((response) => {
      const pageData = unwrapPage(response.data, page, pageSize)
      return {
        items: pageData.items
          .map(normalizePhoto)
          .filter((item): item is PhotoRecord => item !== null),
        total: pageData.total,
        page: pageData.page,
        pageSize: pageData.pageSize,
      }
    })
}

/** 某身份证名下照片；category 为空 = 全部类别。 */
export async function listPersonPhotos(
  idCard: string,
  category?: string,
): Promise<PhotoRecord[]> {
  const data = await apiHttp
    .get<unknown>(`/photos/person/${encodeURIComponent(idCard.trim())}`, {
      params: { category: category || undefined },
    })
    .then((response) => response.data)

  return unwrapItems(data)
    .map(normalizePhoto)
    .filter((item): item is PhotoRecord => item !== null)
}

/* ───────────────────────── 上传 / 内容 / 删除 ───────────────────────── */

/** 上传单人照片（multipart：file / category / applicationId / businessType）。 */
export function uploadPersonPhoto(
  idCard: string,
  input: PhotoUploadInput,
): Promise<PhotoUploadResult> {
  const form = new FormData()
  form.append('file', input.file)
  form.append('category', input.category)
  if (input.applicationId !== undefined && input.applicationId !== null) {
    form.append('applicationId', String(input.applicationId))
  }
  if (input.businessType) form.append('businessType', input.businessType)

  return apiHttp
    .post<unknown>(`/photos/person/${encodeURIComponent(idCard.trim())}`, form, {
      timeout: 120_000,
    })
    .then((response) => {
      const raw = asRecord(response.data) ?? {}
      return { id: toNumber(raw.id ?? raw.photoId) }
    })
}

/** 照片原图内容（Blob）；用于缩略图/预览，避免列表接口拉 Base64。 */
export function getPhotoContent(id: number, signal?: AbortSignal): Promise<Blob> {
  return apiHttp
    .get<Blob>(`/photos/${encodeURIComponent(String(id))}/content`, {
      responseType: 'blob',
      signal,
      timeout: 60_000,
    })
    .then((response) => response.data)
}

/** 身份证 OCR（multipart：file）。 */
export function recognizePhotoIdCard(file: File): Promise<PhotoOcrResult> {
  const form = new FormData()
  form.append('file', file)
  return apiHttp
    .post<unknown>('/photos/ocr', form, { timeout: 180_000 })
    .then((response) => {
      const raw = asRecord(response.data) ?? {}
      const fields: Record<string, string> = {}
      const rawFields = asRecord(raw.fields ?? raw.Fields)
      if (rawFields) {
        for (const [key, value] of Object.entries(rawFields)) {
          fields[key] = toText(value)
        }
      }

      return {
        category: toText(raw.category).trim(),
        name: toText(raw.name ?? fields['name'] ?? fields['姓名']).trim(),
        idCard: toText(raw.idCard ?? fields['idCard'] ?? fields['身份证号']).trim(),
        gender: toText(raw.gender ?? fields['gender'] ?? fields['性别']).trim(),
        birthDate: toText(raw.birthDate ?? fields['birthDate'] ?? fields['出生日期']).trim(),
        address: toText(raw.address ?? fields['address'] ?? fields['住址']).trim(),
        fields,
      }
    })
}

/** 删除照片（ARCHIVE_MANAGE）。 */
export function deletePhoto(id: number): Promise<void> {
  return apiHttp
    .delete(`/photos/${encodeURIComponent(String(id))}`)
    .then(() => undefined)
}

/* ───────────────────────── 待归属照片池 ───────────────────────── */

function normalizePendingPhoto(value: unknown): PhotoPendingItem | null {
  const raw = asRecord(value)
  if (!raw) return null

  const id = toNullableNumber(raw.id ?? raw.pendingId ?? raw.pending_id)
  if (id === null) return null

  return {
    id,
    category: toText(raw.category ?? raw.photoCategory).trim(),
    categoryLabel: toText(raw.categoryName ?? raw.photoCategoryName ?? raw.categoryLabel).trim(),
    photoName: toText(raw.photoName ?? raw.name ?? raw.fileName).trim(),
    fileSize: Math.max(0, toNumber(raw.fileSize ?? raw.size)),
    imageFormat: toText(raw.imageFormat ?? raw.format).trim(),
    recognizedName: toText(raw.recognizedName ?? raw.recognisedName).trim(),
    recognizedIdCard: toText(raw.recognizedIdCard ?? raw.recognisedIdCard).trim(),
    source: toText(raw.source).trim(),
    createdAt: toText(raw.createdAt).trim(),
    createdByName: toText(raw.createdByName ?? raw.uploadedBy).trim(),
  }
}

/** 待归属照片分页（ARCHIVE_VIEW）；不含图片二进制，缩略图走 getPendingPhotoContent。 */
export function listPendingPhotos(params: PhotoPendingQuery = {}): Promise<PhotoPendingPage> {
  const page = params.page ?? 1
  const pageSize = params.pageSize ?? 20
  return apiHttp
    .get<unknown>('/photos/pending', { params: { page, pageSize } })
    .then((response) => {
      const pageData = unwrapPage(response.data, page, pageSize)
      const root = asRecord(response.data)
      const nested = root ? asRecord(root.data) : null
      const source = nested ?? root
      return {
        items: pageData.items
          .map(normalizePendingPhoto)
          .filter((item): item is PhotoPendingItem => item !== null),
        total: pageData.total,
        page: pageData.page,
        pageSize: pageData.pageSize,
        available: toBooleanOrNull(source?.available) ?? true,
        message: toText(source?.message).trim() || undefined,
      }
    })
}

/** 待归属照片原图内容（Blob）；预览/缩略图用。 */
export function getPendingPhotoContent(id: number, signal?: AbortSignal): Promise<Blob> {
  return apiHttp
    .get<Blob>(`/photos/pending/${encodeURIComponent(String(id))}/content`, {
      responseType: 'blob',
      signal,
      timeout: 60_000,
    })
    .then((response) => response.data)
}

/** 归属待归属照片到已建档人员（ARCHIVE_MANAGE）：写正式照片档案并移出待归属池。 */
export function assignPendingPhoto(
  id: number,
  input: PhotoPendingAssignInput,
): Promise<PhotoPendingAssignResult> {
  const body: Record<string, unknown> = { idCard: input.idCard.trim() }
  if (input.name?.trim()) body.name = input.name.trim()
  if (input.category?.trim()) body.category = input.category.trim()
  if (input.applicationId !== undefined && input.applicationId !== null) {
    body.applicationId = input.applicationId
  }

  return apiHttp
    .post<unknown>(`/photos/pending/${encodeURIComponent(String(id))}/assign`, body, {
      timeout: 120_000,
    })
    .then((response) => {
      const raw = asRecord(response.data) ?? {}
      return {
        id: toNumber(raw.id ?? raw.photoId),
        photoName: toText(raw.photoName).trim(),
        fileSize: toNumber(raw.fileSize),
      }
    })
}

/** 删除待归属照片（ARCHIVE_MANAGE）。 */
export function deletePendingPhoto(id: number): Promise<void> {
  return apiHttp
    .delete(`/photos/pending/${encodeURIComponent(String(id))}`)
    .then(() => undefined)
}

/* ───────────────────────── 打印 / 导出 / 类别 ───────────────────────── */

function normalizePrintFailure(value: unknown): PhotoPrintFailure | null {
  if (typeof value === 'string') {
    return value.trim() ? { photoId: null, message: value.trim() } : null
  }
  const raw = asRecord(value)
  if (!raw) return null
  const message = toText(raw.message ?? raw.error ?? raw.reason).trim()
  const photoId = toNullableNumber(raw.photoId ?? raw.id)
  if (!message && photoId === null) return null
  return { photoId, message: message || `照片 #${photoId ?? '—'} 打印失败` }
}

/** 推送照片打印；返回任务号与逐项失败原因。 */
export function printPhotos(input: PhotoPrintInput): Promise<PhotoPrintResult> {
  const photoIds = input.photoIds.filter((id) => Number.isFinite(id) && id > 0)
  if (photoIds.length === 0) {
    return Promise.reject(new Error('请先选择要打印的照片'))
  }

  return apiHttp
    .post<unknown>(
      '/photos/print',
      {
        photoIds,
        layout: input.layout || undefined,
        bindingId: input.bindingId ?? undefined,
        printerName: input.printerName || undefined,
      },
      { timeout: 120_000 },
    )
    .then((response) => {
      const data = response.data

      // 兼容服务端返回“单任务对象 / 任务数组”两种形态。
      if (Array.isArray(data)) {
        const jobs = data.map(asRecord).filter((job): job is Record<string, unknown> => job !== null)
        const failures = jobs.flatMap((job) =>
          unwrapItems(job.failures ?? job.errors)
            .map(normalizePrintFailure)
            .filter((item): item is PhotoPrintFailure => item !== null),
        )
        const okCount = jobs.reduce(
          (sum, job) => sum + toNumber(job.successCount ?? job.okCount ?? job.printedCount),
          0,
        )
        return {
          id: toNullableNumber(jobs[0]?.id ?? jobs[0]?.jobId),
          jobNo: jobs
            .map((job) => toText(job.jobNo ?? job.taskNo))
            .filter(Boolean)
            .join('、'),
          status: toText(jobs[0]?.status) || 'Pending',
          successCount: okCount || Math.max(0, photoIds.length - failures.length),
          failures,
          message: toText(jobs[0]?.message),
        }
      }

      const raw = asRecord(data) ?? {}
      const job = asRecord(raw.job ?? raw.jobInfo) ?? raw
      const failures = unwrapItems(job.failures ?? job.errors ?? raw.failures ?? raw.errors)
        .map(normalizePrintFailure)
        .filter((item): item is PhotoPrintFailure => item !== null)
      const successCount = toNumber(
        job.successCount ?? job.okCount ?? job.printedCount ?? raw.successCount,
        Math.max(0, photoIds.length - failures.length),
      )
      // 服务端 /api/photos/print 返回 { okCount, jobNos, failures }。
      const jobNos = unwrapItems(job.jobNos ?? raw.jobNos)
        .map((value) => toText(value).trim())
        .filter(Boolean)

      return {
        id: toNullableNumber(job.id ?? job.jobId ?? raw.id),
        jobNo:
          jobNos.length > 0
            ? jobNos.join('、')
            : toText(job.jobNo ?? job.taskNo ?? raw.jobNo),
        status: toText(job.status ?? raw.status) || 'Pending',
        successCount,
        failures,
        message: toText(raw.message ?? job.message),
      }
    })
}

/** 导出勾选照片为 ZIP。 */
export function exportPhotoZip(ids: number[]): Promise<Blob> {
  return apiHttp
    .get<Blob>('/photos/export', {
      params: { ids: ids.join(',') },
      responseType: 'blob',
      timeout: 120_000,
    })
    .then((response) => response.data)
}

/** 照片类别字典。 */
export async function listPhotoCategories(): Promise<PhotoCategory[]> {
  const data = await apiHttp
    .get<unknown>('/photos/categories')
    .then((response) => response.data)

  return unwrapItems(data)
    .map((value): PhotoCategory | null => {
      if (typeof value === 'string') {
        const code = value.trim()
        return code ? { code, label: code } : null
      }
      const raw = asRecord(value)
      if (!raw) return null
      const code = toText(raw.code ?? raw.value).trim()
      if (!code) return null
      return {
        code,
        label: toText(raw.label ?? raw.name ?? raw.displayName).trim() || code,
      }
    })
    .filter((item): item is PhotoCategory => item !== null)
}
