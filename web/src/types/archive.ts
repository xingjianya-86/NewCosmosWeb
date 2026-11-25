/** 救助档案管理：字段与 src/NewCosmos.Server 档案查询/证明打印契约保持一致。 */

import type { ApplicationFormModel } from '@/views/applications/form/types'

/* ───────────────────────── 页头统计 ───────────────────────── */

export interface ArchiveStats {
  /** 归档总数（当前库已归档 + 历史导入库合计）。 */
  totalArchives: number
  /** 当月归档数（当前库本月完成归档）。 */
  monthlyArchives: number
}

/* ───────────────────────── 搜索结果 ───────────────────────── */

export interface ArchiveSearchItem {
  /** 来源键（当前库或历史导入库人群代码）。 */
  source: number
  /** 来源中文名（当前库 / 农村低保 / 特困供养 …）。 */
  sourceDisplay: string
  /** 当前库申请 ID；导入库为空。 */
  applicationId: number | null
  /** 导入库来源表名；当前库为空。 */
  sourceTable: string
  /** 导入库来源行 ID；当前库为空。 */
  sourceId: number | null
  applicantName: string
  applicantIdCard: string
  address: string
  familySize: number
  totalAmount: number
  /** 命中家庭成员提示（按姓名/身份证命中非户主时展示）。 */
  matchedPersonInfo: string
  isFromCurrentLibrary: boolean
}

export interface ArchiveSearchResponse {
  items: ArchiveSearchItem[]
}

/* ───────────────────────── 当前库档案详情 ───────────────────────── */

/** 当前库档案摘要（后端 summary 段；字段缺失时前端回退到 form）。 */
export interface ArchiveCurrentSummary {
  applicantName?: string
  applicantIdCard?: string
  classificationName?: string
  classification?: string
  classificationResult?: string
  address?: string
  familySize?: number
  totalAmount?: number
  totalGuaranteeAmount?: number
  guaranteeAmount?: number
  phone?: string
}

export interface ArchiveCurrentDetail {
  applicationId: number
  summary: ArchiveCurrentSummary
  form: ApplicationFormModel | null
}

/* ───────────────────────── 历史导入库档案详情 ───────────────────────── */

export interface ArchiveImportedMember {
  name: string
  relationship: string
  age: number | null
  annualIncome: number | null
  classifiedSubsidyAmount: number
  idCard: string
  workCapacity: string
}

export interface ImportedFamilyDetail {
  sourceDisplay: string
  derivedClassificationCode: string
  applicantName: string
  applicantIdCard: string
  phone: string
  address: string
  province: string
  city: string
  district: string
  town: string
  community: string
  familySize: number
  totalAmount: number
  perCapitaIncome: number | null
  hukouType: string
  supportMode: string
  monthlyGuaranteeAmount: number
  familyClassifiedAmount: number
  classifiedSubsidyAmount: number
  firstReceiveMonth: string
  applyReason: string
  members: ArchiveImportedMember[]
}

/* ───────────────────────── 证明模板 ───────────────────────── */

export interface ArchiveProofGenericTemplate {
  id: number
  name: string
  fileType: string
}

export interface ArchiveProofUnitTemplate {
  id: number
  unitName: string
  templateId: number
  templateName: string
  fileType: string
  createdAt: string
  updatedAt: string
}

export interface ArchiveProofTemplatesResponse {
  generic: ArchiveProofGenericTemplate[]
  units: ArchiveProofUnitTemplate[]
}

/** 单位模板新增/编辑请求（multipart；编辑时 file 可空表示保留原模板）。 */
export interface ArchiveProofUnitInput {
  unitName: string
  fileType: string
  file?: File | null
  /** 可选字段映射 JSON；留空由后端按 {占位符} 直接替换。 */
  configJson?: string
}

/* ───────────────────────── 证明打印任务 ───────────────────────── */

export type ArchiveProofJobMode = 'preview' | 'print' | 'save'

export interface ArchiveProofJobInput {
  /** 当前库档案：传 applicationId。 */
  applicationId?: number
  /** 导入库档案：传 source + sourceTable + sourceId。 */
  source?: number
  sourceTable?: string
  sourceId?: number
  templateId: number
  /** 通配模板必填；单位专属模板传映射单位名。 */
  targetUnitName?: string
  mode: ArchiveProofJobMode
  copies: number
  isDuplex: boolean
  /** 打印机绑定信息（后端可选用；Agent 未指定时使用默认打印机）。 */
  printerName?: string
  printerBindingId?: number | null
}

export interface ArchiveProofJobResult {
  id: number
  jobNo: string
  status: string
  jobKind: string
}

/* ───────────────────────── 证明开具历史 ───────────────────────── */

export interface ArchiveProofHistoryItem {
  id: number
  templateName: string
  printerName: string
  operatorName: string
  status: string
  createdAt: string
  completedAt: string
}

export interface ArchiveProofHistoryParams {
  applicationId?: number | null
  idCard?: string
}
