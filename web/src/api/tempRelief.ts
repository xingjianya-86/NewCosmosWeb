import type {
  TempReliefAnnualCheckResponse,
  TempReliefCandidate,
  TempReliefCandidateListResponse,
  TempReliefCreatedResponse,
  TempReliefDetail,
  TempReliefFamilyStatusResponse,
  TempReliefHukouTypeResponse,
  TempReliefListResponse,
  TempReliefPolicySnapshotResponse,
  TempReliefReportUnitPhoneResponse,
  TempReliefSaveRequest,
  TempReliefSmallAmountLevelsResponse,
  TempReliefSummary,
} from '@/types/tempRelief'
import { apiHttp } from './http'

const BASE = '/social-assistance/temp-relief'

export interface ListTempReliefParams {
  page?: number
  pageSize?: number
  keyword?: string
  /** 空 = 全部（Draft / Confirmed / Stopped）。 */
  status?: string
}

/** 临时救助分页列表（TEMP_VIEW）。 */
export function listTempReliefApplications(
  params: ListTempReliefParams = {},
): Promise<TempReliefListResponse> {
  return apiHttp
    .get<TempReliefListResponse>(BASE, { params })
    .then((response) => response.data)
}

/** 页头统计：办理中 / 已确认累计 / 已终止 / 本年确认人次与金额。 */
export function getTempReliefSummary(): Promise<TempReliefSummary> {
  return apiHttp
    .get<TempReliefSummary>(`${BASE}/summary`)
    .then((response) => response.data)
}

/** 完整档案（主表 + 成员 + 疾病 / 意外灾害 / 教育支出明细）。 */
export function getTempReliefApplication(id: number | string): Promise<TempReliefDetail> {
  return apiHttp
    .get<TempReliefDetail>(`${BASE}/${encodeURIComponent(String(id))}`)
    .then((response) => response.data)
}

/** 申请人候选人检索（5 个导入台账 + 低保申请库）。 */
export function searchTempReliefCandidates(keyword: string): Promise<TempReliefCandidate[]> {
  return apiHttp
    .get<TempReliefCandidateListResponse>(`${BASE}/candidates`, { params: { keyword } })
    .then((response) => response.data.items ?? [])
}

/** 候选人详情（含家庭成员快照）。 */
export function getTempReliefCandidate(
  sourceTable: string,
  sourceFamilyId: number,
): Promise<TempReliefCandidate> {
  return apiHttp
    .get<TempReliefCandidate>(
      `${BASE}/candidates/${encodeURIComponent(sourceTable)}/${sourceFamilyId}`,
    )
    .then((response) => response.data)
}

/** 按身份证跨来源解析户籍分类（农村 / 城镇，可能为空）。 */
export function resolveTempReliefHukouType(idCard: string): Promise<string> {
  return apiHttp
    .get<TempReliefHukouTypeResponse>(`${BASE}/hukou-type`, { params: { idCard } })
    .then((response) => response.data.hukouType ?? '')
}

/** 小额定额档位：户籍可解析 → 单档；未知 → 农村 + 城镇两档。 */
export function getTempReliefSmallAmountLevels(
  hukouType?: string,
): Promise<TempReliefSmallAmountLevelsResponse> {
  return apiHttp
    .get<TempReliefSmallAmountLevelsResponse>(`${BASE}/small-amount-levels`, {
      params: hukouType ? { hukouType } : undefined,
    })
    .then((response) => response.data)
}

/** 已享受 / 在申政策清单。 */
export function getTempReliefPolicySnapshot(idCard: string): Promise<string[]> {
  return apiHttp
    .get<TempReliefPolicySnapshotResponse>(`${BASE}/policy-snapshot`, { params: { idCard } })
    .then((response) => response.data.items ?? [])
}

/** 家庭成员健康状况汇总。 */
export function getTempReliefFamilyStatus(idCard: string): Promise<string> {
  return apiHttp
    .get<TempReliefFamilyStatusResponse>(`${BASE}/family-status`, { params: { idCard } })
    .then((response) => response.data.statusText ?? '')
}

/** 年度唯一校验（编辑时排除自身）。 */
export function checkTempReliefAnnual(
  idCard: string,
  applyYear: string,
  excludeId?: number | string,
): Promise<boolean> {
  return apiHttp
    .get<TempReliefAnnualCheckResponse>(`${BASE}/check-annual`, {
      params: { idCard, applyYear, excludeId },
    })
    .then((response) => response.data.exists)
}

/** 按填报单位取公示异议反馈电话。 */
export function getTempReliefReportUnitPhone(reportUnit: string): Promise<string> {
  return apiHttp
    .get<TempReliefReportUnitPhoneResponse>(`${BASE}/report-unit-phone`, {
      params: { reportUnit },
    })
    .then((response) => response.data.phone ?? '')
}

/** 新增（服务端强制草稿与年度唯一）。 */
export function createTempReliefApplication(
  request: TempReliefSaveRequest,
): Promise<TempReliefCreatedResponse> {
  return apiHttp
    .post<TempReliefCreatedResponse>(BASE, request)
    .then((response) => response.data)
}

/** 更新草稿。 */
export function updateTempReliefApplication(
  id: number | string,
  request: TempReliefSaveRequest,
): Promise<void> {
  return apiHttp
    .put(`${BASE}/${encodeURIComponent(String(id))}`, request)
    .then(() => undefined)
}

/** 删除草稿（软删除）。 */
export function deleteTempReliefApplication(id: number | string): Promise<void> {
  return apiHttp
    .delete(`${BASE}/${encodeURIComponent(String(id))}`)
    .then(() => undefined)
}

/** 确认生效（草稿 → 已确认）。 */
export function confirmTempReliefApplication(id: number | string): Promise<void> {
  return apiHttp
    .post(`${BASE}/${encodeURIComponent(String(id))}/confirm`)
    .then(() => undefined)
}

/** 终止（已确认 → 已终止）。 */
export function stopTempReliefApplication(
  id: number | string,
  reason: string,
): Promise<void> {
  return apiHttp
    .post(`${BASE}/${encodeURIComponent(String(id))}/stop`, { reason })
    .then(() => undefined)
}

/** 归档完成（幂等；记录存在即成功）。 */
export function completeTempReliefArchive(id: number | string): Promise<void> {
  return apiHttp
    .post(`${BASE}/${encodeURIComponent(String(id))}/complete`)
    .then(() => undefined)
}
