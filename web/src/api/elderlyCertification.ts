import type {
  CertificationCampaign,
  CertificationCampaignCreated,
  CertificationCampaignCreateRequest,
  CertificationEvidence,
  CertificationImportApplyRequest,
  CertificationImportApplyResponse,
  CertificationImportPreview,
  CertificationResultUpdateRequest,
  CertificationRosterGenerated,
  CertificationStats,
  CertificationStopDecisionRequest,
  CertificationStopPreviewParams,
  CertificationStopPreviewResponse,
  CertificationStopRequest,
  CertificationTaskDetail,
  CertificationTaskListParams,
  CertificationTaskListResponse,
} from '@/types/elderlyCertification'
import type { CertificationReportFormKey } from '@/types/elderlyCertificationReport'
import { API_PREFIX, apiHttp } from './http'

/* ── 2.1 批次 ───────────────────────────────────────────────────── */

/** 当前用户可见批次，按年度/轮次倒序。 */
export function listCertificationCampaigns(): Promise<CertificationCampaign[]> {
  return apiHttp
    .get<CertificationCampaign[]>('/elderly/certifications/campaigns')
    .then((response) => response.data ?? [])
}

/** 新建批次；同年度同轮次重复返回 VALIDATION_FAILED。 */
export function createCertificationCampaign(
  request: CertificationCampaignCreateRequest,
): Promise<CertificationCampaignCreated> {
  return apiHttp
    .post<CertificationCampaignCreated>('/elderly/certifications/campaigns', request)
    .then((response) => response.data)
}

/** 生成/刷新应认证名单快照；幂等，只补新增人员。 */
export function generateCertificationRoster(
  campaignId: number | string,
): Promise<CertificationRosterGenerated> {
  return apiHttp
    .post<CertificationRosterGenerated>(
      `/elderly/certifications/campaigns/${encodeURIComponent(String(campaignId))}/roster`,
    )
    .then((response) => response.data)
}

export function getCertificationStats(
  campaignId: number | string,
): Promise<CertificationStats> {
  return apiHttp
    .get<CertificationStats>(
      `/elderly/certifications/campaigns/${encodeURIComponent(String(campaignId))}/stats`,
    )
    .then((response) => response.data)
}

/* ── 2.2 名单/任务 ──────────────────────────────────────────────── */

export function listCertificationTasks(
  campaignId: number | string,
  params: CertificationTaskListParams = {},
): Promise<CertificationTaskListResponse> {
  return apiHttp
    .get<CertificationTaskListResponse>(
      `/elderly/certifications/campaigns/${encodeURIComponent(String(campaignId))}/tasks`,
      { params },
    )
    .then((response) => response.data)
}

export function getCertificationTask(
  taskId: number | string,
): Promise<CertificationTaskDetail> {
  return apiHttp
    .get<CertificationTaskDetail>(
      `/elderly/certifications/tasks/${encodeURIComponent(String(taskId))}`,
    )
    .then((response) => response.data)
}

/** 更新单条核查结果；成功返回 204。 */
export function updateCertificationTaskResult(
  taskId: number | string,
  request: CertificationResultUpdateRequest,
): Promise<void> {
  return apiHttp
    .put(
      `/elderly/certifications/tasks/${encodeURIComponent(String(taskId))}/result`,
      request,
    )
    .then(() => undefined)
}

export function listCertificationEvidence(
  taskId: number | string,
): Promise<CertificationEvidence[]> {
  return apiHttp
    .get<CertificationEvidence[]>(
      `/elderly/certifications/tasks/${encodeURIComponent(String(taskId))}/evidence`,
    )
    .then((response) => response.data ?? [])
}

/**
 * 证据图片内容（Blob）。接口要求 Bearer 鉴权，<img src> 无法附带认证头，
 * 页面应使用本函数取回 Blob 后再 URL.createObjectURL。
 */
export function getCertificationEvidenceContent(
  evidenceId: number | string,
  signal?: AbortSignal,
): Promise<Blob> {
  return apiHttp
    .get<Blob>(
      `/elderly/certifications/evidence/${encodeURIComponent(String(evidenceId))}/content`,
      { responseType: 'blob', signal, timeout: 60_000 },
    )
    .then((response) => response.data)
}

