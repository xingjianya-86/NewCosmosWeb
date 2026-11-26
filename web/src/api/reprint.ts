import type {
  CreateReprintBatchInput,
  CreateReprintJobInput,
  ReprintBatchCreated,
  ReprintBatchItem,
  ReprintBatchStatus,
  ReprintDomain,
  ReprintHistoryItem,
  ReprintJobCreated,
  ReprintJobItem,
  ReprintPerson,
  ReprintPersonDomain,
  ReprintPersonDomainQuery,
  ReprintPersonQuery,
  ReprintRecord,
  ReprintRecordDetail,
  ReprintRecordQuery,
  ReprintTemplate,
} from '@/types/reprint'
import { apiHttp } from './http'

/* ───────────────────────── 通用归一化 ───────────────────────── */

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function toText(value: unknown): string {
  return typeof value === 'string'
    ? value
    : value === null || value === undefined
      ? ''
      : String(value)
}

function toNumber(value: unknown, fallback = 0): number {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function toNullableNumber(value: unknown): number | null {
  if (value === null || value === undefined || value === '') return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

function toBoolean(value: unknown, fallback = false): boolean {
  if (typeof value === 'boolean') return value
  if (typeof value === 'number') return value !== 0
  if (typeof value === 'string') {
    const text = value.trim().toLowerCase()
    if (['true', '1', 'yes', 'y'].includes(text)) return true
    if (['false', '0', 'no', 'n'].includes(text)) return false
  }
  return fallback
}

/** 兼容 string[] / 分隔字符串 / [{name}] 三种形态。 */
function toStringArray(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value
      .map((item) => {
        if (typeof item === 'string') return item.trim()
        const raw = asRecord(item)
        return raw ? toText(raw.name ?? raw.label ?? raw.domain ?? raw.key).trim() : ''
      })
      .filter((item) => item.length > 0)
  }

  if (typeof value === 'string') {
    return value
      .split(/[·,，;；|/]/)
      .map((item) => item.trim())
      .filter((item) => item.length > 0)
  }

  return []
}

function unwrapItems(data: unknown): unknown[] {
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

function unwrapRecord(data: unknown): Record<string, unknown> {
  const root = asRecord(data)
  if (!root) return {}
  const nested = asRecord(root.item) ?? asRecord(root.record) ?? asRecord(root.data)
  return nested ?? root
}

/** 去掉 0 / 空串 / undefined，axios 只发送有效查询参数。 */
function compactParams(params: Record<string, unknown>): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(params).filter(([, value]) => {
      if (value === undefined || value === null || value === '') return false
      if (typeof value === 'number' && value <= 0) return false
      return true
    }),
  )
}

/* ───────────────────────── 归一化实体 ───────────────────────── */

function normalizeDomain(value: unknown): ReprintDomain | null {
  const raw = asRecord(value)
  if (!raw) return null
  const key = toText(raw.key ?? raw.domain ?? raw.businessType).trim()
  if (!key) return null
  return {
    key,
    name: toText(raw.name ?? raw.displayName ?? raw.label).trim() || key,
    modes: toStringArray(raw.modes ?? raw.mode).map((mode) => mode.toUpperCase()),
  }
}

function normalizePerson(value: unknown): ReprintPerson | null {
  const raw = asRecord(value)
  if (!raw) return null

  const idCard = toText(raw.idCard ?? raw.idcard ?? raw.idCardNo).trim()
  const name = toText(raw.name ?? raw.personName).trim()
  const key = toText(raw.key ?? raw.personKey ?? raw.id).trim() || idCard || name
  if (!key) return null

  return {
    key,
    name,
    idCard,
    domainNames: toStringArray(raw.domainNames ?? raw.domains ?? raw.domainSummary),
    latestTime: toText(raw.latestTime ?? raw.lastTime ?? raw.businessDate),
    count: Math.max(0, toNumber(raw.count ?? raw.total, 0)),
  }
}

