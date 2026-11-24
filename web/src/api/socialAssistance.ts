import type {
  GracePeriodListResponse,
  SocialAssistanceModuleSummary,
} from '@/types/socialAssistance'
import { apiHttp } from './http'

export interface ListGracePeriodsParams {
  page?: number
  pageSize?: number
  keyword?: string
}

/** 低收入模块首页统计（INCOME_VIEW）。 */
export function getSocialAssistanceSummary(): Promise<SocialAssistanceModuleSummary> {
  return apiHttp
    .get<SocialAssistanceModuleSummary>('/social-assistance/summary')
    .then((response) => response.data)
}

/** 渐退期列表（进行中 + 已到期，只读；INCOME_VIEW + 数据范围）。 */
export function listGracePeriods(
  params: ListGracePeriodsParams = {},
): Promise<GracePeriodListResponse> {
  return apiHttp
    .get<GracePeriodListResponse>('/social-assistance/grace-periods', { params })
    .then((response) => response.data)
}
