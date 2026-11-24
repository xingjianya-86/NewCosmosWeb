import type {
  ApplicationDetail,
  ApplicationListResponse,
  ApplicationStatus,
} from '@/types/application'
import { apiHttp } from './http'

export interface ListApplicationsParams {
  page?: number
  pageSize?: number
  keyword?: string
  status?: ApplicationStatus
}

/** 社会救助申请列表（业务 API .2）。 */
export function listApplications(
  params: ListApplicationsParams = {},
): Promise<ApplicationListResponse> {
  return apiHttp
    .get<ApplicationListResponse>('/applications', { params })
    .then((response) => response.data)
}

/** 社会救助申请详情。 */
export function getApplication(id: number | string): Promise<ApplicationDetail> {
  return apiHttp
    .get<ApplicationDetail>(`/applications/${encodeURIComponent(String(id))}`)
    .then((response) => response.data)
}
