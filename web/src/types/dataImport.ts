/** 数据导入：与 /api/system/import 契约一致。 */

export interface ImportTypeOption {
  code: string
  name: string
  combined: boolean
  supportsScope: boolean
  hasTemplate: boolean
  familyPattern: string
  personPattern: string
}

export interface ImportColumnMapping {
  sourceColumn: string
  targetField: string
  isMapped: boolean
}

export interface ImportPreviewFile {
  fileName: string
  role: string
  sizeText: string
}

export interface ImportMatchedFiles {
  familyFileName: string | null
  personFileName: string | null
}

export interface ImportPreviewResult {
  success: boolean
  errorMessage: string | null
  familyTotalRows: number
  personTotalRows: number
  familyColumns: ImportColumnMapping[]
  personColumns: ImportColumnMapping[]
  familyRows: Record<string, unknown>[]
  personRows: Record<string, unknown>[]
  files: ImportPreviewFile[]
  matched: ImportMatchedFiles | null
}

export interface ImportJobStarted {
  jobId: string
}

export interface ImportRequestInput {
  importType: string
  familyFile?: File | null
  personFile?: File | null
  files?: File[]
  year?: number | null
  rangeStart?: number | null
  rangeEnd?: number | null
  clearBeforeImport?: boolean
}

/** 导入结果（来自任务汇总，失败时字段为零）。 */
export interface ImportUnlinkedPerson {
  name: string
  idCard: string
  headIdCard: string
  rowNumber: number
}

export interface ImportJobSummary {
  jobId: string
  importType: string
  importTypeName: string
  success: boolean | null
  canceled: boolean
  error: string | null
  familyCount: number
  personCount: number
  linkedCount: number
  unlinkedCount: number
  errorCount: number
  warningCount: number
  durationSeconds: number
  unlinkedPersons: ImportUnlinkedPerson[]
}
