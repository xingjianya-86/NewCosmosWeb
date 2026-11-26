/**
 * 数据中心 · 标准配置管理。
 * 字段与 src/NewCosmos.Server/Contracts/StandardContracts.cs 保持一致。
 */

/** 标准配置列表项。 */
export interface StandardConfig {
  id: number
  standardType: string
  standardName: string
  hukouType: string | null
  supportMode: string | null
  standardValue: number
  unit: string
  effectiveStartDate: string
  effectiveEndDate: string | null
  version: number
  isActive: boolean
  description: string | null
  updatedAt: string | null
}

/** 新增/编辑标准配置请求（Version 为乐观并发版本号）。 */
export interface StandardConfigRequest {
  standardType: string
  standardName: string
  hukouType: string | null
  supportMode: string | null
  standardValue: number
  unit: string
  effectiveStartDate: string
  effectiveEndDate: string | null
  version: number
  isActive: boolean
  description: string
}

/** 标准配置总览（条数与城乡关键金额）。 */
export interface StandardOverview {
  incomeStandardsCount: number
  subsidyStandardsCount: number
  destituteStandardsCount: number
  systemConfigsCount: number
  ruralIncomeStandard: number
  urbanIncomeStandard: number
  ruralSubsidyAmount: number
  urbanSubsidyAmount: number
  configStandardsCount: number
}

/** 新增成功后返回数据库自增 Id。 */
export interface StandardCreatedResponse {
  id: number
  message: string
}

/** 编辑/删除/同步/初始化结果。 */
export interface StandardActionResult {
  message: string
}

/** 页面左侧固定分类。 */
export interface StandardCategoryDefinition {
  key: string
  displayName: string
  standardTypes: string[]
}

/** 与 PC StandardConfigManagementViewModel.InitializeCategories 完全一致。 */
export const STANDARD_CATEGORIES: StandardCategoryDefinition[] = [
  {
    key: 'income',
    displayName: '收入标准',
    standardTypes: ['RuralSubsistenceStandard', 'UrbanSubsistenceStandard'],
  },
  {
    key: 'classified-subsidy',
    displayName: '分类补贴标准',
    standardTypes: ['ClassifiedSubsidyStandard'],
  },
  {
    key: 'destitute',
    displayName: '特困供养标准',
    standardTypes: ['RuralDestituteStandard', 'UrbanDestituteStandard'],
  },
  {
    key: 'grace-period',
    displayName: '渐退期配置',
    standardTypes: ['GracePeriodMonths'],
  },
  {
    key: 'system-config',
    displayName: '系统配置',
    standardTypes: ['MinWage', 'RuralSingleRescue', 'UrbanSingleRescue'],
  },
  {
    key: 'min-wage-level',
    displayName: '最低工资档位',
    standardTypes: ['MinWageLevel1', 'MinWageLevel2', 'MinWageLevel3'],
  },
  {
    key: 'caregiver-allowance',
    displayName: '照料护理费',
    standardTypes: [
      'CaregiverAllowanceFull',
      'CaregiverAllowancePartial',
      'CaregiverAllowanceNone',
    ],
  },
  {
    key: 'land-price',
    displayName: '土地价格',
    standardTypes: ['LandSelfFarmPrice', 'LandSubleasePrice', 'LandContractPrice'],
  },
  {
    key: 'agriculture-subsidy',
    displayName: '农业补贴价格',
    standardTypes: [
      'LandFertilityPrice',
      'SoybeanPrice',
      'CornPrice',
      'RotationPrice',
      'SurfaceWaterRicePrice',
      'GroundWaterRicePrice',
    ],
  },
  {
    key: 'elderly-subsidy',
    displayName: '高龄津贴标准',
    standardTypes: ['ELDERLY_SUBSIDY'],
  },
  {
    key: 'severe-disease-threshold',
    displayName: '重病费用口径阈值',
    standardTypes: ['SevereDiseaseCostThreshold'],
  },
]

/** 户口类型选项（值为后端存量口径；农村/城市之外的历史值按原值展示）。 */
export const HUKOU_TYPE_OPTIONS: ReadonlyArray<{ label: string; value: string }> = [
  { label: '农村', value: 'Rural' },
  { label: '城市', value: 'Urban' },
]

/** 供养方式选项。 */
export const SUPPORT_MODE_OPTIONS: ReadonlyArray<{ label: string; value: string }> = [
  { label: '集中供养', value: 'Centralized' },
  { label: '分散供养', value: 'Scattered' },
]
