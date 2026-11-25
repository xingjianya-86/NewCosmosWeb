/**
 * 救助月报表 API。
 *
 * 后端端点（全部要求 REPORT_VIEW）：
 * - GET    /api/social-assistance/monthly-report/options
 * - GET    /api/social-assistance/monthly-report/summary?year=&month=&town=
 * - GET    /api/social-assistance/monthly-report/history?year=
 * - GET    /api/social-assistance/monthly-report/rows?year=&month=&town=&category=
 * - POST   /api/social-assistance/monthly-report/generate
 * - DELETE /api/social-assistance/monthly-report/{year}/{month}
 * - GET    /api/social-assistance/monthly-report/meeting?year=&month=&town=&specialApproval=
 * - POST   /api/social-assistance/monthly-report/meeting
 * - GET    /api/social-assistance/monthly-report/meeting-attendance?year=&month=&town=
 * - GET    /api/social-assistance/monthly-report/export?year=&month=&format=Excel|Csv
 *
 * 打印/Office 模板渲染不在 Server（无 COM / PC Agent 管线），
 * 页面只做数据结构化预览 + 汇总报表下载，不伪造打印结果。
 */

import type {
  MonthlyMeeting,
  MonthlyMeetingAttendance,
  MonthlyMeetingSaveRequest,
  MonthlyReportExportFormat,
  MonthlyReportExportResult,
  MonthlyReportGenerateRequest,
  MonthlyReportGenerateResult,
  MonthlyReportHistoryItem,
  MonthlyReportOptions,
  MonthlyReportRows,
  MonthlyReportSummary,
} from '@/types/monthlyReport'
import { API_PREFIX, apiHttp } from './http'
import { getStoredSessionId } from './session'
import { getDeviceId } from '@/utils/device'

const REPORT_BASE = '/social-assistance/monthly-report'

export interface MonthlyReportQuery {
  year: number
  month: number
  /** 空 / '全部' 表示不限乡镇。 */
  town?: string
}

export interface MonthlyReportRowsQuery extends MonthlyReportQuery {
  /** 空 = 全部（不按分类过滤）。 */
  category?: string
  /** 只取指定数据集（服务端裁剪重查询；空 = 全部）。 */
  datasets?: string[]
}

export function getMonthlyReportOptions(): Promise<MonthlyReportOptions> {
  return apiHttp
    .get<MonthlyReportOptions>(`${REPORT_BASE}/options`)
    .then((response) => response.data)
}

export function getMonthlyReportSummary(
  query: MonthlyReportQuery,
): Promise<MonthlyReportSummary> {
  return apiHttp
    .get<MonthlyReportSummary>(`${REPORT_BASE}/summary`, {
      params: {
        year: query.year,
        month: query.month,
        town: query.town?.trim() || undefined,
      },
    })
    .then((response) => response.data)
}

export function listMonthlyReportHistory(
  year?: number | null,
): Promise<MonthlyReportHistoryItem[]> {
  return apiHttp
    .get<MonthlyReportHistoryItem[]>(`${REPORT_BASE}/history`, {
      params: { year: year ?? undefined },
    })
    .then((response) => response.data)
}

export function getMonthlyReportRows(
  query: MonthlyReportRowsQuery,
): Promise<MonthlyReportRows> {
  return apiHttp
    .get<MonthlyReportRows>(`${REPORT_BASE}/rows`, {
      params: {
        year: query.year,
        month: query.month,
        town: query.town?.trim() || undefined,
        category: query.category?.trim() || undefined,
        datasets: query.datasets?.length ? query.datasets.join(',') : undefined,
      },
    })
    .then((response) => response.data)
}

export function generateMonthlyReport(
  request: MonthlyReportGenerateRequest,
): Promise<MonthlyReportGenerateResult> {
  return apiHttp
    .post<MonthlyReportGenerateResult>(`${REPORT_BASE}/generate`, request)
    .then((response) => response.data)
}

export function deleteMonthlyReport(
  year: number,
  month: number,
): Promise<{ deleted: boolean }> {
  return apiHttp
    .delete<{ deleted: boolean }>(`${REPORT_BASE}/${year}/${month}`)
    .then((response) => response.data)
}

export function getMonthlyReportMeeting(
  query: MonthlyReportQuery & { specialApproval?: boolean },
): Promise<MonthlyMeeting> {
  return apiHttp
    .get<MonthlyMeeting>(`${REPORT_BASE}/meeting`, {
      params: {
        year: query.year,
        month: query.month,
        town: query.town?.trim() || undefined,
        specialApproval: query.specialApproval ? true : undefined,
      },
    })
    .then((response) => response.data)
}

export function saveMonthlyReportMeeting(
  request: MonthlyMeetingSaveRequest,
): Promise<MonthlyMeeting> {
  return apiHttp
    .post<MonthlyMeeting>(`${REPORT_BASE}/meeting`, request)
    .then((response) => response.data)
}

export function getMonthlyMeetingAttendance(
  query: MonthlyReportQuery,
): Promise<MonthlyMeetingAttendance> {
  return apiHttp
    .get<MonthlyMeetingAttendance>(`${REPORT_BASE}/meeting-attendance`, {
      params: {
        year: query.year,
        month: query.month,
        town: query.town?.trim() || undefined,
      },
    })
    .then((response) => response.data)
}

function defaultFileName(year: number, month: number, format: MonthlyReportExportFormat): string {
  const suffix = format === 'Excel' ? 'xlsx' : 'csv'
  return `救助月报表_${year}-${String(month).padStart(2, '0')}.${suffix}`
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
      ? '没有权限导出救助月报表'
      : response.status === 404
        ? '该年月尚未生成月报，请先生成'
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
 * 下载月报汇总（Excel / CSV）。
 *
 * 直接使用 Core 的 IMonthlyReportService.ExportAsync 返回的字节（汇总口径：
 * 年份/月份/总档案/新增/停止/总金额），不做前端二次计算。
 */
export async function exportMonthlyReport(
  year: number,
  month: number,
  format: MonthlyReportExportFormat,
): Promise<MonthlyReportExportResult> {
  const params = new URLSearchParams({
    year: String(year),
    month: String(month),
    format,
  })

  const headers: Record<string, string> = {
    Accept:
      format === 'Excel'
        ? 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/zip'
        : 'text/csv,text/plain',
    'X-NC-Device': getDeviceId(),
  }
  const sessionId = getStoredSessionId()
  if (sessionId) {
    headers.Authorization = `Bearer ${sessionId}`
  }

  const response = await fetch(
    `${API_PREFIX}${REPORT_BASE}/export?${params.toString()}`,
    { method: 'GET', headers },
  )

  if (!response.ok) {
    throw new Error(await readErrorMessage(response))
  }

  const blob = await response.blob()
  const fileName =
    parseFileName(response.headers.get('Content-Disposition')) ?? defaultFileName(year, month, format)

  saveBlob(blob, fileName)
  return { fileName }
}
