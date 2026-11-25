import type {
  AssetTaskDetail,
  AssetTaskListResponse,
  AssetVerificationSummary,
} from '@/types/assetVerification'
import { apiHttp } from './http'

export interface ListAssetTasksParams {
  page?: number
  pageSize?: number
  status?: string
  keyword?: string
  /** YYYY-MM-DD */
  dateFrom?: string
  /** YYYY-MM-DD */
  dateTo?: string
}

export interface ListMonthlyAssetTasksParams {
  year?: number
  month?: number
  page?: number
  pageSize?: number
  status?: string
  keyword?: string
}

/** 核对模块首页统计（失败时各字段为 null，页面降级显示）。 */
export function getAssetVerificationSummary(): Promise<AssetVerificationSummary> {
  return apiHttp
    .get<AssetVerificationSummary>('/asset-verification/summary')
    .then((response) => response.data)
}

/** 快速核对任务列表（只读）。 */
export function listAssetTasks(
  params: ListAssetTasksParams = {},
): Promise<AssetTaskListResponse> {
  return apiHttp
    .get<AssetTaskListResponse>('/asset-verification/tasks', { params })
    .then((response) => response.data)
}

/** 核对任务详情（含同批次家庭成员）。 */
export function getAssetTask(id: number | string): Promise<AssetTaskDetail> {
  return apiHttp
    .get<AssetTaskDetail>(`/asset-verification/tasks/${encodeURIComponent(String(id))}`)
    .then((response) => response.data)
}

/** 月度审核列表（按 application_date 所在年月分页）。 */
export function listMonthlyAssetTasks(
  params: ListMonthlyAssetTasksParams = {},
): Promise<AssetTaskListResponse> {
  return apiHttp
    .get<AssetTaskListResponse>('/asset-verification/monthly', { params })
    .then((response) => response.data)
}

/** 当前经办人机构区划与村社区列表。 */
export interface AssetOperatorVillage {
  id: number
  name: string
  code: string
}

export interface AssetOperatorContext {
  townId: number | null
  countyId: number | null
  townName: string
  countyName: string
  unitName: string
  operatorName: string
  operatorAccount: string
  villages: AssetOperatorVillage[]
}

export interface QuickAssetApplicantPayload {
  name: string
  idType: string
  idCard: string
  relationship: string
  isHead: boolean
}

export interface QuickAssetCheckPayload {
  applicationDate: string
  community: string
  addressDetail: string
  contactPhone: string
  applicationReason: string
  isAgent: boolean
  agentName: string
  agentIdType: string
  agentIdCard: string
  agentRelationship: string
  overwriteExisting: boolean
  applicants: QuickAssetApplicantPayload[]
}

export interface QuickAssetCheckCreated {
  batchId: string
  count: number
}

/** 读取当前账号经办乡镇、经办单位与村社区。 */
export function getAssetOperatorContext(): Promise<AssetOperatorContext> {
  return apiHttp
    .get<AssetOperatorContext>('/asset-verification/operator-context')
    .then((response) => response.data)
}

/** 提交快速资产核查申请（复用 Core SubmitQuickVerificationAsync）。 */
export function createQuickAssetCheck(
  payload: QuickAssetCheckPayload,
): Promise<QuickAssetCheckCreated> {
  return apiHttp
    .post<QuickAssetCheckCreated>('/asset-verification/checks', payload)
    .then((response) => response.data)
}

/** 按身份证号查询历史核查记录（日期、状态、地址，最多 20 条）。 */
export interface AssetHistoryTaskItem {
  id: number
  batchId: string
  applicantName: string
  applicationDate: string
  familyAddress: string
  status: string
  statusText: string
}

export interface AssetHistoryResponse {
  tasks: AssetHistoryTaskItem[]
}

export function getAssetCheckHistory(idCard: string): Promise<AssetHistoryResponse> {
  return apiHttp
    .get<AssetHistoryResponse>('/asset-verification/history', {
      params: { idCard },
    })
    .then((response) => response.data)
}
