import { ApiError, apiHttp } from './http'
import {
  DUTY_DATE_TYPES,
  DUTY_GROUPS,
  type DutyAdjustment,
  type DutyAdjustmentInput,
  type DutyChangeType,
  type DutyDateType,
  type DutyDayRow,
  type DutyExportRange,
  type DutyExportResult,
  type DutyJobInfo,
  type DutyFutureShift,
  type DutyGenerationSummary,
  type DutyGroupCode,
  type DutyHoliday,
  type DutyHolidayDownloadSummary,
  type DutyHolidayInput,
  type DutyImportPreview,
  type DutyImportResult,
  type DutyImportRoster,
  type DutyLeave,
  type DutyLeaveInput,
  type DutyMember,
  type DutyMemberGroup,
  type DutyMemberInput,
  type DutyMonthSchedule,
  type DutyReplayStatus,
  type DutyScheduleCell,
  type DutySettings,
} from '@/types/duty'

const BASE = '/duty'

/* ── 归一化工具：后端字段可能来自 Core 实体或 HTTP DTO，页面只认稳定结构 ── */

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function toText(value: unknown): string {
  if (value === null || value === undefined) return ''
  if (typeof value === 'string') return value.trim()
  if (typeof value === 'number' || typeof value === 'boolean') return String(value)
  return ''
}

function firstText(...values: unknown[]): string {
  for (const value of values) {
    const text = toText(value)
    if (text) return text
  }
  return ''
}

