/**
 * 救助月报表（低收入人口救助帮扶）类型定义。
 * 字段与 src/NewCosmos.Server/Contracts/MonthlyReportContracts.cs 一一对应（camelCase）。
 */

/** B 线统计周期：start/end 为 yyyy-MM-dd，end 为结算日（含）。 */
export interface MonthlyCycle {
  start: string
  end: string
  display: string
}

export interface MonthlyReportOptions {
  years: number[]
  months: number[]
  towns: string[]
  currentYear: number
  currentMonth: number
  currentCycle: MonthlyCycle
}

/** 月报生成历史行（nc_biz_monthly_reports）。 */
export interface MonthlyReportHistoryItem {
  id: number
  year: number
  month: number
  town: string
  totalArchives: number
  newArchives: number
  stoppedArchives: number
  totalAmount: number
  status: string
  generatedAt: string
  generatedBy: string
}

export interface MonthlyReportSummary {
  year: number
  month: number
  town: string
  cycle: MonthlyCycle
  /** 以下计数按乡镇筛选（旧版 GetAddedRowsAsync 等口径）。 */
  addedCount: number
  stoppedCount: number
  increaseCount: number
  decreaseCount: number
  deathCount: number
  /** 在保户数与月保障金为全县口径（旧版 GetStatisticsAsync 口径）。 */
  totalArchives: number
  totalAmount: number
  generated: MonthlyReportHistoryItem | null
}

export interface MonthlyAddedRow {
  applicationId: number
  auditDate: string
  name: string
  idCard: string
  gender: string
  age: string
  address: string
  town: string
  community: string
  familySize: string
  classification: string
  categoryCode: string
  isRural: boolean
  reasonDetail: string
  maritalStatus: string
  healthStatus: string
  bankAccount: string
  annualIncome: number
  relation: string
}

export interface MonthlyStoppedRow {
  headName: string
  headIdCard: string
  headBirth: string
  familySize: string
  address: string
  category: string
  classification: string
  monthAmount: string
  stopReason: string
  stopDate: string
  enjoyDate: string
  categoryCode: string
  bizType: string
  isRural: boolean
}

export interface MonthlyAmountChangeRow {
  headName: string
  headBirth: string
  familySize: string
  address: string
  category: string
  enjoyDate: string
  oldAmount: string
  newAmount: string
  classification: string
  changeAmount: string
  reason: string
  decreasePerson: number
  classifiedSubsidyAmount: number
  oldFamilySize: string
  categoryCode: string
  isRural: boolean
}

export interface MonthlyShiBaoRow {
  headName: string
  memberName: string
  address: string
  category: string
  enjoyDate: string
  originalAmount: string
  originalClassified: string
  reason: string
  decreaseAmount: string
  currentAmount: string
  currentClassified: string
  isRural: boolean
}

export interface MonthlyClassifiedMember {
  name: string
  gender: string
  birthDate: string
  relation: string
  health: string
  monthlyIncome: number
  annualIncome: number
}

export interface MonthlyClassifiedAddRow {
  applicantName: string
  applicantGender: string
  applicantBirthDate: string
  nationality: string
  healthStatus: string
  classification: string
  familySize: string
  phone: string
  address: string
  applicantIdCard: string
  classifiedType: string
  count: number
  amount: number
  isRural: boolean
  members: MonthlyClassifiedMember[]
}

export interface MonthlyDeathRow {
  headName: string
  headIdCard: string
  address: string
  deceasedName: string
  deceasedDate: string
  enjoyDate: string
  newHeadName: string
  categoryCode: string
  isRural: boolean
}

export interface MonthlyExitRectificationRow {
  name: string
  idCard: string
  category: string
  familySize: string
  stopMonth: string
  stopReason: string
  gracePeriod: string
  intoDibao: string
  intoTeku: string
  intoEdge: string
  intoRigid: string
  directExit: string
}

export interface MonthlyTempReliefRow {
  reliefType: string
  name: string
  idCard: string
  age: string
  gender: string
  familySize: string
  address: string
  category: string
  phone: string
  amount: string
  auditDate: string
  reason: string
  selfPay: string
  bankAccount: string
}

/** 指定分类下的一次性报表数据（不适用数据集为空数组）。 */
export interface MonthlyReportRows {
  year: number
  month: number
  town: string
  category: string
  added: MonthlyAddedRow[]
  stopped: MonthlyStoppedRow[]
  increase: MonthlyAmountChangeRow[]
  decrease: MonthlyAmountChangeRow[]
  shiBaoReduction: MonthlyShiBaoRow[]
  classifiedAdds: MonthlyClassifiedAddRow[]
  death: MonthlyDeathRow[]
  exitRectification: MonthlyExitRectificationRow[]
  tempRelief: MonthlyTempReliefRow[]
}

export interface MonthlyReportGenerateRequest {
  year: number
  month: number
  town?: string
  overwrite?: boolean
}

export interface MonthlyReportGenerateResult {
  id: number
  year: number
  month: number
  town: string
  status: string
  totalArchives: number
  newArchives: number
  stoppedArchives: number
  totalAmount: number
  generatedAt: string
}

export interface MonthlyMeeting {
  id: number
  year: number
  month: number
  town: string
  meetingTime: string
  host: string
  recorder: string
  attendees: string
  absentees: string
  memberInfo: string
  applyCategory: string
  memberNames: string
  createdBy: string
  createdAt: string
}

export interface MonthlyMeetingSaveRequest {
  year: number
  month: number
  town?: string
  meetingTime?: string
  host?: string
  recorder?: string
  attendees?: string
  absentees?: string
  applyCategory?: string
}

export interface MonthlyMeetingAttendance {
  id: number
  year: number
  month: number
  town: string
  attendees: string
  absentees: string
  createdBy: string
  createdAt: string
}

export type MonthlyReportExportFormat = 'Excel' | 'Csv'

export interface MonthlyReportExportResult {
  fileName: string
}
