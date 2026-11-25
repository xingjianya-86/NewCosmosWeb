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

/** 补发分段。 */
export interface ElderlyPaybackSegment {
  id?: number
  applicationId?: number
  categoryCode: string
  categoryName: string
  monthlyAmount: number
  segmentStartMonth: string
  segmentEndMonth: string
  months: number
  segmentAmount: number
}

/** 判类 + 补发评估结果（Core ElderlyEvaluateResult）。 */
export interface ElderlyEvaluateResult {
  gender: string
  birthDate: string | null
  age: number
  category: string
  categoryName: string
  monthlyAmount: number
  issueStartMonth: string
  issueAmount: number
  identityFlag: string
  identitySource: string
  payback: {
    startMonth: string
    endMonth: string
    totalMonths: number
    totalAmount: number
    segments: ElderlyPaybackSegment[]
  }
}

/** 高龄登记全字段（编辑/查看表单用；对应 Core ElderlyApplication）。 */
export interface ElderlyApplicationEntity {
  id: number
  applicationNo: string
  name: string
  idCard: string
  gender: string
  birthDate: string | null
  phone: string
  hukouAddress: string
  hukouVillage: string
  hukouCityId: number | null
  hukouCountyId: number | null
  hukouTownId: number | null
  hukouVillageId: number | null
  hukouDetailAddress: string
  familyAddress: string
  familyCityId: number | null
  familyCountyId: number | null
  familyTownId: number | null
  familyVillageId: number | null
  detailAddress: string
  bankName: string
  bankAccount: string
  agentName: string
  agentRelation: string
  agentReceiveName: string
  agentReceiveRelation: string
  agentReceiveBankName: string
  agentReceiveBankAccount: string
  agentReceiveReason: string
  issueStartMonth: string
  issueAmount: number
  category: string
  categoryName: string
  identityFlag: string
  identitySource: string
  isCategoryManual: boolean
  paybackStartMonth: string
  paybackEndMonth: string
  autoStartMonth: string
  autoEndMonth: string
  paybackMonths: number
  paybackAmount: number
  paybackReason: string
  isSpecialCase: boolean
  specialReason: string
  status: string
  statusName: string
  applyDate: string | null
  confirmedAt: string | null
  confirmedBy: string
  stoppedAt: string | null
  stoppedBy: string
  stopReason: string
  dueStopDate: string | null
  actualStopDate: string | null
  isRecover: boolean
  recoverStartMonth: string
  recoverEndMonth: string
  recoverAmount: number
  deathDate: string | null
  remark: string
  sourceType: string | null
  sourceId: number | null
  createdAt: string | null
  updatedAt: string | null
}

/** 表单读取响应：实体 + 区划名称 + 补发分段。 */
export interface ElderlyApplicationFormResponse {
  application: ElderlyApplicationEntity
  hukouProvince: string
  hukouCity: string
  hukouCounty: string
  hukouTown: string
  hukouVillage: string
  familyProvince: string
  familyCity: string
  familyCounty: string
  familyTown: string
  familyVillage: string
  segments: ElderlyPaybackSegment[]
}

/** 表单保存请求（区划传名称，服务端解析 ID 并拼地址）。 */
export interface ElderlyApplicationFormRequest {
  name: string
  idCard: string
  gender: string
  birthDate: string | null
  phone: string
  hukouProvince: string
  hukouCity: string
  hukouCounty: string
  hukouTown: string
  hukouVillage: string
  hukouDetailAddress: string
  familyProvince: string
  familyCity: string
  familyCounty: string
  familyTown: string
  familyVillage: string
  detailAddress: string
  bankName: string
  bankAccount: string
  agentName: string
  agentRelation: string
  agentReceiveName: string
  agentReceiveRelation: string
  agentReceiveBankName: string
  agentReceiveBankAccount: string
  agentReceiveReason: string
  category: string
  isCategoryManual: boolean
  paybackStartMonth: string
  paybackEndMonth: string
  paybackReason: string
  isSpecialCase: boolean
  specialReason: string
  applyDate: string | null
}

/** 导入名册人员（身份证查带出）。 */
export interface ElderlyImportedItem {
  id: number
  name: string
  idCard: string
  phone: string | null
  address: string | null
  age: number | null
  personType: string | null
  originalType: string | null
  dataYear: number | null
  gender: string | null
  birthDate: string | null
  subsidyAmount: number | null
  bankAccount: string | null
  importedAt: string | null
}

/** 身份证 OCR 结果。 */
export interface ElderlyIdCardOcrResult {
  name: string
  idCard: string
  gender: string
  birthDate: string | null
  address: string
  fields: Record<string, string>
}

export interface ElderlyApplicationCreated {
  id: number
  applicationNo: string
}

/** 停发办理页头统计。 */
export interface ElderlyStopStats {
  activeCount: number | null
  monthlyStopped: number | null
}

/** 停发办理请求。 */
export interface ElderlyStopRequest {
  dueStopDate: string | null
  stopReason: string
  deathDate: string | null
  otherReasonText: string
  isRecover: boolean
  recoverStartMonth: string
  recoverEndMonth: string
  recoverAmount: number
  remark: string
}
