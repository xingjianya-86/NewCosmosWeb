const dateFormatter = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

const dateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

const shortDateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

const moneyFormatter = new Intl.NumberFormat('zh-CN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

export type ChipTone = 'ink' | 'seal' | 'pine' | 'amber'

export function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return dateFormatter.format(date)
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return dateTimeFormatter.format(date)
}

export function formatShortDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return shortDateTimeFormatter.format(date)
}

export function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return moneyFormatter.format(value)
}

/** 计发年月等 YYYY-MM 字符串转「2024年10月」；非法值原样返回。 */
export function formatMonth(value: string | null | undefined): string {
  const text = (value ?? '').trim()
  if (!text) return '—'
  const match = /^(\d{4})-(\d{2})$/.exec(text)
  if (!match) return text
  return `${match[1]}年${Number(match[2])}月`
}

export function formatCount(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : String(value)
}

export function formatAge(age: number | null | undefined): string {
  return age === null || age === undefined ? '—' : `${age} 岁`
}

export function applicationStatusTone(status: string): ChipTone {
  switch (status) {
    case 'Confirmed':
      return 'pine'
    case 'Stopped':
      return 'seal'
    case 'Draft':
      return 'amber'
    default:
      return 'ink'
  }
}

export function reviewStatusTone(status: string): ChipTone {
  return status === 'Pending' ? 'amber' : 'pine'
}

export function reviewResultTone(result: string): ChipTone {
  return result === 'Changed' ? 'amber' : 'pine'
}

export function pendingTypeTone(type: string): ChipTone {
  return type === 'Transfer' ? 'seal' : 'pine'
}