function normalizePersonDomain(value: unknown): ReprintPersonDomain | null {
  const raw = asRecord(value)
  if (!raw) return null
  const domain = toText(raw.domain ?? raw.key ?? raw.businessType).trim()
  if (!domain) return null
  return {
    domain,
    name: toText(raw.name ?? raw.displayName ?? raw.domainName).trim() || domain,
    count: Math.max(0, toNumber(raw.count ?? raw.total, 0)),
    latestTime: toText(raw.latestTime ?? raw.lastTime ?? raw.businessDate),
  }
}

function normalizeRecord(value: unknown): ReprintRecord | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toText(raw.id ?? raw.businessId).trim()
  if (!id) return null
  return {
    id,
    name: toText(raw.name ?? raw.personName ?? raw.applicantName),
    idCard: toText(raw.idCard ?? raw.idcard ?? raw.idCardNo),
    businessDate: toText(raw.businessDate ?? raw.businessTime ?? raw.latestTime),
    classification: toText(raw.classification ?? raw.classificationName),
    status: toText(raw.status ?? raw.statusName),
    versionLabel: toText(raw.versionLabel ?? raw.version),
    extra: toText(raw.extra ?? raw.remark ?? raw.summary),
    no: toText(raw.no ?? raw.businessNo ?? raw.documentNo),
  }
}

function normalizeTemplate(value: unknown): ReprintTemplate | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toNullableNumber(raw.id)
  if (id === null) return null
  return {
    id,
    name: toText(raw.name).trim() || `模板 #${id}`,
    fileType: toText(raw.fileType),
    docType: toText(raw.docType),
    isSelected: toBoolean(raw.isSelected, false),
    isApplicable: toBoolean(raw.isApplicable, true),
  }
}

function normalizeHistory(value: unknown): ReprintHistoryItem | null {
  const raw = asRecord(value)
  if (!raw) return null
  return {
    templateName: toText(raw.templateName ?? raw.name) || '模板文书',
    printerName: toText(raw.printerName),
    operatorName: toText(raw.operatorName ?? raw.operator),
    createdAt: toText(raw.createdAt),
    status: toText(raw.status ?? raw.statusName),
  }
}

function normalizeJobItem(value: unknown, index = 0): ReprintJobItem {
  const raw = asRecord(value) ?? {}
  return {
    jobNo: toText(raw.jobNo ?? raw.no).trim() || `#${index + 1}`,
    businessId: toText(raw.businessId),
    name: toText(raw.name),
    status: toText(raw.status ?? raw.statusName) || 'Pending',
    templateName: toText(raw.templateName ?? raw.name),
  }
}

function normalizeJobCreated(data: unknown): ReprintJobCreated {
  const raw = asRecord(data) ?? {}
  const jobNo = toText(raw.jobNo ?? raw.no)
  const jobs = unwrapItems(raw.jobs).map((item, index) => normalizeJobItem(item, index))
  const normalizedJobs =
    jobs.length > 0
      ? jobs
      : jobNo
        ? [{ jobNo, businessId: '', name: '', status: toText(raw.status) || 'Pending', templateName: '' }]
        : []
  return {
    jobNo,
    status: toText(raw.status ?? raw.statusName) || normalizedJobs[0]?.status || 'Pending',
    jobs: normalizedJobs,
  }
}

function normalizeBatchJob(value: unknown, index = 0): ReprintJobItem {
  const raw = asRecord(value) ?? {}
  return {
    jobNo: toText(raw.jobNo ?? raw.no).trim() || `#${index + 1}`,
    businessId: toText(raw.businessId),
    name: toText(raw.name),
    status: toText(raw.status ?? raw.statusName) || 'Pending',
    templateName: toText(raw.templateName),
  }
}

