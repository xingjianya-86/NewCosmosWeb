import type {
  AssetTaskDetail,
  AssetTaskListResponse,
  AssetVerificationSummary,
} from '@/types/assetVerification'
import { apiHttp } from './http'

export interface ListAssetTasksParams {
  page?: number
  pageSize?: number
  status?: string
  keyword?: string
  /** YYYY-MM-DD */
  dateFrom?: string
  /** YYYY-MM-DD */
  dateTo?: string
}

export interface ListMonthlyAssetTasksParams {
  year?: number
  month?: number
  page?: number
  pageSize?: number
  status?: string
  keyword?: string
}

/** 核对模块首页统计（失败时各字段为 null，页面降级显示）。 */
export function getAssetVerificationSummary(): Promise<AssetVerificationSummary> {
  return apiHttp
    .get<AssetVerificationSummary>('/asset-verification/summary')
    .then((response) => response.data)
}

/** 快速核对任务列表（只读）。 */
export function listAssetTasks(
  params: ListAssetTasksParams = {},
): Promise<AssetTaskListResponse> {
  return apiHttp
    .get<AssetTaskListResponse>('/asset-verification/tasks', { params })
    .then((response) => response.data)
}

/** 核对任务详情（含同批次家庭成员）。 */
export function getAssetTask(id: number | string): Promise<AssetTaskDetail> {
  return apiHttp
    .get<AssetTaskDetail>(`/asset-verification/tasks/${encodeURIComponent(String(id))}`)
    .then((response) => response.data)
}

/** 月度审核列表（按 application_date 所在年月分页）。 */
export function listMonthlyAssetTasks(
  params: ListMonthlyAssetTasksParams = {},
): Promise<AssetTaskListResponse> {
  return apiHttp
    .get<AssetTaskListResponse>('/asset-verification/monthly', { params })
    .then((response) => response.data)
}
