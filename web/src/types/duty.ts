/**
 * 值班管理：字段与 src/NewCosmos.Core/Models/Entities/DutyEntities.cs、
 * src/NewCosmos.Core/Constants/DutyConstants.cs 对齐。
 * 后端接口返回统一由 api/duty.ts 归一化，页面只消费本文件的稳定结构。
 */

export const DUTY_GROUPS = ['LEADER', 'MIDDLE', 'MALE', 'FEMALE'] as const
export type DutyGroupCode = (typeof DUTY_GROUPS)[number]

export const DUTY_DATE_TYPES = ['WORKDAY', 'RESTDAY', 'HOLIDAY'] as const
export type DutyDateType = (typeof DUTY_DATE_TYPES)[number]

export const DUTY_CHANGE_TYPES = ['SWAP', 'SUBSTITUTE'] as const
export type DutyChangeType = (typeof DUTY_CHANGE_TYPES)[number]

export type DutyReplayStatus =
  | 'Applied'
  | 'PendingUnbuilt'
  | 'PendingRebuild'
  | 'Invalid'
  | 'Cancelled'

export type DutyHolidayType = 'HOLIDAY' | 'MAKEUP'

/** 月视图单元格：某天某组的值班人。 */
export interface DutyScheduleCell {
  /** 班次 ID；未生成/空缺时为 null。 */
  scheduleId: number | null
  memberId: number | null
  name: string
  phone: string
  /** 是否被串班/代班调整过（显示「调」角标）。 */
  isAdjusted: boolean
  /** 当天该组是否出班（中层在工作日/休息日不出班，显示「·」）。 */
  isRequired: boolean
}

/** 月视图一行：某一天的值班安排。 */
export interface DutyDayRow {
  /** yyyy-MM-dd */
  date: string
  /** 星期缩写（一/六/日）。 */
  week: string
  dateType: DutyDateType
  leader: DutyScheduleCell
  middle: DutyScheduleCell
  male: DutyScheduleCell
  female: DutyScheduleCell
  /** 当天任一组被调整过。 */
  isAdjusted: boolean
  holidayName: string
}

export interface DutySettings {
  restdayLeaderSamePair: boolean
}

export interface DutyMonthSchedule {
  year: number
  month: number
  generated: boolean
  shiftCount: number
  days: DutyDayRow[]
  settings: DutySettings
}

/** 生成结果；后端未返回的统计字段按 0/空处理。 */
export interface DutyGenerationSummary {
  generated: boolean
  hasDownstream: boolean
  hasPendingCrossMonth: boolean
  workdayCount: number
  restdayCount: number
  holidayCount: number
  scheduleCount: number
  conflictSkippedCount: number
  unavailableSkippedCount: number
  anchorDay: number
  replayedChangeCount: number
  pendingChangeCount: number
  pendingChangeMonths: string[]
  skippedChangeCount: number
  /** null=本月原本无数据；false=与原班次一致；true=轮转已变化。 */
  rotationChanged: boolean | null
  downstreamMonths: string[]
  warnings: string[]
}

export interface DutyMemberGroup {
  groupCode: DutyGroupCode
  sortOrder: number
}

export interface DutyMember {
  id: number
  name: string
  phone: string
  isActive: boolean
  /** yyyy-MM-dd */
  joinDate: string | null
  /** yyyy-MM-dd */
  exitDate: string | null
  sortOrder: number
  remark: string
  groups: DutyMemberGroup[]
}

export interface DutyMemberInput {
  name: string
  phone: string
  isActive: boolean
  joinDate: string | null
  exitDate: string | null
  remark: string
  groupCodes: DutyGroupCode[]
}

export interface DutyLeave {
  id: number
  memberId: number
  memberName: string
  /** yyyy-MM-dd */
  startDate: string
  /** yyyy-MM-dd */
  endDate: string
  reason: string
  createdAt: string
  days: number
}

export interface DutyLeaveInput {
  memberId: number
  startDate: string
  endDate: string
  reason: string
}

export interface DutyAdjustment {
  id: number
  changeType: DutyChangeType
  fromMemberId: number | null
  fromMemberName: string
  fromMemberPhone: string
  /** yyyy-MM-dd：串班/代班中甲方（被代者）的班日。 */
  dutyDate: string
  toMemberId: number | null
  toMemberName: string
  toMemberPhone: string
  /** yyyy-MM-dd：串班中乙方被换出的班日；代班为空。 */
  toDutyDate: string | null
  groupCode: DutyGroupCode | ''
  reason: string
  isCancelled: boolean
  cancelledAt: string
  createdAt: string
  replayStatus: DutyReplayStatus
  /** 后端实时计算的状态角标文案（可能带缺失月份）。 */
  statusText: string
  statusColorHex: string
}

export interface DutyAdjustmentInput {
  changeType: DutyChangeType
  fromMemberId: number
  /** yyyy-MM-dd */
  dutyDate: string
  toMemberId: number
  /** yyyy-MM-dd；仅串班必填。 */
  toDutyDate?: string | null
  reason: string
}

export interface DutyFutureShift {
  scheduleId: number
  /** yyyy-MM-dd */
  date: string
  dateType: DutyDateType
  groupCode: DutyGroupCode | ''
  memberId: number
  memberName: string
  isAdjusted: boolean
}

export interface DutyHoliday {
  id: number
  /** yyyy-MM-dd */
  date: string
  dateType: DutyHolidayType
  name: string
  year: number | null
  /** API / MANUAL / SEED */
  source: string
  updatedAt: string
}

export interface DutyHolidayInput {
  date: string
  dateType: DutyHolidayType
  name: string
  source?: string
}

export interface DutyHolidayDownloadSummary {
  year: number
  holidayCount: number
  makeupCount: number
  sourceDisplayName: string
  isFromFallback: boolean
  paperUrl: string
}

export interface DutyImportRosterEntry {
  seq: number
  name: string
}

export interface DutyImportRoster {
  groupCode: DutyGroupCode | ''
  groupName: string
  memberCount: number
  namesPreview: string
  entries: DutyImportRosterEntry[]
}

export interface DutyImportPreview {
  rosters: DutyImportRoster[]
  totalMembers: number
  newCount: number
  updateCount: number
  removeCount: number
  clearedGroupNames: string[]
  errors: string[]
  /** yyyy-MM-dd */
  anchorDate: string | null
  /** 组编码 → 日期类型 → 下个值班成员名。 */
  currentSeqByLine: Record<string, Record<string, string>>
  isValid: boolean
  summaryText: string
}

export interface DutyImportResult {
  success: boolean
  newCount: number
  updateCount: number
  removeCount: number
  anchorDate: string | null
  errors: string[]
  message: string
  summaryText: string
}

export interface DutyExportRange {
  startYear: number
  startMonth: number
  endYear: number
  endMonth: number
}

export interface DutyFileDownload {
  blob: Blob
  fileName: string
}

/** 后端导出/打印统一入队到 Agent：返回作业信息而非浏览器下载。 */
export interface DutyJobInfo {
  id: number
  jobNo: string
  status: string
  jobKind: string
  mode: string
  templateId: number
  templateName: string
  fileCount: number
}

/** 导出接口可能返回文件字节，也可能返回 Agent 作业。 */
export type DutyExportResult =
  | { type: 'file'; blob: Blob; fileName: string }
  | { type: 'job'; job: DutyJobInfo }
