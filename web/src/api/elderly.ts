import type {
  ElderlyApplicationDetail,
  ElderlyApplicationListResponse,
  ElderlyPendingResponse,
  ElderlyReviewListResponse,
  ElderlySummary,
} from '@/types/elderly'
import { apiHttp } from './http'

export interface ListElderlyApplicationsParams {
  page?: number
  pageSize?: number
  /** 空 = 全部（Draft/Confirmed/Stopped）。 */
  status?: string
  keyword?: string
}

export interface ListElderlyReviewsParams {
  page?: number
  pageSize?: number
  /** 空 = 全部（Pending/Completed）。 */
  status?: string
  keyword?: string
}

/** 高龄模块概览（业务 API .2）。 */
export function getElderlySummary(): Promise<ElderlySummary> {
  return apiHttp
    .get<ElderlySummary>('/elderly/summary')
    .then((response) => response.data)
}

/** 高龄津贴登记分页列表。 */
export function listElderlyApplications(
  params: ListElderlyApplicationsParams = {},
): Promise<ElderlyApplicationListResponse> {
  return apiHttp
    .get<ElderlyApplicationListResponse>('/elderly/applications', { params })
    .then((response) => response.data)
}

/** 高龄津贴登记详情（只读）。 */
export function getElderlyApplication(id: number | string): Promise<ElderlyApplicationDetail> {
  return apiHttp
    .get<ElderlyApplicationDetail>(`/elderly/applications/${encodeURIComponent(String(id))}`)
    .then((response) => response.data)
}

/** 下月待办（待新增 / 需停旧增新）。 */
export function getElderlyPending(): Promise<ElderlyPendingResponse> {
  return apiHttp
    .get<ElderlyPendingResponse>('/elderly/pending')
    .then((response) => response.data)
}

/** 高龄津贴类别复核分页列表。 */
export function listElderlyReviews(
  params: ListElderlyReviewsParams = {},
): Promise<ElderlyReviewListResponse> {
  return apiHttp
    .get<ElderlyReviewListResponse>('/elderly/reviews', { params })
    .then((response) => response.data)
}
