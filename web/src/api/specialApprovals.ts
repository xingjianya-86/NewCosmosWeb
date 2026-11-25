import { apiHttp } from './http'

export type SpecialApprovalStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected'

/** 一事一议申报表 + 最近一次会议审议结果（后端 SpecialApprovalRecord）。 */
export interface SpecialApprovalRecord {
  id: number
  applicationId: number
  specialApprovalId?: number | null
  formNo: string

  basicSituation: string
  specialMatters: string
  templateKey: string
  reportUnit: string
  handlerName: string
  formDate?: string | null
  auditResult: string
  auditDate?: string | null
  status: SpecialApprovalStatus
  statusText: string
  createdBy: string
  createdAt: string
  updatedAt: string

  meetingDate?: string | null
  meetingMembers: string
  overrideClassification: string
  approverName: string
  approverTitle: string
  approverOrganization: string
  specialReason?: string
  specialCircumstances?: string
  reviewCreatedBy?: string
  reviewedAt?: string | null

  isSubmitted: boolean
  isReviewed: boolean
  approved?: boolean | null
}

export interface SpecialApprovalSavePayload {
  basicSituation: string
  specialMatters: string
  templateKey: string
  reportUnit: string
  handlerName: string
  formDate: string | null
  auditResult: string
  auditDate: string | null
}

export interface SpecialApprovalCreatePayload extends SpecialApprovalSavePayload {
  applicationId: number
}

/** review 请求体与后端契约一一对应。 */
export interface SpecialApprovalReviewPayload {
  approved: boolean
  classification?: string
  meetingDate?: string
  attendees?: string
  approverName?: string
  approverPosition?: string
  approverUnit?: string
}

function specialApprovalUrl(id: number | string): string {
  return `/special-approvals/${encodeURIComponent(String(id))}`
}

/** 发起一事一议（新建草稿）。 */
export function createSpecialApproval(
  payload: SpecialApprovalCreatePayload,
): Promise<SpecialApprovalRecord> {
  return apiHttp
    .post<SpecialApprovalRecord>('/special-approvals', payload)
    .then((response) => response.data)
}

/** 按申报表 id 读取。 */
export function getSpecialApproval(id: number | string): Promise<SpecialApprovalRecord> {
  return apiHttp
    .get<SpecialApprovalRecord>(specialApprovalUrl(id))
    .then((response) => response.data)
}

/** 按申请 id 读取已有申报表；不存在时抛 404 ApiError。 */
export function getSpecialApprovalByApplication(
  applicationId: number | string,
): Promise<SpecialApprovalRecord> {
  return apiHttp
    .get<SpecialApprovalRecord>(
      `/applications/${encodeURIComponent(String(applicationId))}/special-approval`,
    )
    .then((response) => response.data)
}

/** 保存草稿。 */
export function updateSpecialApproval(
  id: number | string,
  payload: SpecialApprovalSavePayload,
): Promise<SpecialApprovalRecord> {
  return apiHttp
    .put<SpecialApprovalRecord>(specialApprovalUrl(id), payload)
    .then((response) => response.data)
}

/** 提交申报。 */
export function submitSpecialApproval(id: number | string): Promise<SpecialApprovalRecord> {
  return apiHttp
    .post<SpecialApprovalRecord>(`${specialApprovalUrl(id)}/submit`)
    .then((response) => response.data)
}

/** 会议审议（通过/驳回）。 */
export function reviewSpecialApproval(
  id: number | string,
  payload: SpecialApprovalReviewPayload,
): Promise<SpecialApprovalRecord> {
  return apiHttp
    .post<SpecialApprovalRecord>(`${specialApprovalUrl(id)}/review`, payload)
    .then((response) => response.data)
}
