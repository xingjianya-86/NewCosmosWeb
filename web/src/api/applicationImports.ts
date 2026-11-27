import { apiHttp } from './http'

/** 农业补贴导入记录（对应 Core SubsidyImportRecord）。 */
export interface SubsidyImportRecord {
  name: string
  idCard: string
  subsidyType: string
  area: number
  amount: number
}

/** 土地确权台账导入记录（对应 Core LandConfirmationRecord）。 */
export interface LandConfirmationImportRecord {
  memberName: string
  memberIdCard: string
  landPlotInfo: string
  plotCode: string
  contractArea: number
  measuredArea: number
  landArea: number
  landUsage: string
  unitPrice: number
  isImported: boolean
}

function importsUrl(id: number | string, segment: string): string {
  return `/applications/${encodeURIComponent(String(id))}/imports/${segment}`
}

/** 按当前申请人 + 共同生活成员身份证号查询农业补贴。 */
export function getApplicationSubsidyImports(
  id: number | string,
): Promise<SubsidyImportRecord[]> {
  return apiHttp
    .get<SubsidyImportRecord[]>(importsUrl(id, 'subsidies'))
    .then((response) => response.data ?? [])
}

/** 按当前申请人 + 共同生活成员姓名查询土地确权台账。 */
export function getApplicationLandConfirmationImports(
  id: number | string,
): Promise<LandConfirmationImportRecord[]> {
  return apiHttp
    .get<LandConfirmationImportRecord[]>(importsUrl(id, 'land-confirmation'))
    .then((response) => response.data ?? [])
}

/** 银行流水核对带出的成员明细行（对应 Server BankFlowMemberRowDto）。 */
export interface BankFlowMemberRow {
  name: string
  idCard: string
  /** 是否在最新核对批次中命中该身份证。 */
  matched: boolean
  totalBalance: number
  totalBalanceDisplay: string
  maxFlowAmount: number
  maxFlowAmountDisplay: string
  /** 各银行摘要（余额/最高流水）；未命中时为“核对数据中无此身份证”。 */
  bankSummary: string
  /** 超阈值标记（仅提示，不自动决策）。 */
  isExceed: boolean
}

/** 银行流水核对带入预览（对应 Server BankFlowImportPreviewDto，只读不落库）。 */
export interface BankFlowImportPreview {
  batchNo: string
  totalBalance: number
  maxFlow: number
  matchedCount: number
  totalCount: number
  /** 摘要文案（批次号/命中人数/合计余额），由服务端按 Core 口径生成。 */
  summary: string
  /** 超阈值红字提示；空串 = 未超阈值或标准配置缺失。 */
  exceedHint: string
  /** 核对数据中是否存在 60 马力以上农机。 */
  hasMachineryExceed: boolean
  rows: BankFlowMemberRow[]
}

/** 按当前申请人 + 共同生活成员身份证从最新核对批次带出银行流水（只读）。 */
export function getApplicationBankFlowImports(
  id: number | string,
): Promise<BankFlowImportPreview> {
  return apiHttp
    .get<BankFlowImportPreview>(importsUrl(id, 'bank-flow'))
    .then((response) => response.data)
}