function toNumber(value: unknown, fallback = 0): number {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function toNullableNumber(value: unknown): number | null {
  if (value === null || value === undefined || value === '') return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

function toBoolean(value: unknown, fallback = false): boolean {
  if (typeof value === 'boolean') return value
  if (typeof value === 'number') return value !== 0
  if (typeof value === 'string') {
    const normalized = value.trim().toLowerCase()
    if (normalized === 'true' || normalized === '1' || normalized === 'yes') return true
    if (normalized === 'false' || normalized === '0' || normalized === 'no') return false
  }
  return fallback
}

function toNullableBoolean(value: unknown): boolean | null {
  if (value === null || value === undefined || value === '') return null
  return toBoolean(value)
}

function pad2(value: number): string {
  return value < 10 ? `0${value}` : String(value)
}

/** 只取日期部分；兼容 ISO / DateOnly / yyyy/MM/dd。 */
function toDateText(value: unknown): string {
  const text = toText(value)
  if (!text) return ''
  const match = /(\d{4})[-/](\d{1,2})[-/](\d{1,2})/.exec(text)
  if (match) {
    return `${match[1]}-${pad2(Number(match[2]))}-${pad2(Number(match[3]))}`
  }
  return ''
}

function todayText(): string {
  const now = new Date()
  return `${now.getFullYear()}-${pad2(now.getMonth() + 1)}-${pad2(now.getDate())}`
}

function weekdayText(date: string): string {
  const parsed = new Date(`${date}T00:00:00`)
  if (Number.isNaN(parsed.getTime())) return ''
  return new Intl.DateTimeFormat('zh-CN', { weekday: 'short' })
    .format(parsed)
    .replace('星期', '')
    .replace('周', '')
}

function normalizeGroupCode(value: unknown): DutyGroupCode | '' {
  const code = toText(value).toUpperCase()
  if ((DUTY_GROUPS as readonly string[]).includes(code)) return code as DutyGroupCode
  if (code === '领导组') return 'LEADER'
  if (code === '中层管理组' || code === '中层组') return 'MIDDLE'
  if (code === '男生组') return 'MALE'
  if (code === '女生组') return 'FEMALE'
  return ''
}

function normalizeDateType(value: unknown, raw: Record<string, unknown> = {}): DutyDateType {
  if (toBoolean(raw.isHoliday)) return 'HOLIDAY'
  if (toBoolean(raw.isRestDay)) return 'RESTDAY'
  if (toBoolean(raw.isWorkday)) return 'WORKDAY'

  const code = toText(value).toUpperCase()
  if ((DUTY_DATE_TYPES as readonly string[]).includes(code)) return code as DutyDateType
  if (code.includes('HOLIDAY') || code.includes('节')) return 'HOLIDAY'
  if (code.includes('REST') || code.includes('休')) return 'RESTDAY'
  return 'WORKDAY'
}

function unwrapRoot(value: unknown): Record<string, unknown> {
  const root = asRecord(value) ?? {}
  const nested = asRecord(root.data)
  return nested && !root.month && !root.days ? nested : root
}

function unwrapList(value: unknown): unknown[] {
  if (Array.isArray(value)) return value
  const root = asRecord(value)
  if (!root) return []
  if (Array.isArray(root.items)) return root.items
  if (Array.isArray(root.Items)) return root.Items
  const nested = asRecord(root.data)
  if (nested) {
    if (Array.isArray(nested.items)) return nested.items
    if (Array.isArray(nested.Items)) return nested.Items
  }
  return []
}

function toStringList(value: unknown): string[] {
  if (!Array.isArray(value)) return []
  return value.map((item) => toText(item)).filter(Boolean)
}

function emptyCell(required: boolean): DutyScheduleCell {
  return {
    scheduleId: null,
    memberId: null,
    name: '',
    phone: '',
    isAdjusted: false,
    isRequired: required,
  }
}

function defaultRequired(group: DutyGroupCode, dateType: DutyDateType): boolean {
  if (group === 'MIDDLE') return dateType === 'HOLIDAY'
  return true
}

function normalizeCell(
  value: unknown,
  group: DutyGroupCode,
  dateType: DutyDateType,
  adjusted: boolean,
): DutyScheduleCell {
  const requiredDefault = defaultRequired(group, dateType)

  if (typeof value === 'string') {
    const text = value.trim()
    if (!text) return emptyCell(requiredDefault)
    if (text === '·') return emptyCell(false)
    if (text === '—' || text === '-') return emptyCell(requiredDefault)
    return {
      scheduleId: null,
      memberId: null,
      name: text,
      phone: '',
      isAdjusted: adjusted,
      isRequired: true,
    }
  }

  const raw = asRecord(value)
  if (!raw) return emptyCell(requiredDefault)

  const display = firstText(raw.name, raw.memberName, raw.displayText)
  const isPlaceholder = display === '·' || display === '—' || display === '-'
  const name = isPlaceholder ? '' : display
  const isRequired =
    raw.isRequired === undefined && raw.required === undefined
      ? display === '·'
        ? false
        : requiredDefault
      : toBoolean(raw.isRequired ?? raw.required, requiredDefault)

  const member = asRecord(raw.member)
  return {
    scheduleId: toNullableNumber(raw.scheduleId),
    memberId: toNullableNumber(raw.memberId ?? member?.id),
    name,
    phone: firstText(raw.phone, raw.memberPhone, raw.tel),
    isAdjusted: toBoolean(raw.isAdjusted ?? raw.adjusted, adjusted),
    isRequired,
  }
}

function cellFromFlat(
  raw: Record<string, unknown>,
  prefix: string,
  group: DutyGroupCode,
  dateType: DutyDateType,
  adjusted: boolean,
): DutyScheduleCell {
  const name = firstText(
    raw[`${prefix}Name`],
    raw[`${prefix}MemberName`],
    raw[`${prefix}Display`],
  )
  const phone = firstText(raw[`${prefix}Phone`])
  if (!name && !phone) return emptyCell(defaultRequired(group, dateType))
  return {
    scheduleId: toNullableNumber(raw[`${prefix}ScheduleId`]),
    memberId: toNullableNumber(raw[`${prefix}MemberId`]),
    name: name === '·' || name === '—' ? '' : name,
    phone,
    isAdjusted: toBoolean(raw[`${prefix}IsAdjusted`], adjusted),
    isRequired: name === '·' ? false : defaultRequired(group, dateType),
  }
}

function normalizeDay(
  value: unknown,
  year: number,
  month: number,
  index: number,
): DutyDayRow | null {
  const raw = asRecord(value)
  if (!raw) return null

  const date =
    toDateText(raw.date) ||
    toDateText(raw.dutyDate) ||
    `${year}-${pad2(month)}-${pad2(index + 1)}`
  const dateType = normalizeDateType(firstText(raw.dateType, raw.type), raw)
  const dayAdjusted = toBoolean(raw.isAdjusted ?? raw.adjusted)
  const week =
    firstText(raw.week, raw.weekLabel).replace('星期', '').replace('周', '') ||
    weekdayText(date)

  const leaderKey = raw.leader ?? raw.leaderCell
  const middleKey = raw.middle ?? raw.middleCell
  const maleKey = raw.male ?? raw.maleCell
  const femaleKey = raw.female ?? raw.femaleCell

  const leader = leaderKey
    ? normalizeCell(leaderKey, 'LEADER', dateType, dayAdjusted)
    : cellFromFlat(raw, 'leader', 'LEADER', dateType, dayAdjusted)
  const middle = middleKey
    ? normalizeCell(middleKey, 'MIDDLE', dateType, dayAdjusted)
    : cellFromFlat(raw, 'middle', 'MIDDLE', dateType, dayAdjusted)
  const male = maleKey
    ? normalizeCell(maleKey, 'MALE', dateType, dayAdjusted)
    : cellFromFlat(raw, 'male', 'MALE', dateType, dayAdjusted)
  const female = femaleKey
    ? normalizeCell(femaleKey, 'FEMALE', dateType, dayAdjusted)
    : cellFromFlat(raw, 'female', 'FEMALE', dateType, dayAdjusted)

  return {
    date,
    week,
    dateType,
    leader,
    middle,
    male,
    female,
    isAdjusted:
      dayAdjusted ||
      leader.isAdjusted ||
      middle.isAdjusted ||
      male.isAdjusted ||
      female.isAdjusted,
    holidayName: firstText(raw.holidayName),
  }
}

function normalizeSettings(value: unknown): DutySettings {
  const root = asRecord(value) ?? {}
  const nested = asRecord(root.settings) ?? root
  return {
    restdayLeaderSamePair: toBoolean(
      nested.restdayLeaderSamePair ??
        nested.restDayLeaderSamePair ??
        nested.restdayLeaderSamePari,
    ),
  }
}

function normalizeMonth(value: unknown): DutyMonthSchedule {
  const root = unwrapRoot(value)
  const year = toNumber(root.year, new Date().getFullYear())
  const month = toNumber(root.month, new Date().getMonth() + 1)
  const rawDays = Array.isArray(root.days) ? root.days : []
  const days = rawDays
    .map((day, index) => normalizeDay(day, year, month, index))
    .filter((day): day is DutyDayRow => day !== null)
  const computedShiftCount = days.reduce(
    (total, day) =>
      total +
      (day.leader.scheduleId ? 1 : 0) +
      (day.middle.scheduleId ? 1 : 0) +
      (day.male.scheduleId ? 1 : 0) +
      (day.female.scheduleId ? 1 : 0),
    0,
  )

  return {
    year,
    month,
    generated: toBoolean(root.generated ?? root.isGenerated),
    shiftCount: toNumber(root.shiftCount ?? root.scheduleCount, computedShiftCount),
    days,
    settings: normalizeSettings(root.settings ?? root),
  }
}

function normalizeSummary(value: unknown): DutyGenerationSummary {
  const root = asRecord(value) ?? {}
  return {
    generated: toBoolean(root.generated, true),
    hasDownstream: toBoolean(root.hasDownstream),
    hasPendingCrossMonth: toBoolean(root.hasPendingCrossMonth),
    workdayCount: toNumber(root.workdayCount),
    restdayCount: toNumber(root.restdayCount),
    holidayCount: toNumber(root.holidayCount),
    // 新契约只返回 shiftCount；旧版返回 scheduleCount。
    scheduleCount: toNumber(root.shiftCount ?? root.scheduleCount),
    conflictSkippedCount: toNumber(root.conflictSkippedCount),
    unavailableSkippedCount: toNumber(root.unavailableSkippedCount),
    anchorDay: toNumber(root.anchorDay),
    replayedChangeCount: toNumber(root.replayedChangeCount),
    pendingChangeCount: toNumber(root.pendingChangeCount),
    pendingChangeMonths: toStringList(root.pendingChangeMonths),
    skippedChangeCount: toNumber(root.skippedChangeCount),
    rotationChanged: toNullableBoolean(root.rotationChanged),
    downstreamMonths: toStringList(root.downstreamMonths),
    warnings: toStringList(root.warnings),
  }
}

function normalizeMemberGroups(raw: Record<string, unknown>): DutyMemberGroup[] {
  const source = raw.groups ?? raw.groupAssignments
  if (Array.isArray(source)) {
    const groups = source
      .map((item) => {
        if (typeof item === 'string') {
          const groupCode = normalizeGroupCode(item)
          return groupCode ? { groupCode, sortOrder: 0 } : null
        }
        const group = asRecord(item)
        if (!group) return null
        const groupCode = normalizeGroupCode(group.groupCode ?? group.code ?? group.groupName)
        if (!groupCode) return null
        return {
          groupCode,
          sortOrder: toNumber(group.sortOrder ?? group.seq),
        }
      })
      .filter((item): item is DutyMemberGroup => item !== null)
    return groups.sort((a, b) => a.sortOrder - b.sortOrder)
  }

  const codes = toStringList(raw.groupCodes)
  return codes
    .map((code) => normalizeGroupCode(code))
    .filter((code): code is DutyGroupCode => Boolean(code))
    .map((groupCode, index) => ({ groupCode, sortOrder: index }))
}

function normalizeMember(value: unknown): DutyMember | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toNullableNumber(raw.id)
  if (id === null) return null
  return {
    id,
    name: firstText(raw.name, raw.memberName),
    phone: firstText(raw.phone),
    isActive: toBoolean(raw.isActive ?? raw.active, true),
    joinDate: toDateText(raw.joinDate) || null,
    exitDate: toDateText(raw.exitDate) || null,
    sortOrder: toNumber(raw.sortOrder),
    remark: firstText(raw.remark),
    groups: normalizeMemberGroups(raw),
  }
}

function memberIdOf(value: Record<string, unknown>): number | null {
  return toNullableNumber(value.memberId ?? value.id)
}

function normalizeLeave(value: unknown): DutyLeave | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toNullableNumber(raw.id)
  if (id === null) return null
  const startDate = toDateText(raw.startDate)
  const endDate = toDateText(raw.endDate)
  const computedDays =
    startDate && endDate
      ? Math.round(
          (new Date(`${endDate}T00:00:00`).getTime() -
            new Date(`${startDate}T00:00:00`).getTime()) /
            86_400_000,
        ) + 1
      : 0
  return {
    id,
    memberId: toNumber(raw.memberId),
    memberName: firstText(raw.memberName),
    startDate,
    endDate,
    reason: firstText(raw.reason),
    createdAt: toText(raw.createdAt),
    days: Math.max(0, toNumber(raw.days, computedDays)),
  }
}

