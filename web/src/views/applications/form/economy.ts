import type {
  LandConfirmationImportRecord,
  SubsidyImportRecord,
} from '@/api/applicationImports'
import type { ApplicationFormEconomicDetails } from './types'

/** 可在第 3 步逐项编辑的经济明细台账；土地确权组暂不在此编辑。 */
export type EditableLedgerKey = Exclude<
  keyof ApplicationFormEconomicDetails,
  'landConfirmationGroups'
>

export type LedgerColumnType = 'text' | 'number' | 'money' | 'member' | 'switch'

export interface LedgerColumn {
  /** Core 实体字段名（camelCase，与接口 JSON 一致）。 */
  key: string
  label: string
  type?: LedgerColumnType
  /** 列标题中的单位提示。 */
  unit?: string
  width?: number
  placeholder?: string
  precision?: number
  step?: number
  min?: number
  max?: number
  /** 清空时写 null 而不是 0；仅用于 Core 中可空的数值字段。 */
  nullable?: boolean
  /** 旧数据里英文枚举 key → 中文显示（只影响显示，不回写存储值）。 */
  labels?: Record<string, string>
  /** 成员选择列写入的成员字段（不配置则不写）。 */
  memberIdKey?: string
  memberIdCardKey?: string
  memberAgeKey?: string
}

export interface LedgerConfig {
  key: EditableLedgerKey
  title: string
  hint?: string
  addLabel?: string
  /** 首次进入第 3 步时是否展开。 */
  defaultOpen?: boolean
  columns: LedgerColumn[]
  create: () => Record<string, unknown>
}

export interface LedgerGroup {
  key: string
  title: string
  subtitle: string
  ledgers: LedgerConfig[]
}

const text = (
  key: string,
  label: string,
  extra: Partial<LedgerColumn> = {},
): LedgerColumn => ({ key, label, type: 'text', ...extra })

const number = (
  key: string,
  label: string,
  extra: Partial<LedgerColumn> = {},
): LedgerColumn => ({ key, label, type: 'number', precision: 0, step: 1, ...extra })

const money = (
  key: string,
  label: string,
  extra: Partial<LedgerColumn> = {},
): LedgerColumn => ({ key, label, type: 'money', precision: 2, step: 100, ...extra })

const member = (extra: Partial<LedgerColumn> = {}): LedgerColumn => ({
  key: 'memberName',
  label: '成员',
  type: 'member',
  width: 180,
  memberIdCardKey: 'memberIdCard',
  ...extra,
})

const flag = (key: string, label: string): LedgerColumn => ({
  key,
  label,
  type: 'switch',
  width: 100,
})

export const LABOR_INCOME_LEDGER: LedgerConfig = {
  key: 'laborIncomes',
  title: '工资性收入',
  hint: '按成员逐条登记工资、奖金、津贴；月收入为 Core 汇总口径。',
  defaultOpen: true,
  columns: [
    member({
      memberIdKey: 'memberId',
      memberAgeKey: 'memberAge',
    }),
    number('memberAge', '年龄', { unit: '岁', width: 90, max: 130 }),
    text('incomeSubType', '收入子类', { placeholder: '工资 / 奖金 / 津贴', width: 140 }),
    text('workUnit', '工作单位', { placeholder: '单位或务工地点', width: 170 }),
    money('monthlyIncome', '月收入', { unit: '元/月', width: 120, nullable: true }),
    number('monthsWorked', '工作月数', { unit: '月', width: 100, nullable: true, max: 12 }),
    money('annualIncome', '计入收入', {
      unit: '元/月',
      width: 130,
      placeholder: '按 Core 口径',
    }),
    text('remark', '备注', { width: 180 }),
  ],
  create: () => ({
    memberId: 0,
    memberName: '',
    memberIdCard: '',
    memberAge: 0,
    incomeSubType: '工资',
    workUnit: '',
    monthlyIncome: null,
    monthsWorked: null,
    annualIncome: 0,
    remark: '',
  }),
}

