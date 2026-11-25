/**
 * 临时救助：字段与 src/NewCosmos.Server/Contracts/TempReliefContracts.cs 对齐。
 *
 * 业务口径（2026-11-25 两项政策）：
 * - 两大类：Insured（在保障人群内）/ Uninsured（不在保障人群内）
 * - 三小类：常规·上级（Large + County）、常规·备用金（Large + Reserve）、小额快救（Small）
 * - 六状态：Draft / Submitted / Confirmed / Returned / Rejected / Stopped
 * - 时限：小额 2 日、在册常规 5 日、非在册常规 15 日（服务端计算，前端只展示）
 * - 金额、档位、备案、Can* 均以服务端返回为准；前端不重算政策公式。
 */

export type TempReliefReliefType = 'Large' | 'Small'
export type TempReliefPopulationStatus = 'Insured' | 'Uninsured'
export type TempReliefPaymentChannel = 'County' | 'Reserve'
export type TempReliefStatus =
  | 'Draft'
  | 'Submitted'
  | 'Confirmed'
  | 'Returned'
  | 'Rejected'
  | 'Stopped'
export type TempReliefFilingStatus = 'Pending' | 'Filed'

/** 页头统计；单项失败为 null，前端显示「—」，不伪造 0。 */
export interface TempReliefSummary {
  draftCount: number | null
  confirmedCount: number | null
  stoppedCount: number | null
  yearConfirmed: number | null
  yearConfirmedAmount: number | null
  pendingCount: number | null
  submittedCount: number | null
  overdueCount: number | null
}

/**
 * 列表项。身份证已脱敏，完整证件只在详情返回。
 *
 * 注意：服务端列表契约仍在补齐「遇困人数 / 备案 / Can*」展示字段，
 * 以下以可选字段兼容；缺失时列表页按状态与发放渠道做同口径兜底，
 * 服务端补齐后自动优先采用返回值。
 */
export interface TempReliefListItem {
  id: number
  applicationNo: string
  applicantName: string
  applicantIdCardMasked: string
  reliefType: TempReliefReliefType
  reliefTypeText: string
  populationStatus: TempReliefPopulationStatus
  populationStatusText: string
  paymentChannel: TempReliefPaymentChannel
  paymentChannelText: string
  categoryDisplayName: string
  familyCategory: string
  town: string
  village: string
  applyDate: string | null
  confirmAmount: number | null
  status: TempReliefStatus
  statusText: string
  deadlineText: string
  deadlineDate: string | null
  isOverdue: boolean
  stoppedAt: string | null
  stopReason: string
  reportUnit: string
  createdAt: string | null

  /** 家庭遇困人数（Core ReliefPersonCount；列表契约补齐后返回）。 */
  reliefPersonCount?: number | null
  /** 备案状态（Core FilingStatus；列表契约补齐后返回）。 */
  filingStatus?: TempReliefFilingStatus | null
  /** 备案状态中文（已备案 / 待备案）。 */
  filingStatusText?: string
  /** 行级动作许可（Core / 详情契约同口径；列表契约补齐后返回）。 */
  canSubmit?: boolean
  canReturn?: boolean
  canReject?: boolean
  canConfirm?: boolean
  canMarkFiled?: boolean
  canFile?: boolean
}

