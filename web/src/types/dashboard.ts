/** 首页概览接口返回：字段与 server/Contracts/DashboardContracts.cs 对齐。 */

export interface DashboardOverview {
  monthlyNewAdditions: number
  monthlyExits: number
  newAssetChecks: number
  monthlyNewElderly: number
}

export interface SocialAssistanceOverview {
  activeCount: number
  monthlyNewAdditions: number
  monthlyExits: number
  gracePeriodExpiring: number | null
}

export interface ElderlyOverview {
  activeCount: number
  monthlyNew: number
  pendingNew: number | null
  pendingTransfer: number | null
}

export interface AssetVerificationOverview {
  pendingReportCount: number
  monthlyCompleted: number
  monthlyNew: number
  yearTotal: number
}

export interface ReminderOverview {
  gracePeriodExpiring: number | null
  elderlyPendingNew: number | null
  elderlyPendingTransfer: number | null
  collegeStudentGraduating: number | null
}

/**
 * 数据块为 null 表示该块查询失败（服务端 JSON 会省略 null 字段），
 * 页面显示「—」并提供重试，不能用 0 冒充。
 */
export interface DashboardSummary {
  dashboard?: DashboardOverview | null
  socialAssistance?: SocialAssistanceOverview | null
  elderly?: ElderlyOverview | null
  assetVerification?: AssetVerificationOverview | null
  reminders?: ReminderOverview | null
}