function normalizeBatchCreated(data: unknown): ReprintBatchCreated {
  const raw = asRecord(data) ?? {}
  const jobs = unwrapItems(raw.jobs).map(normalizeBatchJob)
  return {
    batchNo: toText(raw.batchNo ?? raw.no),
    total: Math.max(jobs.length, toNumber(raw.total, jobs.length)),
    jobs: jobs.map(({ jobNo, businessId, name }) => ({ jobNo, businessId, name })),
  }
}

function normalizeBatchItem(value: unknown, index = 0): ReprintBatchItem {
  const raw = asRecord(value) ?? {}
  return {
    jobNo: toText(raw.jobNo ?? raw.no).trim() || `#${index + 1}`,
    businessId: toText(raw.businessId),
    name: toText(raw.name),
    status: toText(raw.status ?? raw.statusName) || 'Pending',
    error: toText(raw.error ?? raw.errorMessage ?? raw.message),
  }
}

function normalizeBatchStatus(data: unknown): ReprintBatchStatus {
  const raw = asRecord(data) ?? {}
  const items = unwrapItems(raw.items).map(normalizeBatchItem)
  const completed = toNumber(raw.completed ?? raw.completedCount, 0)
  const failed = toNumber(raw.failed ?? raw.failedCount, 0)
  return {
    total: Math.max(items.length, toNumber(raw.total ?? raw.totalCount, items.length)),
    completed: Math.max(0, completed),
    failed: Math.max(0, failed),
    items,
    done: toBoolean(raw.done ?? raw.finished ?? raw.isDone, false),
  }
}

/* ───────────────────────── 查询与操作 ───────────────────────── */

/** 业务域清单（顺序即页面分类顺序）。 */
export async function listReprintDomains(): Promise<ReprintDomain[]> {
  const data = await apiHttp.get<unknown>('/reprint/domains').then((response) => response.data)
  return unwrapItems(data)
    .map(normalizeDomain)
    .filter((item): item is ReprintDomain => item !== null)
}

/** 跨域按人检索（关键词/年月/单域筛选）。 */
export async function listReprintPersons(
  params: ReprintPersonQuery = {},
): Promise<ReprintPerson[]> {
  const data = await apiHttp
    .get<unknown>('/reprint/persons', {
      params: compactParams({
        keyword: params.keyword?.trim(),
        year: params.year,
        month: params.month,
        domain: params.domain,
      }),
    })
    .then((response) => response.data)

  return unwrapItems(data)
    .map(normalizePerson)
    .filter((item): item is ReprintPerson => item !== null)
}

/** 人员名下有记录的域分支。 */
export async function listReprintPersonDomains(
  personKey: string,
  params: ReprintPersonDomainQuery = {},
): Promise<ReprintPersonDomain[]> {
  const data = await apiHttp
    .get<unknown>(`/reprint/persons/${encodeURIComponent(personKey)}/domains`, {
      params: compactParams({ year: params.year, month: params.month }),
    })
    .then((response) => response.data)

  return unwrapItems(data)
    .map(normalizePersonDomain)
    .filter((item): item is ReprintPersonDomain => item !== null)
}

/** 指定域下的记录列表（按人时传 idCard；批量预览可只传年月）。 */
export async function listReprintRecords(
  params: ReprintRecordQuery,
): Promise<ReprintRecord[]> {
  const data = await apiHttp
    .get<unknown>('/reprint/records', {
      params: compactParams({
        domain: params.domain,
        idCard: params.idCard?.trim(),
        year: params.year,
        month: params.month,
      }),
    })
    .then((response) => response.data)

  return unwrapItems(data)
    .map(normalizeRecord)
    .filter((item): item is ReprintRecord => item !== null)
}

