import type { ApplicationFormBasic } from './types'

const ID_CARD_18 = /^\d{17}[\dXx]$/
const ID_CARD_15 = /^\d{15}$/

export interface IdCardInfo {
  /** YYYY-MM-DD */
  birthDate: string
  gender: string
  age: number | null
}

export interface BasicValidationIssue {
  field: keyof ApplicationFormBasic
  message: string
}

/** 去掉首尾空格，末位 x 统一大写。 */
export function normalizeIdCard(value: string | null | undefined): string {
  return (value ?? '').trim().toUpperCase()
}

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function isValidDate(year: number, month: number, day: number): boolean {
  if (month < 1 || month > 12 || day < 1 || day > 31) return false
  const date = new Date(Date.UTC(year, month - 1, day))
  return (
    date.getUTCFullYear() === year &&
    date.getUTCMonth() === month - 1 &&
    date.getUTCDate() === day
  )
}

/** 从身份证解析出生日期、性别与周岁；格式不合法时返回 null。 */
export function parseIdCard(value: string | null | undefined): IdCardInfo | null {
  const text = normalizeIdCard(value)
  let year: number
  let month: number
  let day: number
  let sequence: string

  if (ID_CARD_18.test(text)) {
    year = Number(text.slice(6, 10))
    month = Number(text.slice(10, 12))
    day = Number(text.slice(12, 14))
    sequence = text.slice(14, 17)
  } else if (ID_CARD_15.test(text)) {
    year = Number(text.slice(6, 8)) + 1900
    month = Number(text.slice(8, 10))
    day = Number(text.slice(10, 12))
    sequence = text.slice(12, 15)
  } else {
    return null
  }

  if (!isValidDate(year, month, day)) return null

  return {
    birthDate: `${year}-${pad(month)}-${pad(day)}`,
    gender: Number(sequence) % 2 === 1 ? '男' : '女',
    age: ageFromBirthDate(`${year}-${pad(month)}-${pad(day)}`),
  }
}

/** 校验身份证；返回空字符串表示通过。 */
export function getIdCardError(value: string | null | undefined, required = true): string {
  const text = normalizeIdCard(value)
  if (!text) return required ? '请输入身份证号' : ''
  if (!ID_CARD_18.test(text) && !ID_CARD_15.test(text)) return '请输入 18 位身份证号'
  if (!parseIdCard(text)) return '身份证号中的出生日期无效'
  return ''
}

/** 按出生日期计算周岁；出生日期不合法时返回 null。 */
export function ageFromBirthDate(birthDate: string | null | undefined): number | null {
  const text = (birthDate ?? '').trim()
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(text)
  if (!match) return null

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (!isValidDate(year, month, day)) return null

  const now = new Date()
  let age = now.getFullYear() - year
  const beforeBirthday =
    now.getMonth() + 1 < month || (now.getMonth() + 1 === month && now.getDate() < day)
  if (beforeBirthday) age -= 1
  return age >= 0 ? age : null
}

/** 第 1 步必填校验（步骤切换与保存前使用）。 */
export function validateBasic(basic: ApplicationFormBasic): BasicValidationIssue[] {
  const issues: BasicValidationIssue[] = []

  if (!basic.applicantName.trim()) {
    issues.push({ field: 'applicantName', message: '请输入申请人姓名' })
  }

  const idCardError = getIdCardError(basic.applicantIdCard)
  if (idCardError) {
    issues.push({ field: 'applicantIdCard', message: idCardError })
  }

  return issues
}

export function isBasicValid(basic: ApplicationFormBasic): boolean {
  return validateBasic(basic).length === 0
}
