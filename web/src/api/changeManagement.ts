import type {
  ChangeHistoryResponse,
  ChangeManagementDetail,
  ChangeManagementFilters,
  ChangeManagementListResponse,
  ChangeOperationResult,
  GraceConfirmRequest,
  GraceConfirmResult,
  HeadChangeRequest,
  HouseholdDeathRequest,
  ImportedArchiveSearchResponse,
  MemberChangeRequest,
  MigrateImportedArchiveResponse,
  VerifyStopRequest,
} from '@/types/changeManagement'
import { apiHttp } from './http'

const BASE = '/social-assistance/change-management'

export interface ListChangeManagementParams {
  page?: number
  pageSize?: number
  keyword?: string
  town?: string
  classification?: string
}

/** 在享对象分页列表（CHANGE_VIEW + 申请数据范围）。 */
export function listChangeManagement(
  params: ListChangeManagementParams = {},
): Promise<ChangeManagementListResponse> {
  return apiHttp
    .get<ChangeManagementListResponse>(BASE, { params })
    .then((response) => response.data)
}

/** 筛选与表单字典选项（乡镇/分类来自当前数据范围；原因字来自 nc_dict_items）。 */
export function getChangeManagementFilters(): Promise<ChangeManagementFilters> {
  return apiHttp
    .get<ChangeManagementFilters>(`${BASE}/filters`)
    .then((response) => response.data)
}

/** 历史导入库待建档检索（当前库无结果时的第二来源，权限 CHANGE_VIEW）。 */
export function searchImportedArchives(keyword: string): Promise<ImportedArchiveSearchResponse> {
  return apiHttp
    .get<ImportedArchiveSearchResponse>(`${BASE}/imported-search`, {
      params: { keyword },
    })
    .then((response) => response.data)
}

/** 导入库建档迁移（幂等；权限 CHANGE_MANAGE）。 */
export function migrateImportedArchive(
  sourceTable: string,
  sourceId: number | string,
  classificationCode?: string,
): Promise<MigrateImportedArchiveResponse> {
  return apiHttp
    .post<MigrateImportedArchiveResponse>(
      `${BASE}/imported/${encodeURIComponent(sourceTable)}/${encodeURIComponent(String(sourceId))}/migrate`,
      { classificationCode: classificationCode ?? null },
    )
    .then((response) => response.data)
}

/** 选中户详情（成员、分类、保障金、渐退期、本月已办提示）。 */export function getChangeManagementDetail(
  applicationId: number | string,
): Promise<ChangeManagementDetail> {
  return apiHttp
    .get<ChangeManagementDetail>(`${BASE}/${encodeURIComponent(String(applicationId))}`)
    .then((response) => response.data)
}

/** 变更历史（按变更时间倒序，最多 200 条）。 */
export function getChangeManagementHistory(
  applicationId: number | string,
): Promise<ChangeHistoryResponse> {
  return apiHttp
    .get<ChangeHistoryResponse>(
      `${BASE}/${encodeURIComponent(String(applicationId))}/history`,
    )
    .then((response) => response.data)
}

/** 户主变更（停旧建新）。 */
export function submitHeadChange(
  applicationId: number | string,
  request: HeadChangeRequest,
): Promise<ChangeOperationResult> {
  return apiHttp
    .post<ChangeOperationResult>(
      `${BASE}/${encodeURIComponent(String(applicationId))}/head-change`,
      request,
    )
    .then((response) => response.data)
}

/** 家庭成员变更（新增/减员/属性变更，逐人原因）。 */
export function submitMemberChange(
  applicationId: number | string,
  request: MemberChangeRequest,
): Promise<ChangeOperationResult> {
  return apiHttp
    .post<ChangeOperationResult>(
      `${BASE}/${encodeURIComponent(String(applicationId))}/member-change`,
      request,
    )
    .then((response) => response.data)
}

/** 户主死亡变更（单人户由服务端按实际成员自动走整户停止）。 */
export function submitHouseholdDeath(
  applicationId: number | string,
  request: HouseholdDeathRequest,
): Promise<ChangeOperationResult> {
  return apiHttp
    .post<ChangeOperationResult>(
      `${BASE}/${encodeURIComponent(String(applicationId))}/household-death`,
      request,
    )
    .then((response) => response.data)
}

/** 渐退期确认/调整月数（仅对当前在效渐退期）。 */
export function submitGraceConfirm(
  applicationId: number | string,
  request: GraceConfirmRequest,
): Promise<GraceConfirmResult> {
  return apiHttp
    .post<GraceConfirmResult>(
      `${BASE}/${encodeURIComponent(String(applicationId))}/grace-confirm`,
      request,
    )
    .then((response) => response.data)
}

/** 核验停止（拒绝配合核验工作，只停不建新档）。 */
export function submitVerifyStop(
  applicationId: number | string,
  request: VerifyStopRequest = {},
): Promise<ChangeOperationResult> {
  return apiHttp
    .post<ChangeOperationResult>(
      `${BASE}/${encodeURIComponent(String(applicationId))}/verify-stop`,
      request,
    )
    .then((response) => response.data)
}