function normalizeReplayStatus(value: unknown, isCancelled: boolean): DutyReplayStatus {
  if (isCancelled) return 'Cancelled'
  const text = toText(value)
  const statuses: DutyReplayStatus[] = [
    'Applied',
    'PendingUnbuilt',
    'PendingRebuild',
    'Invalid',
    'Cancelled',
  ]
  return statuses.includes(text as DutyReplayStatus)
    ? (text as DutyReplayStatus)
    : 'Applied'
}

function defaultStatusText(status: DutyReplayStatus): string {
  switch (status) {
    case 'PendingUnbuilt':
      return '待生效（缺月）'
    case 'PendingRebuild':
      return '待生效（待重建）'
    case 'Invalid':
      return '已失效'
    case 'Cancelled':
      return '已撤销'
    default:
      return '已生效'
  }
}

function defaultStatusColor(status: DutyReplayStatus): string {
  switch (status) {
    case 'Applied':
      return '#2E7D32'
    case 'PendingUnbuilt':
    case 'PendingRebuild':
      return '#B45309'
    case 'Invalid':
      return '#C62828'
    default:
      return '#78909C'
  }
}

function normalizeAdjustment(value: unknown): DutyAdjustment | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toNullableNumber(raw.id)
  if (id === null) return null

  const changeType: DutyChangeType =
    toText(raw.changeType).toUpperCase() === 'SWAP' ? 'SWAP' : 'SUBSTITUTE'
  const isCancelled = toBoolean(raw.isCancelled)
  const replayStatus = normalizeReplayStatus(raw.replayStatus, isCancelled)

  return {
    id,
    changeType,
    fromMemberId: toNullableNumber(raw.fromMemberId),
    fromMemberName: firstText(raw.fromMemberName),
    fromMemberPhone: firstText(raw.fromMemberPhone),
    dutyDate: toDateText(raw.dutyDate),
    toMemberId: toNullableNumber(raw.toMemberId),
    toMemberName: firstText(raw.toMemberName),
    toMemberPhone: firstText(raw.toMemberPhone),
    toDutyDate: toDateText(raw.toDutyDate) || null,
    groupCode: normalizeGroupCode(raw.groupCode),
    reason: firstText(raw.reason),
    isCancelled,
    cancelledAt: toText(raw.cancelledAt),
    createdAt: toText(raw.createdAt),
    replayStatus,
    statusText: firstText(raw.statusText) || defaultStatusText(replayStatus),
    statusColorHex: firstText(raw.statusColorHex) || defaultStatusColor(replayStatus),
  }
}

