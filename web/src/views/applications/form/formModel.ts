import {
  createEmptyForm,
  type ApplicationFormBasic,
  type ApplicationFormMember,
  type ApplicationFormModel,
  type ApplicationFormSupporter,
} from './types'
import { normalizeIdCard, parseIdCard } from './validation'
import { normalizeDisabilityLevel, normalizeDisabilityType, normalizeHealthStatus } from './healthStatus'

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

/** 载入时把身体状况/残疾类型/等级归一化为中文档位（历史值原样保留），避免英文 key 渗入表单。 */
function normalizeHealthFields<T>(row: T): T {
  const target = row as {
    healthStatus?: string
    disabilityType?: string
    disabilityLevel?: string
  }
  if (typeof target.healthStatus === 'string') {
    target.healthStatus = normalizeHealthStatus(target.healthStatus)
  }
  if (typeof target.disabilityType === 'string') {
    target.disabilityType = normalizeDisabilityType(target.disabilityType)
  }
  if (typeof target.disabilityLevel === 'string') {
    target.disabilityLevel = normalizeDisabilityLevel(target.disabilityLevel)
  }
  return row
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
    // 身体状况统一归一化为中文档位（英文 Core key / 旧显示值在加载时即纠正），
    // 保证第 1 步/第 2 步显示与联动守卫拿到的都是表单值。
    basic: normalizeHealthFields({ ...empty.basic, ...(input.basic ?? {}) }),
    members: Array.isArray(input.members)
      ? input.members.map((item) => normalizeHealthFields(createMember(item)))
      : [],
    supporters: Array.isArray(input.supporters)
      ? input.supporters.map((item) => normalizeHealthFields(createSupporter(item)))
      : [],
    caregivers: Array.isArray(input.caregivers)
      ? input.caregivers.map((item) => normalizeHealthFields({ ...item }))
      : [],
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
    healthStatus: '健康或良好',
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
    relation: '本人/户主',
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
