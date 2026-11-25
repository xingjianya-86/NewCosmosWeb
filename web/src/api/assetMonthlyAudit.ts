import type {
  AssetTaskListItem,
  AssetTaskListResponse,
} from '@/types/assetVerification'
import { listMonthlyAssetTasks } from './assetVerification'
import type { ListMonthlyAssetTasksParams } from './assetVerification'

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
