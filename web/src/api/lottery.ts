import type {
  LotteryDraw,
  LotteryDrawPage,
  LotteryJob,
  LotteryJobStatus,
  LotteryModelStatus,
  LotteryPrediction,
  LotteryPurchaseFilter,
  LotteryPurchasePage,
  LotteryPurchaseRecord,
  LotteryPurchaseSummary,
  LotterySyncResult,
  LotteryType,
  LotteryVerification,
} from '@/types/lottery'
import { lotteryPrizeLevelLabel } from '@/types/lottery'
import { ApiError, apiHttp, getErrorMessage } from './http'

/* ───────────────────────── 容错归一化 ───────────────────────── */

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function toText(value: unknown): string {
  if (typeof value === 'string') return value
  if (typeof value === 'number' || typeof value === 'boolean') return String(value)
  return ''
}

function toNumber(value: unknown, fallback = 0): number {
  if (typeof value === 'number') return Number.isFinite(value) ? value : fallback
  if (typeof value === 'string' && value.trim()) {
    // 兼容 "1,234,567" / " 42 " 这类被格式化的数字字符串。
    const parsed = Number(value.replace(/,/g, '').trim())
    return Number.isFinite(parsed) ? parsed : fallback
  }
  return fallback
}

function toBool(value: unknown): boolean {
  if (typeof value === 'boolean') return value
  if (typeof value === 'number') return value !== 0
  if (typeof value === 'string') {
    const text = value.trim().toLowerCase()
    return text === 'true' || text === '1' || text === 'yes' || text === 'y'
  }
  return false
}

/** 后端可能返回数组、JSON 字符串、逗号分隔字符串或 null；统一成 number[]。 */
function toNumberArray(value: unknown): number[] {
  if (Array.isArray(value)) {
    return value
      .map((item) => toNumber(item, Number.NaN))
      .filter((item) => Number.isFinite(item))
  }

  if (typeof value === 'number') return Number.isFinite(value) ? [value] : []

  if (typeof value === 'string') {
    const text = value.trim()
    if (!text) return []
    if (text.startsWith('[')) {
      try {
        return toNumberArray(JSON.parse(text) as unknown)
      } catch {
        // 继续按分隔符解析，兼容 "1,2,3" 之类的历史写法。
      }
    }
    return text
      .split(/[,，\s]+/)
      .map((item) => toNumber(item, Number.NaN))
      .filter((item) => Number.isFinite(item))
  }

  return []
}

/** 百分比字段兼容 0-1 与 0-100 两种口径，统一输出 0-100。 */
function toPercent(value: unknown): number {
  if (typeof value === 'string' && value.trim().endsWith('%')) {
    const parsed = Number(value.trim().slice(0, -1))
    return Number.isFinite(parsed) ? Math.min(100, Math.max(0, parsed)) : 0
  }
  const parsed = toNumber(value, 0)
  // 严格小于 1 才按 0-1 小数处理，避免把进度 1% 误判成 100%。
  const percent = parsed > 0 && parsed < 1 ? parsed * 100 : parsed
  return Math.min(100, Math.max(0, percent))
}

function toProgress(value: unknown): number {
  return Math.round(toPercent(value) * 10) / 10
}

/** 同时查看根对象与 data 包裹层；取第一个非空字段。 */
function layers(value: unknown): Record<string, unknown>[] {
  const root = asRecord(value)
  if (!root) return []
  const nested = asRecord(root.data)
  return nested ? [nested, root] : [root]
}

function pick(layersValue: Record<string, unknown>[], keys: string[]): unknown {
  for (const layer of layersValue) {
    for (const key of keys) {
      const value = layer[key]
      if (value !== undefined && value !== null) return value
    }
  }
  return undefined
}

function pickArray(value: unknown, keys: string[]): unknown[] {
  const root = asRecord(value)
  if (!root) return Array.isArray(value) ? value : []

  const allLayers = layers(value)
  for (const layer of allLayers) {
    for (const key of keys) {
      if (Array.isArray(layer[key])) return layer[key] as unknown[]
    }
  }

  const nested = asRecord(root.data)
  if (nested && Array.isArray(nested.data)) return nested.data as unknown[]
  if (Array.isArray(root.data)) return root.data as unknown[]

  return []
}

