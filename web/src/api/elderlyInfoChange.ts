import type {
  InfoChangeCreateRequest,
  InfoChangeCreated,
  InfoChangeDetail,
  InfoChangeFieldKey,
  InfoChangeApproveRequest,
  InfoChangeListParams,
  InfoChangeListResponse,
  InfoChangeRecord,
  InfoChangeRejectRequest,
} from '@/types/elderlyInfoChange'
import { INFO_CHANGE_FIELD_MAP } from '@/types/elderlyInfoChange'
import { apiHttp } from './http'

/**
 * 高龄年度认证·信息变更接口（合同 v1 第 5 节）。
 *
 * 权限：查询 ELDERLY_VIEW；提交/审批 ELDERLY_EDIT。
 * 服务端按登录用户数据范围过滤，前端不传 userId/orgId。
 */

const INFO_CHANGES = '/elderly/certifications/info-changes'

function fieldLabel(fieldKey: string): string {
  return INFO_CHANGE_FIELD_MAP[fieldKey as InfoChangeFieldKey]?.fieldLabel ?? fieldKey
}

function normalizeDetail(detail: InfoChangeDetail): InfoChangeDetail {
  return {
    ...detail,
    fieldLabel: detail.fieldLabel?.trim() || fieldLabel(detail.fieldKey),
    oldValue: detail.oldValue ?? '',
    newValue: detail.newValue ?? '',
  }
}

function normalizeRecord(record: InfoChangeRecord): InfoChangeRecord {
  return {
    ...record,
    effectiveMonth: record.effectiveMonth ?? null,
    appliedAt: record.appliedAt ?? null,
    reviewedAt: record.reviewedAt ?? null,
    reviewOpinion: record.reviewOpinion ?? '',
    details: (record.details ?? []).map(normalizeDetail),
  }
}

/** GET /api/elderly/certifications/info-changes?status=&page=&pageSize=&keyword= */
export function listInfoChanges(
  params: InfoChangeListParams = {},
): Promise<InfoChangeListResponse> {
  return apiHttp
    .get<InfoChangeListResponse>(INFO_CHANGES, { params })
    .then((response) => {
      const data = response.data
      return {
        items: (data.items ?? []).map(normalizeRecord),
        pageIndex: data.pageIndex ?? 1,
        pageSize: data.pageSize ?? params.pageSize ?? 20,
        totalCount: data.totalCount ?? 0,
        totalPages: data.totalPages ?? 0,
      }
    })
}

/** GET /api/elderly/certifications/tasks/{taskId}/info-changes */
export function listTaskInfoChanges(taskId: number | string): Promise<InfoChangeRecord[]> {
  return apiHttp
    .get<InfoChangeRecord[] | { items?: InfoChangeRecord[] }>(
      `/elderly/certifications/tasks/${encodeURIComponent(String(taskId))}/info-changes`,
    )
    .then((response) => {
      const data = response.data
      const list = Array.isArray(data) ? data : (data?.items ?? [])
      return list.map(normalizeRecord)
    })
}

/**
 * POST /api/elderly/certifications/tasks/{taskId}/info-changes
 * 同人同字段已有 Pending 时由服务端作废旧单。
 */
export function createTaskInfoChange(
  taskId: number | string,
  request: InfoChangeCreateRequest,
): Promise<InfoChangeCreated | null> {
  return apiHttp
    .post<InfoChangeCreated>(
      `/elderly/certifications/tasks/${encodeURIComponent(String(taskId))}/info-changes`,
      request,
    )
    .then((response) => response.data ?? null)
}

/** POST /api/elderly/certifications/info-changes/{id}/approve */
export function approveInfoChange(
  id: number | string,
  request: InfoChangeApproveRequest,
): Promise<void> {
  return apiHttp
    .post(
      `${INFO_CHANGES}/${encodeURIComponent(String(id))}/approve`,
      request,
    )
    .then(() => undefined)
}

/** POST /api/elderly/certifications/info-changes/{id}/reject */
export function rejectInfoChange(
  id: number | string,
  request: InfoChangeRejectRequest,
): Promise<void> {
  return apiHttp
    .post(
      `${INFO_CHANGES}/${encodeURIComponent(String(id))}/reject`,
      request,
    )
    .then(() => undefined)
}
