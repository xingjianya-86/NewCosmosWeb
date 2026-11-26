export interface OrganizationTreeNode {
  id: number
  name: string
  abbreviation: string
  depth: number
  children: OrganizationTreeNode[]
}

export interface OrganizationStats {
  totalOrganizations: number
  totalUsers: number
  parentOrganizations: number
  rootOrganizations: number
  /** 数据范围内特困供养机构数。 */
  specialCareInstitutionCount: number
}

export interface OrganizationTreeResponse {
  nodes: OrganizationTreeNode[]
  stats: OrganizationStats
}

export interface OrganizationDetail {
  id: number
  name: string
  abbreviation: string
  principal: string
  principalIdentityCard: string
  phone: string
  address: string
  postalCode: string
  parentId: number | null
  parentName: string
  institutionNature: string
  institutionType: string
  institutionCategory: string
  isSpecialCareInstitution: boolean
  provinceId: number | null
  cityId: number | null
  countyId: number | null
  townId: number | null
  villageId: number | null
  provinceName: string
  cityName: string
  countyName: string
  townName: string
  villageName: string
  /** 组织关联用户数（详情接口附带）。 */
  userCount: number
  /** 创建时间（ISO 字符串）。 */
  createdAt: string | null
  /** 更新时间（ISO 字符串）。 */
  updatedAt: string | null
}

export interface OrganizationSaveInput {
  name: string
  abbreviation: string
  principal: string
  principalIdentityCard: string
  phone: string
  address: string
  postalCode: string
  parentId: number | null
  institutionNature: string
  institutionType: string
  institutionCategory: string
  isSpecialCareInstitution: boolean
  provinceId: number | null
  cityId: number | null
  countyId: number | null
  townId: number | null
  villageId: number | null
}

export interface OrganizationOption {
  id: number
  name: string
  parentId: number | null
}

export interface OrganizationOptions {
  organizations: OrganizationOption[]
  institutionNatures: string[]
  institutionTypes: string[]
  institutionCategories: string[]
}

export interface OrganizationPreviewRow {
  rowNumber: number
  name: string
  abbreviation: string
  principal: string
  phone: string
  address: string
  region: string
}

export interface OrganizationPreview {
  totalRows: number
  previewRows: OrganizationPreviewRow[]
  error: string | null
}

export interface OrganizationImportResult {
  success: boolean
  importedCount: number
  errorCount: number
  errors: string[]
  warnings: string[]
  message: string
}