/* ───────────────────────── 开奖数据 ───────────────────────── */

function normalizeDraw(value: unknown): LotteryDraw | null {
  const raw = asRecord(value)
  if (!raw) return null

  const draw: LotteryDraw = {
    drawNumber: toText(raw.drawNumber ?? raw.drawNo ?? raw.issue),
    drawDate: toText(raw.drawDate ?? raw.drawTime ?? raw.date),
    redNumbers: toNumberArray(raw.redNumbers ?? raw.redNumbersJson ?? raw.reds),
    blueNumbers: toNumberArray(raw.blueNumbers ?? raw.blueNumbersJson ?? raw.blues),
    salesAmount: toNumber(raw.salesAmount ?? raw.sales ?? raw.salesMoney),
    poolMoney: toNumber(raw.poolMoney ?? raw.poolAmount ?? raw.pool),
    prizeDescription: toText(raw.prizeDescription ?? raw.prizeDetail ?? raw.prizeText),
  }

  if (!draw.drawNumber && !draw.drawDate && draw.redNumbers.length === 0) return null
  return draw
}

function normalizeDrawPage(value: unknown): LotteryDrawPage {
  const allLayers = layers(value)
  const items = pickArray(value, ['items', 'Items', 'list', 'draws', 'records'])
    .map(normalizeDraw)
    .filter((item): item is LotteryDraw => item !== null)

  const page = toNumber(pick(allLayers, ['page', 'pageIndex', 'currentPage']), 1)
  const pageSize = toNumber(pick(allLayers, ['pageSize', 'size', 'limit']), 30)
  const total = toNumber(
    pick(allLayers, ['total', 'totalCount', 'count']),
    items.length,
  )

  // 兼容直接返回数组的旧实现。
  if (Array.isArray(value)) {
    return {
      items: value
        .map(normalizeDraw)
        .filter((item): item is LotteryDraw => item !== null),
      total: value.length,
      page: 1,
      pageSize: 30,
    }
  }

  return { items, total, page, pageSize }
}

/** 最新开奖；无数据返回 null。 */
export function getLatestDraw(type: LotteryType): Promise<LotteryDraw | null> {
  return apiHttp
    .get<unknown>('/lottery/draws/latest', { params: { type } })
    .then((response) => {
      if (response.data === null || response.data === undefined) return null
      const allLayers = layers(response.data)
      const nested = pick(allLayers, ['draw', 'latest', 'latestDraw'])
      return normalizeDraw(nested ?? allLayers[0] ?? response.data)
    })
}

/** 分页开奖记录。 */
export function getDrawList(
  type: LotteryType,
  page = 1,
  pageSize = 30,
): Promise<LotteryDrawPage> {
  return apiHttp
    .get<unknown>('/lottery/draws', { params: { type, page, pageSize } })
    .then((response) => normalizeDrawPage(response.data))
}

/** 历史开奖总期数。 */
export function getDrawCount(type: LotteryType): Promise<number> {
  return apiHttp.get<unknown>('/lottery/draws/count', { params: { type } }).then((response) => {
    if (typeof response.data === 'number' || typeof response.data === 'string') {
      return toNumber(response.data)
    }
    const allLayers = layers(response.data)
    return toNumber(pick(allLayers, ['count', 'total', 'totalCount']))
  })
}

function normalizeVerification(value: unknown): LotteryVerification | null {
  const raw = asRecord(value)
  if (!raw) return null

  return {
    totalVerified: toNumber(raw.totalVerified ?? raw.verifiedCount ?? raw.verified),
    hitCount: toNumber(raw.hitCount ?? raw.hits),
    pendingCount: toNumber(raw.pendingCount ?? raw.pending),
    totalPrizeAmount: toNumber(raw.totalPrizeAmount ?? raw.prizeAmount),
    bestPrizeLevel: toNumber(raw.bestPrizeLevel),
    bestPrizeDescription: toText(raw.bestPrizeDescription),
    encouragementMessage: toText(raw.encouragementMessage ?? raw.encouragement),
  }
}