function normalizeFutureShift(value: unknown): DutyFutureShift | null {
  const raw = asRecord(value)
  if (!raw) return null
  const scheduleId = toNullableNumber(raw.scheduleId ?? raw.id)
  if (scheduleId === null) return null
  return {
    scheduleId,
    date: toDateText(raw.date) || toDateText(raw.dutyDate),
    dateType: normalizeDateType(raw.dateType),
    groupCode: normalizeGroupCode(raw.groupCode),
    memberId: toNumber(memberIdOf(raw)),
    memberName: firstText(raw.memberName),
    isAdjusted: toBoolean(raw.isAdjusted),
  }
}

function normalizeHoliday(value: unknown): DutyHoliday | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toNullableNumber(raw.id)
  if (id === null) return null
  const date = toDateText(raw.date) || toDateText(raw.holidayDate)
  const dateType =
    toText(raw.dateType).toUpperCase() === 'MAKEUP' ? 'MAKEUP' : 'HOLIDAY'
  return {
    id,
    date,
    dateType,
    name: firstText(raw.name),
    year: toNullableNumber(raw.year),
    source: firstText(raw.source) || 'MANUAL',
    updatedAt: toText(raw.updatedAt),
  }
}

function normalizeImportRoster(value: unknown): DutyImportRoster | null {
  const raw = asRecord(value)
  if (!raw) return null
  const groupCode = normalizeGroupCode(raw.groupCode)
  if (!groupCode) return null
  const entries = Array.isArray(raw.entries)
    ? raw.entries.map((entry) => {
        const row = asRecord(entry) ?? {}
        return {
          seq: toNumber(row.seq),
          name: firstText(row.name),
        }
      })
    : []
  return {
    groupCode,
    groupName: firstText(raw.groupName) || groupDisplayName(groupCode),
    memberCount: toNumber(raw.memberCount, entries.length),
    namesPreview: firstText(raw.namesPreview),
    entries,
  }
}

