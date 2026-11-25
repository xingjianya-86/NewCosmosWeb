/**
 * 快速核验 · 模拟测算：字段与 server/Contracts/QuickVerifyContracts.cs 对齐。
 * 数值与展示文本均由 Core 计算/格式化，前端只渲染，不在前端重算。
 */

export interface QuickVerifyItem {
  title: string
  detail: string
  value: string
  badge: string
}

export type QuickVerifyGroupKey =
  | 'contractLand'
  | 'historyLand'
  | 'subsidies'
  | 'securityIdentities'
  | 'assets'
  | 'bankFlows'
  | 'assetReports'
  | 'deaths'
  | 'tempReliefs'
  | 'elderlyBenefits'

export interface QuickVerifyGroup {
  key: QuickVerifyGroupKey | string
  title: string
  isFailure: boolean
  failureReason: string
  matchNote: string
  hasItems: boolean
  count: number
  countText: string
  items: QuickVerifyItem[]
}

export interface QuickVerifyImportSuggestion {
  landTotal: number
  landSource: string
  subsidyTotal: number
  subsidyCount: number
}

export interface QuickVerifyResponse {
  idCard: string
  idCardMasked: string
  queryName: string
  nameSource: string
  nameDisplay: string
  summary: string
  hasAnyRecord: boolean
  hitGroupCount: number
  groups: QuickVerifyGroup[]
  importSuggestion: QuickVerifyImportSuggestion
}

export interface QuickVerifyStandards {
  hukou: string
  hukouText: string
  year: number
  yearNote: string
  standardType: string
  standardSource: string
  standardAvailable: boolean
  standardError: string
  subsistenceStandardMonthly: number
  edgeStandardMonthly: number
  perChildMonthlyFee: number
  supportFeeRatio: number
  lowIncomeMultiplier: number
  selfFarmUnitPrice: number
  subleaseUnitPrice: number
  contractUnitPrice: number
  unitPriceText: string
  standardText: string
  edgeText: string
  perChildFeeText: string
}

export interface QuickVerifySimulationInput {
  name: string
  idCard: string
  hukou: string
  familySizeExcludingHead: number
  children: number
  perChildMonthlyFee: number | null
  landSelfArea: number
  landSubleaseArea: number
  landContractArea: number
  subsidyAnnual: number
}

export interface QuickVerifySimulationLine {
  label: string
  value: string
}

export interface QuickVerifySimulationResult {
  name: string
  idCard: string
  hukou: string
  hukouText: string
  familySize: number
  children: number
  perChildMonthlyFee: number
  alimonyAnnual: number
  landAnnual: number
  subsidyAnnual: number
  annualFamilyIncome: number
  perCapitaAnnual: number
  perCapitaMonthly: number
  subsistenceStandardMonthly: number
  edgeStandardMonthly: number
  landSelfArea: number
  landSubleaseArea: number
  landContractArea: number
  selfFarmUnitPrice: number
  subleaseUnitPrice: number
  contractUnitPrice: number
  zoneKind: 'Low' | 'Edge' | 'Over' | string
  zoneText: string
  annualIncomeText: string
  perCapitaAnnualText: string
  perCapitaMonthlyText: string
  standardText: string
  edgeText: string
  perChildFeeText: string
  lines: QuickVerifySimulationLine[]
}

export interface QuickVerifyReportRequest extends QuickVerifySimulationInput {
  includeIncome: boolean
  includeVerification: boolean
}

export interface QuickVerifyReportResponse {
  text: string
  length: number
  incomeIncluded: boolean
  verificationIncluded: boolean
  verificationError: string
}
