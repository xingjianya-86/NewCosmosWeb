/**
 * 核对月报表（含周报）类型与预留导出接口。
 *
 * 现状：Server 端只有 summary / tasks / monthly 只读查询，尚无月报导出端点。
 * 因此这里只定义调用契约，exportMonthlyReport 不请求任何不存在的路径。
 * 后端提供导出接口后，仅需在此处替换实现，页面无需改动。
 */

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

/** 后端接入后返回可下载的文件信息。 */
export interface MonthlyReportExportResult {
  fileName: string
  downloadUrl: string
}

export const MONTHLY_REPORT_EXPORT_PENDING_MESSAGE = '导出接口待接入'

/** 导出端点尚未接入时抛出，页面据此给出“导出接口待接入”提示而不是伪造下载。 */
export class MonthlyReportExportPendingError extends Error {
  constructor(message: string = MONTHLY_REPORT_EXPORT_PENDING_MESSAGE) {
    super(message)
    this.name = 'MonthlyReportExportPendingError'
  }
}

export function isMonthlyReportExportPending(
  error: unknown,
): error is MonthlyReportExportPendingError {
  return error instanceof MonthlyReportExportPendingError
}

/**
 * 预留：按周期 / 全局导出核查人员名册。
 *
 * 当前后端没有对应端点，调用直接抛出 Pending 错误，不发起 HTTP 请求、不生成文件。
 */
export async function exportMonthlyReport(
  _request: MonthlyReportExportRequest,
): Promise<MonthlyReportExportResult> {
  throw new MonthlyReportExportPendingError()
}
