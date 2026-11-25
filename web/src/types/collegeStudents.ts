/**
 * 低收入人口救助帮扶 · 大学生管理。
 * 字段与 src/NewCosmos.Server/Contracts/CollegeStudentContracts.cs 保持一致；
 * 状态/学制常量与 Core Constants/CollegeStudentConstants.cs 保持一致。
 */

export type CollegeStudentStatus =
  | 'Studying'
  | 'Graduated'
  | 'NotEligible'
  | 'NoHigherEducation'

/** 大学生档案列表项（含只读分栏标志）。 */
export interface CollegeStudentListItem {
  id: number
  applicationId: number
  familyMemberId: number
  studentName: string
  idCard: string
  educationLevel: string
  schoolName: string
  schoolDuration: number | null
  schoolDurationDisplay: string
  enrollmentYear: number | null
  graduationYear: number | null
  status: string
  statusDisplay: string
  remark: string
  isHouseholdStopped: boolean
  isBeyondJobSeekingPeriod: boolean
  isGraduatingThisYear: boolean
}

/** 可关联的 18-23 周岁家庭成员。 */
export interface EligibleCollegeMember {
  id: number
  applicationId: number
  name: string
  idCard: string
  gender: string
  age: number | null
  memberCategory: string
  householdHeadName: string
  phone: string
  homeLocationDisplay: string
  genderAgeDisplay: string
}

export interface CollegeStudentFormRequest {
  applicationId: number
  familyMemberId: number
  studentName: string
  idCard: string
  educationLevel: string
  schoolName: string
  schoolDuration: number | null
  enrollmentYear: number | null
  status: CollegeStudentStatus
  remark: string
}

export interface CollegeStudentCreated {
  id: number
}

export interface CollegeStatusOption {
  value: CollegeStudentStatus
  label: string
}

export interface SchoolDurationOption {
  value: number
  label: string
}

/** 状态键（与 CollegeStudentConstants 一致）。 */
export const COLLEGE_STUDENT_STATUS = {
  Studying: 'Studying',
  Graduated: 'Graduated',
  NotEligible: 'NotEligible',
  NoHigherEducation: 'NoHigherEducation',
} as const

/** 学历层次选项（落库存中文值）。 */
export const COLLEGE_EDUCATION_LEVELS: readonly string[] = ['本科', '专科']

/** 学制选项。 */
export const COLLEGE_SCHOOL_DURATIONS: readonly SchoolDurationOption[] = [
  { value: 3, label: '三年制' },
  { value: 4, label: '四年制' },
  { value: 5, label: '五年制' },
]

/** 状态选项（与旧版编辑表单顺序一致）。 */
export const COLLEGE_STATUS_OPTIONS: readonly CollegeStatusOption[] = [
  { value: 'Studying', label: '在读' },
  { value: 'Graduated', label: '已毕业' },
  { value: 'NotEligible', label: '不符合人员' },
  { value: 'NoHigherEducation', label: '未收到高等教育' },
]

/** 入学年份选项（近 15 年，最新在前；对应旧版 EnrollmentYearOptions）。 */
export function buildEnrollmentYearOptions(today = new Date()): number[] {
  const currentYear = today.getFullYear()
  return Array.from({ length: 15 }, (_item, index) => currentYear - index)
}

/** 首页/页面年度毕业提醒文案（对应 CollegeStudentConstants.BuildGraduationBannerText）。 */
export function buildGraduationBannerText(count: number): string {
  return `有 ${count} 位大学生今年6月毕业，毕业后最长择业期半年，请及时前往「低收入人口救助帮扶」更新毕业状态并核查是否需退出最低生活保障`
}
