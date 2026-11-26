/** 数据中心：与 /api/system/data-center 契约一致。 */

export interface DatabaseStatus {
  connected: boolean
  databaseName: string
  version: string
  activeConnections: number
  sizeBytes: number
  sizeText: string
  totalTables: number
  totalRecords: number
  activeUsers: number
  checkedAt: string
}

export interface TableSize {
  tableName: string
  tableComment: string
  sizeBytes: number
  sizeText: string
  rowCount: number
}

export interface DataCenterOverview {
  database: DatabaseStatus
  tables: TableSize[]
}

export interface SchemaStatus {
  isInitialized: boolean
  statusText: string
  statusSummary: string
  statusIcon: string
  statusColor: string
  currentVersion: string
  initializedAt: string
  initializedBy: string
  totalRequiredTables: number
  totalExistingTables: number
  missingTables: string[]
  missingColumns: Record<string, string[]>
  totalMissingColumns: number
  hasMissingTables: boolean
  hasMissingColumns: boolean
}

export interface SchemaFixResult {
  totalFixed: number
  failedCount: number
  skippedCount: number
  summary: string
  failedItems: string[]
}

export interface BackupFile {
  fileName: string
  sizeBytes: number
  sizeText: string
  createdAt: string
}

export interface SoftDeleteTableStat {
  tableName: string
  softDeleted: number
  kept: number
  toDelete: number
}

export interface ChainIssue {
  oldApplicationId: number
  oldName: string
  oldStatus: string
  newApplicationId: number
  chainType: string
}

export interface SoftDeleteCensus {
  tables: SoftDeleteTableStat[]
  totalSoftDeleted: number
  totalKept: number
  totalToDelete: number
  chainIssues: ChainIssue[]
  detectedAt: string
}

export interface SoftDeleteCleanupResult {
  backupFile: string
  deletedRows: number
  restoredRows: number
  deletedArchives: number
  message: string
}

export interface ClientVersion {
  deviceName: string
  deviceKind: string
  appVersion: string
  userName: string
  fullName: string
  loginAt: string
  lastSeenAt: string
  online: boolean
}

export interface ClientVersionList {
  items: ClientVersion[]
  onlineCount: number
  summary: string
}

export type DataCenterJobState =
  | 'pending'
  | 'running'
  | 'succeeded'
  | 'failed'
  | 'cancelled'

export interface DataCenterJob {
  jobId: string
  kind: string
  title: string
  state: DataCenterJobState
  percent: number
  message: string
  startedAt: string
  finishedAt: string | null
  error: string | null
  logs: string[]
}

export interface StartJobResponse {
  jobId: string
  message: string
}
