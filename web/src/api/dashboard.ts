import type { DashboardSummary } from '@/types/dashboard'
import { apiHttp } from './http'

/** 首页概览统计（登录即可访问）。 */
export function getDashboardSummary(): Promise<DashboardSummary> {
  return apiHttp.get<DashboardSummary>('/dashboard/summary').then((response) => response.data)
}
