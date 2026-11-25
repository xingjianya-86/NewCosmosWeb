import type {
  ManualRecoveryRecordDto,
  RecoveryArchiveLookupDto,
  RecoveryCalculateResult,
  RecoveryCalculateSegment,
  RecoveryMonthlyStandardPoint,
  RecoveryRecord,
  RecoveryRecordDto,
  RecoverySaveResult,
  RecoverySegment,
  RecoveryStats,
  StoppedPersonDto,
} from '@/types/recovery'
import { apiHttp } from './http'

/* ───────────────────────── 响应归一化 ───────────────────────── */

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function toNumber(value: unknown, fallback = 0): number {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function toText(value: unknown): string {
  return typeof value === 'string' ? value : value === null || value === undefined ? '' : String(value)
}

function unwrapArray(data: unknown): unknown[] {
  if (Array.isArray(data)) return data
  const root = asRecord(data)
  if (!root) return []
  if (Array.isArray(root.items)) return root.items
  if (Array.isArray(root.Items)) return root.Items
  const nested = asRecord(root.data)
  if (nested) {
    if (Array.isArray(nested.items)) return nested.items
    if (Array.isArray(nested.Items)) return nested.Items
  }
  return []
}

function normalizeRecord(value: unknown): RecoveryRecord | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toNumber(raw.id, 0)
  if (id <= 0) return null

  return {
    id,
    inputMode: toText(raw.inputMode),
    sourceType: toText(raw.sourceType),
    sourceId: raw.sourceId === null || raw.sourceId === undefined ? null : toNumber(raw.sourceId),
    personName: toText(raw.personName),
    idCard: toText(raw.idCard),
    recoveryReason: toText(raw.recoveryReason),
    monthlyAmount: toNumber(raw.monthlyAmount),
    recoveryMonths: toNumber(raw.recoveryMonths),
    recoveryAmount: toNumber(raw.recoveryAmount),
    amountSource: toText(raw.amountSource),
    amountNote: toText(raw.amountNote),
    autoAmount: toNumber(raw.autoAmount),
    recoveredAmount: toNumber(raw.recoveredAmount),
    startMonth: toText(raw.startMonth),
    endMonth: toText(raw.endMonth),
    status: toText(raw.status),
    createdBy: toText(raw.createdBy),
    createdAt: toText(raw.createdAt) || null,
    updatedAt: toText(raw.updatedAt) || null,
    statusDisplay: toText(raw.statusDisplay),
    sourceTypeDisplay: toText(raw.sourceTypeDisplay),
    inputModeDisplay: toText(raw.inputModeDisplay),
    amountSourceDisplay: toText(raw.amountSourceDisplay),
    canDelete: Boolean(raw.canDelete),
    canClose: Boolean(raw.canClose),
    canReprint: Boolean(raw.canReprint),
  }
}

function extractSavedRecord(data: unknown): RecoverySaveResult {
  const direct = normalizeRecord(data)
  if (direct) return { id: direct.id, record: direct }

  const root = asRecord(data)
  if (!root) return { id: 0, record: null }

  const nested = normalizeRecord(root.record) ?? normalizeRecord(root.data)
  const id = toNumber(root.id ?? root.recordId ?? root.recoveryRecordId, nested?.id ?? 0)
  return { id, record: nested }
}

/* ───────────────────────── 停止人员 / 统计 / 查档 ───────────────────────── */

/** 搜索停止人员（姓名或身份证号）。 */
export function searchStoppedPersons(keyword: string): Promise<StoppedPersonDto[]> {
  return apiHttp
    .get<unknown>('/recovery/stopped-persons', { params: { keyword } })
    .then((response) => unwrapArray(response.data) as StoppedPersonDto[])
}

/** 停止人员页头统计。 */
export function getRecoveryStats(): Promise<RecoveryStats> {
  return apiHttp
    .get<RecoveryStats>('/recovery/stopped-persons/stats')
    .then((response) => response.data)
}

/** 按身份证查档（手工录入自动带出）。 */
export function lookupRecoveryArchive(
  idCard: string,
): Promise<RecoveryArchiveLookupDto | null> {
  return apiHttp
    .get<RecoveryArchiveLookupDto | null>('/recovery/lookup', { params: { idCard } })
    .then((response) => response.data ?? null)
}

/** 来源档案的默认月保障标准。 */
export function getRecoveryDefaultMonthlyAmount(
  sourceType: string,
  sourceId: number,
): Promise<number> {
  return apiHttp
    .get<unknown>('/recovery/default-monthly-amount', { params: { sourceType, sourceId } })
    .then((response) => {
      const raw = asRecord(response.data)
      return raw ? toNumber(raw.amount) : toNumber(response.data)
    })
}

