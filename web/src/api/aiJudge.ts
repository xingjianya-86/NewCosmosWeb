import { apiHttp } from './http'

/**
 * AI 重病判定请求（病种目录口径）。
 * name 为空时服务端由 ICD-10 字典按 code 兜底带名；仍带不出名返回 400。
 * userRejected = 经办人本会话内已人工取消过「是否重病」，服务端据此过 ShouldAutoCheck 闸门。
 */
export interface AiSeverityRequest {
  name?: string
  code: string
  userRejected: boolean
}

/** 与 Core `SevereDiseaseJudgment` 对齐的判定响应。 */
export interface AiSeverityResult {
  verdict: string
  matchedItem: string
  confidence: number
  reason: string
  /** deterministic=本地清单毫秒级 / llm=服务端模型兜底 */
  source: string
  policyVersion: string
  model: string
  /** severe 且 matched_item∈清单（自动勾选的唯一闸门） */
  autoEligible: boolean
  /** 服务端已按 userRejected 过闸门的结论：true=允许自动勾选 */
  autoChecked: boolean
  /** 建议条文案；仅 verdict==='severe' 时非空 */
  suggestion: string
}

/** 费用口径判定请求（黑龙政办法第六十八条）。 */
export interface AiSeverityCostRequest {
  /** 前 12 个月个人累计自付（元） */
  selfPaid12m: number
  /** HukouType 非 Urban 即农村档 */
  isRural: boolean
  userRejected: boolean
}

export interface AiSeverityCostResult {
  severe: boolean
  /** 上年度居民人均可支配收入阈值（元）；≤0 表示未配置 */
  threshold: number
  autoChecked: boolean
  /** 建议条文案；severe=false 时为空 */
  suggestion: string
}

/**
 * 判定超时必须 per-request 指定：默认 20s 会掐断 LLM 路径
 * （服务端 AI_REQUEST_TIMEOUT_SECONDS=90，客户端须更长，见 AiJudgeConstants.JudgeTimeoutSeconds 口径）。
 * 失败语义：本模块抛出，调用方 catch 后**静默**降级为「无建议」——
 * 不弹 ElMessage、不阻断表单保存（对齐桌面「失败=无建议」§7 降级口径）。
 */
const JUDGE_TIMEOUT_MS = 110_000

/** 病种目录口径判定：确定性 → 缓存 → LLM 三级管线。 */
export function judgeSeverity(payload: AiSeverityRequest): Promise<AiSeverityResult> {
  return apiHttp
    .post<AiSeverityResult>('/ai/severity', payload, { timeout: JUDGE_TIMEOUT_MS })
    .then((response) => response.data)
}

/** 费用口径判定：阈值缺失/≤0 时服务端返回 severe=false 且 suggestion 为空（静默降级）。 */
export function judgeSeverityByCost(payload: AiSeverityCostRequest): Promise<AiSeverityCostResult> {
  return apiHttp
    .post<AiSeverityCostResult>('/ai/severity/cost', payload, { timeout: JUDGE_TIMEOUT_MS })
    .then((response) => response.data)
}
