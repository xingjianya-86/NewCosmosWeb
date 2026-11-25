import type {
  TempReliefAnnualCheckResponse,
  TempReliefCandidate,
  TempReliefCandidateListResponse,
  TempReliefChangeDateResponse,
  TempReliefCreatedResponse,
  TempReliefDetail,
  TempReliefFamilyStatusResponse,
  TempReliefFormDefaults,
  TempReliefIcd10Response,
  TempReliefHukouTypeResponse,
  TempReliefInsuredIdentityResponse,
  TempReliefListResponse,
  TempReliefOcrResponse,
  TempReliefPaymentChannel,
  TempReliefPhotoListResponse,
  TempReliefPhotoUploadResponse,
  TempReliefPolicySnapshotResponse,
  TempReliefPopulationStatus,
  TempReliefPublicizePreview,
  TempReliefPrintDataResponse,
  TempReliefReasonRequest,
  TempReliefRejectRequest,
  TempReliefReliefType,
  TempReliefReportUnitPhoneResponse,
  TempReliefSaveRequest,
  TempReliefSmallAmountLevelsResponse,
  TempReliefSmallTiersResponse,
  TempReliefSummary,
} from '@/types/tempRelief'
import {
  createPrintJob,
  listDocumentTemplates,
  type CreatedPrintJob,
  type DocumentTemplate,
  type PrintJobMode,
} from './archivePrint'
import { apiHttp } from './http'

export type { CreatedPrintJob, DocumentTemplate, PrintJobMode }

const BASE = '/social-assistance/temp-relief'

/** 临时救助推送打印的业务类型键，与后端 PrintBusinessTypes.TempRelief 一致。 */
export const TEMP_RELIEF_PRINT_BUSINESS_TYPE = 'TempRelief'

export interface ListTempReliefParams {
  page?: number
  pageSize?: number
  keyword?: string
  /** 空 = 全部；支持单状态，也支持逗号分隔（如 Draft,Returned）。 */
  status?: string
  populationStatus?: TempReliefPopulationStatus
  reliefType?: TempReliefReliefType
  paymentChannel?: TempReliefPaymentChannel
  /** 乡镇 / 街道模糊匹配。 */
  town?: string
  /** 高级筛选：业务日期年份。 */
  year?: number
  /** 高级筛选：业务日期月份（1-12，需同时传 year）。 */
  month?: number
  /** 仅看逾期件。 */
  overdueOnly?: boolean
}

export interface ListArchivedTempReliefParams {
  page?: number
  pageSize?: number
  keyword?: string
  /** 补打年份；与 month 同时传时按该月筛选。 */
  year?: number
  /** 补打月份（1-12，需同时传 year）。 */
  month?: number
}

/** 临时救助分页列表（TEMP_VIEW）。 */
export function listTempReliefApplications(
  params: ListTempReliefParams = {},
): Promise<TempReliefListResponse> {
  return apiHttp
    .get<TempReliefListResponse>(BASE, { params })
    .then((response) => response.data)
}

/** 已完结档案分页（已确认 + 已终止 + 不予救助，供补打）。 */
export function getArchivedTempReliefApplications(
  params: ListArchivedTempReliefParams = {},
): Promise<TempReliefListResponse> {
  return apiHttp
    .get<TempReliefListResponse>(`${BASE}/archived`, { params })
    .then((response) => response.data)
}

/** 页头统计：待办 / 已提交 / 本年确认 / 逾期 / 本年金额。 */
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

/** 打印预览数据（模板 FieldKeys + 成员行 + 打印分类）。 */
export function getTempReliefPrintData(id: number | string): Promise<TempReliefPrintDataResponse> {
  return apiHttp
    .get<TempReliefPrintDataResponse>(`${BASE}/${encodeURIComponent(String(id))}/print-data`)
    .then((response) => response.data)
}

/** 申请人候选人检索（导入台账 + 低保申请库）。 */
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

