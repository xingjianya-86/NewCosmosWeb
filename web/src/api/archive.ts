import type {
  ArchiveCurrentDetail,
  ArchiveProofHistoryItem,
  ArchiveProofHistoryParams,
  ArchiveProofJobInput,
  ArchiveProofJobResult,
  ArchiveProofTemplatesResponse,
  ArchiveProofUnitInput,
  ArchiveSearchResponse,
  ArchiveStats,
  ImportedFamilyDetail,
} from '@/types/archive'
import { apiHttp } from './http'

/** 页头统计：归档总数 / 当月归档。 */
export function getArchiveStats(): Promise<ArchiveStats> {
  return apiHttp.get<ArchiveStats>('/archives/stats').then((response) => response.data)
}

/** 按姓名或身份证检索当前库 + 历史导入库档案。 */
export function searchArchives(keyword: string): Promise<ArchiveSearchResponse> {
  return apiHttp
    .get<ArchiveSearchResponse>('/archives/search', { params: { keyword } })
    .then((response) => response.data)
}

/** 当前库档案详情（摘要 + 完整申请表单）。 */
export function getArchiveCurrent(
  applicationId: number | string,
): Promise<ArchiveCurrentDetail> {
  return apiHttp
    .get<ArchiveCurrentDetail>(`/archives/current/${encodeURIComponent(String(applicationId))}`)
    .then((response) => response.data)
}

export interface GetArchiveImportedParams {
  source: number
  sourceTable: string
  sourceId: number | string
}

/** 历史导入库家庭详情（按人群口径返回字段）。 */
export function getArchiveImported(
  params: GetArchiveImportedParams,
): Promise<ImportedFamilyDetail> {
  return apiHttp
    .get<ImportedFamilyDetail>('/archives/imported', { params })
    .then((response) => response.data)
}

/** 证明模板：通配模板 + 单位专属模板。 */
export function getArchiveProofTemplates(): Promise<ArchiveProofTemplatesResponse> {
  return apiHttp
    .get<ArchiveProofTemplatesResponse>('/archives/proof/templates')
    .then((response) => response.data)
}

function buildUnitFormData(input: ArchiveProofUnitInput): FormData {
  const form = new FormData()
  form.append('unitName', input.unitName)
  form.append('fileType', input.fileType)
  if (input.file) form.append('file', input.file)
  if (input.configJson?.trim()) form.append('configJson', input.configJson.trim())
  return form
}

/** 新增单位模板（上传模板文件，后端自动建模板并建立单位映射）。 */
export function createArchiveProofUnit(
  input: ArchiveProofUnitInput,
): Promise<{ id: number }> {
  return apiHttp
    .post<{ id: number }>('/archives/proof/units', buildUnitFormData(input))
    .then((response) => response.data)
}

/** 更新单位模板（file 可空，留空保留原模板）。 */
export function updateArchiveProofUnit(
  id: number | string,
  input: ArchiveProofUnitInput,
): Promise<void> {
  return apiHttp
    .put(
      `/archives/proof/units/${encodeURIComponent(String(id))}`,
      buildUnitFormData(input),
    )
    .then(() => undefined)
}

/** 删除单位模板映射（仅解除映射，模板文件保留）。 */
export function deleteArchiveProofUnit(id: number | string): Promise<void> {
  return apiHttp
    .delete(`/archives/proof/units/${encodeURIComponent(String(id))}`)
    .then(() => undefined)
}

/**
 * 创建证明打印任务（预览 / 打印 / 保存）。
 *
 * 实际 PDF 生成、打印与落盘由本机 PC Agent 领取执行；Web 不内嵌 PDF。
 */
export function createArchiveProofJob(
  input: ArchiveProofJobInput,
): Promise<ArchiveProofJobResult> {
  return apiHttp
    .post<ArchiveProofJobResult>('/archives/proof/jobs', input)
    .then((response) => response.data)
}

/** 该档案的证明开具历史。 */
export function getArchiveProofHistory(
  params: ArchiveProofHistoryParams,
): Promise<ArchiveProofHistoryItem[]> {
  return apiHttp
    .get<ArchiveProofHistoryItem[]>('/archives/proof/history', { params })
    .then((response) => response.data ?? [])
}
