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

export function textOrDash(value: string | null | undefined): string {
  const text = (value ?? '').trim()
  return text || '—'
}

const RELATIONSHIP_LABELS: Record<string, string> = {
  Head: '本人/户主',
  Spouse: '配偶',
  Son: '儿子',
  Daughter: '女儿',
  Grandson: '孙子',
  Granddaughter: '孙女',
  Father: '父亲',
  Mother: '母亲',
  Brother: '兄弟',
  Sister: '姐妹',
  Other: '其他',
}

const ID_TYPE_LABELS: Record<string, string> = {
  ResidentIdCard: '居民身份证',
  IdCard: '居民身份证',
  Passport: '护照',
  Other: '其他',
}

const APPLICATION_REASON_LABELS: Record<string, string> = {
  LowIncome: '收入低',
  Illness: '疾病',
  Disaster: '灾害',
  Disability: '残疾',
  Education: '就学',
  Unemployment: '失业',
  LandLoss: '失地',
  Accident: '意外事故',
  Other: '其他',
}

function labelOf(
  value: string | null | undefined,
  labels: Record<string, string>,
): string {
  const text = (value ?? '').trim()
  if (!text) return '—'
  return labels[text] ?? (/[\u4e00-\u9fa5]/.test(text) ? text : '其他')
}

export function relationshipText(value: string | null | undefined): string {
  return labelOf(value, RELATIONSHIP_LABELS)
}

export function idTypeText(value: string | null | undefined): string {
  return labelOf(value, ID_TYPE_LABELS)
}

export function applicationReasonText(value: string | null | undefined): string {
  return labelOf(value, APPLICATION_REASON_LABELS)
}