/** 增量同步开奖数据；同步完成后后端会顺带验证购彩记录。 */
export function syncHistory(type: LotteryType): Promise<LotterySyncResult> {
  // 首次全量同步可能较慢，单独放宽超时；其它接口沿用全局 20 秒。
  return apiHttp.post<unknown>('/lottery/sync', { type }, { timeout: 120_000 }).then((response) => {
    const allLayers = layers(response.data)
    const rawVerification = pick(allLayers, ['verification', 'verifyResult', 'verificationSummary'])
    return {
      newCount: toNumber(pick(allLayers, ['newCount', 'new', 'addedCount', 'count'])),
      verification: normalizeVerification(rawVerification),
    }
  })
}

/* ───────────────────────── 预测 / 模型 ───────────────────────── */

function normalizePrediction(value: unknown): LotteryPrediction | null {
  const raw = asRecord(value)
  if (!raw) return null

  const redNumbers = toNumberArray(raw.redNumbers ?? raw.redNumbersJson ?? raw.reds)
  const blueNumbers = toNumberArray(raw.blueNumbers ?? raw.blueNumbersJson ?? raw.blues)
  if (redNumbers.length === 0 && blueNumbers.length === 0) return null

  return {
    redNumbers,
    blueNumbers,
    confidence: toPercent(raw.confidence ?? raw.confidenceScore ?? raw.score),
    algorithm: toText(raw.algorithm),
    algorithmName: toText(raw.algorithmName ?? raw.algorithm) || '机器学习(LSTM)',
  }
}

function extractPredictions(value: unknown): LotteryPrediction[] {
  if (Array.isArray(value)) {
    return value.map(normalizePrediction).filter((item): item is LotteryPrediction => item !== null)
  }

  const candidates = pickArray(value, ['predictions', 'items', 'results', 'list'])
  if (candidates.length > 0) {
    return candidates
      .map(normalizePrediction)
      .filter((item): item is LotteryPrediction => item !== null)
  }

  const single = normalizePrediction(value)
  return single ? [single] : []
}

/** 快速机选（不入库，仅返回一组号码）。 */
export function quickPick(type: LotteryType, count = 1): Promise<LotteryPrediction[]> {
  return apiHttp
    .post<unknown>('/lottery/quick-pick', { type, count })
    .then((response) => extractPredictions(response.data))
}

/** 模型状态：未就绪时前端应先引导用户去训练。 */
export function getModelStatus(type: LotteryType): Promise<LotteryModelStatus> {
  return apiHttp
    .get<unknown>('/lottery/model-status', { params: { type } })
    .then((response) => {
      const allLayers = layers(response.data)
      const trainedAt = toText(pick(allLayers, ['trainedAt', 'trainedTime']))
      return {
        ready: toBool(pick(allLayers, ['ready', 'isReady', 'available'])),
        window: toNumber(pick(allLayers, ['window', 'lookbackWindow']), 73),
        trainedAt: trainedAt || null,
      }
    })
}

/* ───────────────────────── 任务 ───────────────────────── */

function normalizeJobStatus(value: unknown): LotteryJobStatus {
  const status = toText(value).trim().toLowerCase()
  if (!status) return 'Unknown'
  if (
    status.includes('queue') ||
    status.includes('pending') ||
    status.includes('wait') ||
    status.includes('排队') ||
    status.includes('等待')
  ) {
    return 'Queued'
  }
  if (
    status.includes('run') ||
    status.includes('process') ||
    status.includes('start') ||
    status.includes('运行') ||
    status.includes('训练中') ||
    status.includes('预测中')
  ) {
    return 'Running'
  }
  if (
    status.includes('success') ||
    status.includes('complete') ||
    status.includes('done') ||
    status.includes('finish') ||
    status.includes('成功') ||
    status.includes('完成')
  ) {
    return 'Succeeded'
  }
  if (status.includes('fail') || status.includes('error') || status.includes('失败') || status.includes('错误')) {
    return 'Failed'
  }
  if (status.includes('cancel') || status.includes('取消')) return 'Cancelled'
  return 'Unknown'
}

function normalizeLogTail(value: unknown): string {
  if (typeof value === 'string') return value
  if (Array.isArray(value)) {
    return value
      .map((item) => (typeof item === 'string' ? item : JSON.stringify(item)))
      .join('\n')
  }
  return ''
}