export const BUSINESS_INCOME_LEDGER: LedgerConfig = {
  key: 'businessIncomes',
  title: '经营净收入',
  hint: '登记家庭成员从事农业、商业等经营活动的月净收入。',
  columns: [
    member({
      memberIdKey: 'memberId',
      memberAgeKey: 'memberAge',
    }),
    number('memberAge', '年龄', { unit: '岁', width: 90, max: 130 }),
    text('companyName', '经营主体', { placeholder: '公司 / 店铺 / 项目名称', width: 180 }),
    text('vendorType', '经营类型', { placeholder: '农业经营 / 商业经营', width: 150 }),
    money('monthlyIncome', '月净收入', { unit: '元/月', width: 130, nullable: true }),
  ],
  create: () => ({
    memberId: 0,
    memberName: '',
    memberIdCard: '',
    memberAge: 0,
    companyName: '',
    vendorType: '',
    monthlyIncome: null,
  }),
}

export const PROPERTY_INCOME_LEDGER: LedgerConfig = {
  key: 'propertyIncomes',
  title: '财产净收入',
  hint: '登记房屋出租、土地流转、利息等财产性月收入。',
  columns: [
    member({
      memberIdKey: 'memberId',
      memberAgeKey: 'memberAge',
    }),
    number('memberAge', '年龄', { unit: '岁', width: 90, max: 130 }),
    text('incomeType', '收入类型', { placeholder: '房租 / 流转 / 利息', width: 140 }),
    text('propertyDescription', '财产说明', { placeholder: '财产名称、位置或来源', width: 210 }),
    money('amount', '月收入', { unit: '元/月', width: 120 }),
  ],
  create: () => ({
    memberId: 0,
    memberName: '',
    memberIdCard: '',
    memberAge: 0,
    incomeType: '',
    propertyDescription: '',
    amount: 0,
  }),
}

export const TRANSFER_INCOME_LEDGER: LedgerConfig = {
  key: 'transferIncomes',
  title: '转移净收入',
  hint: '登记养老金、低保金、残疾人补贴等转移性收入；月发放额按 Core 口径汇总。',
  columns: [
    member({
      memberIdKey: 'memberId',
      memberAgeKey: 'memberAge',
    }),
    number('memberAge', '年龄', { unit: '岁', width: 90, max: 130 }),
    text('incomeType', '收入类型', { placeholder: '养老金 / 低保金 / 补贴', width: 160 }),
    money('monthlyAmount', '月发放额', { unit: '元/月', width: 130, nullable: true }),
    number('monthsOrTimes', '发放次数', { unit: '次', width: 110, nullable: true, max: 12 }),
    money('totalAmount', '计入收入', {
      unit: '元/月',
      width: 130,
      placeholder: '按 Core 口径',
    }),
  ],
  create: () => ({
    memberId: 0,
    memberName: '',
    memberIdCard: '',
    memberAge: 0,
    incomeType: '',
    monthlyAmount: null,
    monthsOrTimes: null,
    totalAmount: 0,
  }),
}

export const OTHER_INCOME_LEDGER: LedgerConfig = {
  key: 'otherIncomes',
  title: '其他收入',
  hint: '登记上述分类之外的零星、临时性收入。',
  columns: [
    text('incomeType', '收入类型', { placeholder: '其他收入名称', width: 180 }),
    money('amount', '月收入', { unit: '元/月', width: 130 }),
  ],
  create: () => ({
    incomeType: '',
    amount: 0,
  }),
}

