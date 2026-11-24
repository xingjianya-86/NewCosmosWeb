/** 社会救助申请：字段与 docs/API_CONTRACT_APPLICATIONS.md 保持一致。 */

export type ApplicationStatus = 'Draft' | 'Submitted' | 'Approved' | 'Completed' | 'Stopped'

export interface ApplicationListItem {
  id: number
  applicationNo: string
  applicantName: string
  applicantIdCardMasked: string
  applicantPhone: string
  status: ApplicationStatus
  statusText: string
  currentStep: number
  familySize: number
  classificationResult: string
  totalAnnualIncome: number
  perCapitaAnnualIncome: number
  town: string
  village: string
  address: string
  createdAt: string
  updatedAt: string
}

export interface ApplicationListResponse {
  items: ApplicationListItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface ApplicationMember {
  id: number
  name: string
  idCard: string
  relation: string
  gender: string
  birthDate: string
  age: number
  healthStatus: string
  employmentStatus: string
  monthlyIncome: number
}

export interface ApplicationDetail {
  id: number
  applicationNo: string
  status: ApplicationStatus
  statusText: string
  currentStep: number
  applicantName: string
  applicantIdCard: string
  applicantPhone: string
  hukouType: string
  gender: string
  ethnicity: string
  hukouAddress: string
  townId: number
  villageId: number
  town: string
  village: string
  address: string
  familySize: number
  classificationResult: string
  totalAnnualIncome: number
  perCapitaAnnualIncome: number
  totalFamilyIncome: number
  perCapitaIncome: number
  householdMonthlyGuaranteeAmount: number
  applicationReason: string
  applicationReasonDetail: string
  submitAt: string | null
  createdAt: string
  updatedAt: string
  members: ApplicationMember[]
}
