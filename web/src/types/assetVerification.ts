/** 家庭经济状况核对：字段与 Server 端 AssetVerificationContracts 保持一致。 */

export type AssetCheckStatus = '0' | '1' | '2' | '3'

export interface AssetVerificationSummary {
  pendingReportCount: number | null
  monthlyCompleted: number | null
  monthlyNew: number | null
  yearTotal: number | null
}

export interface AssetTaskListItem {
  id: number
  batchId: string
  archiveName: string
  idCardMasked: string
  relationship: string
  isHead: boolean
  headIdCardMasked: string
  familyAddress: string
  community: string
  applicationDate: string
  status: AssetCheckStatus | string
  statusText: string
  contactPhoneMasked: string
}

export interface AssetTaskListResponse {
  items: AssetTaskListItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface AssetTaskMember {
  id: number
  applicantName: string
  idCard: string
  relationship: string
  isHead: boolean
}

export interface AssetTaskDetail {
  id: number
  batchId: string
  applicantName: string
  applicantIdCard: string
  applicantIdType: string
  relationship: string
  isHead: boolean
  headIdCard: string
  familyAddress: string
  community: string
  applicationReason: string
  applicationDate: string
  contactPhone: string
  status: AssetCheckStatus | string
  statusText: string
  createdAt: string
  updatedAt: string
  members: AssetTaskMember[]
}
