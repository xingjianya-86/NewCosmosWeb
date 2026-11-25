import type {
  AssetTaskListItem,
  AssetTaskListResponse,
} from '@/types/assetVerification'
import { getDeviceId } from '@/utils/device'
import { listMonthlyAssetTasks } from './assetVerification'
import type { ListMonthlyAssetTasksParams } from './assetVerification'
import { API_PREFIX, ApiError, apiHttp } from './http'
import { getStoredSessionId } from './session'

export type { ListMonthlyAssetTasksParams }

/** 月度审核概览聚合上限：单页 100 条，最多扫描 20 页（2000 行）。 */
const OVERVIEW_PAGE_SIZE = 100
const OVERVIEW_MAX_PAGES = 20

/**
 * 月度核对概览（按户聚合）。
 *
 * Server 的 `/monthly` 只返回分页任务行，没有按状态聚合的字段；这里逐页读取当月任务，
 * 按户主身份证号（缺失时回退批次号）去重后计数，口径与旧桌面端
 * `AssetVerificationService.GetStatsByDateRangeAsync` 的“按户”统计保持一致。
 */
export interface MonthlyAuditOverview {
  submitted: number
  hasReport: number
  archived: number
  rejected: number
  total: number
}

export interface MonthlyAuditOverviewResult {
  /** 数据完整时返回概览；当月任务超过聚合上限时为 null，页面显示“—”。 */
  overview: MonthlyAuditOverview | null
  /** 已读取的任务行数。 */
  scannedCount: number
  /** Server 报告的当月任务行总数。 */
  totalCount: number
  /** false 表示未扫描完整月，概览不可用。 */
  complete: boolean
}

/** 月度审核任务列表（复用现有只读月度接口）。 */
export function listMonthlyAuditTasks(
  params: ListMonthlyAssetTasksParams = {},
): Promise<AssetTaskListResponse> {
  return listMonthlyAssetTasks(params)
}

/**
 * 聚合当月核对概览。
 *
 * 一次只读查询拿不到按户计数，因此顺序分页拉取；调用方应把失败视为“概览暂不可用”，
 * 不要把 null 当作“当月无人”。
 */
export async function getMonthlyAuditOverview(
  year: number,
  month: number,
): Promise<MonthlyAuditOverviewResult> {
  const households = new Map<string, AssetTaskListItem>()
  let page = 1
  let totalPages = 1
  let totalCount = 0
  let scannedCount = 0

  while (page <= totalPages && page <= OVERVIEW_MAX_PAGES) {
    const response = await listMonthlyAuditTasks({
      year,
      month,
      page,
      pageSize: OVERVIEW_PAGE_SIZE,
    })

    totalCount = response.totalCount ?? 0
    totalPages = response.totalPages ?? 0

    const rows = response.items ?? []
    scannedCount += rows.length

    for (const row of rows) {
      const key = householdKey(row)
      const current = households.get(key)
      // 同户优先保留户主行，与旧版 DISTINCT ON ... (applicant_id_card = head_id_card) 一致。
      if (!current || (row.isHead && !current.isHead)) {
        households.set(key, row)
      }
    }

    if (rows.length === 0) break
    page += 1
  }

  if (scannedCount < totalCount) {
    return { overview: null, scannedCount, totalCount, complete: false }
  }

  const counts: Record<string, number> = {}
  for (const household of households.values()) {
    const status = String(household.status ?? '')
    counts[status] = (counts[status] ?? 0) + 1
  }

  return {
    overview: {
      submitted: counts['0'] ?? 0,
      hasReport: counts['1'] ?? 0,
      archived: counts['2'] ?? 0,
      rejected: counts['3'] ?? 0,
      total: households.size,
    },
    scannedCount,
    totalCount,
    complete: true,
  }
}

function householdKey(item: AssetTaskListItem): string {
  const headIdCard = (item.headIdCardMasked ?? '').trim()
  if (headIdCard && headIdCard !== '—') return `head:${headIdCard}`

  const batchId = (item.batchId ?? '').trim()
  if (batchId && batchId !== '—') return `batch:${batchId}`

  return `id:${item.id}`
}

/* ── 核查照片 ─────────────────────────────────────────────────── */

/** 与 Core PhotoCategoryConstants 保持一致，供照片上传分类下拉使用。 */
export const ASSET_PHOTO_CATEGORY_OPTIONS: Array<{ value: string; label: string }> = [
  { value: 'PERSON_PHOTO', label: '本人照片/证件照' },
  { value: 'ID_CARD', label: '身份证' },
  { value: 'HOUSEHOLD_REGISTER', label: '户口本' },
  { value: 'MEDICAL_RECORD', label: '病历/诊断证明' },
  { value: 'DISABILITY_CERT', label: '残疾证' },
  { value: 'BANK_CARD', label: '银行卡/一卡通' },
  { value: 'INCOME_PROOF', label: '收入/务工证明' },
  { value: 'PUBLICITY_CHECK', label: '公示/核查照片' },
  { value: 'HOUSEHOLD_INSPECTION', label: '入户排查' },
  { value: 'OTHER_CERT', label: '其他证件照' },
]

export function photoCategoryText(category: string | null | undefined): string {
  const code = (category ?? '').trim()
  if (!code) return '未分类'
  return ASSET_PHOTO_CATEGORY_OPTIONS.find((option) => option.value === code)?.label ?? code
}

export interface AssetCheckPhoto {
  id: number
  category: string
  name: string
  format: string
  size: number
  /** 服务端返回的 Base64 数据（可能已带 data: 前缀）。 */
  base64: string
}

interface AssetCheckPhotoListResponse {
  photos: AssetCheckPhoto[]
}

