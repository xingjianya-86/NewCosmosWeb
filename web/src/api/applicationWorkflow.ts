import type { WorkflowListResponse, WorkflowSummary } from '@/types/applicationWorkflow'
import { apiHttp } from './http'

export interface ListWorkflowParams {
  tab: number
  page?: number
  pageSize?: number
  keyword?: string
}

/** 五阶段计数。 */
export function getWorkflowSummary(): Promise<WorkflowSummary> {
  return apiHttp
    .get<WorkflowSummary>('/social-assistance/workflow/summary')
    .then((response) => response.data)
}

/** 当前阶段分页列表。 */
export function listWorkflow(params: ListWorkflowParams): Promise<WorkflowListResponse> {
  return apiHttp
    .get<WorkflowListResponse>('/social-assistance/workflow', { params })
    .then((response) => response.data)
}

/** 删除草稿申请（仅草稿阶段可用）。 */
export async function deleteApplication(id: number): Promise<void> {
  await apiHttp.delete(`/applications/${encodeURIComponent(String(id))}`)
}
