import type {
  WorkflowListResponse,
  WorkflowPhoto,
  WorkflowSummary,
} from '@/types/applicationWorkflow'
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

/** 删除草稿/申请（仅草稿与已建档阶段可用）。 */
export async function deleteApplication(id: number): Promise<void> {
  await apiHttp.delete(`/applications/${encodeURIComponent(String(id))}`)
}

interface WorkflowPhotoListResponse {
  photos: WorkflowPhoto[]
}

/**
 * 某申请申请人的档案照片（按 applicant_id_card 跨申请聚合，id 倒序）。
 * 服务端已完成登录、INCOME_VIEW 与数据范围校验，越权/不存在返回 404。
 */
export function listWorkflowPhotos(applicationId: number): Promise<WorkflowPhoto[]> {
  return apiHttp
    .get<WorkflowPhotoListResponse>(
      `/social-assistance/applications/${encodeURIComponent(String(applicationId))}/photos`,
    )
    .then((response) => response.data?.photos ?? [])
}