/** 单条记录详情。 */
export async function getReprintRecord(domain: string, id: string): Promise<ReprintRecordDetail> {
  const data = await apiHttp
    .get<unknown>(
      `/reprint/records/${encodeURIComponent(domain)}/${encodeURIComponent(id)}`,
    )
    .then((response) => response.data)

  const raw = unwrapRecord(data)
  const record = normalizeRecord(raw)
  const fieldsRaw = asRecord(raw.fields) ?? {}
  const fields: Record<string, string> = {}
  for (const [key, value] of Object.entries(fieldsRaw)) {
    if (value !== null && value !== undefined) fields[key] = toText(value)
  }

  return {
    id: record?.id ?? id,
    name: record?.name ?? '',
    idCard: record?.idCard ?? '',
    businessDate: record?.businessDate ?? '',
    classification: record?.classification ?? '',
    status: record?.status ?? '',
    versionLabel: record?.versionLabel ?? '',
    extra: record?.extra ?? '',
    no: record?.no ?? '',
    fields,
    payload: raw,
  }
}

/** 当前记录可用的输出模板。 */
export async function listReprintTemplates(
  domain: string,
  id: string,
): Promise<ReprintTemplate[]> {
  const data = await apiHttp
    .get<unknown>(
      `/reprint/records/${encodeURIComponent(domain)}/${encodeURIComponent(id)}/templates`,
    )
    .then((response) => response.data)

  return unwrapItems(data)
    .map(normalizeTemplate)
    .filter((item): item is ReprintTemplate => item !== null)
}

/** 当前记录的历史打印留痕（模板/打印机/操作人/时间）。 */
export async function listReprintHistory(
  domain: string,
  id: string,
): Promise<ReprintHistoryItem[]> {
  const data = await apiHttp
    .get<unknown>(
      `/reprint/records/${encodeURIComponent(domain)}/${encodeURIComponent(id)}/history`,
    )
    .then((response) => response.data)

  return unwrapItems(data)
    .map(normalizeHistory)
    .filter((item): item is ReprintHistoryItem => item !== null)
}

/** 创建补打任务：预览 / 打印 / 保存由本机 Agent 领取执行。 */
export async function createReprintJob(
  input: CreateReprintJobInput,
): Promise<ReprintJobCreated> {
  const data = await apiHttp
    .post<unknown>('/reprint/jobs', {
      domain: input.domain,
      id: input.id,
      mode: input.mode,
      source: input.source,
      templateIds: input.templateIds,
      copies: Math.max(1, Math.min(99, Math.round(input.copies))),
      isDuplex: input.isDuplex,
      printerBindingId: input.printerBindingId,
      printerName: input.printerName,
    })
    .then((response) => response.data)

  return normalizeJobCreated(data)
}

/**
 * 原样补打：直接读取历史留痕 PDF 流。
 * PDF 由浏览器打开/下载，不经过本机 Agent。
 */
export async function fetchReprintOriginal(
  domain: string,
  id: string,
  index = 0,
): Promise<Blob> {
  const response = await apiHttp.get<Blob>(
    `/reprint/records/${encodeURIComponent(domain)}/${encodeURIComponent(id)}/original`,
    {
      params: { index },
      responseType: 'blob',
    },
  )
  return response.data
}

/** 按月批量补打：按域 + 年月一次性入队（逐户或动态合并由后端决定）。 */
export async function createReprintBatch(
  input: CreateReprintBatchInput,
): Promise<ReprintBatchCreated> {
  const data = await apiHttp
    .post<unknown>('/reprint/batch', {
      domain: input.domain,
      year: input.year,
      month: input.month,
      templateIds: input.templateIds,
      mode: input.mode,
      copies: Math.max(1, Math.min(99, Math.round(input.copies))),
      isDuplex: input.isDuplex,
      keyword: input.keyword?.trim() || undefined,
    })
    .then((response) => response.data)

  return normalizeBatchCreated(data)
}

/** 批量任务进度（逐户状态 + done 标记）。 */
export async function getReprintBatch(batchNo: string): Promise<ReprintBatchStatus> {
  const data = await apiHttp
    .get<unknown>(`/reprint/batches/${encodeURIComponent(batchNo)}`)
    .then((response) => response.data)

  return normalizeBatchStatus(data)
}