function normalizeCurrentSeq(value: unknown): Record<string, Record<string, string>> {
  const root = asRecord(value)
  if (!root) return {}
  const result: Record<string, Record<string, string>> = {}
  for (const [group, lines] of Object.entries(root)) {
    const lineRoot = asRecord(lines)
    if (!lineRoot) continue
    const lineResult: Record<string, string> = {}
    for (const [dateType, memberName] of Object.entries(lineRoot)) {
      lineResult[dateType] = toText(memberName)
    }
    result[group] = lineResult
  }
  return result
}

function normalizeImportPreview(value: unknown): DutyImportPreview {
  const root = asRecord(value) ?? {}
  const errors = toStringList(root.errors)
  const summaryText = firstText(root.summaryText)
  return {
    rosters: (Array.isArray(root.rosters) ? root.rosters : [])
      .map(normalizeImportRoster)
      .filter((item): item is DutyImportRoster => item !== null),
    totalMembers: toNumber(root.totalMembers),
    newCount: toNumber(root.newCount),
    updateCount: toNumber(root.updateCount),
    removeCount: toNumber(root.removeCount),
    clearedGroupNames: toStringList(root.clearedGroupNames),
    errors,
    anchorDate: toDateText(root.anchorDate) || null,
    currentSeqByLine: normalizeCurrentSeq(root.currentSeqByLine),
    isValid: root.isValid === undefined ? errors.length === 0 : toBoolean(root.isValid),
    summaryText,
  }
}

function normalizeImportResult(value: unknown): DutyImportResult {
  const root = asRecord(value) ?? {}
  return {
    success: toBoolean(root.success, true),
    newCount: toNumber(root.newCount),
    updateCount: toNumber(root.updateCount),
    removeCount: toNumber(root.removeCount),
    anchorDate: toDateText(root.anchorDate) || null,
    errors: toStringList(root.errors),
    message: firstText(root.message),
    summaryText: firstText(root.summaryText),
  }
}

function normalizeHolidaySummary(value: unknown, year: number): DutyHolidayDownloadSummary {
  const root = asRecord(value) ?? {}
  return {
    year: toNumber(root.year, year),
    holidayCount: toNumber(root.holidayCount),
    makeupCount: toNumber(root.makeupCount),
    sourceDisplayName: firstText(root.sourceDisplayName),
    isFromFallback: toBoolean(root.isFromFallback),
    paperUrl: firstText(root.paperUrl),
  }
}

/* ── 值显示辅助 ──────────────────────────────────────────────── */

export function groupDisplayName(code: DutyGroupCode | ''): string {
  switch (code) {
    case 'LEADER':
      return '领导组'
    case 'MIDDLE':
      return '中层管理组'
    case 'MALE':
      return '男生组'
    case 'FEMALE':
      return '女生组'
    default:
      return code || '未分组'
  }
}

export function dateTypeDisplayName(code: DutyDateType): string {
  switch (code) {
    case 'RESTDAY':
      return '休息日'
    case 'HOLIDAY':
      return '法定节假日'
    default:
      return '工作日'
  }
}

/* ── 月份值班表 ──────────────────────────────────────────────── */

export function getDutyMonth(year: number, month: number): Promise<DutyMonthSchedule> {
  return apiHttp
    .get<unknown>(`${BASE}/months/${year}/${month}`)
    .then((response) => normalizeMonth(response.data))
}

export function generateDutyMonth(
  year: number,
  month: number,
  force = false,
): Promise<DutyGenerationSummary> {
  return apiHttp
    .post<unknown>(`${BASE}/months/${year}/${month}/generate`, null, {
      params: { force },
      timeout: 120_000,
    })
    .then((response) => normalizeSummary(response.data))
}

