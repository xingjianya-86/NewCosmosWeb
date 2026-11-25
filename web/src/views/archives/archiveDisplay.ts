/** 救助档案管理的展示映射与小工具（与旧版 ArchiveQueryViewModel 口径一致）。 */

const CLASSIFICATION_LABELS: Record<string, string> = {
  RuralSubsistence: '最低生活保障对象',
  UrbanSubsistence: '最低生活保障对象',
  RuralLowIncome: '最低生活保障边缘家庭',
  UrbanLowIncome: '最低生活保障边缘家庭',
  RuralLowIncomeSingle: '最低生活保障对象（单人）',
  UrbanLowIncomeSingle: '最低生活保障对象（单人）',
  RuralDestituteCentralized: '特困人员（集中供养）',
  UrbanDestituteCentralized: '特困人员（集中供养）',
  RuralDestituteScattered: '特困人员（分散供养）',
  UrbanDestituteScattered: '特困人员（分散供养）',
  RuralRigidExpenditure: '刚性支出困难家庭',
  UrbanRigidExpenditure: '刚性支出困难家庭',
  RuralIncomeExceeded: '收入超标',
  UrbanIncomeExceeded: '收入超标',
  LowIncome: '最低生活保障边缘家庭',
  Destitute: '特困人员',
  RigidExpenditure: '刚性支出困难家庭',
  IncomeExceeded: '收入超标',
}

/** 分类代码转中文全称；未知代码原样展示。 */
export function classificationLabel(code: string | null | undefined): string {
  const value = (code ?? '').trim()
  if (!value) return ''
  return CLASSIFICATION_LABELS[value] ?? value
}

/** 数字计数：空值显示 —，否则千分位。 */
export function formatCount(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return value.toLocaleString('zh-CN')
}

/** 数字（默认两位小数）；空值显示 —。 */
export function formatDecimal(
  value: number | null | undefined,
  digits = 2,
): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return value.toFixed(digits)
}

/** 金额（两位小数 + 单位）；0 或空显示 —。 */
export function formatAmount(
  value: number | null | undefined,
  unit = '元',
): string {
  if (value === null || value === undefined || !Number.isFinite(value) || value <= 0) {
    return '—'
  }
  return `${value.toFixed(2)} ${unit}`
}

/** ISO / 后端时间字符串 → yyyy-MM-dd HH:mm。 */
export function formatDateTimeText(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  const pad = (part: number): string => String(part).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

const JOB_STATUS_LABELS: Record<string, string> = {
  Pending: '排队中',
  Running: '执行中',
  Processing: '执行中',
  Succeeded: '已完成',
  Completed: '已完成',
  Failed: '失败',
  Cancelled: '已取消',
}

export function archiveJobStatusLabel(status: string): string {
  return JOB_STATUS_LABELS[status] ?? (status || '—')
}

export type ArchiveJobStatusTone = 'online' | 'offline' | 'idle' | 'default'

export function archiveJobStatusTone(status: string): ArchiveJobStatusTone {
  if (status === 'Succeeded' || status === 'Completed') return 'online'
  if (status === 'Failed' || status === 'Cancelled') return 'offline'
  if (status === 'Pending' || status === 'Running' || status === 'Processing') return 'idle'
  return 'default'
}

/** 任务是否已进入终态（用于停止轮询）。 */
export function isArchiveJobTerminal(status: string): boolean {
  return ['Succeeded', 'Completed', 'Failed', 'Cancelled'].includes(status)
}