function normalizeJob(value: unknown): LotteryJob {
  const root = asRecord(value) ?? {}
  const rootLooksLikeJob = ['jobId', 'id', 'status', 'state', 'progress'].some(
    (key) => root[key] !== undefined,
  )
  const container = rootLooksLikeJob
    ? root
    : (asRecord(root.job) ?? asRecord(root.data) ?? root)
  const raw = asRecord(container.job) ?? container
  const resultRaw = raw.result ?? raw.Result
  const resultRecord = asRecord(resultRaw)
  const predictionsRaw =
    raw.predictions ??
    resultRecord?.predictions ??
    resultRecord?.items ??
    (Array.isArray(resultRaw) ? resultRaw : undefined)

  let resultText = ''
  if (typeof resultRaw === 'string') {
    resultText = resultRaw
  } else if (resultRaw !== undefined && resultRaw !== null) {
    try {
      resultText = JSON.stringify(resultRaw)
    } catch {
      resultText = String(resultRaw)
    }
  }

  const errorRaw = raw.error ?? raw.errorMessage
  const errorText =
    typeof errorRaw === 'string'
      ? errorRaw
      : errorRaw !== undefined && errorRaw !== null
        ? asRecord(errorRaw)
          ? toText((asRecord(errorRaw)?.message ?? asRecord(errorRaw)?.detail))
          : String(errorRaw)
        : ''

  return {
    jobId: toText(raw.jobId ?? raw.id ?? raw.taskId),
    type: toText(raw.type ?? raw.lotteryType),
    status: normalizeJobStatus(raw.status ?? raw.state),
    progress: toProgress(raw.progress ?? raw.percent ?? raw.percentage),
    message: toText(raw.message ?? raw.stage ?? raw.statusMessage),
    result: resultText,
    error: errorText,
    logTail: normalizeLogTail(
      raw.logTail ?? raw.logs ?? raw.log ?? resultRecord?.logTail ?? resultRecord?.logs,
    ),
    predictions: extractPredictions(predictionsRaw),
    savedCount: toNumber(raw.savedCount ?? resultRecord?.savedCount),
  }
}

/** 训练入队。训练可能持续数十分钟，调用方需轮询 getJob。 */
export function enqueueTrain(type: LotteryType): Promise<LotteryJob> {
  return apiHttp
    .post<unknown>('/lottery/train', { type })
    .then((response) => normalizeJob(response.data))
}

/** 预测入队。成功后后端自动写入购彩记录。 */
export function enqueuePredict(type: LotteryType, count: number): Promise<LotteryJob> {
  return apiHttp
    .post<unknown>('/lottery/predict', { type, count })
    .then((response) => normalizeJob(response.data))
}

/** 查询任务状态；训练/预测页面按固定间隔轮询。 */
export function getJob(jobId: string, type?: LotteryType): Promise<LotteryJob> {
  return apiHttp
    .get<unknown>(`/lottery/jobs/${encodeURIComponent(jobId)}`, {
      params: type ? { type } : undefined,
    })
    .then((response) => {
      const job = normalizeJob(response.data)
      return job.jobId ? job : { ...job, jobId }
    })
}

/* ───────────────────────── 购彩记录 ───────────────────────── */

function normalizePurchase(value: unknown): LotteryPurchaseRecord | null {
  const raw = asRecord(value)
  if (!raw) return null

  const id = toNumber(raw.id ?? raw.purchaseId)
  if (id <= 0) return null

  const statusTextRaw = toText(raw.statusText)
  const isVerified =
    raw.isVerified !== undefined || raw.verified !== undefined
      ? toBool(raw.isVerified ?? raw.verified)
      : raw.isPending !== undefined
        ? !toBool(raw.isPending)
        : Boolean(statusTextRaw && statusTextRaw !== '待开奖')
  const hitRaw = raw.isHit ?? raw.hit
  const prizeLevel = toNumber(raw.prizeLevel)
  const isHit =
    hitRaw !== undefined
      ? toBool(hitRaw)
      : prizeLevel > 0 || statusTextRaw === '已中奖'
  const statusText = statusTextRaw || (!isVerified ? '待开奖' : isHit ? '已中奖' : '未中奖')

  return {
    id,
    lotteryType: toText(raw.lotteryType ?? raw.type),
    drawNumber: toText(raw.drawNumber ?? raw.drawNo),
    redNumbers: toNumberArray(raw.redNumbers ?? raw.redNumbersJson ?? raw.reds),
    blueNumbers: toNumberArray(raw.blueNumbers ?? raw.blueNumbersJson ?? raw.blues),
    actualRedNumbers: toNumberArray(
      raw.actualRedNumbers ?? raw.actualRedNumbersJson ?? raw.drawRedNumbers,
    ),
    actualBlueNumbers: toNumberArray(
      raw.actualBlueNumbers ?? raw.actualBlueNumbersJson ?? raw.drawBlueNumbers,
    ),
    algorithm: toText(raw.algorithm ?? raw.algorithmName),
    purchaseDate: toText(raw.purchaseDate ?? raw.createdAt),
    isVerified,
    isHit,
    hitRedCount: toNumber(raw.hitRedCount),
    hitBlueCount: toNumber(raw.hitBlueCount),
    prizeLevel,
    prizeLevelText: toText(raw.prizeLevelText) || lotteryPrizeLevelLabel(prizeLevel),
    prizeAmount: toNumber(raw.prizeAmount),
    statusText,
  }
}