export async function clearDutyMonth(year: number, month: number): Promise<void> {
  try {
    await apiHttp.post(`${BASE}/months/${year}/${month}/clear`, null, {
      timeout: 120_000,
    })
  } catch (error) {
    if (error instanceof ApiError && (error.status === 404 || error.status === 405)) {
      await apiHttp.post(`${BASE}/clear`, null, {
        params: { year, month },
        timeout: 120_000,
      })
      return
    }
    throw error
  }
}

async function postMonthAction(
  year: number,
  month: number,
  action: string,
  months: string[],
): Promise<number> {
  const body = { months }
  const primary = `${BASE}/months/${year}/${month}/${action}`
  const fallback = `${BASE}/${action}`
  const readCount = (data: unknown): number => {
    const root = asRecord(data)
    return root
      ? toNumber(root.rebuilt ?? root.generated ?? root.count, months.length)
      : toNumber(data, months.length)
  }
  try {
    const response = await apiHttp.post<unknown>(primary, body, { timeout: 120_000 })
    return readCount(response.data)
  } catch (error) {
    if (error instanceof ApiError && (error.status === 404 || error.status === 405)) {
      const response = await apiHttp.post<unknown>(fallback, body, { timeout: 120_000 })
      return readCount(response.data)
    }
    throw error
  }
}

/** 按时间顺序重排后续月份。 */
export function rebuildDownstreamMonths(
  year: number,
  month: number,
  months: string[],
): Promise<number> {
  return postMonthAction(year, month, 'rebuild-downstream', months)
}

/** 补齐缺失月份（跨月待生效串班/代班）。 */
export function fillMissingMonths(
  year: number,
  month: number,
  months: string[],
): Promise<number> {
  return postMonthAction(year, month, 'fill-missing', months)
}

/**
 * 按 yyyy-MM 逐月补齐。服务端 /fill-missing 以路径年月为生成目标，
 * 因此调用方拿到受影响月份列表时必须逐月调用，不能只传当前月。
 */
export async function fillDutyMonths(months: string[]): Promise<number> {
  let total = 0
  const unique = Array.from(new Set(months))
  for (const key of unique) {
    const match = /^(\d{4})-(\d{2})$/.exec(key.trim())
    if (!match) continue
    const year = Number(match[1])
    const month = Number(match[2])
    if (month < 1 || month > 12) continue
    total += await fillMissingMonths(year, month, [key])
  }
  return total
}

/* ── 设置 ────────────────────────────────────────────────────── */

export function getDutySettings(): Promise<DutySettings> {
  return apiHttp
    .get<unknown>(`${BASE}/settings`)
    .then((response) => normalizeSettings(response.data))
}

export function saveDutySettings(settings: DutySettings): Promise<void> {
  return apiHttp
    .put(`${BASE}/settings`, settings)
    .then(() => undefined)
}

/* ── 成员 ────────────────────────────────────────────────────── */

export function listDutyMembers(): Promise<DutyMember[]> {
  return apiHttp
    .get<unknown>(`${BASE}/members`)
    .then((response) =>
      unwrapList(response.data)
        .map(normalizeMember)
        .filter((item): item is DutyMember => item !== null),
    )
}

export function createDutyMember(input: DutyMemberInput): Promise<void> {
  return apiHttp
    .post(`${BASE}/members`, { id: 0, ...input })
    .then(() => undefined)
}

export function updateDutyMember(id: number, input: DutyMemberInput): Promise<void> {
  return apiHttp
    .put(`${BASE}/members/${id}`, { id, ...input })
    .then(() => undefined)
}

export function deleteDutyMember(id: number): Promise<void> {
  return apiHttp.delete(`${BASE}/members/${id}`).then(() => undefined)
}

export function moveDutyMember(
  id: number,
  direction: 'up' | 'down',
  group: DutyGroupCode,
): Promise<void> {
  return apiHttp
    .post(`${BASE}/members/${id}/move`, null, { params: { direction, group } })
    .then(() => undefined)
}

export function toggleDutyMember(id: number): Promise<void> {
  return apiHttp.post(`${BASE}/members/${id}/toggle`).then(() => undefined)
}

export function downloadDutyMemberTemplate(): Promise<Blob> {
  return apiHttp
    .get<Blob>(`${BASE}/members/import-template`, {
      responseType: 'blob',
      timeout: 120_000,
    })
    .then((response) => response.data)
}

function buildImportForm(file: File): FormData {
  const form = new FormData()
  form.append('file', file)
  return form
}

