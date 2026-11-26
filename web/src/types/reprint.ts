/** 历史补打中心 · 前后端契约类型（GET/POST /api/reprint/*）。 */

/** 补打业务域模式：archive=档案集逐户 / asset=资产核查逐户 / dynamic=动态管理合并。 */
export interface ReprintDomain {
  key: string
  name: string
  /** 后端返回的模式标识数组（大小写不敏感，前端按关键字识别）。 */
  modes: string[]
}

export interface ReprintPersonQuery {
  keyword?: string
  year?: number
  /** 0 或缺省表示全部月份。 */
  month?: number
  domain?: string
}

export interface ReprintPerson {
  key: string
  name: string
  idCard: string
  domainNames: string[]
  latestTime: string
  count: number
}

export interface ReprintPersonList {
  items: ReprintPerson[]
}

export interface ReprintPersonDomainQuery {
  year?: number
  month?: number
}

/** 人员名下域分支（点击后查询该域记录）。 */
export interface ReprintPersonDomain {
  domain: string
  name: string
  count: number
  latestTime: string
}

export interface ReprintRecordQuery {
  domain: string
  /** 按人查询时必填；批量预览可缺省。 */
  idCard?: string
  year?: number
  month?: number
}

export interface ReprintRecord {
  /** 业务记录 ID（用于 /records/{domain}/{id} 路径）。 */
  id: string
  name: string
  idCard: string
  businessDate: string
  classification: string
  status: string
  versionLabel: string
  extra: string
  no: string
}

/** 详情接口在列表字段之外保留原始载荷；页面当前用于摘要兜底。 */
export interface ReprintRecordDetail extends ReprintRecord {
  fields: Record<string, string>
  payload: Record<string, unknown>
}

export interface ReprintTemplate {
  id: number
  name: string
  fileType: string
  docType: string
  /** 后端预选状态。 */
  isSelected: boolean
  /** 不适用于当前记录时禁用勾选。 */
  isApplicable: boolean
}

export interface ReprintHistoryItem {
  templateName: string
  printerName: string
  operatorName: string
  createdAt: string
  status: string
}

export type ReprintJobMode = 'preview' | 'print' | 'save'
export type ReprintJobSource = 'original' | 'template'
export type ReprintBatchMode = 'print' | 'save'

export interface CreateReprintJobInput {
  domain: string
  id: string
  mode: ReprintJobMode
  source: ReprintJobSource
  templateIds: number[]
  copies: number
  isDuplex: boolean
  printerBindingId?: number
  printerName?: string
}

export interface ReprintJobItem {
  jobNo: string
  businessId: string
  name: string
  status: string
  templateName: string
}

export interface ReprintJobCreated {
  jobNo: string
  status: string
  jobs: ReprintJobItem[]
}

export interface CreateReprintBatchInput {
  domain: string
  year: number
  month: number
  templateIds: number[]
  mode: ReprintBatchMode
  copies: number
  isDuplex: boolean
  /**
   * 关键词（契约外可选）：后端支持时用于收窄批量范围；不支持时忽略。
   * 未传时按域 + 年月执行整批。
   */
  keyword?: string
}

export interface ReprintBatchJob {
  jobNo: string
  businessId: string
  name: string
}

export interface ReprintBatchCreated {
  batchNo: string
  total: number
  jobs: ReprintBatchJob[]
}

export interface ReprintBatchItem {
  jobNo: string
  businessId: string
  name: string
  status: string
  error: string
}

export interface ReprintBatchStatus {
  total: number
  completed: number
  failed: number
  items: ReprintBatchItem[]
  done: boolean
}
