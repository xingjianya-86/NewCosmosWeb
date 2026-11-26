import type {
  BackupFile,
  ClientVersionList,
  DataCenterJob,
  DataCenterOverview,
  SchemaFixResult,
  SchemaStatus,
  SoftDeleteCensus,
  StartJobResponse,
} from '@/types/dataCenter'
import { apiHttp } from './http'

const BASE = '/system/data-center'

export function getDataCenterOverview(): Promise<DataCenterOverview> {
  return apiHttp.get<DataCenterOverview>(`${BASE}/overview`).then((response) => response.data)
}

export function getSchemaStatus(): Promise<SchemaStatus> {
  return apiHttp.get<SchemaStatus>(`${BASE}/schema`).then((response) => response.data)
}

export function initializeSchema(): Promise<StartJobResponse> {
  return apiHttp
    .post<StartJobResponse>(`${BASE}/schema/initialize`)
    .then((response) => response.data)
}

export function fixSchema(): Promise<StartJobResponse> {
  return apiHttp
    .post<StartJobResponse>(`${BASE}/schema/fix`)
    .then((response) => response.data)
}

export function listBackups(): Promise<BackupFile[]> {
  return apiHttp.get<BackupFile[]>(`${BASE}/backups`).then((response) => response.data)
}

export function startBackup(): Promise<StartJobResponse> {
  return apiHttp.post<StartJobResponse>(`${BASE}/backups`).then((response) => response.data)
}

export function downloadBackupUrl(fileName: string): string {
  return `${apiHttp.defaults.baseURL}${BASE}/backups/${encodeURIComponent(fileName)}/download`
}

export function downloadBackup(fileName: string): Promise<Blob> {
  return apiHttp
    .get<Blob>(`${BASE}/backups/${encodeURIComponent(fileName)}/download`, {
      responseType: 'blob',
      timeout: 300_000,
    })
    .then((response) => response.data)
}

export function restoreBackup(
  fileName: string,
  confirmFileName: string,
): Promise<StartJobResponse> {
  return apiHttp
    .post<StartJobResponse>(`${BASE}/backups/${encodeURIComponent(fileName)}/restore`, {
      confirmFileName,
    })
    .then((response) => response.data)
}

export function uploadBackup(file: File): Promise<BackupFile> {
  const form = new FormData()
  form.append('file', file)
  return apiHttp
    .post<BackupFile>(`${BASE}/backups/upload`, form, { timeout: 300_000 })
    .then((response) => response.data)
}

export function cleanupLogs(): Promise<StartJobResponse> {
  return apiHttp
    .post<StartJobResponse>(`${BASE}/maintenance/logs`)
    .then((response) => response.data)
}

export function detectSoftDelete(): Promise<SoftDeleteCensus> {
  return apiHttp
    .get<SoftDeleteCensus>(`${BASE}/soft-delete`)
    .then((response) => response.data)
}

export function cleanupSoftDeleted(): Promise<StartJobResponse> {
  return apiHttp
    .post<StartJobResponse>(`${BASE}/soft-delete/cleanup`)
    .then((response) => response.data)
}

export function listClientVersions(): Promise<ClientVersionList> {
  return apiHttp.get<ClientVersionList>(`${BASE}/clients`).then((response) => response.data)
}

export function getDataCenterJob(jobId: string): Promise<DataCenterJob> {
  return apiHttp
    .get<DataCenterJob>(`${BASE}/jobs/${encodeURIComponent(jobId)}`)
    .then((response) => response.data)
}

export function cancelDataCenterJob(jobId: string): Promise<void> {
  return apiHttp
    .post(`${BASE}/jobs/${encodeURIComponent(jobId)}/cancel`)
    .then(() => undefined)
}

export type { SchemaFixResult }