async function postImportFile(
  action: 'preview' | 'import',
  file: File,
): Promise<unknown> {
  const primary = `${BASE}/members/import/${action}`
  const fallback = `${BASE}/import/${action}`
  try {
    const response = await apiHttp.post<unknown>(primary, buildImportForm(file), {
      timeout: 300_000,
    })
    return response.data
  } catch (error) {
    if (error instanceof ApiError && (error.status === 404 || error.status === 405)) {
      const response = await apiHttp.post<unknown>(fallback, buildImportForm(file), {
        timeout: 300_000,
      })
      return response.data
    }
    throw error
  }
}

export function previewDutyMemberImport(file: File): Promise<DutyImportPreview> {
  return postImportFile('preview', file).then(normalizeImportPreview)
}

export function importDutyMembers(file: File): Promise<DutyImportResult> {
  return postImportFile('import', file).then(normalizeImportResult)
}

/* ── 请假 ────────────────────────────────────────────────────── */

function normalizeMonthList(value: unknown): string[] {
  if (Array.isArray(value)) return toStringList(value)
  const root = asRecord(value)
  if (!root) return []
  const nested = root.affectedMonths ?? root.deletedMonths ?? root.months
  return toStringList(nested)
}

export function listDutyLeaves(memberId?: number): Promise<DutyLeave[]> {
  return apiHttp
    .get<unknown>(`${BASE}/leaves`, {
      params: memberId ? { memberId } : undefined,
    })
    .then((response) =>
      unwrapList(response.data)
        .map(normalizeLeave)
        .filter((item): item is DutyLeave => item !== null),
    )
}

export function createDutyLeave(input: DutyLeaveInput): Promise<string[]> {
  return apiHttp
    .post<unknown>(`${BASE}/leaves`, input, { timeout: 120_000 })
    .then((response) => normalizeMonthList(response.data))
}

export function deleteDutyLeave(id: number): Promise<string[]> {
  return apiHttp
    .delete<unknown>(`${BASE}/leaves/${id}`, { timeout: 120_000 })
    .then((response) => normalizeMonthList(response.data))
}

export function getDutyLeaveAffectedMonths(startDate: string): Promise<string[]> {
  return apiHttp
    .get<unknown>(`${BASE}/leaves/affected-months`, {
      // 契约参数为 start；同时带 startDate 兼容旧实现。
      params: { start: startDate, startDate },
    })
    .then((response) => normalizeMonthList(response.data))
}

/* ── 班务调整 ────────────────────────────────────────────────── */

export function listDutyAdjustments(includeCancelled = true): Promise<DutyAdjustment[]> {
  return apiHttp
    .get<unknown>(`${BASE}/adjustments`, { params: { includeCancelled } })
    .then((response) =>
      unwrapList(response.data)
        .map(normalizeAdjustment)
        .filter((item): item is DutyAdjustment => item !== null),
    )
}

export function createDutyAdjustment(input: DutyAdjustmentInput): Promise<void> {
  return apiHttp
    .post(`${BASE}/adjustments`, input, { timeout: 120_000 })
    .then(() => undefined)
}

export function rescheduleDutyAdjustment(id: number, newDate: string): Promise<void> {
  return apiHttp
    .put(`${BASE}/adjustments/${id}`, { newDate }, { timeout: 120_000 })
    .then(() => undefined)
}

export function revokeDutyAdjustment(id: number): Promise<void> {
  return apiHttp
    .post(`${BASE}/adjustments/${id}/revoke`, null, { timeout: 120_000 })
    .then(() => undefined)
}

export function listDutyMemberFutureShifts(memberId: number): Promise<DutyFutureShift[]> {
  return apiHttp
    .get<unknown>(`${BASE}/members/${memberId}/future-shifts`)
    .then((response) =>
      unwrapList(response.data)
        .map(normalizeFutureShift)
        .filter((item): item is DutyFutureShift => item !== null)
        .sort((a, b) => a.date.localeCompare(b.date)),
    )
}

/* ── 节假日 ──────────────────────────────────────────────────── */

export function listDutyHolidays(year: number): Promise<DutyHoliday[]> {
  return apiHttp
    .get<unknown>(`${BASE}/holidays`, { params: { year } })
    .then((response) =>
      unwrapList(response.data)
        .map(normalizeHoliday)
        .filter((item): item is DutyHoliday => item !== null)
        .sort((a, b) => a.date.localeCompare(b.date)),
    )
}

export function saveDutyHoliday(input: DutyHolidayInput): Promise<void> {
  return apiHttp
    .post(`${BASE}/holidays`, {
      ...input,
      holidayDate: input.date,
      year: Number(input.date.slice(0, 4)) || null,
    })
    .then(() => undefined)
}

