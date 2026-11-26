/** 彩票助手：字段与 /api/lottery/* 契约保持一致；金额单位均为元。 */

export type LotteryType = 'SSQ' | 'DLT'

export type LotteryPurchaseFilter = 'All' | 'Pending' | 'Hit' | 'Miss'

export type LotteryJobStatus =
  | 'Queued'
  | 'Running'
  | 'Succeeded'
  | 'Failed'
  | 'Cancelled'
  | 'Unknown'

export interface LotteryTypeOption {
  value: LotteryType
  label: string
  redCount: number
  blueCount: number
}

/** 双色球 6 红 1 蓝；大乐透 5 前区 + 2 后区（后端统一用 redNumbers / blueNumbers）。 */
export const LOTTERY_TYPE_OPTIONS: ReadonlyArray<LotteryTypeOption> = [
  { value: 'SSQ', label: '双色球', redCount: 6, blueCount: 1 },
  { value: 'DLT', label: '大乐透', redCount: 5, blueCount: 2 },
]

export const LOTTERY_PURCHASE_FILTERS: ReadonlyArray<{
  value: LotteryPurchaseFilter
  label: string
}> = [
  { value: 'All', label: '全部' },
  { value: 'Pending', label: '待开奖' },
  { value: 'Hit', label: '已中奖' },
  { value: 'Miss', label: '未中奖' },
]

export const LOTTERY_JOB_STATUS_LABELS: Record<LotteryJobStatus, string> = {
  Queued: '排队中',
  Running: '运行中',
  Succeeded: '已完成',
  Failed: '失败',
  Cancelled: '已取消',
  Unknown: '未知状态',
}

export const LOTTERY_PREDICTION_COUNTS = [1, 3, 5, 10, 20] as const

export function lotteryTypeLabel(value: string | null | undefined): string {
  if (value === 'SSQ') return '双色球'
  if (value === 'DLT') return '大乐透'
  return value || '—'
}

export function lotteryTypeFromLabel(label: string): LotteryType {
  const option = LOTTERY_TYPE_OPTIONS.find((item) => item.label === label)
  return option?.value ?? 'SSQ'
}

/** 奖级中文名：0 表示未中奖；后端未给 prizeLevelText 时的兜底。 */
export function lotteryPrizeLevelLabel(level: number | null | undefined): string {
  if (!level || level <= 0) return ''
  const names = ['一等奖', '二等奖', '三等奖', '四等奖', '五等奖', '六等奖', '七等奖', '八等奖', '九等奖']
  return names[level - 1] ?? `第 ${level} 等奖`
}

export interface LotteryDraw {
  drawNumber: string
  drawDate: string
  redNumbers: number[]
  blueNumbers: number[]
  salesAmount: number
  poolMoney: number
  prizeDescription: string
}

export interface LotteryDrawPage {
  items: LotteryDraw[]
  total: number
  page: number
  pageSize: number
}

/** GET /api/lottery/summary：首屏一次取回最新开奖、期数、最近列表和购彩统计。 */
export interface LotterySummary {
  latestDraw: LotteryDraw | null
  drawCount: number
  recentDraws: LotteryDraw[]
  purchaseSummary: LotteryPurchaseSummary
}

export interface LotteryVerification {
  totalVerified: number
  hitCount: number
  pendingCount: number
  totalPrizeAmount: number
  bestPrizeLevel: number
  bestPrizeDescription: string
  encouragementMessage: string
}

export interface LotterySyncResult {
  newCount: number
  verification: LotteryVerification | null
}

export interface LotteryPrediction {
  redNumbers: number[]
  blueNumbers: number[]
  /** 策略契合度（百分比，0-100），不代表中奖概率。 */
  confidence: number
  algorithm: string
  algorithmName: string
}

export interface LotteryPurchaseRecord {
  id: number
  lotteryType: string
  drawNumber: string
  redNumbers: number[]
  blueNumbers: number[]
  actualRedNumbers: number[]
  actualBlueNumbers: number[]
  algorithm: string
  purchaseDate: string
  isVerified: boolean
  isHit: boolean
  hitRedCount: number
  hitBlueCount: number
  prizeLevel: number
  prizeLevelText: string
  prizeAmount: number
  statusText: string
}

export interface LotteryPurchasePage {
  items: LotteryPurchaseRecord[]
  total: number
  page: number
  pageSize: number
}

export interface LotteryPurchaseSummary {
  totalCount: number
  hitCount: number
  pendingCount: number
  totalPrizeAmount: number
  bestPrizeLevel: number
  bestPrizeDescription: string
}

export interface LotteryModelStatus {
  ready: boolean
  window: number
  trainedAt: string | null
}

/** GET /api/lottery/worker/models 的元素（Worker 原始契约）。 */
export interface LotteryWorkerModel {
  lotteryType: string
  window: number
  trainedAt: string | null
}

/** GET /api/lottery/worker/health 的常用字段；其余原始字段保留在 queue/limits。 */
export interface LotteryWorkerHealth {
  status: string
  service: string
  version: string
  missingDependencies: string[]
  queue: Record<string, unknown>
  limits: Record<string, unknown>
}

export interface LotteryJob {
  jobId: string
  type: string
  status: LotteryJobStatus
  progress: number
  message: string
  result: string
  error: string
  logTail: string
  predictions: LotteryPrediction[]
  savedCount: number
}