/** 读取某条核对记录已归档的照片（Base64）。 */
export function listCheckPhotos(checkId: number | string): Promise<AssetCheckPhoto[]> {
  return apiHttp
    .get<AssetCheckPhotoListResponse>(
      `/asset-verification/checks/${encodeURIComponent(String(checkId))}/photos`,
    )
    .then((response) => response.data?.photos ?? [])
}

/** 上传一张核查照片到指定核对记录。 */
export function uploadCheckPhoto(
  checkId: number | string,
  file: File,
  category = 'PERSON_PHOTO',
): Promise<void> {
  const form = new FormData()
  form.append('file', file)
  form.append('category', category)

  return apiHttp
    .post(`/asset-verification/checks/${encodeURIComponent(String(checkId))}/photos`, form)
    .then(() => undefined)
}

/** 照片 Base64 → 可直接用于 img src 的 data URL。 */
export function photoDataUrl(photo: Pick<AssetCheckPhoto, 'base64' | 'format'>): string {
  const raw = (photo.base64 ?? '').trim()
  if (!raw) return ''
  if (raw.startsWith('data:')) return raw

  const format = (photo.format ?? '').trim().toLowerCase() || 'jpeg'
  const mime = format.includes('/')
    ? format
    : format === 'jpg' || format === 'jpeg'
      ? 'image/jpeg'
      : `image/${format}`
  return `data:${mime};base64,${raw}`
}

/* ── 核查报告版本与文件 ───────────────────────────────────────── */

export interface AssetReportVersion {
  id: number
  fileName: string
  reportDate: string
  version: number
  isCurrent: boolean
  size: number
}

interface AssetReportListResponse {
  reports: AssetReportVersion[]
}

/** 读取某条核对记录的报告版本列表。 */
export function listCheckReports(checkId: number | string): Promise<AssetReportVersion[]> {
  return apiHttp
    .get<AssetReportListResponse>(
      `/asset-verification/checks/${encodeURIComponent(String(checkId))}/reports`,
    )
    .then((response) => response.data?.reports ?? [])
}

/** 上传报告文件（PDF/图片），服务端会生成新版本。 */
export function uploadCheckReport(
  checkId: number | string,
  file: File,
  options: { signal?: AbortSignal } = {},
): Promise<void> {
  const form = new FormData()
  form.append('file', file)

  return apiHttp
    .post(
      `/asset-verification/checks/${encodeURIComponent(String(checkId))}/reports`,
      form,
      { signal: options.signal, timeout: 120_000 },
    )
    .then(() => undefined)
}

export interface AssetReportContent {
  blob: Blob
  contentType: string
  fileName: string
}

function readContentDispositionFileName(header: string | null): string {
  if (!header) return ''
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header)?.[1]
  if (encoded) {
    try {
      return decodeURIComponent(encoded.trim())
    } catch {
      return encoded.trim()
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(header)?.[1]
  return plain?.trim() ?? ''
}

/**
 * 下载报告原件字节。
 *
 * 报告内容接口返回二进制，不能走 axios 的 JSON 拦截器，这里用 fetch 手工带会话头；
 * 401/403/404 等错误仍按 ApiError 抛出，页面统一用中文提示。
 */
export async function fetchCheckReportContent(
  reportId: number | string,
  options: { signal?: AbortSignal } = {},
): Promise<AssetReportContent> {
  const headers: Record<string, string> = {
    Accept: 'application/pdf,image/*,application/octet-stream,*/*',
    'X-NC-Device': getDeviceId(),
  }
  const sessionId = getStoredSessionId()
  if (sessionId) headers.Authorization = `Bearer ${sessionId}`

  let response: Response
  try {
    response = await fetch(
      `${API_PREFIX}/asset-verification/reports/${encodeURIComponent(String(reportId))}/content`,
      { headers, signal: options.signal },
    )
  } catch (error) {
    if (options.signal?.aborted) throw error
    throw new ApiError('报告文件下载失败，请检查网络后重试', {
      code: 'NETWORK_ERROR',
      status: 0,
    })
  }

  if (!response.ok) {
    let message = ''
    const contentType = response.headers.get('content-type') ?? ''
    if (contentType.includes('application/json')) {
      try {
        const body = (await response.json()) as { message?: unknown } | null
        if (body && typeof body.message === 'string') message = body.message
      } catch {
        // 忽略无法解析的错误体
      }
    }

    if (!message) {
      message =
        response.status === 401
          ? '登录状态已失效，请重新登录'
          : response.status === 404
            ? '报告文件不存在或已删除'
            : `报告文件下载失败（HTTP ${response.status}）`
    }

    throw new ApiError(message, {
      code: `HTTP_${response.status}`,
      status: response.status,
    })
  }

  const blob = await response.blob()
  return {
    blob,
    contentType: response.headers.get('content-type') ?? blob.type ?? '',
    fileName: readContentDispositionFileName(response.headers.get('content-disposition')),
  }
}

/** 驳回（不予认定）一条“有报告未建档”的核对记录。 */
export function rejectAssetCheck(checkId: number | string): Promise<void> {
  return apiHttp
    .post(`/asset-verification/checks/${encodeURIComponent(String(checkId))}/reject`)
    .then(() => undefined)
}

/**
 * 推送报告打印。
 *
 * 服务端当前返回 501（打印链路待接入），页面按钮保持禁用；保留此函数以便后端就绪后直接开放。
 */
export function printCheckReport(reportId: number | string): Promise<void> {
  return apiHttp
    .post(
      `/asset-verification/reports/${encodeURIComponent(String(reportId))}/print`,
    )
    .then(() => undefined)
}