/** 来源档案的历史月保障标准时间轴。 */
export function getRecoveryHistory(
  sourceType: string,
  sourceId: number,
): Promise<RecoveryMonthlyStandardPoint[]> {
  return apiHttp
    .get<unknown>('/recovery/history', { params: { sourceType, sourceId } })
    .then((response) => unwrapArray(response.data) as RecoveryMonthlyStandardPoint[])
}

/** 自动计算应追缴总额（分段依据以服务端为准）。 */
export function calculateRecovery(params: {
  sourceType: string
  sourceId: number
  startMonth: string
  endMonth: string
  monthlyAmount: number
}): Promise<RecoveryCalculateResult> {
  return apiHttp
    .get<unknown>('/recovery/calculate', { params })
    .then((response) => {
      const raw = asRecord(response.data) ?? {}
      // 兼容契约字段 amount 与 Core RecoveryAmountResult.TotalAmount 两种命名。
      return {
        canCalculate: Boolean(raw.canCalculate),
        reason: toText(raw.reason),
        amount: toNumber(raw.amount ?? raw.totalAmount),
        amountSource: toText(raw.amountSource) || 'Flat',
        segments: unwrapArray(raw.segments) as RecoveryCalculateSegment[],
      }
    })
}

/* ───────────────────────── 追缴记录 ───────────────────────── */

/** 我的追缴记录（无分页，按关键词过滤）。 */
export function listRecoveryRecords(
  keyword = '',
  status = '',
): Promise<RecoveryRecord[]> {
  return apiHttp
    .get<unknown>('/recovery/records', {
      params: {
        keyword: keyword.trim() || undefined,
        status: status || undefined,
      },
    })
    .then((response) =>
      unwrapArray(response.data)
        .map(normalizeRecord)
        .filter((item): item is RecoveryRecord => item !== null),
    )
}

/** 追缴记录详情。 */
export function getRecoveryRecord(id: number | string): Promise<RecoveryRecord> {
  return apiHttp
    .get<unknown>(`/recovery/records/${encodeURIComponent(String(id))}`)
    .then((response) => {
      const record = normalizeRecord(response.data)
      if (!record) throw new Error('追缴记录不存在')
      return record
    })
}

/** 追缴记录已落库分段明细。 */
export function getRecoverySegments(id: number | string): Promise<RecoverySegment[]> {
  return apiHttp
    .get<unknown>(`/recovery/records/${encodeURIComponent(String(id))}/segments`)
    .then((response) => unwrapArray(response.data) as RecoverySegment[])
}

/** 追缴记录编号（打印 / 留痕展示）。 */
export function getRecoveryRecordCode(id: number | string): Promise<string> {
  return apiHttp
    .get<unknown>(`/recovery/records/${encodeURIComponent(String(id))}/code`)
    .then((response) => {
      const raw = asRecord(response.data)
      return raw ? toText(raw.code) : toText(response.data)
    })
}

/** 新增搜索录入追缴记录。 */
export function createRecoveryRecord(dto: RecoveryRecordDto): Promise<RecoverySaveResult> {
  return apiHttp
    .post<unknown>('/recovery/records', dto)
    .then((response) => extractSavedRecord(response.data))
}

/** 新增手工录入追缴记录。 */
export function createManualRecoveryRecord(
  dto: ManualRecoveryRecordDto,
): Promise<RecoverySaveResult> {
  return apiHttp
    .post<unknown>('/recovery/records/manual', dto)
    .then((response) => extractSavedRecord(response.data))
}

/** 更新搜索录入追缴记录。 */
export function updateRecoveryRecord(
  id: number | string,
  dto: RecoveryRecordDto,
): Promise<void> {
  return apiHttp
    .put(`/recovery/records/${encodeURIComponent(String(id))}`, dto)
    .then(() => undefined)
}

/** 更新手工录入追缴记录。 */
export function updateManualRecoveryRecord(
  id: number | string,
  dto: ManualRecoveryRecordDto,
): Promise<void> {
  return apiHttp
    .put(`/recovery/records/${encodeURIComponent(String(id))}/manual`, dto)
    .then(() => undefined)
}

/** 删除草稿。 */
export function deleteRecoveryRecord(id: number | string): Promise<void> {
  return apiHttp
    .delete(`/recovery/records/${encodeURIComponent(String(id))}`)
    .then(() => undefined)
}

/** 推进状态：结案 Confirmed / 出文书 Printed。 */
export function updateRecoveryRecordStatus(
  id: number | string,
  status: 'Confirmed' | 'Printed',
): Promise<void> {
  return apiHttp
    .post(`/recovery/records/${encodeURIComponent(String(id))}/status`, { status })
    .then(() => undefined)
}
