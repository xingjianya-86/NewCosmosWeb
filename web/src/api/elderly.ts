import type {
  ElderlyApplicationCreated,
  ElderlyApplicationDetail,
  ElderlyApplicationFormRequest,
  ElderlyApplicationFormResponse,
  ElderlyApplicationListResponse,
  ElderlyEvaluateResult,
  ElderlyIdCardOcrResult,
  ElderlyImportedItem,
  ElderlyOperatorAddress,
  ElderlyPendingResponse,
  ElderlyReviewListResponse,
  ElderlyStopRequest,
  ElderlyStopStats,
  ElderlySummary,
} from '@/types/elderly'
import { apiHttp } from './http'

export interface ListElderlyApplicationsParams {
  page?: number
  pageSize?: number
  /** 空 = 全部（Draft/Confirmed/Stopped）。 */
  status?: string
  keyword?: string
}

export interface ListElderlyReviewsParams {
  page?: number
  pageSize?: number
  /** 空 = 全部（Pending/Completed）。 */
  status?: string
  keyword?: string
}

/** 高龄模块概览（业务 API .2）。 */
export function getElderlySummary(): Promise<ElderlySummary> {
  return apiHttp
    .get<ElderlySummary>('/elderly/summary')
    .then((response) => response.data)
}

/** 高龄津贴登记分页列表。 */
export function listElderlyApplications(
  params: ListElderlyApplicationsParams = {},
): Promise<ElderlyApplicationListResponse> {
  return apiHttp
    .get<ElderlyApplicationListResponse>('/elderly/applications', { params })
    .then((response) => response.data)
}

/** 高龄津贴登记详情（只读）。 */
export function getElderlyApplication(id: number | string): Promise<ElderlyApplicationDetail> {
  return apiHttp
    .get<ElderlyApplicationDetail>(`/elderly/applications/${encodeURIComponent(String(id))}`)
    .then((response) => response.data)
}

/** 下月待办（待新增 / 需停旧增新）。 */
export function getElderlyPending(): Promise<ElderlyPendingResponse> {
  return apiHttp
    .get<ElderlyPendingResponse>('/elderly/pending')
    .then((response) => response.data)
}

/** 高龄津贴类别复核分页列表。 */
export function listElderlyReviews(
  params: ListElderlyReviewsParams = {},
): Promise<ElderlyReviewListResponse> {
  return apiHttp
    .get<ElderlyReviewListResponse>('/elderly/reviews', { params })
    .then((response) => response.data)
}

/** 登记表单全字段（编辑/查看）。 */
export function getElderlyApplicationForm(
  id: number | string,
): Promise<ElderlyApplicationFormResponse> {
  return apiHttp
    .get<ElderlyApplicationFormResponse>(
      `/elderly/applications/${encodeURIComponent(String(id))}/form`,
    )
    .then((response) => response.data)
}

/** 判类 + 补发评估。 */
export function evaluateElderly(
  idCard: string,
  applyDate: string,
): Promise<ElderlyEvaluateResult> {
  return apiHttp
    .get<ElderlyEvaluateResult>('/elderly/evaluate', { params: { idCard, applyDate } })
    .then((response) => response.data)
}

/** 特殊补发区间重算（手动起止月）。 */
export function evaluateElderlySpecial(
  idCard: string,
  startMonth: string,
  endMonth: string,
): Promise<ElderlyEvaluateResult['payback']> {
  return apiHttp
    .post<ElderlyEvaluateResult['payback']>('/elderly/evaluate/special', {
      idCard,
      startMonth,
      endMonth,
    })
    .then((response) => response.data)
}

/** 身份证精确查导入名册。 */
export function getElderlyImportedByIdCard(
  idCard: string,
): Promise<ElderlyImportedItem | null> {
  return apiHttp
    .get<ElderlyImportedItem | null>('/elderly/imported', { params: { idCard } })
    .then((response) => response.data ?? null)
}

/** 姓名/身份证检索导入名册。 */
export function searchElderlyImported(keyword: string): Promise<ElderlyImportedItem[]> {
  return apiHttp
    .get<ElderlyImportedItem[]>('/elderly/imported/search', { params: { keyword } })
    .then((response) => response.data ?? [])
}

/** 新增登记。 */
export function createElderlyApplication(
  request: ElderlyApplicationFormRequest,
): Promise<ElderlyApplicationCreated> {
  return apiHttp
    .post<ElderlyApplicationCreated>('/elderly/applications', request)
    .then((response) => response.data)
}

/** 更新登记/补全。 */
export function updateElderlyApplication(
  id: number | string,
  request: ElderlyApplicationFormRequest,
): Promise<void> {
  return apiHttp
    .put(`/elderly/applications/${encodeURIComponent(String(id))}`, request)
    .then(() => undefined)
}

/** 确认登记生效。 */
export function confirmElderlyApplication(id: number | string): Promise<void> {
  return apiHttp
    .post(`/elderly/applications/${encodeURIComponent(String(id))}/confirm`)
    .then(() => undefined)
}

/** 删除草稿。 */
export function deleteElderlyApplication(id: number | string): Promise<void> {
  return apiHttp
    .delete(`/elderly/applications/${encodeURIComponent(String(id))}`)
    .then(() => undefined)
}

/** 当前账号经办机构区划（高龄表单默认地址与户籍锁定）。 */
export function getElderlyOperatorAddress(): Promise<ElderlyOperatorAddress> {
  return apiHttp
    .get<ElderlyOperatorAddress>('/elderly/operator-address')
    .then((response) => response.data)
}

/** 停发办理页头统计（在享人数 / 本月停发人次）。 */
export function getElderlyStopStats(): Promise<ElderlyStopStats> {
  return apiHttp.get<ElderlyStopStats>('/elderly/stops/stats').then((response) => response.data)
}

/** 当前库在享人员停发。 */
export function stopElderlyApplication(
  id: number | string,
  request: ElderlyStopRequest,
): Promise<void> {
  return apiHttp
    .post(`/elderly/applications/${encodeURIComponent(String(id))}/stop`, request)
    .then(() => undefined)
}

/** 导入名册建档并停发。 */
export function stopElderlyFromHistory(
  historyId: number | string,
  request: ElderlyStopRequest,
): Promise<{ id: number }> {
  return apiHttp
    .post<{ id: number }>(
      `/elderly/stops/from-history/${encodeURIComponent(String(historyId))}`,
      request,
    )
    .then((response) => response.data)
}

/** 身份证 OCR。 */
export function ocrElderlyIdCard(file: File): Promise<ElderlyIdCardOcrResult> {
  const form = new FormData()
  form.append('file', file)
  return apiHttp
    .post<ElderlyIdCardOcrResult>('/elderly/ocr/idcard', form, { timeout: 120_000 })
    .then((response) => response.data)
}
