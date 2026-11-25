import {
  createEmptyForm,
  type ApplicationFormBasic,
  type ApplicationFormMember,
  type ApplicationFormModel,
  type ApplicationFormSupporter,
} from './types'
import { normalizeIdCard, parseIdCard } from './validation'

/** 克隆表单为普通对象，避免把 Vue 响应式代理直接交给 axios/JSON。 */
export function cloneForm(form: ApplicationFormModel): ApplicationFormModel {
  const copy = JSON.parse(JSON.stringify(form)) as ApplicationFormModel

  // 服务端金额字段是非空 decimal；输入框清空时可能产生 null，提交前归一为 0。
  copy.members = copy.members.map((member) => ({
    ...member,
    annualIncome: member.annualIncome ?? 0,
    monthlyIncomeCapacity: member.monthlyIncomeCapacity ?? 0,
  }))
  copy.supporters = copy.supporters.map((supporter) => ({
    ...supporter,
    annualSupportFee: supporter.annualSupportFee ?? 0,
    monthlySupportFee: supporter.monthlySupportFee ?? 0,
  }))
  for (const key of Object.keys(copy.economy) as (keyof typeof copy.economy)[]) {
    copy.economy[key] = (copy.economy[key] ?? 0) as never
  }
  copy.classification.householdMonthlyGuaranteeAmount =
    copy.classification.householdMonthlyGuaranteeAmount ?? 0
  copy.classification.classifiedSubsidyAmount =
    copy.classification.classifiedSubsidyAmount ?? 0
  copy.classification.caregiverSubsidyAmount =
    copy.classification.caregiverSubsidyAmount ?? 0
  copy.classification.totalGuaranteeAmount = copy.classification.totalGuaranteeAmount ?? 0

  return copy
}

/** 兼容后端缺字段的返回：以 createEmptyForm 为底逐段合并。 */
export function normalizeForm(
  input: Partial<ApplicationFormModel> | null | undefined,
): ApplicationFormModel {
  const empty = createEmptyForm()
  if (!input) return empty

  return {
    ...empty,
    ...input,
    id: Number(input.id ?? empty.id) || 0,
    applicationNo: input.applicationNo ?? '',
    status: input.status || empty.status,
    statusText: input.statusText || empty.statusText,
    currentStep: Number(input.currentStep ?? empty.currentStep) || 1,
    basic: { ...empty.basic, ...(input.basic ?? {}) },
    members: Array.isArray(input.members) ? input.members.map((item) => createMember(item)) : [],
    supporters: Array.isArray(input.supporters)
      ? input.supporters.map((item) => createSupporter(item))
      : [],
    caregivers: Array.isArray(input.caregivers) ? input.caregivers : [],
    economy: { ...empty.economy, ...(input.economy ?? {}) },
    economicDetails: {
      ...empty.economicDetails,
      ...(input.economicDetails ?? {}),
    },
    survey: { ...empty.survey, ...(input.survey ?? {}) },
    visitRecords: Array.isArray(input.visitRecords) ? input.visitRecords : [],
    classification: { ...empty.classification, ...(input.classification ?? {}) },
  }
}

export function createMember(
  overrides: Partial<ApplicationFormMember> = {},
): ApplicationFormMember {
  return {
    id: 0,
    name: '',
    idCard: '',
    relation: '配偶',
    gender: '',
    birthDate: '',
    age: null,
    ethnicity: 'Han',
    phone: '',
    hukouType: 'Rural',
    healthStatus: '健康',
    employmentStatus: '',
    workUnit: '',
    mainIncomeSource: '',
    annualIncome: null,
    monthlyIncomeCapacity: null,
    isHouseholdHead: false,
    isApplicant: false,
    memberCategory: '',
    hasDisability: false,
    disabilityType: '',
    disabilityLevel: '',
    diseaseName: '',
    isSevereDisease: false,
    isLaborExempt: false,
    educationLevel: '',
    maritalStatus: '',
    politicalStatus: '',
    hukouAddress: '',
    homeAddress: '',
    diseaseCategory: '',
    medicalSelfPaid12m: null,
    disabilityCertificateNo: '',
    personType: '',
    isSingleRescue: false,
    selfCareAbility: '',
    ...overrides,
  }
}

export function createSupporter(
  overrides: Partial<ApplicationFormSupporter> = {},
): ApplicationFormSupporter {
  return {
    id: 0,
    name: '',
    idCard: '',
    relation: '子/婿',
    gender: '',
    age: null,
    annualSupportFee: null,
    monthlySupportFee: null,
    isSupportAbility: true,
    ...overrides,
  }
}

/** 新表单默认户主：姓名/身份证/性别/年龄取自第 1 步申请人。 */
export function createHeadMember(basic: ApplicationFormBasic): ApplicationFormMember {
  const member = createMember({
    relation: '户主',
    isHouseholdHead: true,
    isApplicant: true,
    name: basic.applicantName.trim(),
    idCard: normalizeIdCard(basic.applicantIdCard),
    gender: basic.gender,
    ethnicity: basic.ethnicity || 'Han',
    phone: basic.phone,
    hukouType: basic.hukouType || 'Rural',
  })
  return applyApplicantToMember(member, basic)
}

/** 把第 1 步申请人信息写入指定成员（户主=申请人 按钮与自动同步共用）。 */
export function applyApplicantToMember(
  member: ApplicationFormMember,
  basic: ApplicationFormBasic,
): ApplicationFormMember {
  member.name = basic.applicantName.trim()
  member.idCard = normalizeIdCard(basic.applicantIdCard)
  member.phone = basic.phone
  member.ethnicity = basic.ethnicity || member.ethnicity
  member.hukouType = basic.hukouType || member.hukouType
  if (basic.gender) member.gender = basic.gender

  const info = parseIdCard(member.idCard)
  if (info) {
    member.gender = info.gender
    member.birthDate = info.birthDate
    member.age = info.age
  }

  return member
}
