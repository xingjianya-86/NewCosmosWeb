/** 保障对象动态管理：字段与 server/Contracts/ChangeManagementContracts.cs 对齐。 */

export interface ChangeManagementListResponse {
  items: ChangeManagementListItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface ChangeManagementListItem {
  applicationId: number
  applicationNo: string
  applicantName: string
  applicantIdCardMasked: string
  town: string
  village: string
  classification: string
  classificationName: string
  familySize: number
  householdMonthlyGuaranteeAmount: number
  totalGuaranteeAmount: number
  perCapitaAnnualIncome: number
  status: string
  statusText: string
  gracePeriodEndDate: string | null
  gracePeriodMonths: number | null
  graceDaysLeft: number | null
}

export interface ChangeDictOption {
  code: string
  name: string
}

export interface ChangeClassificationOption {
  code: string
  name: string
}

export interface ChangeManagementFilters {
  towns: string[]
  classifications: ChangeClassificationOption[]
  deathReasons: ChangeDictOption[]
  memberAddReasons: ChangeDictOption[]
  memberRemoveReasons: ChangeDictOption[]
  relationships: ChangeDictOption[]
  healthStatuses: ChangeDictOption[]
  employmentStatuses: ChangeDictOption[]
  educationLevels: ChangeDictOption[]
  maritalStatuses: ChangeDictOption[]
  politicalStatuses: ChangeDictOption[]
  laborAbilities: ChangeDictOption[]
  hukouTypes: ChangeDictOption[]
  memberCategories: ChangeDictOption[]
  genders: ChangeDictOption[]
}

export interface ChangeMonthlyFlags {
  economicReviewDone: boolean
  memberChangeDone: boolean
  headChangeDone: boolean
  headDeathDone: boolean
}

export interface ChangeGraceInfo {
  id: number
  months: number | null
  startDate: string | null
  endDate: string | null
  originalClassification: string
  originalClassificationName: string
  originalGuaranteeAmount: number | null
  graceGrantAmount: number | null
  daysLeft: number | null
  isActive: boolean
}

export interface ChangeHeadInfo {
  memberId: number
  name: string
  idCard: string
  idCardMasked: string
  isVirtual: boolean
}

export interface ChangeMember {
  id: number
  name: string
  idCard: string
  idCardMasked: string
  relationship: string
  relationshipText: string
  gender: string
  age: number | null
  birthDate: string | null
  healthStatus: string
  healthStatusText: string
  employmentStatus: string
  employmentStatusText: string
  workCapacity: string
  workCapacityText: string
  phone: string
  workUnit: string
  educationLevel: string
  educationLevelText: string
  maritalStatus: string
  maritalStatusText: string
  politicalStatus: string
  politicalStatusText: string
  memberCategory: string
  memberCategoryText: string
  isHouseholdHead: boolean
  isApplicant: boolean
  isSupport: boolean
}

export interface ChangeManagementDetail {
  applicationId: number
  applicationNo: string
  applicantName: string
  applicantIdCard: string
  applicantIdCardMasked: string
  applicantPhone: string
  gender: string
  hukouType: string
  hukouTypeName: string
  town: string
  village: string
  address: string
  classification: string
  classificationName: string
  status: string
  statusText: string
  isStopped: boolean
  familySize: number
  householdMonthlyGuaranteeAmount: number
  classifiedSubsidyAmount: number
  caregiverSubsidyAmount: number
  totalGuaranteeAmount: number
  totalAnnualIncome: number
  perCapitaAnnualIncome: number
  totalFamilyIncome: number
  perCapitaIncome: number
  subsistenceStandard: number | null
  firstApprovedAt: string | null
  createdAt: string | null
  canManage: boolean
  canChangeMember: boolean
  canChangeHead: boolean
  canHouseholdDeath: boolean
  canGraceConfirm: boolean
  canVerifyStop: boolean
  monthly: ChangeMonthlyFlags
  grace: ChangeGraceInfo | null
  head: ChangeHeadInfo
  members: ChangeMember[]
}

export interface ChangeHistoryItem {
  id: number
  changeNo: string
  changeType: string
  changeTypeDisplay: string
  changeReason: string
  changeReasonType: string | null
  changeDate: string
  operatorName: string
  changedAt: string
}

export interface ChangeHistoryResponse {
  items: ChangeHistoryItem[]
}

/** 历史导入库待建档项（变更页双源搜索的导入库半边）。 */
export interface ImportedArchiveItem {
  source: number
  sourceDisplay: string
  sourceTable: string
  sourceId: number
  applicantName: string
  applicantIdCardMasked: string
  address: string
  familySize: number
  totalAmount: number
  hukouType: string
  matchedPersonInfo: string
  derivedClassificationCode: string
  derivedClassificationName: string
  /** 已建档则为当前库档案 ID。 */
  existingApplicationId: number | null
}

export interface ImportedArchiveSearchResponse {
  items: ImportedArchiveItem[]
}

export interface MigrateImportedArchiveResponse {
  applicationId: number
  alreadyExisted: boolean
  migratedMemberCount: number
}

export interface HeadChangeRequest {
  newHeadMemberId: number
  changeReason: string
}

export interface HouseholdDeathRequest {
  deceasedHeadMemberId: number | null
  newHeadMemberId: number | null
  deathDate: string
  deathTime: string
  deathReason: string
  deathCertificateNo: string
}

export interface GraceConfirmRequest {
  months: number
}

export interface VerifyStopRequest {
  stopDate?: string | null
}

export interface MemberAddRequest {
  name: string
  idCard: string
  relationshipToHead: string
  gender?: string | null
  age?: number | null
  birthDate?: string | null
  healthStatus?: string | null
  phone?: string | null
  reasonCode: string
  reasonName: string
  eventDate?: string | null
  remark?: string | null
}

export interface MemberRemoveRequest {
  memberId: number
  reasonCode: string
  reasonName: string
  eventDate?: string | null
  eventTime?: string | null
  remark?: string | null
}

export interface MemberModifyRequest {
  memberId: number
  relationshipToHead?: string | null
  healthStatus?: string | null
  employmentStatus?: string | null
  workCapacity?: string | null
  phone?: string | null
  workUnit?: string | null
  educationLevel?: string | null
  maritalStatus?: string | null
  politicalStatus?: string | null
  reason: string
  remark?: string | null
}

export interface MemberChangeRequest {
  changeReason: string
  additions: MemberAddRequest[]
  removals: MemberRemoveRequest[]
  modifications: MemberModifyRequest[]
}

export interface ChangeOperationResult {
  changeId: number
  isSuccess: boolean
  oldClassification: string
  oldClassificationName: string
  newClassification: string
  newClassificationName: string
  oldGuaranteeAmount: number
  newGuaranteeAmount: number
  triggeredGracePeriod: boolean
  triggeredStop: boolean
  rebuilt: boolean
  newApplicationId: number
}

export interface GraceConfirmResult {
  success: boolean
  months: number
  startDate: string | null
  endDate: string | null
}
