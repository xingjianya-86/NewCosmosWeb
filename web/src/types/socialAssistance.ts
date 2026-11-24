/** 低收入人口救助帮扶：字段与 server/Contracts/SocialAssistanceContracts.cs 对齐。 */

export interface SocialAssistanceModuleSummary {
  activeCount: number | null
  monthlyNewAdditions: number | null
  monthlyExits: number | null
  gracePeriodExpiring: number | null
}

export interface GracePeriodListItem {
  applicationId: number
  applicationNo: string
  applicantName: string
  applicantIdCardMasked: string
  town: string
  village: string
  startDate: string | null
  endDate: string | null
  daysLeft: number
  status: 'Active' | 'Expired' | string
  statusText: string
}

export interface GracePeriodListResponse {
  items: GracePeriodListItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  totalPages: number
}
