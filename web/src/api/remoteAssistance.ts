import type { ApplicationListResponse } from '@/types/application'
import { apiHttp } from './http'

/* ── 异地协助 API ────────────────────────────────────────────── */

/** 异地协助列表分页。 */
export interface RemoteAssistanceListParams {
  page?: number
  pageSize?: number
  keyword?: string
  status?: string
}

export function listRemoteAssistance(
  params: RemoteAssistanceListParams = {},
): Promise<ApplicationListResponse> {
  return apiHttp
    .get<ApplicationListResponse>('/applications/remote-assistance', { params })
    .then((response) => response.data)
}

/** 异地协助办结返回。 */
export interface RemoteAssistanceCompleteResult {
  status: string
  currentStep: number
}

/** 异地协助办结（将状态置为 RemoteAssistanceCompleted，终态只读）。 */
export function completeRemoteAssistance(
  id: number | string,
): Promise<RemoteAssistanceCompleteResult> {
  return apiHttp
    .post<RemoteAssistanceCompleteResult>(
      `/applications/${encodeURIComponent(String(id))}/remote-complete`,
    )
    .then((response) => response.data)
}
