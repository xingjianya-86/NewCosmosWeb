/**
 * 临时救助：字段与 src/NewCosmos.Server/Contracts/TempReliefContracts.cs 对齐。
 * 金额不在此计算；小额确定金额上限由服务端按 Core 配置标准返回。
 */

export type TempReliefReliefType = 'Large' | 'Small'
export type TempReliefStatus = 'Draft' | 'Confirmed' | 'Stopped'

export interface TempReliefSummary {
  draftCount: number | null
  confirmedCount: number | null
  stoppedCount: number | null
  yearConfirmed: number | null
  yearConfirmedAmount: number | null
}

export interface TempReliefListItem {
  id: number
  applicationNo: string
  applicantName: string
  applicantIdCardMasked: string
  reliefType: string
  reliefTypeText: string
  familyCategory: string
  town: string
  village: string
  applyDate: string | null
  confirmAmount: number | null
  status: string
  statusText: string
  stoppedAt: string | null
  stopReason: string
  reportUnit: string
  createdAt: string | null
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

export interface TempReliefDetail {
  id: number
  applicationNo: string
  reliefType: string
  reliefTypeText: string
  applyYear: string
  sourceType: string
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
  confirmAmount: number | null
  publicizeStartDate: string | null
  publicizeEndDate: string | null
  verifyResult: string
  acceptancePerson: string
  status: string
  statusText: string
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

export interface TempReliefSaveRequest {
  reliefType: TempReliefReliefType
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
