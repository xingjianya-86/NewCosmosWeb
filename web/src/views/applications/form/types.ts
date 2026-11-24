/** 低收入人口认定表单共享模型；前端五个步骤组件共用同一份 reactive form。 */

export interface ApplicationFormBasic {
  hukouType: string
  applicantName: string
  applicantIdCard: string
  gender: string
  ethnicity: string
  phone: string
  hukouProvince: string
  hukouCity: string
  hukouDistrict: string
  hukouTown: string
  hukouVillage: string
  hukouDetailAddress: string
  province: string
  city: string
  district: string
  town: string
  village: string
  address: string
  bankName: string
  bankAccount: string
  disabilityCardNo: string
  applicationReason: string
  applicationReasonDetail: string
  remark: string
}

export interface ApplicationFormMember {
  id: number
  name: string
  idCard: string
  relation: string
  gender: string
  birthDate: string
  age: number | null
  ethnicity: string
  phone: string
  hukouType: string
  healthStatus: string
  employmentStatus: string
  workUnit: string
  mainIncomeSource: string
  annualIncome: number | null
  monthlyIncomeCapacity: number | null
  isHouseholdHead: boolean
  isApplicant: boolean
  memberCategory: string
  hasDisability: boolean
  disabilityType: string
  disabilityLevel: string
  diseaseName: string
  isSevereDisease: boolean
  isLaborExempt: boolean
}

export interface ApplicationFormSupporter {
  id: number
  name: string
  idCard: string
  relation: string
  gender: string
  age: number | null
  annualSupportFee: number | null
  monthlySupportFee: number | null
  isSupportAbility: boolean
}

export interface ApplicationFormEconomy {
  workIncome: number | null
  businessIncome: number | null
  propertyIncome: number | null
  transferIncome: number | null
  otherIncome: number | null
  alimonyIncome: number | null
  landIncome: number | null
  subsidyTotal: number | null
  rigidExpenditure: number | null
  familyLandArea: number | null
  selfFarmedLandArea: number | null
  subleasedLandArea: number | null
  contractedLandArea: number | null
  totalAnnualIncome: number
  totalFamilyIncome: number
  perCapitaAnnualIncome: number
  perCapitaIncome: number
}

export interface ApplicationFormSurvey {
  surveyDate: string
  surveyorName: string
  surveyorOrganization: string
  applicationReason: string
  applicationReasonDetail: string
  surveyNotes: string
}

export interface ApplicationFormClassification {
  classificationResult: string
  classificationText: string
  isEligible: boolean
  ineligibleReason: string
  determinationBasis: string
  householdMonthlyGuaranteeAmount: number | null
  classifiedSubsidyAmount: number | null
  caregiverSubsidyAmount: number | null
  totalGuaranteeAmount: number | null
  isInGracePeriod: boolean
  gracePeriodMonths: number | null
  gracePeriodEndDate: string
}

export interface ApplicationFormModel {
  id: number
  applicationNo: string
  status: string
  statusText: string
  currentStep: number
  basic: ApplicationFormBasic
  members: ApplicationFormMember[]
  supporters: ApplicationFormSupporter[]
  economy: ApplicationFormEconomy
  survey: ApplicationFormSurvey
  classification: ApplicationFormClassification
}

export function createEmptyForm(): ApplicationFormModel {
  return {
    id: 0,
    applicationNo: '',
    status: 'Draft',
    statusText: '草稿',
    currentStep: 1,
    basic: {
      hukouType: 'Rural',
      applicantName: '',
      applicantIdCard: '',
      gender: '',
      ethnicity: 'Han',
      phone: '',
      hukouProvince: '黑龙江省',
      hukouCity: '牡丹江市',
      hukouDistrict: '林口县',
      hukouTown: '',
      hukouVillage: '',
      hukouDetailAddress: '',
      province: '黑龙江省',
      city: '牡丹江市',
      district: '林口县',
      town: '',
      village: '',
      address: '',
      bankName: '',
      bankAccount: '',
      disabilityCardNo: '',
      applicationReason: '',
      applicationReasonDetail: '',
      remark: '',
    },
    members: [],
    supporters: [],
    economy: {
      workIncome: 0,
      businessIncome: 0,
      propertyIncome: 0,
      transferIncome: 0,
      otherIncome: 0,
      alimonyIncome: 0,
      landIncome: 0,
      subsidyTotal: 0,
      rigidExpenditure: 0,
      familyLandArea: 0,
      selfFarmedLandArea: 0,
      subleasedLandArea: 0,
      contractedLandArea: 0,
      totalAnnualIncome: 0,
      totalFamilyIncome: 0,
      perCapitaAnnualIncome: 0,
      perCapitaIncome: 0,
    },
    survey: {
      surveyDate: '',
      surveyorName: '',
      surveyorOrganization: '',
      applicationReason: '',
      applicationReasonDetail: '',
      surveyNotes: '',
    },
    classification: {
      classificationResult: '',
      classificationText: '',
      isEligible: false,
      ineligibleReason: '',
      determinationBasis: '',
      householdMonthlyGuaranteeAmount: 0,
      classifiedSubsidyAmount: 0,
      caregiverSubsidyAmount: 0,
      totalGuaranteeAmount: 0,
      isInGracePeriod: false,
      gracePeriodMonths: null,
      gracePeriodEndDate: '',
    },
  }
}
