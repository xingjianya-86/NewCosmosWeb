/**
 * 档案制作中心 / 档案输出：字段与 src/NewCosmos.Server 契约保持一致。
 *
 * 制作中心只负责展示业务摘要与来源数据；真正的 PDF 生成、打印与落盘
 * 全部由本机 Agent 领取输出任务后执行，Web 端不内嵌 PDF。
 */

export interface ArchiveProductionSummaryItem {
  label: string
  value: string
}

export interface ArchiveProductionSidebarField {
  label: string
  value: string
  /** 该字段是否由业务数据自动填充（用于边栏标记与统计）。 */
  autoFilled: boolean
}

export interface ArchiveProductionSidebarGroup {
  title: string
  fields: ArchiveProductionSidebarField[]
}

export interface ArchiveProductionInfo {
  businessType: string
  businessId: number
  title: string
  statusText: string
  summary: ArchiveProductionSummaryItem[]
  sidebarGroups: ArchiveProductionSidebarGroup[]
  classification: string
  operationOverride: string
}

export interface ArchiveProductionQuery {
  businessType: string
  businessId: number
}

export interface ArchiveOutputTemplatesQuery extends ArchiveProductionQuery {
  classification?: string
  operationOverride?: string
}

export interface ArchiveOutputTemplate {
  id: number
  name: string
  fileType: string
  docType: string
  sortOrder: number
  /** 后端建议的默认勾选状态；缺省按 true 处理。 */
  defaultSelected: boolean
}

export type ArchiveOutputMode = 'preview' | 'print' | 'save'

export interface ArchiveOutputJobInput {
  businessType: string
  businessId: number
  classification?: string
  operationOverride?: string
  templateIds: number[]
  mode: ArchiveOutputMode
  copies?: number
  isDuplex?: boolean
  printerBindingId?: number | null
  printerName?: string
}

export interface ArchiveOutputJobTemplate {
  id: number
  name: string
}

export interface ArchiveOutputJobResult {
  id: number
  jobNo: string
  status: string
  jobKind: string
  templates: ArchiveOutputJobTemplate[]
}

export interface ArchiveOutputCompleteInput {
  businessType: string
  businessId: number
}

export interface ArchiveOutputCompleteResult {
  completed: boolean
}