export function deleteDutyHoliday(id: number): Promise<void> {
  return apiHttp.delete(`${BASE}/holidays/${id}`).then(() => undefined)
}

export function downloadDutyHolidays(year: number): Promise<DutyHolidayDownloadSummary> {
  return apiHttp
    .post<unknown>(`${BASE}/holidays/download`, null, {
      params: { year },
      timeout: 120_000,
    })
    .then((response) => normalizeHolidaySummary(response.data, year))
}

/* ── 导出 / 打印 ─────────────────────────────────────────────── */

function fileNameFromDisposition(header: unknown, fallback: string): string {
  const text = toText(header)
  if (!text) return fallback
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(text)?.[1]
  if (encoded) {
    try {
      return decodeURIComponent(encoded.trim())
    } catch {
      return encoded.trim()
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(text)?.[1]
  return plain?.trim() || fallback
}

function normalizeJobInfo(value: unknown): DutyJobInfo | null {
  const root = asRecord(value)
  if (!root) return null
  const jobNo = firstText(root.jobNo)
  const id = toNullableNumber(root.id)
  if (!jobNo && id === null) return null
  return {
    id: id ?? 0,
    jobNo,
    status: firstText(root.status),
    jobKind: firstText(root.jobKind),
    mode: firstText(root.mode),
    templateId: toNumber(root.templateId),
    templateName: firstText(root.templateName),
    fileCount: toNumber(root.fileCount),
  }
}

async function readBlobJson(blob: Blob): Promise<Record<string, unknown> | null> {
  try {
    const parsed = JSON.parse(await blob.text()) as unknown
    return asRecord(parsed)
  } catch {
    return null
  }
}

async function postExport(
  url: string,
  body: Record<string, unknown>,
  fallbackName: string,
): Promise<DutyExportResult> {
  const response = await apiHttp.post<Blob>(url, body, {
    responseType: 'blob',
    timeout: 300_000,
  })
  const contentType = toText(response.headers['content-type']).toLowerCase()
  if (contentType.includes('application/json')) {
    const payload = await readBlobJson(response.data)
    const job = normalizeJobInfo(payload)
    if (job) return { type: 'job', job }
    const code = firstText(payload?.code) || `HTTP_${response.status}`
    const message = firstText(payload?.message) || '导出失败'
    throw new ApiError(message, { code, status: response.status })
  }
  return {
    type: 'file',
    blob: response.data,
    fileName: fileNameFromDisposition(
      response.headers['content-disposition'],
      fallbackName,
    ),
  }
}

/** 政务值班表连续月份导出（后端按月生成文件，可能打包为 zip）。 */
export function exportDutyGovRange(range: DutyExportRange): Promise<DutyExportResult> {
  const { startYear, startMonth, endYear, endMonth } = range
  const name =
    startYear === endYear && startMonth === endMonth
      ? `${startYear}年${startMonth}月政务值班表.xlsx`
      : `政务值班表_${startYear}-${pad2(startMonth)}_${endYear}-${pad2(endMonth)}.zip`
  return postExport(
    `${BASE}/export`,
    {
      ...range,
      fromYear: startYear,
      fromMonth: startMonth,
      toYear: endYear,
      toMonth: endMonth,
      year: startYear,
      month: startMonth,
      format: 'xlsx',
    },
    name,
  )
}

/**
 * 当前月 PDF 导出。服务端当前与 Excel 共用 /export（Agent 保存作业）；
 * 若后端未来直接返回文件字节，前端同样按文件下载处理。
 */
export function exportDutyMonthPdf(
  year: number,
  month: number,
): Promise<DutyExportResult> {
  return postExport(
    `${BASE}/export`,
    {
      year,
      month,
      startYear: year,
      startMonth: month,
      endYear: year,
      endMonth: month,
      format: 'pdf',
    },
    `${year}年${month}月政务值班表.pdf`,
  )
}

export function printDutyMonth(year: number, month: number): Promise<string> {
  return apiHttp
    .post<unknown>(
      `${BASE}/print`,
      { year, month, copies: 1 },
      { timeout: 120_000 },
    )
    .then((response) => {
      const job = normalizeJobInfo(response.data)
      if (job) {
        return `打印任务已提交：${job.jobNo || `#${job.id}`}（${job.status || '待处理'}，${job.fileCount || 1} 个文件）`
      }
      const root = asRecord(response.data)
      return firstText(root?.message) || '打印任务已提交'
    })
}

/* ── 页面辅助 ────────────────────────────────────────────────── */

/** 今天（yyyy-MM-dd），供表单默认值使用。 */
export const dutyToday = todayText