export const SUBSIDY_LEDGER: LedgerConfig = {
  key: 'subsidies',
  title: '农业补贴',
  hint: '按补贴类型逐条登记面积、单价与折算比例；实际金额由 Core 口径计算，可从补贴库按成员一键导入。',
  defaultOpen: true,
  columns: [
    text('subsidyType', '补贴类型', { placeholder: '地力 / 大豆 / 玉米等', width: 160 }),
    number('area', '面积', { unit: '亩', precision: 2, step: 0.1, width: 110 }),
    money('unitPrice', '单价', { unit: '元/亩', width: 120 }),
    number('count', '数量', { unit: '份', width: 90 }),
    number('ratioFactor', '比例系数', {
      unit: '0–1',
      precision: 4,
      step: 0.01,
      max: 1,
      width: 120,
    }),
    money('originalAmount', '原始金额', { unit: '元', width: 120 }),
    money('amount', '实际金额', { unit: '元', width: 120 }),
    member({ label: '关联成员', memberIdCardKey: 'memberIdCard', width: 180 }),
  ],
  create: () => ({
    subsidyType: '',
    area: 0,
    unitPrice: 0,
    count: 1,
    ratioFactor: 1,
    originalAmount: 0,
    amount: 0,
    memberName: '',
    memberIdCard: '',
  }),
}

export const BREEDING_INCOME_LEDGER: LedgerConfig = {
  key: 'breedingIncomes',
  title: '养殖业收入',
  hint: '登记家庭养殖项目的年收入，按年度口径填报。',
  columns: [
    text('breedingType', '养殖类型', { placeholder: '牛 / 羊 / 禽类等', width: 160 }),
    number('quantity', '数量', { unit: '头/只', width: 110 }),
    money('annualIncome', '年收入', { unit: '元/年', width: 130 }),
    text('remark', '备注', { width: 220 }),
  ],
  create: () => ({
    breedingType: '',
    quantity: 0,
    annualIncome: 0,
    remark: '',
  }),
}

export const RIGID_EXPENDITURE_LEDGER: LedgerConfig = {
  key: 'rigidExpenditures',
  title: '刚性支出',
  hint: '按支出人员逐条登记医疗、教育、残疾康复、居住等刚性支出，月支出口径。',
  defaultOpen: true,
  columns: [
    member({
      memberIdKey: 'memberId',
      memberIdCardKey: 'memberIdCard',
    }),
    text('expenditureType', '支出类型', { placeholder: '医疗 / 教育 / 残疾康复 / 居住', width: 180 }),
    money('amount', '月支出', { unit: '元/月', width: 130 }),
    text('remark', '备注', { width: 220 }),
  ],
  create: () => ({
    memberId: 0,
    memberName: '',
    memberIdCard: '',
    personDescription: '',
    expenditureType: '',
    amount: 0,
    remark: '',
  }),
}

export const FAMILY_PROPERTY_LEDGER: LedgerConfig = {
  key: 'familyProperties',
  title: '家庭房产',
  hint: '逐套登记家庭房产的位置、结构与估值。',
  defaultOpen: true,
  columns: [
    text('propertyType', '房产类型', { placeholder: '自建房 / 商品房等', width: 140 }),
    text('address', '地址', { placeholder: '房产坐落位置', width: 220 }),
    text('housingStructure', '房屋结构', { placeholder: '砖混 / 框架等', width: 130 }),
    text('housingNature', '房屋性质', { placeholder: '自有 / 租赁等', width: 130 }),
    number('area', '面积', { unit: '㎡', precision: 2, step: 1, width: 110 }),
    number('roomCount', '间数', { unit: '间', width: 90, max: 99 }),
    number('buildYear', '建成年份', { width: 110, max: 2100 }),
    money('estimatedValue', '估算价值', { unit: '元', width: 130 }),
  ],
  create: () => ({
    memberId: 0,
    propertyType: '',
    address: '',
    housingStructure: '',
    housingNature: '',
    area: 0,
    roomCount: 0,
    buildYear: 0,
    estimatedValue: 0,
  }),
}

