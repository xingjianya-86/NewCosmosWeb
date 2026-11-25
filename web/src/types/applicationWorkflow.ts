/** 业务申请工作流接口类型，对应 server/Contracts/ApplicationWorkflowContracts.cs。 */

export interface WorkflowSummary {
  assetAccess: boolean
  submittedAssetChecks: number | null
  assetChecksWithReport: number | null
  drafts: number | null
  archiveBuilt: number | null
  archived: number | null
}

export type WorkflowKind = 'asset' | 'application'
export type WorkflowStatusTone = 'info' | 'warning' | 'danger' | 'success' | 'neutral'

export interface WorkflowItem {
  key: string
  kind: WorkflowKind
  id: number
  name: string
  idCardMasked: string
  status: string
  statusText: string
  statusTone: WorkflowStatusTone
  relation: string
  applicationNo: string
  familyAddress: string
  community: string
  createdAt: string | null
  isSingleRescue: boolean
  hasGraceNote: boolean
  graceNote: string | null
  hasBatchHint: boolean
  batchHintText: string | null
  classificationResult: string
  classificationText: string
  archiveStatus: string
  archiveStatusText: string
  householdMonthlyGuaranteeAmount: number | null
}

export interface WorkflowListResponse {
  items: WorkflowItem[]
  tab: number
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

/** 工作流「照片」对话框条目，对应 server/Contracts/ApplicationWorkflowPhotoContracts.cs。 */
export interface WorkflowPhoto {
  id: number
  category: string
  photoName: string
  format: string
  /** 服务端已拼好的 data:{mime};base64,... */
  dataUrl: string
}
