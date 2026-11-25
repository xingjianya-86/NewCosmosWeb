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

const moneyFormatter = new Intl.NumberFormat('zh-CN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

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

/** 金额只做格式化，不做任何重算。 */
export function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return moneyFormatter.format(value)
}

export function formatAmount(value: number | null | undefined): string {
  const text = formatMoney(value)
  return text === '—' ? text : `${text} 元`
}

export function graceDaysText(days: number | null | undefined): string {
  if (days === null || days === undefined || !Number.isFinite(days)) return '—'
  return days >= 0 ? `剩 ${days} 天` : `逾期 ${Math.abs(days)} 天`
}

export function formatPeriod(
  startDate: string | null | undefined,
  months: number | null | undefined,
): string {
  if (!startDate || !months || months <= 0) return '—'
  const start = new Date(startDate)
  if (Number.isNaN(start.getTime())) return '—'
  const end = new Date(start.getFullYear(), start.getMonth() + months, start.getDate())
  end.setDate(end.getDate() - 1)
  return `${formatDate(startDate)} 至 ${dateFormatter.format(end)}（${months} 个月）`
}

export function ageText(age: number | null | undefined): string {
  return age && age > 0 ? `${age} 岁` : '—'
}
