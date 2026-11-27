/**
 * 高龄年度认证（v1）：字段名/状态 key 严格对齐
 * docs/API_CONTRACT_ELDERLY_CERTIFICATION.md，不得在前端自行发明。
 *
 * 约定：金额 number；日期 string | null；long 由 JS number 承载。
 */

/* ── 状态字典 ───────────────────────────────────────────────────── */

export type CertificationCampaignStatus = 'Draft' | 'Collecting' | 'Reviewing' | 'Closed'

export type CertificationStatus =
  | 'Pending'
  | 'Verified'
  | 'Refused'
  | 'Failed'
  | 'Deceased'
  | 'MovedOut'
  | 'NotLocated'
  | 'Exempt'
  | 'Stopped'
  | 'Invalid'

export type CertificationVerifyMethod = 'Face' | 'Onsite' | 'Video' | 'Photo' | 'Other'

export type CertificationEvidenceStatus =
  | 'None'
  | 'Complete'
  | 'Partial'
  | 'Missing'
  | 'Ambiguous'

export type CertificationStopDecision = 'None' | 'Pending' | 'Approved' | 'Deferred' | 'Excluded'

/* ── 2.1 批次 ───────────────────────────────────────────────────── */

export interface CertificationCampaign {
  id: number
  campaignNo: string
  year: number
  round: number
  name: string
  displayName: string
  periodStart: string | null
  periodEnd: string | null
  validUntil: string | null
  status: CertificationCampaignStatus | string
  statusText: string
  requireEvidence: boolean
  deadlineStopOffset: number
  rosterGeneratedAt: string | null
  closedAt: string | null
  remark: string
}

export interface CertificationCampaignCreateRequest {
  year: number
  round: number
  name: string
  periodStart: string | null
  periodEnd: string | null
  requireEvidence: boolean
}

export interface CertificationCampaignCreated {
  id: number
}

export interface CertificationRosterGenerated {
  added: number
}

export interface CertificationStats {
  total: number
  pending: number
  verified: number
  refused: number
  failed: number
  deceased: number
  movedOut: number
  notLocated: number
  exempt: number
  stopped: number
  invalid: number
  evidenceIssue: number
  stopCandidate: number
  completionRate: number
}

/* ── 2.2 名单/任务 ──────────────────────────────────────────────── */

export interface CertificationTaskListItem {
  id: number
  campaignId: number
  idCard: string
  name: string
  gender: string
  birthDate: string | null
  ageSnapshot: number | null
  category: string
  categoryText: string
  issueAmount: number
  applicationId: number | null
  applicationNo: string
  sourceKind: string
  hukouVillage: string
  hukouAddress: string
  familyAddress: string
  phone: string
  certStatus: CertificationStatus | string
  certStatusText: string
  verifyMethod: CertificationVerifyMethod | string
  verifyMethodText: string
  verifiedAt: string | null
  verifiedBy: string
  resultNote: string
  deathDate: string | null
  expectedEvidenceCount: number
  evidenceCount: number
  evidenceStatus: CertificationEvidenceStatus | string
  evidenceStatusText: string
  stopDecision: CertificationStopDecision | string
  stopDecisionText: string
  followUpDate: string | null
}

/**
 * 任务详情。合同 2.2 说明「字段同上，另含完整地址/代办人/银行等快照字段」，
 * 但未逐项列出；此处仅补充与现有高龄档案同名的快照字段，全部可选。
 */
export interface CertificationTaskDetail extends CertificationTaskListItem {
  hukouDetailAddress?: string
  detailAddress?: string
  bankName?: string
  bankAccount?: string
  agentName?: string
  agentRelation?: string
  agentReceiveName?: string
  agentReceiveRelation?: string
  agentReceiveBankName?: string
  agentReceiveBankAccount?: string
  agentReceiveReason?: string
  issueStartMonth?: string
}

export interface CertificationTaskListResponse {
  items: CertificationTaskListItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface CertificationResultUpdateRequest {
  certStatus: CertificationStatus | string
  verifyMethod: CertificationVerifyMethod | string
  verifiedAt: string | null
  verifiedBy: string
  resultNote: string
  deathDate: string | null
}

export interface CertificationEvidence {
  id: number
  taskId: number
  seq: number
  evidenceType: string
  evidenceTypeText: string
  originalName: string
  displayName: string
  imageFormat: string
  fileSize: number
  sha256: string
  matchStatus: string
  matchMethod: string
}

/* ── 2.3 批量导入 ──────────────────────────────────────────────── */

export interface CertificationImportRow {
  rowNo: number
  idCard: string
  name: string
  village: string
  certStatus: CertificationStatus | string
  certStatusText: string
  verifyMethod: CertificationVerifyMethod | string
  verifiedAt: string | null
  expectedEvidenceCount: number
  matchedEvidenceCount: number
  evidenceStatus: CertificationEvidenceStatus | string
  evidenceStatusText: string
  matchStatus: string
  matchStatusText: string
  errorMessage: string
  taskId: number | null
}

export interface CertificationImportUnmatchedEntry {
  entryName: string
  fileName: string
  length: number
  extension: string
}

export interface CertificationImportPreview {
  previewId: number
  campaignId: number
  batchNo: string
  totalRows: number
  matchedRows: number
  unknownRows: number
  duplicateRows: number
  nameMismatchRows: number
  invalidRows: number
  conflictRows: number
  evidenceTotal: number
  evidenceMatched: number
  evidenceMissing: number
  evidenceUnmatched: number
  summary: string
  rows: CertificationImportRow[]
  unmatchedEntries: CertificationImportUnmatchedEntry[]
  warnings: string[]
  errors: string[]
}

export interface CertificationImportApplyRequest {
  previewId: number
  allowAdverseOverwrite: boolean
  /** 为空表示应用全部可应用行；否则只应用指定 Excel 行号。 */
  rowNumbers: number[] | null
}

export interface CertificationImportApplyResponse {
  batchId: number
  batchNo: string
  appliedRows: number
  skippedRows: number
  errorRows: number
  evidenceInserted: number
  evidenceSkipped: number
  evidenceUnmatched: number
  infoChangesCreated: number
  infoChangesSkipped: number
  infoChangeErrors: number
  summary: string
}

/* ── 2.4 停发预览 ──────────────────────────────────────────────── */

export interface CertificationStopPreviewItem extends CertificationTaskListItem {
  suggestedStopReason: string
  suggestedStopReasonText: string
  suggestedStopNote?: string
  dueStopDate: string | null
}

export interface CertificationStopPreviewResponse {
  items: CertificationStopPreviewItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface CertificationStopRequest {
  stopReason: string
  dueStopDate: string | null
  deathDate: string | null
  isRecover: boolean
  recoverStartMonth: string
  recoverEndMonth: string
  recoverAmount: number
  remark: string
  otherReasonText?: string
}

export interface CertificationStopDecisionRequest {
  decision: CertificationStopDecision | string
  note: string
  followUpDate: string | null
}

/* ── 查询参数 ──────────────────────────────────────────────────── */

export interface CertificationTaskListParams {
  page?: number
  pageSize?: number
  keyword?: string
  certStatus?: string
  evidenceStatus?: string
}

export interface CertificationStopPreviewParams {
  page?: number
  pageSize?: number
  keyword?: string
  certStatus?: string
}