export const VEHICLE_LEDGER: LedgerConfig = {
  key: 'vehicles',
  title: '车辆',
  hint: '逐辆登记家庭车辆及购置、现值信息。',
  columns: [
    text('vehicleType', '车辆类型', { placeholder: '轿车 / 货车 / 摩托车', width: 140 }),
    text('brand', '品牌', { width: 120 }),
    text('model', '型号', { width: 140 }),
    text('licensePlate', '车牌号', { width: 130 }),
    number('purchaseYear', '购置年份', { width: 110, max: 2100 }),
    money('purchasePrice', '购置价', { unit: '元', width: 130 }),
    money('estimatedValue', '现值', { unit: '元', width: 130 }),
  ],
  create: () => ({
    memberId: 0,
    vehicleType: '',
    brand: '',
    model: '',
    licensePlate: '',
    purchaseYear: 0,
    purchasePrice: 0,
    estimatedValue: 0,
  }),
}

export const MACHINERY_LEDGER: LedgerConfig = {
  key: 'machineries',
  title: '农机具',
  hint: '逐台登记农业机械；数量、马力与现值将进入财产核对。',
  columns: [
    text('machineryType', '机械类型', { placeholder: '拖拉机 / 收割机等', width: 150 }),
    text('brand', '品牌', { width: 120 }),
    text('model', '型号', { width: 140 }),
    number('quantity', '数量', { unit: '台', width: 90 }),
    number('horsepower', '马力', { unit: '马力', precision: 2, step: 1, width: 110 }),
    number('purchaseYear', '购置年份', { width: 110, max: 2100 }),
    money('purchasePrice', '购置价', { unit: '元', width: 130 }),
    money('estimatedValue', '现值', { unit: '元', width: 130 }),
  ],
  create: () => ({
    memberId: 0,
    machineryType: '',
    brand: '',
    model: '',
    quantity: 1,
    horsepower: 0,
    purchaseYear: 0,
    purchasePrice: 0,
    estimatedValue: 0,
  }),
}

export const FINANCIAL_ASSET_LEDGER: LedgerConfig = {
  key: 'financialAssets',
  title: '金融资产',
  hint: '按家庭登记现金、存款、证券与商业保险；无该项时保持开关关闭。',
  columns: [
    flag('hasCash', '有现金'),
    money('cashAmount', '现金金额', { unit: '元', width: 120 }),
    flag('hasBankDeposit', '有存款'),
    money('bankDepositAmount', '存款金额', { unit: '元', width: 120 }),
    flag('hasSecurities', '有证券'),
    money('securitiesAmount', '证券金额', { unit: '元', width: 120 }),
    flag('hasCommercialInsurance', '有商业保险'),
    text('commercialInsuranceType', '保险类型', { width: 150 }),
    money('commercialInsuranceAmount', '保险金额', { unit: '元', width: 120 }),
  ],
  create: () => ({
    hasCash: false,
    cashAmount: 0,
    hasBankDeposit: false,
    bankDepositAmount: 0,
    hasSecurities: false,
    securitiesAmount: 0,
    hasCommercialInsurance: false,
    commercialInsuranceType: '',
    commercialInsuranceAmount: 0,
  }),
}

/** 旧数据可能残留的英文土地用途 key → 中文（Core 实际存中文常量）。 */
export const LAND_USAGE_LABELS: Record<string, string> = {
  SELF_FARM: '自行种植',
  SELF_FARMING: '自行种植',
  SELF_FARMED: '自行种植',
  SELFUSE: '自行种植',
  SUBLEASE: '转包他人',
  SUBLEASED: '转包他人',
  TRANSFER: '转包他人',
  CONTRACT: '承包土地',
  CONTRACTED: '承包土地',
}

export function landUsageText(value: unknown): string {
  const raw = typeof value === 'string' ? value.trim() : ''
  if (!raw) return ''
  return LAND_USAGE_LABELS[raw.toUpperCase()] ?? raw
}

