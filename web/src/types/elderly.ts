/** 高龄津贴发放（只读）：字段与 src/NewCosmos.Server/Contracts/ElderlyContracts.cs 保持一致。 */

export type ElderlyApplicationStatus = 'Draft' | 'Confirmed' | 'Stopped'

/** 概览统计；某个统计块失败时字段为 null（前端显示「—」）。 */
export interface ElderlySummary {
  activeCount: number | null
  monthlyNew: number | null
  pendingNew: number | null
  pendingTransfer: number | null
}

export interface ElderlyApplicationListItem {
  id: number
  applicationNo: string
  name: string
  idCardMasked: string
  gender: string
  birthDate: string | null
  age: number | null
  category: string
  categoryText: string
  status: string
  statusText: string
  applyDate: string | null
  confirmedAt: string | null
  issueStartMonth: string
  issueAmount: number
}

export interface ElderlyApplicationListResponse {
  items: ElderlyApplicationListItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface ElderlyApplicationDetail {
  id: number
  applicationNo: string
  name: string
  idCard: string
  gender: string
  birthDate: string | null
  age: number | null
  phone: string
  hukouAddress: string
  hukouVillage: string
  hukouDetailAddress: string
  familyAddress: string
  detailAddress: string
  category: string
  categoryText: string
  isCategoryManual: boolean
  identityFlag: string
  identitySource: string
  issueAmount: number
  issueStartMonth: string
  bankName: string
  bankAccount: string
  agentName: string
  agentRelation: string
  agentReceiveName: string
  agentReceiveRelation: string
  agentReceiveBankName: string
  agentReceiveBankAccount: string
  agentReceiveReason: string
  paybackStartMonth: string
  paybackEndMonth: string
  paybackMonths: number
  paybackAmount: number
  paybackReason: string
  isSpecialCase: boolean
  specialReason: string
  status: string
  statusText: string
  applyDate: string | null
  confirmedAt: string | null
  confirmedBy: string
  dueStopDate: string | null
  actualStopDate: string | null
  stoppedAt: string | null
  stoppedBy: string
  stopReason: string
  stopReasonText: string
  isRecover: boolean
  recoverStartMonth: string
  recoverEndMonth: string
  recoverAmount: number
  remark: string
  createdAt: string | null
  updatedAt: string | null
}

export interface ElderlyPendingItem {
  idCardMasked: string
  name: string
  age: number
  type: 'New' | 'Transfer' | string
  typeText: string
  currentCategory: string
  currentCategoryText: string
  suggestedAction: string
  statusLabel: string
  memberRole: string
  gender: string
  birthDate: string | null
  elderlyApplicationId: number
  reviewId: number
  sourceApplicationNo: string
  elderlyApplicationNo: string
}

export interface ElderlyPendingResponse {
  pendingNew: number
  pendingTransfer: number
  items: ElderlyPendingItem[]
}

export interface ElderlyReviewListItem {
  id: number
  reviewNo: string
  name: string
  idCardMasked: string
  triggerSource: string
  triggerText: string
  triggerReason: string
  oldCategory: string
  oldCategoryText: string
  newCategory: string
  newCategoryText: string
  categoryChangeText: string
  reviewResult: string
  resultText: string
  status: string
  statusText: string
  effectiveMonth: string
  issueStartMonth: string
  reviewedBy: string
  reviewedAt: string | null
  createdAt: string | null
}

export interface ElderlyReviewListResponse {
  items: ElderlyReviewListItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}
