import type { RecoverySourceType, StoppedPersonDto } from '@/types/recovery'
import { recoverySourceTypeLabel } from '@/types/recovery'

/** 停止人员选择结果在页面间的传递键（列表 → 表单）。 */
export const RECOVERY_PERSON_STORAGE_KEY = 'recovery:person'

const ID_CARD_18 = /^\d{17}[\dXx]$/
const ID_CARD_WEIGHTS = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2]
const ID_CARD_CHECK_CODES = ['1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2']

export function pad2(value: number): string {
  return value < 10 ? `0${value}` : String(value)
}

export function buildMonth(year: number, month: number): string {
  return `${year}-${pad2(month)}`
}

export function parseMonth(value: string | null | undefined): { year: number; month: number } | null {
  if (!value) return null
  const match = /^(\d{4})-(\d{1,2})/.exec(value)
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  if (!Number.isFinite(year) || !Number.isFinite(month) || month < 1 || month > 12) return null
  return { year, month }
}

/** 含首尾月的月数；无效区间返回 0。 */
export function monthDiff(startMonth: string, endMonth: string): number {
  const start = parseMonth(startMonth)
  const end = parseMonth(endMonth)
  if (!start || !end) return 0
  const months = (end.year - start.year) * 12 + end.month - start.month + 1
  return months > 0 ? months : 0
}

export function formatMoney(value: number | null | undefined, withUnit = true): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return withUnit ? '—' : ''
  const text = value.toLocaleString('zh-CN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
  return withUnit ? `${text} 元` : text
}

export function formatMonth(value: string | null | undefined): string {
  if (!value) return '—'
  const parsed = parseMonth(value)
  return parsed ? `${parsed.year}年${parsed.month}月` : value
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())} ${pad2(date.getHours())}:${pad2(date.getMinutes())}`
}

export function normalizeIdCard(value: string | null | undefined): string {
  return (value ?? '').trim().toUpperCase()
}

/** 与 Core Helpers/IdCardValidator.IsValid 同口径：18 位 + 校验码。 */
export function isValidIdCard(value: string | null | undefined): boolean {
  const text = normalizeIdCard(value)
  if (!ID_CARD_18.test(text)) return false
  let sum = 0
  for (let index = 0; index < 17; index += 1) {
    sum += Number(text[index]) * (ID_CARD_WEIGHTS[index] ?? 0)
  }
  return text[17] === ID_CARD_CHECK_CODES[sum % 11]
}

export function roundFen(value: number): number {
  if (!Number.isFinite(value)) return 0
  return Math.round((value + Number.EPSILON) * 100) / 100
}

export function sourceLabel(value: string | null | undefined): string {
  return recoverySourceTypeLabel(value)
}

export function readStoppedPerson(): StoppedPersonDto | null {
  try {
    const raw = sessionStorage.getItem(RECOVERY_PERSON_STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<StoppedPersonDto> | null
    if (!parsed || typeof parsed !== 'object') return null
    return {
      sourceType: String(parsed.sourceType ?? ''),
      sourceDisplay: String(parsed.sourceDisplay ?? sourceLabel(parsed.sourceType ?? '')),
      sourceId: Number(parsed.sourceId ?? 0),
      personName: String(parsed.personName ?? ''),
      idCard: String(parsed.idCard ?? ''),
      monthlyAmount: Number(parsed.monthlyAmount ?? 0),
    }
  } catch {
    return null
  }
}

export function writeStoppedPerson(person: StoppedPersonDto): void {
  try {
    sessionStorage.setItem(RECOVERY_PERSON_STORAGE_KEY, JSON.stringify(person))
  } catch {
    // 隐私模式 / 存储配额不足时忽略：表单页会提示重新选择人员。
  }
}

export function clearStoppedPerson(): void {
  try {
    sessionStorage.removeItem(RECOVERY_PERSON_STORAGE_KEY)
  } catch {
    // 忽略存储异常。
  }
}

/** 分类值是否在已知下拉项内。 */
export function isKnownSourceType(value: string): value is RecoverySourceType {
  return (
    value === 'RuralSubsistence'
    || value === 'UrbanSubsistence'
    || value === 'RigidExpenditure'
    || value === 'TempRelief'
    || value === 'Elderly'
    || value === 'Other'
  )
}