/** 我的购彩记录（分页 + 状态筛选）。 */
export function getPurchases(
  type: LotteryType,
  filter: LotteryPurchaseFilter = 'All',
  page = 1,
  pageSize = 30,
): Promise<LotteryPurchasePage> {
  return apiHttp
    .get<unknown>('/lottery/purchases', {
      params: { type: type || undefined, filter, page, pageSize },
    })
    .then((response) => {
      const allLayers = layers(response.data)
      const rows = pickArray(response.data, ['items', 'Items', 'list', 'records', 'purchases'])
        .map(normalizePurchase)
        .filter((item): item is LotteryPurchaseRecord => item !== null)

      return {
        items: rows,
        total: toNumber(pick(allLayers, ['total', 'totalCount', 'count']), rows.length),
        page: toNumber(pick(allLayers, ['page', 'pageIndex', 'currentPage']), page),
        pageSize: toNumber(pick(allLayers, ['pageSize', 'size', 'limit']), pageSize),
      }
    })
}

/** 购彩汇总（累计注数 / 已中奖 / 待开奖 / 奖金）。 */
export function getPurchaseSummary(type: LotteryType): Promise<LotteryPurchaseSummary> {
  return apiHttp
    .get<unknown>('/lottery/purchases/summary', { params: { type: type || undefined } })
    .then((response) => {
      const allLayers = layers(response.data)
      return {
        totalCount: toNumber(pick(allLayers, ['totalCount', 'total'])),
        hitCount: toNumber(pick(allLayers, ['hitCount', 'hit'])),
        pendingCount: toNumber(pick(allLayers, ['pendingCount', 'pending'])),
        totalPrizeAmount: toNumber(pick(allLayers, ['totalPrizeAmount', 'prizeAmount'])),
        bestPrizeLevel: toNumber(pick(allLayers, ['bestPrizeLevel'])),
        bestPrizeDescription: toText(pick(allLayers, ['bestPrizeDescription'])),
      }
    })
}

/** 立即验证购彩记录（对照已同步的开奖数据）。 */
export function verifyPurchases(type: LotteryType): Promise<LotteryVerification> {
  return apiHttp.post<unknown>('/lottery/purchases/verify', { type }, { timeout: 60_000 }).then((response) => {
    const allLayers = layers(response.data)
    const nested = pick(allLayers, ['verification', 'verifyResult'])
    return normalizeVerification(nested ?? allLayers[0] ?? response.data) ?? {
      totalVerified: 0,
      hitCount: 0,
      pendingCount: 0,
      totalPrizeAmount: 0,
      bestPrizeLevel: 0,
      bestPrizeDescription: '',
      encouragementMessage: '',
    }
  })
}

/* ───────────────────────── 错误文案 ───────────────────────── */

/** 403 时补充 LOTTERY_ACCESS 权限提示；其它错误沿用服务端消息。 */
export function describeLotteryError(error: unknown, fallback = '操作失败，请稍后重试'): string {
  const message = getErrorMessage(error, fallback)
  if (
    error instanceof ApiError &&
    error.status === 403 &&
    !message.includes('LOTTERY_ACCESS')
  ) {
    return `${message}（需要 LOTTERY_ACCESS 权限，请联系管理员分配）`
  }
  return message
}
