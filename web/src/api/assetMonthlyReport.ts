/**
 * 核对月报表（含周报）类型与导出接口。
 *
 * 后端端点：GET /api/asset-verification/monthly/export
 * - 月报传 year + month（后端按 A 线核查周期 [上月11日, 本月11日) 过滤）；
 * - 周报传 weekStart + weekEnd（结束日含当天）；
 * - 全局导出传 allMonths=true（不限月份）；
 * - status 为空表示全部状态，onlyCurrent=true 表示不展开整户。
 */

import { API_PREFIX } from './http'
import { getStoredSessionId } from './session'
import { getDeviceId } from '@/utils/device'

export type MonthlyReportPeriodMode = 'monthly' | 'weekly'

/** 与 nc_biz_asset_checks.status 一致：0 已提交 / 1 有报告未建档 / 2 已建档 / 3 不予认定。 */
export type MonthlyReportStatus = '0' | '1' | '2' | '3'

/** 报表周期。monthly = A 线月周期；weekly = 2026-08 起的自然周。 */
export interface MonthlyReportPeriod {
  mode: MonthlyReportPeriodMode
  year: number
  month: number
  /** ISO 周序号；过渡周为 0；月报模式为 null。 */
  weekNumber: number | null
  /** 起始日（含），YYYY-MM-DD。 */
  startDate: string
  /** 结束日（含），YYYY-MM-DD。 */
  endDate: string
  /** 展示文案：2026年11月 / 2026年第47周 / 2026年过渡周。 */
  label: string
}

/** 统计值未知时统一为 null，由页面显示“—”。 */
export interface MonthlyReportStats {
  submittedCount: number | null
  hasReportCount: number | null
  archivedCount: number | null
  rejectedCount: number | null
  totalCount: number | null
}

/** period = 按所选周期导出；all = 全局导出（不限月份）。 */
export type MonthlyReportExportScope = 'period' | 'all'

export interface MonthlyReportExportRequest {
  period: MonthlyReportPeriod
  scope: MonthlyReportExportScope
  /** period 范围可空表示全部状态；all 范围必须指定状态。 */
  status: MonthlyReportStatus | null
  /** true = 仅导出当前状态人员；false = 展开整户名册。 */
  onlyCurrentStatusPersons: boolean
}

/** 导出完成后返回浏览器实际保存的文件名。 */
export interface MonthlyReportExportResult {
  fileName: string
}

const EXPORT_PATH = `${API_PREFIX}/asset-verification/monthly/export`

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function defaultFileName(request: MonthlyReportExportRequest): string {
  if (request.scope === 'all') return '核对月报表_全部月份.xlsx'

  const { period } = request
  if (period.mode === 'weekly') {
    const label = period.label.replace(/[\\/:*?"<>|]/g, '_')
    return `核对周报表_${label}.xlsx`
  }

  return `核对月报表_${period.year}-${pad(period.month)}.xlsx`
}

/** 解析 Content-Disposition：优先 filename*（RFC 5987），其次 filename。 */
function parseFileName(header: string | null): string | null {
  if (!header) return null

  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header)
  if (encoded?.[1]) {
    try {
      return decodeURIComponent(encoded[1].trim().replace(/^"|"$/g, ''))
    } catch {
      // 编码异常时回退普通 filename
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(header)
  return plain?.[1]?.trim() || null
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { message?: unknown } | null
    if (body && typeof body.message === 'string' && body.message.trim()) {
      return body.message.trim()
    }
  } catch {
    // 非 JSON 错误体，使用状态码兜底
  }

  return response.status === 401
    ? '登录状态已失效，请重新登录'
    : response.status === 403
      ? '没有权限导出核对月报表'
      : `导出失败（HTTP ${response.status}）`
}

function saveBlob(blob: Blob, fileName: string): void {
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000)
}

/**
 * 按周期 / 全局导出核查人员名册（xlsx）。
 *
 * 使用 fetch 以便携带认证头并直接拿到文件流；失败时抛出带后端 message 的 Error，
 * 页面用 getErrorMessage 展示即可。
 */
export async function exportMonthlyReport(
  request: MonthlyReportExportRequest,
): Promise<MonthlyReportExportResult> {
  const params = new URLSearchParams()

  if (request.scope === 'all') {
    params.set('allMonths', 'true')
  } else if (request.period.mode === 'weekly') {
    params.set('weekStart', request.period.startDate)
    params.set('weekEnd', request.period.endDate)
  } else {
    params.set('year', String(request.period.year))
    params.set('month', String(request.period.month))
  }

  if (request.status) {
    params.set('status', request.status)
  }
  params.set('onlyCurrent', request.onlyCurrentStatusPersons ? 'true' : 'false')

  const headers: Record<string, string> = {
    Accept:
      'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/zip',
    'X-NC-Device': getDeviceId(),
  }
  const sessionId = getStoredSessionId()
  if (sessionId) {
    headers.Authorization = `Bearer ${sessionId}`
  }

  const response = await fetch(`${EXPORT_PATH}?${params.toString()}`, {
    method: 'GET',
    headers,
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response))
  }

  const blob = await response.blob()
  const fileName =
    parseFileName(response.headers.get('Content-Disposition')) ?? defaultFileName(request)

  saveBlob(blob, fileName)
  return { fileName }
}