export const LAND_REGISTRATION_LEDGER: LedgerConfig = {
  key: 'landRegistrations',
  title: '土地登记 / 土地确权',
  hint: '逐块登记承包地、自种地、转包地等土地信息；土地确权台账可按成员一键导入。',
  defaultOpen: true,
  columns: [
    text('ownerName', '所有人', { width: 120 }),
    text('ownerIdCard', '身份证号', { width: 190 }),
    text('landType', '土地类型', { placeholder: '耕地 / 林地等', width: 130 }),
    text('landUsage', '土地用途', {
      placeholder: '自行种植 / 转包他人 / 承包土地',
      width: 190,
      labels: LAND_USAGE_LABELS,
    }),
    text('measurementType', '计量方式', { placeholder: '实测 / 台账', width: 130 }),
    number('area', '面积', { unit: '亩', precision: 2, step: 0.1, width: 110 }),
    money('price', '单价', { unit: '元/亩', width: 120 }),
    money('subtotal', '小计', { unit: '元', width: 120 }),
    text('location', '地块位置', { width: 200 }),
  ],
  create: () => ({
    ownerName: '',
    ownerIdCard: '',
    landType: '',
    landUsage: '',
    measurementType: '',
    area: 0,
    price: 0,
    subtotal: 0,
    location: '',
  }),
}

export const LEDGER_GROUPS: LedgerGroup[] = [
  {
    key: 'income',
    title: '收入明细',
    subtitle: '工资、经营、财产、转移与其他收入逐条登记，汇总由服务端按 Core 口径生成。',
    ledgers: [
      LABOR_INCOME_LEDGER,
      BUSINESS_INCOME_LEDGER,
      PROPERTY_INCOME_LEDGER,
      TRANSFER_INCOME_LEDGER,
      OTHER_INCOME_LEDGER,
    ],
  },
  {
    key: 'land',
    title: '土地信息',
    subtitle: '土地确权台账与土地登记逐块核对；确权数据可按成员一键导入后继续逐项编辑。',
    ledgers: [LAND_REGISTRATION_LEDGER],
  },
  {
    key: 'subsidy',
    title: '农业补贴',
    subtitle: '按成员逐条登记补贴类型、面积与折算比例，可从补贴库一键导入。',
    ledgers: [SUBSIDY_LEDGER],
  },
  {
    key: 'expense',
    title: '刚性支出',
    subtitle: '医疗、教育、残疾康复、居住等刚性支出按人员逐条登记，月支出口径。',
    ledgers: [RIGID_EXPENDITURE_LEDGER],
  },
  {
    key: 'property',
    title: '财产信息',
    subtitle: '房产、车辆、农机具与金融资产逐项登记。',
    ledgers: [
      FAMILY_PROPERTY_LEDGER,
      VEHICLE_LEDGER,
      MACHINERY_LEDGER,
      FINANCIAL_ASSET_LEDGER,
    ],
  },
  {
    key: 'other',
    title: '其他',
    subtitle: '养殖业收入等其他补充台账，按项目逐条登记。',
    ledgers: [BREEDING_INCOME_LEDGER],
  },
]

export function createLedgerRow(ledger: LedgerConfig): Record<string, unknown> {
  return ledger.create()
}

/* ------------------------------------------------------------------ *
 * 旧版导入：农业补贴 / 土地确权台账
 * ------------------------------------------------------------------ */

export interface ImportMergeResult {
  /** 实际新增条数。 */
  added: number
  /** 因重复而跳过的条数。 */
  skipped: number
  /** 接口返回总条数。 */
  total: number
}

export interface LandImportMergeResult extends ImportMergeResult {
  /** 涉及成员（户）数。 */
  memberCount: number
}

function round2(value: number): number {
  return Math.round(value * 100) / 100
}

function textOf(value: unknown): string {
  return typeof value === 'string' ? value.trim() : ''
}

function numberOf(value: unknown): number {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : 0
}

/** 只保留真正的身份证号；旧版土地台账接口曾把承包方姓名回填到 memberIdCard。 */
function idCardOf(value: unknown): string {
  const raw = textOf(value)
  return /^\d{15}(\d{2}[\dXx])?$/.test(raw) ? raw : ''
}

/** 补贴去重键：补贴类型 + 成员身份证号 + 面积。 */
export function subsidyImportKey(row: Record<string, unknown>): string {
  return [
    textOf(row.subsidyType),
    textOf(row.memberIdCard),
    round2(numberOf(row.area)),
  ].join('|')
}