export interface TempReliefListResponse {
  items: TempReliefListItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface TempReliefMember {
  id: number
  memberName: string
  gender: string
  relation: string
  idCard: string
  workUnit: string
  annualIncome: number | null
  sortOrder: number
}

export interface TempReliefDisease {
  id: number
  diseaseName: string
  diseaseCode: string
  memberName: string
  hospital: string
  treatStartDate: string | null
  treatEndDate: string | null
  medicalTotal: number | null
  insurancePaid: number | null
  selfPaid: number | null
  sortOrder: number
}

export interface TempReliefAccident {
  id: number
  accidentType: string
  memberName: string
  happenDate: string | null
  happenPlace: string
  injurySituation: string
  propertyLoss: number | null
  compensationPaid: number | null
  responsibilityDesc: string
  materialDesc: string
  sortOrder: number
}

export interface TempReliefEducation {
  id: number
  studentName: string
  educationStage: string
  schoolName: string
  schoolDuration: number | null
  tuitionFee: number | null
  feeDate: string | null
  sortOrder: number
}

/** 完整档案（主表全部新字段 + 成员 + 三类困难明细 + 服务端计算展示字段）。 */
export interface TempReliefDetail {
  id: number
  applicationNo: string
  reliefType: TempReliefReliefType
  reliefTypeText: string
  populationStatus: TempReliefPopulationStatus
  populationStatusText: string
  paymentChannel: TempReliefPaymentChannel
  paymentChannelText: string
  categoryDisplayName: string
  applyYear: string
  sourceType: string
  sourceName: string
  sourceTable: string
  sourceFamilyId: number
  isManualEntry: boolean
  hukouType: string
  applicantName: string
  applicantIdCard: string
  gender: string
  age: number | null
  phone: string
  beneficiaryName: string | null
  beneficiaryIdCard: string | null
  beneficiaryGender: string | null
  beneficiaryAge: number | null
  beneficiaryRelation: string | null
  familyProvince: string
  familyCity: string
  familyDistrict: string
  familyTown: string
  familyDetail: string
  familyAddress: string
  hukouProvince: string
  hukouCity: string
  hukouDistrict: string
  hukouTown: string
  hukouAddress: string
  town: string
  village: string
  familySize: number | null
  reliefPersonCount: number
  familyCategory: string
  bankName: string
  bankAccount: string
  policyEnjoyed: string
  familyMemberStatus: string
  difficultyReason: string
  difficultyType: string
  applyDate: string | null
  reportUnit: string
  reportTime: string | null
  smallAmountLevel: string
  standardTier: string | null
  standardTierLabel: string
  standardMultiplier: number | null
  confirmAmount: number | null
  publicizeStartDate: string | null
  publicizeEndDate: string | null
  publicizeRangeText: string
  publicizeDays: number | null
  publicizeRequired: boolean
  verifyResult: string
  acceptancePerson: string
  status: TempReliefStatus
  statusText: string
  acceptedAt: string | null
  investigationDoneAt: string | null
  deadlineDate: string | null
  deadlineText: string
  remainingDays: number | null
  isOverdue: boolean
  canSubmit: boolean
  canReturn: boolean
  canReject: boolean
  canConfirm: boolean
  canMarkFiled: boolean
  /** 与 CanMarkFiled 同义（任务契约别名）。 */
  canFile: boolean
  economicCheckRequired: boolean
  countyConfirmRequired: boolean
  filingRequired: boolean
  countyConfirmedAt: string | null
  countyConfirmedBy: string
  filingStatus: TempReliefFilingStatus | null
  filingStatusText: string
  rejectReason: string | null
  rejectNotifiedAt: string | null
  rejectNoticeMethod: string | null
  confirmedAt: string | null
  confirmedBy: string
  stoppedAt: string | null
  stoppedBy: string
  stopReason: string
  createdAt: string | null
  updatedAt: string | null
  members: TempReliefMember[]
  diseases: TempReliefDisease[]
  accidents: TempReliefAccident[]
  educations: TempReliefEducation[]
}

export interface TempReliefMemberSnapshot {
  memberName: string
  gender: string
  relation: string
  idCard: string
  workUnit: string
  annualIncome: number | null
}

export interface TempReliefCandidate {
  sourceFamilyId: number
  sourceTable: string
  sourceName: string
  hukouType: string
  name: string
  idCard: string
  gender: string
  phone: string
  province: string
  city: string
  district: string
  detailAddress: string
  familyAddress: string
  hukouAddress: string
  town: string
  village: string
  familySize: number | null
  familyCategory: string
  bankName: string
  bankAccount: string
  oneCardAccount: string
  matchedPersonInfo: string
  matchedMemberIdCard: string
  hasAppliedThisYear: boolean
  members: TempReliefMemberSnapshot[]
}

export interface TempReliefCandidateListResponse {
  items: TempReliefCandidate[]
}

/** 在册身份自动判定结果（按身份证反查四类在册台账，人工不可改）。 */
export interface TempReliefInsuredIdentityResponse {
  isInsured: boolean
  populationStatus: TempReliefPopulationStatus
  populationStatusText: string
  ledgerNames: string[]
  primaryLedgerName: string
  basisText: string
}

export interface TempReliefHukouTypeResponse {
  hukouType: string
}

export interface TempReliefSmallAmountLevel {
  standardType: string
  standardName: string
  standardValue: number
  unit: string
  hukouType: string
}

export interface TempReliefSmallAmountLevelsResponse {
  levels: TempReliefSmallAmountLevel[]
}

/** 小额快救政策档位（50% / 75% / 100%），上限金额由 Core 公式计算。 */
export interface TempReliefSmallTier {
  code: string
  label: string
  multiplier: number
  standardValue: number
  personCount: number
  ceilingAmount: number
}

export interface TempReliefSmallTiersResponse {
  baseStandard: number
  personCount: number
  items: TempReliefSmallTier[]
}

export interface TempReliefPolicySnapshotResponse {
  items: string[]
}

export interface TempReliefFamilyStatusResponse {
  statusText: string
}

export interface TempReliefAnnualCheckResponse {
  exists: boolean
}

export interface TempReliefReportUnitPhoneResponse {
  phone: string
}

export interface TempReliefCreatedResponse {
  id: number
}

/** 打印预览数据：模板 FieldKeys + 成员行 + 打印分类。 */
export interface TempReliefPrintDataResponse {
  id: number
  applicationNo: string
  applicantName: string
  applicantIdCardMasked: string
  reliefType: TempReliefReliefType
  categoryDisplayName: string
  classification: string
  acceptanceDate: string | null
  investigationDate: string | null
  fields: Record<string, string>
  tableRows: Array<Record<string, string>>
  members: TempReliefMember[]
}

export interface TempReliefPhoto {
  id: number
  photoCategory: string
  photoCategoryName: string
  photoName: string
  imageFormat: string
  dataUrl: string
  fileSize: number
  createdAt: string
  createdByName: string
}

export interface TempReliefPhotoListResponse {
  items: TempReliefPhoto[]
}

export interface TempReliefPhotoUploadResponse {
  id: number
  photoCategory: string
  photoName: string
  fileSize: number
}

/** 身份证 OCR 结果（类别仅支持 ID_CARD）。 */
export interface TempReliefOcrResponse {
  category: string
  name: string
  idCard: string
  gender: string
  birthDate: string | null
  address: string
  fields: Record<string, string>
}

export interface TempReliefMemberInput {
  id?: number
  memberName: string
  gender?: string
  relation?: string
  idCard?: string
  workUnit?: string
  annualIncome?: number | null
  sortOrder?: number
}

export interface TempReliefDiseaseInput {
  id?: number
  diseaseName: string
  diseaseCode?: string
  memberName?: string
  hospital?: string
  treatStartDate?: string | null
  treatEndDate?: string | null
  medicalTotal?: number | null
  insurancePaid?: number | null
  selfPaid?: number | null
  sortOrder?: number
}

export interface TempReliefAccidentInput {
  id?: number
  accidentType: string
  memberName?: string
  happenDate?: string | null
  happenPlace?: string
  injurySituation?: string
  propertyLoss?: number | null
  compensationPaid?: number | null
  responsibilityDesc?: string
  materialDesc?: string
  sortOrder?: number
}

export interface TempReliefEducationInput {
  id?: number
  studentName: string
  educationStage?: string
  schoolName?: string
  schoolDuration?: number | null
  tuitionFee?: number | null
  feeDate?: string | null
  sortOrder?: number
}

/**
 * 保存请求（创建 / 更新共用）。
 * 人群状态、发放渠道与金额上限由 Core 按身份证 / 政策重新判定，
 * populationStatus / paymentChannel 仅作表单回显输入，不作为落库依据。
 */
export interface TempReliefSaveRequest {
  reliefType: TempReliefReliefType
  /** 人群状态（Insured / Uninsured）；服务端以 Core 判定结果为准。 */
  populationStatus?: TempReliefPopulationStatus | null
  /** 发放渠道（County / Reserve）；小额固定 Reserve，由 Core 归一。 */
  paymentChannel?: TempReliefPaymentChannel | null
  /** 来源表；留空表示非在册手工登记（source_type=Manual）。 */
  sourceTable: string
  sourceFamilyId: number
  hukouType: string
  applicantName: string
  applicantIdCard: string
  gender: string
  age: number | null
  phone: string
  beneficiaryName: string | null
  beneficiaryIdCard: string | null
  beneficiaryGender: string | null
  beneficiaryAge: number | null
  beneficiaryRelation: string | null
  familyProvince: string
  familyCity: string
  familyDistrict: string
  familyTown: string
  familyDetail: string
  familyAddress: string
  hukouProvince: string
  hukouCity: string
  hukouDistrict: string
  hukouTown: string
  hukouAddress: string
  town: string
  village: string
  familySize: number | null
  /** 家庭遇困人数（含户主；空 / 0 时由 Core 按家庭人口兜底）。 */
  reliefPersonCount?: number | null
  familyCategory: string
  bankName: string
  bankAccount: string
  policyEnjoyed: string
  familyMemberStatus: string
  difficultyReason: string
  difficultyType: string
  applyDate: string | null
  reportUnit: string
  reportTime: string | null
  /** 小额档位展示名（兼容旧前端；服务端以 standardTier 为准）。 */
  smallAmountLevel: string
  /** 小额快救档位码：S50 / S75 / S100；常规留空。 */
  standardTier?: string | null
  confirmAmount: number | null
  publicizeStartDate: string | null
  publicizeEndDate: string | null
  verifyResult: string
  acceptancePerson: string
  members: TempReliefMemberInput[]
  diseases: TempReliefDiseaseInput[]
  accidents: TempReliefAccidentInput[]
  educations: TempReliefEducationInput[]
}

export interface TempReliefReasonRequest {
  reason: string
}

export interface TempReliefRejectRequest {
  reason: string
  notifiedAt?: string | null
  noticeMethod?: string | null
}

export interface TempReliefChangeDateRequest {
  applyDate: string
}

export interface TempReliefChangeDateResponse {
  applicationNo: string
}

export interface TempReliefStopRequest {
  reason: string
}
