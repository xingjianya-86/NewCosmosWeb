/**
 * 高龄年度认证·信息变更（v1）：字段名/状态 key 严格对齐
 * docs/API_CONTRACT_ELDERLY_INFO_CHANGE.md，不得在前端自行发明。
 *
 * 约定：一次提交可包含多个字段，共用同一个 changeNo；
 * 身份证/姓名/户籍/类别/身份标识不允许走普通信息变更。
 */

/* ── 字典 ──────────────────────────────────────────────────────── */

export type InfoChangeFieldKey =
  | 'phone'
  | 'family_address'
  | 'detail_address'
  | 'bank_name'
  | 'bank_account'
  | 'agent_name'
  | 'agent_relation'
  | 'agent_receive_name'
  | 'agent_receive_relation'
  | 'agent_receive_bank_name'
  | 'agent_receive_bank_account'

/** 合同 1：LOW 可直接审批；HIGH 需凭证/复核。 */
export type InfoChangeRisk = 'LOW' | 'HIGH'

export type InfoChangeStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'

export type InfoChangeSourceType = 'Manual' | 'CertificationImport' | 'Certification'

export type InfoChangeFieldGroup = 'contact' | 'bank' | 'agent' | 'agentReceive'

export interface InfoChangeFieldDefinition {
  fieldKey: InfoChangeFieldKey
  fieldLabel: string
  risk: InfoChangeRisk
  group: InfoChangeFieldGroup
}

/* ── 字段白名单 ────────────────────────────────────────────────── */

export const INFO_CHANGE_FIELD_GROUP_LABELS: Record<InfoChangeFieldGroup, string> = {
  contact: '联系方式与地址',
  bank: '社保卡',
  agent: '代办人',
  agentReceive: '代领人',
}

export const INFO_CHANGE_FIELD_MAP: Record<InfoChangeFieldKey, InfoChangeFieldDefinition> = {
  phone: { fieldKey: 'phone', fieldLabel: '联系电话', risk: 'LOW', group: 'contact' },
  family_address: {
    fieldKey: 'family_address',
    fieldLabel: '家庭住址',
    risk: 'LOW',
    group: 'contact',
  },
  detail_address: {
    fieldKey: 'detail_address',
    fieldLabel: '家庭详细地址',
    risk: 'LOW',
    group: 'contact',
  },
  bank_name: { fieldKey: 'bank_name', fieldLabel: '社保卡开户行', risk: 'HIGH', group: 'bank' },
  bank_account: { fieldKey: 'bank_account', fieldLabel: '社保卡账号', risk: 'HIGH', group: 'bank' },
  agent_name: { fieldKey: 'agent_name', fieldLabel: '代办人姓名', risk: 'HIGH', group: 'agent' },
  agent_relation: {
    fieldKey: 'agent_relation',
    fieldLabel: '代办人关系',
    risk: 'HIGH',
    group: 'agent',
  },
  agent_receive_name: {
    fieldKey: 'agent_receive_name',
    fieldLabel: '代领人姓名',
    risk: 'HIGH',
    group: 'agentReceive',
  },
  agent_receive_relation: {
    fieldKey: 'agent_receive_relation',
    fieldLabel: '代领人关系',
    risk: 'HIGH',
    group: 'agentReceive',
  },
  agent_receive_bank_name: {
    fieldKey: 'agent_receive_bank_name',
    fieldLabel: '代领人开户行',
    risk: 'HIGH',
    group: 'agentReceive',
  },
  agent_receive_bank_account: {
    fieldKey: 'agent_receive_bank_account',
    fieldLabel: '代领人账号',
    risk: 'HIGH',
    group: 'agentReceive',
  },
}

export const INFO_CHANGE_FIELDS: readonly InfoChangeFieldDefinition[] =
  Object.values(INFO_CHANGE_FIELD_MAP)

/* ── 变更单 ────────────────────────────────────────────────────── */

export interface InfoChangeDetail {
  id: number
  changeId?: number
  fieldKey: InfoChangeFieldKey | string
  fieldLabel: string
  oldValue: string
  newValue: string
  /** 服务端回填字段风险；缺失时前端按白名单补。 */
  risk?: InfoChangeRisk | string
  riskText?: string
  createdAt?: string | null
}

export interface InfoChangeRecord {
  id: number
  changeNo: string
  campaignId: number | null
  taskId: number | null
  applicationId: number | null
  idCard: string
  name: string
  /** 整单最高风险；合同 change_type 为 LOW/HIGH。 */
  changeType: InfoChangeRisk | string
  changeTypeText?: string
  reason: string
  status: InfoChangeStatus | string
  statusText?: string
  sourceType?: InfoChangeSourceType | string
  sourceTypeText?: string
  sourceFile?: string
  sourceRow?: number | null
  effectiveMonth: string | null
  appliedAt: string | null
  appliedBy: string
  reviewedAt: string | null
  reviewedBy: string
  reviewOpinion: string
  createdAt: string | null
  createdBy?: string
  updatedAt?: string | null
  details: InfoChangeDetail[]
}

export interface InfoChangeListParams {
  status?: string
  page?: number
  pageSize?: number
  keyword?: string
}

export interface InfoChangeListResponse {
  items: InfoChangeRecord[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}

/* ── 提交 / 审批 ───────────────────────────────────────────────── */

export interface InfoChangeCreateDetail {
  fieldKey: InfoChangeFieldKey | string
  /** 可空；服务端为空时取申请主表当前值。 */
  oldValue: string
  newValue: string
}

export interface InfoChangeCreateRequest {
  reason: string
  sourceType: InfoChangeSourceType | string
  /** 银行卡等可填生效月；电话/地址立即生效时省略。 */
  effectiveMonth?: string | null
  details: InfoChangeCreateDetail[]
}

/** 新建成功的最小响应；服务端可能返回完整变更单。 */
export interface InfoChangeCreated {
  id: number
  changeNo: string
  status?: InfoChangeStatus | string
}

export interface InfoChangeApproveRequest {
  reviewOpinion: string
  /** 仅银行卡类变更需要；其他变更可省略。 */
  effectiveMonth?: string | null
}

export interface InfoChangeRejectRequest {
  reviewOpinion: string
}
