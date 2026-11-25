/** 后补追缴：字段与 src/NewCosmos.Server/Contracts/RecoveryContracts.cs 保持一致。 */

/* ───────────────────────── 枚举值 ───────────────────────── */

export type RecoverySourceType =
  | 'RuralSubsistence'
  | 'UrbanSubsistence'
  | 'RigidExpenditure'
  | 'TempRelief'
  | 'Elderly'
  | 'Other'

export type RecoveryStatus = 'Draft' | 'Confirmed' | 'Printed'

export type RecoveryInputMode = 'Search' | 'Manual'

export type RecoveryAmountSource = 'Segment' | 'Flat' | 'Manual'

/** 分段依据：变更记录 / 台账现值 / 渐退期原标准。 */
export type RecoverySegmentSource = 'ChangeRecord' | 'Ledger' | 'GracePeriod'

/* ───────────────────────── 显示映射（中文） ───────────────────────── */

export const RECOVERY_SOURCE_TYPE_LABELS: Record<string, string> = {
  RuralSubsistence: '农村低保',
  UrbanSubsistence: '城市低保',
  RigidExpenditure: '刚性支出',
  TempRelief: '临时救助',
  Elderly: '普惠高龄',
  Other: '其他',
}

/** 手工录入分类下拉顺序（与旧版 RecoveryConstants.GetAllSourceTypes 一致）。 */
export const RECOVERY_SOURCE_TYPE_OPTIONS: ReadonlyArray<{
  value: RecoverySourceType
  label: string
}> = [
  { value: 'RuralSubsistence', label: '农村低保' },
  { value: 'UrbanSubsistence', label: '城市低保' },
  { value: 'RigidExpenditure', label: '刚性支出' },
  { value: 'TempRelief', label: '临时救助' },
  { value: 'Elderly', label: '普惠高龄' },
  { value: 'Other', label: '其他' },
]

export const RECOVERY_STATUS_LABELS: Record<string, string> = {
  Draft: '草稿',
  Confirmed: '已确认',
  Printed: '已打印',
}

export const RECOVERY_AMOUNT_SOURCE_LABELS: Record<string, string> = {
  Segment: '自动分段计算',
  Flat: '月标准×月数',
  Manual: '手工填写',
}

export const RECOVERY_SEGMENT_SOURCE_LABELS: Record<string, string> = {
  ChangeRecord: '变更记录',
  Ledger: '台账现值',
  GracePeriod: '渐退期原标准',
}

export const RECOVERY_INPUT_MODE_LABELS: Record<string, string> = {
  Search: '搜索录入',
  Manual: '手工录入',
}

export function recoverySourceTypeLabel(value: string | null | undefined): string {
  if (!value) return ''
  return RECOVERY_SOURCE_TYPE_LABELS[value] ?? value
}

export function recoveryStatusLabel(value: string | null | undefined): string {
  if (!value) return ''
  return RECOVERY_STATUS_LABELS[value] ?? value
}

export function recoveryAmountSourceLabel(value: string | null | undefined): string {
  if (!value) return ''
  return RECOVERY_AMOUNT_SOURCE_LABELS[value] ?? value
}

export function recoverySegmentSourceLabel(value: string | null | undefined): string {
  if (!value) return ''
  return RECOVERY_SEGMENT_SOURCE_LABELS[value] ?? value
}

export function recoveryInputModeLabel(value: string | null | undefined): string {
  if (!value) return ''
  return RECOVERY_INPUT_MODE_LABELS[value] ?? value
}

/* ───────────────────────── 停止人员 / 统计 / 查档 ───────────────────────── */

/** GET /api/recovery/stopped-persons */
export interface StoppedPersonDto {
  sourceType: string
  sourceDisplay: string
  sourceId: number
  personName: string
  idCard: string
  monthlyAmount: number
}

/** GET /api/recovery/stopped-persons/stats */
export interface RecoveryStats {
  totalCount: number
  ruralSubsistenceCount: number
  urbanSubsistenceCount: number
  rigidExpenditureCount: number
  tempReliefCount: number
  elderlyCount: number
}

/** GET /api/recovery/lookup */
export interface RecoveryArchiveLookupDto {
  sourceType: string
  sourceId: number
  personName: string
  idCard: string
  monthlyAmount: number
  stopReason: string
  isStopped: boolean
}

/** GET /api/recovery/history */
export interface RecoveryMonthlyStandardPoint {
  effectiveMonth: string
  monthlyAmount: number
  source: string
}

/* ───────────────────────── 自动计算 ───────────────────────── */

/** GET /api/recovery/calculate 的分段。 */
export interface RecoveryCalculateSegment {
  segmentNo: number
  monthlyAmount: number
  segmentStartMonth: string
  segmentEndMonth: string
  months: number
  segmentAmount: number
  source: string
  sourceDisplay: string
}

/** GET /api/recovery/calculate */
export interface RecoveryCalculateResult {
  canCalculate: boolean
  reason: string
  amount: number
  amountSource: string
  segments: RecoveryCalculateSegment[]
}

/** 分段明细（记录详情 / 保存请求共用）。 */
export interface RecoverySegment {
  segmentNo?: number
  monthlyAmount: number
  segmentStartMonth: string
  segmentEndMonth: string
  months: number
  segmentAmount: number
  source: string
  sourceDisplay?: string
}

/* ───────────────────────── 追缴记录 ───────────────────────── */

/** GET /api/recovery/records */
export interface RecoveryRecord {
  id: number
  inputMode: string
  sourceType: string
  sourceId: number | null
  personName: string
  idCard: string
  recoveryReason: string
  monthlyAmount: number
  recoveryMonths: number
  recoveryAmount: number
  amountSource: string
  amountNote: string
  autoAmount: number
  recoveredAmount: number
  startMonth: string
  endMonth: string
  status: string
  createdBy: string
  createdAt: string | null
  updatedAt: string | null
  statusDisplay: string
  sourceTypeDisplay: string
  inputModeDisplay: string
  amountSourceDisplay: string
  canDelete: boolean
  canClose: boolean
  canReprint: boolean
}

/** POST /api/recovery/records 请求体。 */
export interface RecoveryRecordDto {
  sourceType: string
  sourceId: number
  personName: string
  idCard: string
  recoveryReason: string
  monthlyAmount: number
  startMonth: string
  endMonth: string
  recoveredAmount: number
  recoveryAmount: number
  amountSource: string
  amountNote: string
  autoAmount: number
  segments: RecoverySegment[]
}

/** POST /api/recovery/records/manual 请求体。 */
export interface ManualRecoveryRecordDto {
  personName: string
  idCard: string
  sourceType: string
  recoveryReason: string
  monthlyAmount: number
  startMonth: string
  endMonth: string
  recoveredAmount: number
  recoveryAmount: number
  amountSource: string
  amountNote: string
  autoAmount: number
  segments: RecoverySegment[]
}

/** 保存接口归一化结果（兼容只返回 id / 返回整条记录两种后端实现）。 */
export interface RecoverySaveResult {
  id: number
  record: RecoveryRecord | null
}
