/**
 * 近亲属备案：字段与 src/NewCosmos.Core/Models/Entities/NearRelativeEntities.cs
 * 及 server/Contracts/NearRelativeContracts.cs 对齐（camelCase JSON）。
 */

/** 一张备案 = 1 名工作人员 + N 名救助对象。 */
export interface NearRelativeStaffInput {
  id: number
  staffName: string
  staffIdCard: string
  staffPhone: string
  workUnit: string
  position: string
  town: string
  /** 编辑保存时的版本号（加载时的 updatedAt），服务端据此做并发校验。 */
  updatedAt?: string | null
}

export interface NearRelativeStaff extends NearRelativeStaffInput {
  createdAt?: string | null
  updatedAt?: string | null
  createdBy?: string
  updatedBy?: string
}

/** 提交给服务端的对象行（id / staffId 由服务端重建，不参与写入）。 */
export interface NearRelativeLinkInput {
  id: number
  applicationId: number | null
  relation: string
  name: string
  idCard: string
  gender: string
  birthDate: string
  familyAddress: string
  hukouAddress: string
  residenceAddress: string
  familySize: number | null
  helpType: string
  monthAmount: number | null
  reportAmount: number | null
  startTime: string
  familyRelation: string
  applyReason: string
  applyTime: string
  familyDifficulty: string
  economyInvestigate: string
  townOpinion: string
  dynamicRecord: string
  sortOrder: number
}

export interface NearRelativeLink extends NearRelativeLinkInput {
  staffId: number
}

/** 单条备案详情（工作人员 + 对象明细）。 */
export interface NearRelativeEntry {
  staff: NearRelativeStaff
  links: NearRelativeLink[]
}

/** 列表项：身份证已脱敏，时间取最近更新（无则创建时间）。 */
export interface NearRelativeListItem {
  id: number
  staffName: string
  staffIdCardMasked: string
  workUnit: string
  position: string
  town: string
  linkCount: number
  createdAt: string | null
  updatedAt: string | null
}

export interface NearRelativeListResponse {
  items: NearRelativeListItem[]
  pageIndex: number
  pageSize: number
  totalCount: number
  allCount: number
  totalPages: number
  towns: string[]
}

/** 已有备案摘要：工作人员身份证命中既有备案时用于提示载入编辑。 */
export interface NearRelativeExistingFiling {
  id: number
  staffName: string
  linkCount: number
}

/** 工作人员身份证查工作信息 + 是否已有备案。 */
export interface NearRelativeStaffLookup {
  staff: NearRelativeStaff | null
  foundInUsers: boolean
  foundInCaregivers: boolean
  foundInDestituteArchive: boolean
  hasAny: boolean
  existing: NearRelativeExistingFiling | null
}

/** 档案带出结果：按身份证 / 申请 ID 从救助档案映射的对象行。 */
export interface NearRelativeArchiveFill {
  applicationId: number
  applicationNo: string
  status: string
  statusDisplay: string
  applicationCreatedAt: string | null
  link: NearRelativeLink
  occupiedByOtherStaff: boolean
  occupiedByStaffName: string
}

export interface NearRelativeSavePayload {
  staff: NearRelativeStaffInput
  links: NearRelativeLinkInput[]
}

export interface NearRelativeSaveResponse {
  id: number
}
