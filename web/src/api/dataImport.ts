import type {
  ImportJobStarted,
  ImportJobSummary,
  ImportPreviewResult,
  ImportRequestInput,
  ImportTypeOption,
} from '@/types/dataImport'
import { apiHttp } from './http'

const BASE = '/system/import'

export function listImportTypes(): Promise<ImportTypeOption[]> {
  return apiHttp.get<ImportTypeOption[]>(`${BASE}/types`).then((response) => response.data ?? [])
}

export function downloadImportTemplate(importType: string): Promise<Blob> {
  return apiHttp
    .get<Blob>(`${BASE}/templates/${encodeURIComponent(importType)}`, {
      responseType: 'blob',
      timeout: 120_000,
    })
    .then((response) => response.data)
}

function buildForm(input: ImportRequestInput): FormData {
  const form = new FormData()
  form.append('importType', input.importType)
  if (input.familyFile) form.append('familyFile', input.familyFile)
  if (input.personFile) form.append('personFile', input.personFile)
  for (const file of input.files ?? []) form.append('files', file)
  if (input.year) form.append('year', String(input.year))
  if (input.rangeStart) form.append('rangeStart', String(input.rangeStart))
  if (input.rangeEnd) form.append('rangeEnd', String(input.rangeEnd))
  if (input.clearBeforeImport) form.append('clearBeforeImport', 'true')
  return form
}

export function previewImport(input: ImportRequestInput): Promise<ImportPreviewResult> {
  return apiHttp
    .post<ImportPreviewResult>(`${BASE}/preview`, buildForm(input), {
      timeout: 300_000,
    })
    .then((response) => response.data)
}

export function startImport(input: ImportRequestInput): Promise<ImportJobStarted> {
  return apiHttp
    .post<ImportJobStarted>(`${BASE}/run`, buildForm(input), {
      timeout: 300_000,
    })
    .then((response) => response.data)
}

export function getImportJobSummary(jobId: string): Promise<ImportJobSummary> {
  return apiHttp
    .get<ImportJobSummary>(`${BASE}/jobs/${encodeURIComponent(jobId)}`)
    .then((response) => response.data)
}

export function downloadImportLog(jobId: string): Promise<Blob> {
  return apiHttp
    .get<Blob>(`${BASE}/jobs/${encodeURIComponent(jobId)}/log`, {
      responseType: 'blob',
      timeout: 120_000,
    })
    .then((response) => response.data)
}
