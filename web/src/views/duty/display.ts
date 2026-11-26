import { ApiError, getErrorMessage } from '@/api/http'
export { groupDisplayName } from '@/api/duty'
import type {
  DutyAdjustment,
  DutyDateType,
  DutyDayRow,
  DutyGroupCode,
  DutyReplayStatus,
  DutyScheduleCell,
} from '@/types/duty'

export interface GroupMeta {
  code: DutyGroupCode
  label: string
  shortLabel: string
  tone: 'violet' | 'amber' | 'pine' | 'seal'
}

export const DUTY_GROUP_ORDER: DutyGroupCode[] = ['LEADER', 'MIDDLE', 'MALE', 'FEMALE']

export const GROUP_META: Record<DutyGroupCode, GroupMeta> = {
  LEADER: { code: 'LEADER', label: '领导组', shortLabel: '领导', tone: 'violet' },
  MIDDLE: { code: 'MIDDLE', label: '中层管理组', shortLabel: '中层', tone: 'amber' },
  MALE: { code: 'MALE', label: '男生组', shortLabel: '男', tone: 'pine' },
  FEMALE: { code: 'FEMALE', label: '女生组', shortLabel: '女', tone: 'seal' },
}

export interface DateTypeMeta {
  label: string
  tone: 'ink' | 'pine' | 'seal'
}

export const DATE_TYPE_META: Record<DutyDateType, DateTypeMeta> = {
  WORKDAY: { label: '工作日', tone: 'ink' },
  RESTDAY: { label: '休息日', tone: 'pine' },
  HOLIDAY: { label: '法定节假日', tone: 'seal' },
}

export function dateTypeLabel(code: DutyDateType): string {
  return DATE_TYPE_META[code].label
}

export function cellOf(day: DutyDayRow, group: DutyGroupCode): DutyScheduleCell {
  return day[group.toLowerCase() as 'leader' | 'middle' | 'male' | 'female']
}

export function pad2(value: number): string {
  return value < 10 ? `0${value}` : String(value)
}

export function todayText(): string {
  const now = new Date()
  return `${now.getFullYear()}-${pad2(now.getMonth() + 1)}-${pad2(now.getDate())}`
}

/** yyyy-MM-dd → 11月26日 */
export function formatMonthDay(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
  if (!match) return value || '—'
  return `${Number(match[2])}月${Number(match[3])}日`
}

/** yyyy-MM-dd → 11-26 */
export function formatShortDate(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
  if (!match) return value || '—'
  return `${match[2]}-${match[3]}`
}

export function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  return value.slice(0, 10)
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const match = /^(\d{4}-\d{2}-\d{2})[T ](\d{2}:\d{2})/.exec(value)
  return match ? `${match[1]} ${match[2]}` : value
}

export function weekdayText(value: string): string {
  const parsed = new Date(`${value}T00:00:00`)
  if (Number.isNaN(parsed.getTime())) return ''
  return new Intl.DateTimeFormat('zh-CN', { weekday: 'short' })
    .format(parsed)
    .replace('星期', '')
    .replace('周', '')
}

export function monthTitle(year: number, month: number): string {
  return `${year} 年 ${month} 月`
}

export function monthKey(year: number, month: number): string {
  return `${year}-${pad2(month)}`
}

export function shiftMonth(
  year: number,
  month: number,
  offset: number,
): { year: number; month: number } {
  const total = year * 12 + (month - 1) + offset
  return { year: Math.floor(total / 12), month: (total % 12) + 1 }
}

export function parseMonthKey(value: string): { year: number; month: number } | null {
  const match = /^(\d{4})-(\d{2})$/.exec(value.trim())
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  if (month < 1 || month > 12) return null
  return { year, month }
}

export function monthCount(startYear: number, startMonth: number, endYear: number, endMonth: number): number {
  return (endYear - startYear) * 12 + (endMonth - startMonth) + 1
}

export interface StatusMeta {
  label: string
  tone: 'pine' | 'amber' | 'seal' | 'muted'
}

const STATUS_META: Record<DutyReplayStatus, StatusMeta> = {
  Applied: { label: '已生效', tone: 'pine' },
  PendingUnbuilt: { label: '待生效（缺月）', tone: 'amber' },
  PendingRebuild: { label: '待生效（待重建）', tone: 'amber' },
  Invalid: { label: '已失效', tone: 'seal' },
  Cancelled: { label: '已撤销', tone: 'muted' },
}

export function statusMeta(adjustment: DutyAdjustment): StatusMeta {
  return STATUS_META[adjustment.replayStatus]
}

export function adjustmentDirection(adjustment: DutyAdjustment): string {
  if (adjustment.changeType === 'SWAP') {
    return `${adjustment.fromMemberName}（${formatShortDate(adjustment.dutyDate)}） ↔ ${adjustment.toMemberName}（${formatShortDate(adjustment.toDutyDate ?? '')}）`
  }
  return `${adjustment.toMemberName} 代 ${adjustment.fromMemberName}（${formatShortDate(adjustment.dutyDate)}）`
}

/** 统一 403 文案；其余错误走后端 message。 */
export function dutyError(
  error: unknown,
  fallback: string,
  action = '执行该操作',
  permission = 'DUTY_MANAGE',
): string {
  if (error instanceof ApiError && error.status === 403) {
    return `无权限${action}（需要 ${permission} 权限）`
  }
  return getErrorMessage(error, fallback)
}

export function isForbidden(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403
}

export function saveBlob(blob: Blob, fileName: string): void {
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000)
}

export function readableSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 KB'
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}