/** 把接口返回的补贴记录转为台账行；金额原样保留，面积型行反推单价。 */
export function createSubsidyRowFromImport(
  record: SubsidyImportRecord,
): Record<string, unknown> {
  const area = round2(numberOf(record.area))
  const amount = round2(numberOf(record.amount))
  // 面积型：面积 × 单价 ≈ 导入金额；金额型（面积为 0）保留 OriginalAmount 供 Core 金额型分支计算。
  const unitPrice = area > 0 && amount > 0 ? round2(amount / area) : 0
  return {
    subsidyType: textOf(record.subsidyType),
    area,
    unitPrice,
    count: 1,
    ratioFactor: 1,
    originalAmount: amount,
    amount,
    memberName: textOf(record.name),
    memberIdCard: textOf(record.idCard),
  }
}

/** 追加补贴（不覆盖已录行），按 subsidyType + memberIdCard + area 去重。 */
export function mergeSubsidyImports(
  rows: Record<string, unknown>[],
  records: SubsidyImportRecord[],
): ImportMergeResult {
  const seen = new Set(rows.map(subsidyImportKey))
  let added = 0
  let skipped = 0

  for (const record of records) {
    const key = subsidyImportKey({
      subsidyType: record.subsidyType,
      memberIdCard: record.idCard,
      area: record.area,
    })
    if (seen.has(key)) {
      skipped += 1
      continue
    }
    seen.add(key)
    rows.push(createSubsidyRowFromImport(record))
    added += 1
  }

  return { added, skipped, total: records.length }
}

/** 土地确权去重键：优先地块编码，否则 所有人 + 地块位置 + 面积。 */
export function landImportKey(row: Record<string, unknown>): string {
  const plotCode = textOf(row.plotCode)
  if (plotCode) return `code:${plotCode}`
  return [
    textOf(row.ownerName),
    textOf(row.location),
    round2(numberOf(row.area)),
  ].join('|')
}

/** 把确权记录转为土地登记行；面积优先实测，其次确权面积，最后合同面积。 */
export function createLandRegistrationRowFromImport(
  record: LandConfirmationImportRecord,
): Record<string, unknown> {
  const measured = numberOf(record.measuredArea)
  const confirmed = numberOf(record.landArea)
  const contracted = numberOf(record.contractArea)
  const area = round2(measured > 0 ? measured : confirmed > 0 ? confirmed : contracted)
  const price = round2(numberOf(record.unitPrice))
  return {
    ownerName: textOf(record.memberName),
    ownerIdCard: idCardOf(record.memberIdCard),
    landType: '确权土地',
    landUsage: landUsageText(record.landUsage) || '自行种植',
    measurementType: measured > 0 ? '实测' : '台账',
    area,
    price,
    subtotal: round2(area * price),
    location: textOf(record.landPlotInfo) || textOf(record.plotCode),
    // 仅前端去重用；后端土地登记实体无此列，保存时忽略。
    plotCode: textOf(record.plotCode),
  }
}

/** 按成员分组追加土地确权记录，组内按返回顺序，组间按成员首次出现顺序。 */
export function mergeLandConfirmationImports(
  rows: Record<string, unknown>[],
  records: LandConfirmationImportRecord[],
): LandImportMergeResult {
  const grouped = new Map<string, LandConfirmationImportRecord[]>()
  for (const record of records) {
    const member = textOf(record.memberName) || '未命名成员'
    const list = grouped.get(member)
    if (list) list.push(record)
    else grouped.set(member, [record])
  }

  const seen = new Set(rows.map(landImportKey))
  let added = 0
  let skipped = 0

  for (const group of grouped.values()) {
    for (const record of group) {
      const row = createLandRegistrationRowFromImport(record)
      const key = landImportKey(row)
      if (seen.has(key)) {
        skipped += 1
        continue
      }
      seen.add(key)
      rows.push(row)
      added += 1
    }
  }

  return { added, skipped, total: records.length, memberCount: grouped.size }
}
