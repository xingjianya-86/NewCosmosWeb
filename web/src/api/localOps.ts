import { listPrintJobs, LOCAL_OP_RESULT_PREFIX } from './archivePrint'
import { createLocalOp } from './printers'

/**
 * 本机操作（LocalOp）下发 + 结果轮询。
 *
 * 通道：POST /devices/{id}/local-ops 入队 → Agent 认领执行 → 结果落在任务的
 * errorMessage（成功 = `RESULT:` + JSON，失败 = 错误摘要），前端轮询现有
 * GET /print/jobs 读取，不新增任何服务端端点。
 */

// 结构化结果前缀定义在 archivePrint（打印任务消息契约）；此处转出以保持既有导入路径。
export { LOCAL_OP_RESULT_PREFIX }

/** 轮询间隔（毫秒）：1.5 秒一次，与打印任务面板的节奏一致。 */
const DEFAULT_INTERVAL_MS = 1500

/** 轮询超时上限（毫秒）：本机操作（含 UAC 交互）约 30 秒内应有结论。 */
const DEFAULT_TIMEOUT_MS = 30_000

export interface LocalOpOutcome {
  jobId: number
  jobNo: string
  /** 任务状态：Pending / Processing / Completed / Failed。 */
  status: string
  /** 完成消息：RESULT:JSON（成功）或错误摘要（失败）。 */
  message: string
}

export interface RunLocalOpOptions {
  intervalMs?: number
  timeoutMs?: number
  /** 返回 true 时提前停止轮询（页面卸载），抛出 LocalOpCancelledError。 */
  isCancelled?: () => boolean
}

/** 页面卸载导致的轮询中止：调用方静默忽略，不弹错误。 */
export class LocalOpCancelledError extends Error {
  constructor() {
    super('本机操作轮询已取消')
    this.name = 'LocalOpCancelledError'
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

/**
 * 下发本机操作并轮询任务状态直到 Completed / Failed。
 * 超时抛错（任务可能仍在执行，可在任务列表继续查看）。
 */
export async function runLocalOp(
  deviceId: number,
  op: string,
  args?: Record<string, unknown>,
  options: RunLocalOpOptions = {},
): Promise<LocalOpOutcome> {
  const interval = options.intervalMs ?? DEFAULT_INTERVAL_MS
  const timeout = options.timeoutMs ?? DEFAULT_TIMEOUT_MS
  const startedAt = Date.now()

  const created = await createLocalOp(deviceId, op, args)

  for (;;) {
    await sleep(interval)
    if (options.isCancelled?.()) throw new LocalOpCancelledError()
    if (Date.now() - startedAt > timeout) {
      throw new Error(`本机操作超时未完成（>${Math.round(timeout / 1000)} 秒），可在任务列表查看后续结果`)
    }

    // 本机操作刚创建、按创建时间排最前，第一页即可命中。
    const page = await listPrintJobs({ pageIndex: 1, pageSize: 20 })
    const job = page.items.find((item) => item.id === created.jobId)
    if (!job) continue
    if (job.status === 'Completed' || job.status === 'Failed') {
      return {
        jobId: created.jobId,
        jobNo: created.jobNo,
        status: job.status,
        message: job.errorMessage,
      }
    }
  }
}

/**
 * 解析结构化结果：`RESULT:` + JSON。
 * 非结构化（失败消息、老 Agent、JSON 被截断）返回 null，调用方回退展示原文。
 */
export function parseLocalOpResult<T = Record<string, unknown>>(
  message: string | null | undefined,
): T | null {
  if (!message || !message.startsWith(LOCAL_OP_RESULT_PREFIX)) return null
  try {
    return JSON.parse(message.slice(LOCAL_OP_RESULT_PREFIX.length)) as T
  } catch {
    return null
  }
}
