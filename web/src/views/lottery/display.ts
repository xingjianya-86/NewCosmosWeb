import type { LotteryJob, LotteryVerification } from '@/types/lottery'

export interface LotteryNumberToken {
  text: string
  isBlue: boolean
  isHit: boolean
}

const DATE_ONLY = /^(\d{4}-\d{2}-\d{2})/

/** 只取 yyyy-MM-dd；解析失败时原样返回，空值显示 “—”。 */
export function formatDateOnly(value: string | null | undefined): string {
  if (!value) return '—'
  const matched = DATE_ONLY.exec(value)
  if (matched) return matched[1]
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(
    date.getDate(),
  ).padStart(2, '0')}`
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return `${formatDateOnly(value)} ${String(date.getHours()).padStart(2, '0')}:${String(
    date.getMinutes(),
  ).padStart(2, '0')}`
}

export function formatCount(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return value.toLocaleString('zh-CN')
}

export function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return `¥${value.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

/** 销售额 / 奖池这类大额整数展示。 */
export function formatAmount(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return value.toLocaleString('zh-CN', { maximumFractionDigits: 0 })
}

export function formatNumberList(numbers: number[]): string {
  return numbers.length > 0 ? numbers.map((n) => String(n).padStart(2, '0')).join(' ') : '—'
}

export function formatPercent(value: number): string {
  return `${value.toFixed(1)}%`
}

/** 开奖号码小球：命中实心红/蓝，未命中浅底描边。 */
export function buildNumberTokens(
  redNumbers: number[],
  blueNumbers: number[],
  actualRedNumbers: number[],
  actualBlueNumbers: number[],
  verified: boolean,
): LotteryNumberToken[] {
  const actualReds = new Set(actualRedNumbers)
  const actualBlues = new Set(actualBlueNumbers)
  const tokens: LotteryNumberToken[] = []

  for (const number of redNumbers) {
    tokens.push({
      text: String(number).padStart(2, '0'),
      isBlue: false,
      isHit: verified && actualReds.has(number),
    })
  }
  for (const number of blueNumbers) {
    tokens.push({
      text: String(number).padStart(2, '0'),
      isBlue: true,
      isHit: verified && actualBlues.has(number),
    })
  }
  return tokens
}

export function jobStatusClass(job: LotteryJob): string {
  return `is-${job.status.toLowerCase()}`
}

/** 任务日志尾部：优先后端日志，其次阶段消息 / 错误。 */
export function jobLogText(job: LotteryJob | null): string {
  if (!job) return ''
  const lines = [job.logTail, job.message, job.error]
    .map((item) => item.trim())
    .filter(Boolean)
  const text = lines.join('\n')
  if (text.length <= 4000) return text
  return `…（仅显示最后 4000 字）\n${text.slice(-4000)}`
}

/** 把号码整理成可粘贴 / 可下载的多行文本。 */
export function predictionText(
  lotteryLabel: string,
  generatedAt: Date,
  algorithm: string,
  predictions: Array<{ redNumbers: number[]; blueNumbers: number[] }>,
): string {
  const lines = [
    `=== ${lotteryLabel} 预测结果 ===`,
    `生成时间: ${formatClock(generatedAt)}`,
    `算法: ${algorithm}`,
    `注数: ${predictions.length}`,
    '',
  ]

  predictions.forEach((item, index) => {
    lines.push(
      `第${index + 1}注: ${item.redNumbers.map((n) => String(n).padStart(2, '0')).join(',')} + ${
        item.blueNumbers.map((n) => String(n).padStart(2, '0')).join(',') || '—'
      }`,
    )
  })

  lines.push('', '祝您好运！')
  return lines.join('\n')
}

export function formatClock(value: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())} ${pad(
    value.getHours(),
  )}:${pad(value.getMinutes())}:${pad(value.getSeconds())}`
}

export function downloadFileName(lotteryType: 'SSQ' | 'DLT', value: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `lottery_${lotteryType.toLowerCase()}_${value.getFullYear()}${pad(
    value.getMonth() + 1,
  )}${pad(value.getDate())}_${pad(value.getHours())}${pad(value.getMinutes())}${pad(
    value.getSeconds(),
  )}.txt`
}

/** 剪贴板写入；非安全上下文或权限被拒时退回 execCommand。 */
export async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // 继续走兜底路径。
  }

  try {
    const textarea = document.createElement('textarea')
    textarea.value = text
    textarea.setAttribute('readonly', 'readonly')
    textarea.style.position = 'fixed'
    textarea.style.top = '-1000px'
    textarea.style.opacity = '0'
    document.body.appendChild(textarea)
    textarea.select()
    const copied = document.execCommand('copy')
    document.body.removeChild(textarea)
    return copied
  } catch {
    return false
  }
}

export function downloadTextFile(filename: string, content: string): void {
  const blob = new Blob([content], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  document.body.removeChild(anchor)
  URL.revokeObjectURL(url)
}

/** 验证摘要文案（同步完成 / 立即验证共用）。 */
export function verificationLines(summary: LotteryVerification): string[] {
  const lines = [`已验证 ${summary.totalVerified} 注，命中 ${summary.hitCount} 注`]
  if (summary.totalPrizeAmount > 0) lines.push(`中奖金额：${formatMoney(summary.totalPrizeAmount)}`)
  if (summary.bestPrizeLevel > 0) {
    lines.push(`最佳奖级：${summary.bestPrizeDescription || `第 ${summary.bestPrizeLevel} 等奖`}`)
  }
  if (summary.pendingCount > 0) lines.push(`待开奖：${summary.pendingCount} 注`)
  if (summary.encouragementMessage) lines.push('', summary.encouragementMessage)
  return lines
}