/** 按身份证自动判定「保障人群」身份（四类在册台账反查，人工不可改）。 */
export function getTempReliefInsuredIdentity(
  idCard: string,
): Promise<TempReliefInsuredIdentityResponse> {
  return apiHttp
    .get<TempReliefInsuredIdentityResponse>(`${BASE}/insured-identity`, { params: { idCard } })
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

/** 小额快救政策档位（50% / 75% / 100%）与档位上限。 */
export function getTempReliefSmallTiers(
  personCount?: number,
): Promise<TempReliefSmallTiersResponse> {
  return apiHttp
    .get<TempReliefSmallTiersResponse>(`${BASE}/small-tiers`, {
      params: personCount ? { personCount } : undefined,
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
/** 新建表单默认值：户籍地址与乡镇验收人（按当前用户机构带出）。 */
export function getTempReliefFormDefaults(): Promise<TempReliefFormDefaults> {
  return apiHttp
    .get<TempReliefFormDefaults>(`${BASE}/form-defaults`)
    .then((response) => response.data)
}

/** 申请日期对应的批次/公示预览（小额快救免公示）。 */
export function getTempReliefPublicizePreview(
  applyDate: string,
  reliefType: string,
): Promise<TempReliefPublicizePreview> {
  return apiHttp
    .get<TempReliefPublicizePreview>(`${BASE}/publicize-preview`, {
      params: { applyDate, reliefType },
    })
    .then((response) => response.data)
}

/** ICD-10 编码查询常见病名。 */
export function getTempReliefIcd10(code: string): Promise<TempReliefIcd10Response> {
  return apiHttp
    .get<TempReliefIcd10Response>(`${BASE}/icd10`, { params: { code } })
    .then((response) => response.data)
}

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

/** 更新草稿 / 退回补正。 */
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

/** 提交（草稿 / 退回补正 → 已提交，写受理时间作为时限起点）。 */
export function submitTempReliefApplication(id: number | string): Promise<void> {
  return apiHttp
    .post(`${BASE}/${encodeURIComponent(String(id))}/submit`)
    .then(() => undefined)
}

/** 退回补正（已提交 → 退回补正）。 */
export function returnTempReliefApplication(
  id: number | string,
  reasonOrRequest: string | TempReliefReasonRequest,
): Promise<void> {
  const reason =
    typeof reasonOrRequest === 'string' ? reasonOrRequest : reasonOrRequest.reason
  return apiHttp
    .post(`${BASE}/${encodeURIComponent(String(id))}/return`, { reason })
    .then(() => undefined)
}

/** 不予救助（草稿 / 已提交 → 不予救助），理由与告知信息落库。 */
export function rejectTempReliefApplication(
  id: number | string,
  reasonOrRequest: string | TempReliefRejectRequest,
): Promise<void> {
  const payload: TempReliefRejectRequest =
    typeof reasonOrRequest === 'string' ? { reason: reasonOrRequest } : reasonOrRequest
  return apiHttp
    .post(`${BASE}/${encodeURIComponent(String(id))}/reject`, payload)
    .then(() => undefined)
}

/** 确认生效（草稿 / 已提交 → 已确认；上级渠道同时置待备案）。 */
export function confirmTempReliefApplication(id: number | string): Promise<void> {
  return apiHttp
    .post(`${BASE}/${encodeURIComponent(String(id))}/confirm`)
    .then(() => undefined)
}

/** 登记备案（上级部门发放：已确认 + 待备案 → 已备案）。 */
export function fileTempReliefApplication(id: number | string): Promise<void> {
  return apiHttp
    .post(`${BASE}/${encodeURIComponent(String(id))}/file`)
    .then(() => undefined)
}

/** 兼容旧调用名：与 fileTempReliefApplication 同义。 */
export const markTempReliefFiled = fileTempReliefApplication

/** 改期（仅草稿 / 退回补正；服务端重编号并返回新申请编号）。 */
export function changeTempReliefApplyDate(
  id: number | string,
  applyDate: string,
): Promise<TempReliefChangeDateResponse> {
  return apiHttp
    .post<TempReliefChangeDateResponse>(`${BASE}/${encodeURIComponent(String(id))}/change-date`, {
      applyDate,
    })
    .then((response) => response.data)
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

// ── 照片 ────────────────────────────────────────────────────────────────

/** 档案照片列表（按类别可选）。 */
export function listTempReliefPhotos(
  id: number | string,
  category?: string,
): Promise<TempReliefPhotoListResponse> {
  return apiHttp
    .get<TempReliefPhotoListResponse>(`${BASE}/${encodeURIComponent(String(id))}/photos`, {
      params: category ? { category } : undefined,
    })
    .then((response) => response.data)
}

/** 上传档案照片（multipart；category / photoName 走查询参数）。 */
export function uploadTempReliefPhoto(
  id: number | string,
  file: File,
  options: { category?: string; photoName?: string } = {},
): Promise<TempReliefPhotoUploadResponse> {
  const form = new FormData()
  form.append('file', file)
  return apiHttp
    .post<TempReliefPhotoUploadResponse>(
      `${BASE}/${encodeURIComponent(String(id))}/photos`,
      form,
      { params: { category: options.category, photoName: options.photoName } },
    )
    .then((response) => response.data)
}

/** 删除档案照片。 */
export function deleteTempReliefPhoto(
  id: number | string,
  photoId: number | string,
): Promise<void> {
  return apiHttp
    .delete(
      `${BASE}/${encodeURIComponent(String(id))}/photos/${encodeURIComponent(String(photoId))}`,
    )
    .then(() => undefined)
}

// ── 身份证 OCR ──────────────────────────────────────────────────────────

/**
 * 身份证 OCR。
 * - 传 id：POST /{id}/ocr（校验记录存在）
 * - 不传 id：POST /ocr/idcard（新建表单）
 */
export function recognizeTempReliefIdCard(
  file: File,
  options: { id?: number | string | null; category?: string } = {},
): Promise<TempReliefOcrResponse> {
  const form = new FormData()
  form.append('file', file)
  const id = options.id
  const url =
    id === undefined || id === null || id === ''
      ? `${BASE}/ocr/idcard`
      : `${BASE}/${encodeURIComponent(String(id))}/ocr`
  return apiHttp
    .post<TempReliefOcrResponse>(url, form, {
      params: { category: options.category ?? 'ID_CARD' },
    })
    .then((response) => response.data)
}

// ── 推送打印（复用统一 /print/jobs） ────────────────────────────────────

/** 临时救助可用打印模板（按业务域 TempRelief 筛选）。 */
export function listTempReliefPrintTemplates(): Promise<DocumentTemplate[]> {
  return listDocumentTemplates(TEMP_RELIEF_PRINT_BUSINESS_TYPE)
}

export interface CreateTempReliefPrintJobInput {
  /** 临时救助申请 id。 */
  applicationId: number
  templateId: number
  templateName?: string
  /** 打印机绑定 id；打印模式必填，预览模式可省略。 */
  bindingId?: number | null
  mode?: PrintJobMode
  copies?: number
  isDuplex?: boolean
}

/** 推送临时救助打印 / 补打任务（预览 / 打印共用同一份数据快照）。 */
export function createTempReliefPrintJob(
  input: CreateTempReliefPrintJobInput,
): Promise<CreatedPrintJob> {
  return createPrintJob({
    businessType: TEMP_RELIEF_PRINT_BUSINESS_TYPE,
    businessId: input.applicationId,
    templateId: input.templateId,
    templateName: input.templateName,
    bindingId: input.bindingId ?? undefined,
    copies: Math.min(99, Math.max(1, input.copies ?? 1)),
    isDuplex: input.isDuplex ?? false,
    mode: input.mode ?? 'print',
  })
}
