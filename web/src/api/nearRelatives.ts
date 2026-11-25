import type {
  NearRelativeArchiveFill,
  NearRelativeEntry,
  NearRelativeListResponse,
  NearRelativeSavePayload,
  NearRelativeSaveResponse,
  NearRelativeStaffLookup,
} from '@/types/nearRelatives'
import { apiHttp } from './http'

/** 近亲属备案接口（统一权限 NEAR_RELATIVE_MANAGE，服务端强制校验）。 */

export interface ListNearRelativesParams {
  town?: string
  keyword?: string
  page?: number
  pageSize?: number
}

export interface FillNearRelativeArchiveParams {
  idCard?: string
  applicationId?: number
  excludeStaffId?: number
}

const NEAR_RELATIVE_BASE = '/social-assistance/near-relatives'

function nearRelativeUrl(id: number | string): string {
  return `${NEAR_RELATIVE_BASE}/${encodeURIComponent(String(id))}`
}

/** 备案名册（乡镇 + 关键词筛选、分页）。 */
export function listNearRelatives(
  params: ListNearRelativesParams = {},
): Promise<NearRelativeListResponse> {
  return apiHttp
    .get<NearRelativeListResponse>(NEAR_RELATIVE_BASE, { params })
    .then((response) => response.data)
}

/** 单条备案详情。 */
export function getNearRelative(id: number | string): Promise<NearRelativeEntry> {
  return apiHttp.get<NearRelativeEntry>(nearRelativeUrl(id)).then((response) => response.data)
}

/** 新增备案；返回新备案 id。 */
export function createNearRelative(
  payload: NearRelativeSavePayload,
): Promise<NearRelativeSaveResponse> {
  return apiHttp
    .post<NearRelativeSaveResponse>(NEAR_RELATIVE_BASE, payload)
    .then((response) => response.data)
}

/** 编辑备案；staff.updatedAt 作为版本号。 */
export function updateNearRelative(
  id: number | string,
  payload: NearRelativeSavePayload,
): Promise<NearRelativeSaveResponse> {
  return apiHttp
    .put<NearRelativeSaveResponse>(nearRelativeUrl(id), payload)
    .then((response) => response.data)
}

/** 删除备案（软删工作人员 + 对象明细）。 */
export function deleteNearRelative(id: number | string): Promise<void> {
  return apiHttp.delete(nearRelativeUrl(id)).then(() => undefined)
}

/** 按工作人员身份证带出工作信息；命中既有备案时返回 existing。 */
export function lookupNearRelativeStaff(idCard: string): Promise<NearRelativeStaffLookup> {
  return apiHttp
    .get<NearRelativeStaffLookup>(`${NEAR_RELATIVE_BASE}/staff-lookup`, {
      params: { idCard },
    })
    .then((response) => response.data)
}

/** 按救助对象身份证或申请 ID 从档案带出对象行；未命中返回 null。 */
export function fillNearRelativeArchive(
  params: FillNearRelativeArchiveParams,
): Promise<NearRelativeArchiveFill | null> {
  return apiHttp
    .get<NearRelativeArchiveFill | null>(`${NEAR_RELATIVE_BASE}/archive-fill`, { params })
    .then((response) => response.data ?? null)
}
