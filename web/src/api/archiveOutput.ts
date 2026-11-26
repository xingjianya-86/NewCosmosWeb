import type {
  ArchiveOutputCompleteInput,
  ArchiveOutputCompleteResult,
  ArchiveOutputJobInput,
  ArchiveOutputJobResult,
  ArchiveOutputJobTemplate,
  ArchiveOutputTemplate,
  ArchiveOutputTemplatesQuery,
  ArchiveProductionInfo,
  ArchiveProductionQuery,
  ArchiveProductionSidebarField,
  ArchiveProductionSidebarGroup,
  ArchiveProductionSummaryItem,
} from '@/types/archiveOutput'
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

function normalizeSummaryItem(value: unknown): ArchiveProductionSummaryItem | null {
  const raw = asRecord(value)
  if (!raw) return null
  return {
    label: toText(raw.label),
    value: toText(raw.value),
  }
}

function normalizeSidebarField(value: unknown): ArchiveProductionSidebarField | null {
  const raw = asRecord(value)
  if (!raw) return null
  return {
    label: toText(raw.label),
    value: toText(raw.value),
    autoFilled: Boolean(raw.autoFilled),
  }
}

function normalizeSidebarGroup(value: unknown): ArchiveProductionSidebarGroup | null {
  const raw = asRecord(value)
  if (!raw) return null
  return {
    title: toText(raw.title) || toText(raw.groupName),
    fields: unwrapItems(raw.fields)
      .map(normalizeSidebarField)
      .filter((field): field is ArchiveProductionSidebarField => field !== null),
  }
}

function normalizeProduction(
  data: unknown,
  fallback: ArchiveProductionQuery,
): ArchiveProductionInfo {
  const raw = asRecord(data) ?? {}
  return {
    businessType: toText(raw.businessType) || fallback.businessType,
    businessId: toNumber(raw.businessId, fallback.businessId),
    title: toText(raw.title),
    statusText: toText(raw.statusText),
    summary: unwrapItems(raw.summary)
      .map(normalizeSummaryItem)
      .filter((item): item is ArchiveProductionSummaryItem => item !== null),
    sidebarGroups: unwrapItems(raw.sidebarGroups)
      .map(normalizeSidebarGroup)
      .filter((group): group is ArchiveProductionSidebarGroup => group !== null),
    classification: toText(raw.classification),
    operationOverride: toText(raw.operationOverride),
  }
}

function normalizeTemplate(value: unknown): ArchiveOutputTemplate | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toNullableNumber(raw.id)
  if (id === null) return null

  return {
    id,
    name: toText(raw.name) || `模板 #${id}`,
    fileType: toText(raw.fileType),
    docType: toText(raw.docType),
    sortOrder: toNumber(raw.sortOrder),
    // 契约明确 defaultSelected；缺省时按“默认全选”处理。
    defaultSelected: raw.defaultSelected === undefined ? true : Boolean(raw.defaultSelected),
  }
}

function normalizeJobTemplate(value: unknown): ArchiveOutputJobTemplate | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toNullableNumber(raw.id)
  if (id === null) return null
  return {
    id,
    name: toText(raw.name) || `模板 #${id}`,
  }
}

function normalizeJob(data: unknown): ArchiveOutputJobResult {
  const raw = asRecord(data) ?? {}
  const id = toNumber(raw.id)
  return {
    id,
    jobNo: toText(raw.jobNo) || (id > 0 ? `#${id}` : ''),
    status: toText(raw.status) || 'Pending',
    jobKind: toText(raw.jobKind) || 'Template',
    templates: unwrapItems(raw.templates)
      .map(normalizeJobTemplate)
      .filter((item): item is ArchiveOutputJobTemplate => item !== null),
  }
}

/* ───────────────────────── 档案制作中心 ───────────────────────── */

/** 制作中心业务信息：摘要、来源数据边栏、分类与操作覆盖。 */
export function getArchiveProduction(
  params: ArchiveProductionQuery,
): Promise<ArchiveProductionInfo> {
  return apiHttp
    .get<unknown>('/archives/production', {
      params: {
        businessType: params.businessType,
        businessId: params.businessId,
      },
    })
    .then((response) => normalizeProduction(response.data, params))
}

/* ───────────────────────── 档案输出 ───────────────────────── */

/** 当前业务可用的输出模板（按 sortOrder 升序）。 */
export async function listArchiveOutputTemplates(
  params: ArchiveOutputTemplatesQuery,
): Promise<ArchiveOutputTemplate[]> {
  const data = await apiHttp
    .get<unknown>('/archives/output/templates', {
      params: {
        businessType: params.businessType,
        businessId: params.businessId,
        classification: params.classification || undefined,
        operationOverride: params.operationOverride || undefined,
      },
    })
    .then((response) => response.data)

  return unwrapItems(data)
    .map(normalizeTemplate)
    .filter((item): item is ArchiveOutputTemplate => item !== null)
    .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'zh-CN'))
}

/** 创建输出任务：预览 / 打印 / 保存由本机 Agent 领取执行。 */
export function createArchiveOutputJob(
  payload: ArchiveOutputJobInput,
): Promise<ArchiveOutputJobResult> {
  return apiHttp
    .post<unknown>('/archives/output/jobs', payload)
    .then((response) => normalizeJob(response.data))
}

/** 完成归档：将当前业务标记为已归档。 */
export function completeArchiveOutput(
  payload: ArchiveOutputCompleteInput,
): Promise<ArchiveOutputCompleteResult> {
  return apiHttp
    .post<unknown>('/archives/output/complete', payload)
    .then((response) => {
      const raw = asRecord(response.data) ?? {}
      return { completed: raw.completed === undefined ? true : Boolean(raw.completed) }
    })
}
