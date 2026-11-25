import { apiHttp } from './http'

/** 档案制作使用的业务类型键，与后端 PrintBusinessTypes.FamilyApplication 一致。 */
export const ARCHIVE_PRINT_BUSINESS_TYPE = 'FamilyApplication'

export type PrintJobMode = 'preview' | 'print'

export interface DocumentTemplate {
  id: number
  name: string
  docType: string
  fileType: string
  sortOrder: number
}

export interface CreatePrintJobPayload {
  businessType: string
  businessId: number
  templateId: number
  templateName?: string
  /** 打印机绑定 id（nc_sys_printer_bindings.id），后端据此写 target_device_id/target_printer。 */
  bindingId?: number | null
  printerName?: string
  copies: number
  isDuplex: boolean
  mode: PrintJobMode
}

export interface CreatedPrintJob {
  id: number
  jobNo: string
  status: string
  jobKind: string
  mode: PrintJobMode
}

export interface PrintJobListItem {
  id: number
  jobNo: string
  businessType: string
  businessId: number | null
  templateId: number | null
  templateName: string
  applicantName: string
  printerName: string
  copies: number
  isDuplex: boolean
  mode: PrintJobMode
  status: string
  jobKind: string
  errorMessage: string
  createdAt: string
  completedAt: string
}

export interface PrintJobPage {
  items: PrintJobListItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface ListPrintJobsParams {
  status?: string
  pageIndex?: number
  pageSize?: number
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function toText(value: unknown): string {
  return typeof value === 'string' ? value : value === null || value === undefined ? '' : String(value)
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

function resolveMode(raw: Record<string, unknown>): PrintJobMode {
  const mode = toText(raw.mode).toLowerCase()
  if (mode === 'preview') return 'preview'
  if (mode === 'print') return 'print'
  // 兼容旧任务：预览任务以 jobKind=Preview 落库。
  return toText(raw.jobKind).toLowerCase() === 'preview' ? 'preview' : 'print'
}

function normalizeTemplate(value: unknown): DocumentTemplate | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toNullableNumber(raw.id)
  if (id === null) return null

  return {
    id,
    name: toText(raw.name) || `模板 #${id}`,
    docType: toText(raw.docType),
    fileType: toText(raw.fileType),
    sortOrder: toNumber(raw.sortOrder),
  }
}

function normalizeJob(value: unknown): PrintJobListItem | null {
  const raw = asRecord(value)
  if (!raw) return null
  const id = toNullableNumber(raw.id)
  if (id === null) return null

  const jobKind = toText(raw.jobKind) || 'Template'
  return {
    id,
    jobNo: toText(raw.jobNo) || `#${id}`,
    businessType: toText(raw.businessType),
    businessId: toNullableNumber(raw.businessId),
    templateId: toNullableNumber(raw.templateId),
    templateName: toText(raw.templateName) || toText(raw.displayTitle) || '模板文书',
    applicantName: toText(raw.applicantName),
    printerName: toText(raw.printerName),
    copies: Math.max(1, toNumber(raw.copies, 1)),
    isDuplex: Boolean(raw.isDuplex),
    mode: resolveMode(raw),
    status: toText(raw.status) || 'Pending',
    jobKind,
    errorMessage: toText(raw.errorMessage) || toText(raw.error),
    createdAt: toText(raw.createdAt) || toText(raw.createdAtDisplay),
    completedAt: toText(raw.completedAt),
  }
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

/** 文书模板列表（业务类型固定为救助申请家庭档案）。 */
export async function listDocumentTemplates(
  businessType: string = ARCHIVE_PRINT_BUSINESS_TYPE,
): Promise<DocumentTemplate[]> {
  const data = await apiHttp
    .get<unknown>('/templates', { params: { businessType } })
    .then((response) => response.data)

  return unwrapItems(data)
    .map(normalizeTemplate)
    .filter((item): item is DocumentTemplate => item !== null)
    .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, 'zh-CN'))
}

export function createPrintJob(payload: CreatePrintJobPayload): Promise<CreatedPrintJob> {
  return apiHttp.post<unknown>('/print/jobs', payload).then((response) => {
    const raw = asRecord(response.data) ?? {}
    return {
      id: toNumber(raw.id),
      jobNo: toText(raw.jobNo),
      status: toText(raw.status) || 'Pending',
      jobKind: toText(raw.jobKind) || 'Template',
      mode: resolveMode(raw),
    }
  })
}

export async function listPrintJobs(
  params: ListPrintJobsParams = {},
): Promise<PrintJobPage> {
  const pageIndex = params.pageIndex ?? 1
  const pageSize = params.pageSize ?? 20
  const data = await apiHttp
    .get<unknown>('/print/jobs', {
      params: {
        status: params.status || undefined,
        pageIndex,
        pageSize,
      },
    })
    .then((response) => response.data)

  const items = unwrapItems(data)
    .map(normalizeJob)
    .filter((item): item is PrintJobListItem => item !== null)
  const root = asRecord(data)
  const totalCount = root
    ? toNumber(root.totalCount ?? root.total, items.length)
    : items.length
  const totalPages = root
    ? toNumber(root.totalPages, Math.max(1, Math.ceil(totalCount / pageSize)))
    : Math.max(1, Math.ceil(items.length / pageSize))

  return {
    items,
    pageIndex: root ? toNumber(root.pageIndex ?? root.page, pageIndex) : pageIndex,
    pageSize: root ? toNumber(root.pageSize, pageSize) : pageSize,
    totalCount,
    totalPages,
  }
}

export function retryPrintJob(id: number): Promise<void> {
  return apiHttp
    .post(`/print/jobs/${encodeURIComponent(String(id))}/retry`)
    .then(() => undefined)
}

export async function deletePrintJob(id: number): Promise<void> {
  await apiHttp.delete(`/print/jobs/${encodeURIComponent(String(id))}`)
}