/**
 * 证据内容接口地址（合同 2.2）。用于下载/新窗口打开等场景；
 * 需要认证头的页面展示请改用 getCertificationEvidenceContent。
 */
export function getEvidenceContentUrl(evidenceId: number | string): string {
  return `${API_PREFIX}/elderly/certifications/evidence/${encodeURIComponent(String(evidenceId))}/content`
}

/* ── 2.3 批量导入（Excel + ZIP） ───────────────────────────────── */

/** multipart：excel + zip + campaignId；不手动设置 Content-Type。 */
export function previewCertificationImport(
  campaignId: number | string,
  excel: File,
  zip: File,
): Promise<CertificationImportPreview> {
  const form = new FormData()
  form.append('excel', excel)
  form.append('zip', zip)
  form.append('campaignId', String(campaignId))
  return apiHttp
    .post<CertificationImportPreview>('/elderly/certifications/import/preview', form, {
      timeout: 600_000,
    })
    .then((response) => ({
      ...response.data,
      rows: response.data.rows ?? [],
      unmatchedEntries: response.data.unmatchedEntries ?? [],
      warnings: response.data.warnings ?? [],
      errors: response.data.errors ?? [],
    }))
}

export function applyCertificationImport(
  request: CertificationImportApplyRequest,
): Promise<CertificationImportApplyResponse> {
  return apiHttp
    .post<CertificationImportApplyResponse>(
      '/elderly/certifications/import/apply',
      request,
      { timeout: 600_000 },
    )
    .then((response) => response.data)
}

/* ── 2.4 停发预览 ──────────────────────────────────────────────── */

export function getStopPreview(
  campaignId: number | string,
  params: CertificationStopPreviewParams = {},
): Promise<CertificationStopPreviewResponse> {
  return apiHttp
    .get<CertificationStopPreviewResponse>(
      `/elderly/certifications/campaigns/${encodeURIComponent(String(campaignId))}/stop-preview`,
      { params },
    )
    .then((response) => response.data)
}

/** 逐人停发（不做批量）；成功后服务端回写 stopDecision=Approved。 */
export function stopCertificationTask(
  taskId: number | string,
  request: CertificationStopRequest,
): Promise<void> {
  return apiHttp
    .post(
      `/elderly/certifications/tasks/${encodeURIComponent(String(taskId))}/stop`,
      request,
    )
    .then(() => undefined)
}

/** 暂缓 / 排除 / 重新进入待停发。 */
export function setCertificationStopDecision(
  taskId: number | string,
  request: CertificationStopDecisionRequest,
): Promise<void> {
  return apiHttp
    .post(
      `/elderly/certifications/tasks/${encodeURIComponent(String(taskId))}/stop-decision`,
      request,
    )
    .then(() => undefined)
}

/* ── 2.5 报表导出（Excel / PDF） ───────────────────────────────── */

/**
 * 下载年度认证 Excel 报表（合同 5）。
 *
 * 必须走 apiHttp 以携带 Bearer；responseType 为 blob，页面拿到 Blob 后再
 * URL.createObjectURL + <a download>，不能用 window.open 拼 URL。
 */
export function downloadCertificationReport(
  campaignId: number | string,
  formKey: CertificationReportFormKey,
): Promise<Blob> {
  return downloadReportFile(campaignId, formKey, 'xlsx')
}

/** 下载年度认证 PDF 报表（服务端直出，含内置中文字体）。 */
export function downloadCertificationReportPdf(
  campaignId: number | string,
  formKey: CertificationReportFormKey,
): Promise<Blob> {
  return downloadReportFile(campaignId, formKey, 'pdf')
}

function downloadReportFile(
  campaignId: number | string,
  formKey: CertificationReportFormKey,
  extension: 'xlsx' | 'pdf',
): Promise<Blob> {
  return apiHttp
    .get<Blob>(
      `/elderly/certifications/campaigns/${encodeURIComponent(String(campaignId))}/reports/${encodeURIComponent(formKey)}.${extension}`,
      { responseType: 'blob', timeout: 300_000 },
    )
    .then((response) => response.data)
}
