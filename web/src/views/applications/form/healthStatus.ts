/**
 * 身体状况（healthStatus）派生助手 —— Step1Basic（申请人）与 Step2Members（成员/赡养人）共用。
 *
 * ⚠️ 与后端 Core HealthStatusDeriver **同口径，改一处必须同步另一处**。
 * 桌面参照（语义权威）：ApplicationFormViewModel.Initialization.UpdateHealthStatus /
 * OnDisabilityCertificateChanged / OnIsSevereDiseaseChanged、
 * FamilyMember.UpdateHealthStatus / ParseDisabilityCertificate / ClearDisabilityInfo。
 *
 * 派生只服务「编辑时即时反馈」；提交/加载时的权威派生在后端。
 */

import {
  DISABILITY_LEVEL_OPTIONS,
  DISABILITY_TYPE_OPTIONS,
  DISEASE_CATEGORY_OPTIONS,
  HEALTH_STATUS_OPTIONS,
} from './options'

/** 无任何疾病：取 DISEASE_CATEGORY_OPTIONS 首项，不硬编码（改选项即跟随）。 */
const NO_DISEASE_CATEGORY = DISEASE_CATEGORY_OPTIONS[0].value

/** 输出档位取自 HEALTH_STATUS_OPTIONS（顺序：健康或良好/一般或较弱/重病/重残/重病且重残/其他）。 */
const HEALTHY = HEALTH_STATUS_OPTIONS[0].value
const FAIR = HEALTH_STATUS_OPTIONS[1].value
const SEVERE_DISEASE = HEALTH_STATUS_OPTIONS[2].value
const SEVERE_DISABILITY = HEALTH_STATUS_OPTIONS[3].value
const SEVERE_DISEASE_AND_DISABILITY = HEALTH_STATUS_OPTIONS[4].value

/** 重残等级：一级/二级（DISABILITY_LEVEL_OPTIONS 前两项；表单值只有中文）。 */
const SEVERE_DISABILITY_LEVELS: readonly string[] = [
  DISABILITY_LEVEL_OPTIONS[0].value,
  DISABILITY_LEVEL_OPTIONS[1].value,
]

/** 残疾类型 Core key → 表单中文值（与 options.ts 的 value 一致）；key 统一小写比较。 */
const DISABILITY_TYPE_ALIASES: Readonly<Record<string, string>> = {
  vision: '视力',
  hearing: '听力',
  speech: '言语',
  physical: '肢体',
  intellectual: '智力',
  mental: '精神',
  multiple: '多重',
}

/** 残疾等级 Core key → 表单中文值（Level1..Level4 → 一级..四级）。 */
const DISABILITY_LEVEL_ALIASES: Readonly<Record<string, string>> = {
  level1: '一级',
  level2: '二级',
  level3: '三级',
  level4: '四级',
}

/** 残疾类型归一：英文 Core key → 中文；未知值原样保留。 */
export function normalizeDisabilityType(value: string | null | undefined): string {
  const raw = (value ?? '').trim()
  if (!raw) return ''
  if (DISABILITY_TYPE_OPTIONS.some((item) => item.value === raw)) return raw
  return DISABILITY_TYPE_ALIASES[raw.toLowerCase()] ?? raw
}

/** 残疾等级归一：英文 Core key → 中文；未知值原样保留。 */
export function normalizeDisabilityLevel(value: string | null | undefined): string {
  const raw = (value ?? '').trim()
  if (!raw) return ''
  if (DISABILITY_LEVEL_OPTIONS.some((item) => item.value === raw)) return raw
  return DISABILITY_LEVEL_ALIASES[raw.toLowerCase()] ?? raw
}

/**
 * 覆盖守卫：仅当当前值属于这些档位时才允许被派生结果覆盖；
 * 其它历史值（患病/残疾/其他等）原样保留。与后端派生守卫逐字一致。
 */
const OVERWRITABLE_HEALTH_STATUSES: ReadonlySet<string> = new Set([
  '',
  HEALTHY,
  FAIR,
  SEVERE_DISEASE,
  SEVERE_DISABILITY,
  SEVERE_DISEASE_AND_DISABILITY,
])

/**
 * 英文 Core key / 旧显示值 / 历史短词 → 表单中文档位。
 *
 * 词表以 `nc_dict_items.HealthStatuses.item_value` 为准（健康或良好 / 一般或较弱 / 重病 …），
 * 与后端 `DictValueMapper.ToFormHealthStatus`、`DefaultValuesConstants.HEALTH_STATUS`、
 * `PageDefaultValues.HealthOptions`、`DictDisplayHelper.GetHealthStatusDisplay` 同一套。
 *
 * 兼容三类来源：① 接口未做映射（旧服务端构建、详情接口）的英文 Core key；
 * ② 旧字典显示值；③ 历史表单短词「健康 / 一般」。key 统一小写比较。
 */
const HEALTH_STATUS_ALIASES: Readonly<Record<string, string>> = {
  healthy: HEALTHY,
  // 历史表单短词（旧 HEALTH_STATUS_OPTIONS）→ 字典口径
  '健康': HEALTHY,
  '健康或良好': HEALTHY,
  weak: FAIR,
  fair: FAIR,
  '一般': FAIR,
  '一般或较弱': FAIR,
  severeillness: SEVERE_DISEASE,
  severedisease: SEVERE_DISEASE,
  severedisability: SEVERE_DISABILITY,
  severeillnessanddisability: SEVERE_DISEASE_AND_DISABILITY,
  other: '其他',
}

/**
 * 归一化身体状况值为表单中文档位（词表 = nc_dict_items.HealthStatuses.item_value）。
 * - 空值 → ''；已知英文 key / 旧显示值 / 历史短词 → 对应字典口径；
 * - 其余历史值（患病/残疾等）原样保留，交给覆盖守卫判断。
 */
export function normalizeHealthStatus(value: string | null | undefined): string {
  const raw = (value ?? '').trim()
  if (!raw) return ''
  const direct = HEALTH_STATUS_OPTIONS.find((item) => item.value === raw)
  if (direct) return direct.value
  return HEALTH_STATUS_ALIASES[raw.toLowerCase()] ?? raw
}

export interface DisabilityCertificateInfo {
  /** 残疾类型表单值（视力/听力/言语/肢体/智力/精神/多重）；解析失败为空串。 */
  type: string
  /** 残疾等级表单值（一级/二级/三级/四级）；解析失败为空串。 */
  level: string
}

/**
 * 残疾证号解析：倒数第 2 位 '1'..'7' → DISABILITY_TYPE_OPTIONS[0..6].value，
 * 最后 1 位 '1'..'4' → DISABILITY_LEVEL_OPTIONS[0..3].value。
 *
 * 长度 < 2 或序号越界/非数字 → 对应类型/等级**清空**（对齐桌面 ClearDisabilityInfo，
 * 清除旧残留）；两个序号各自独立解析。
 *
 * 与 Core HealthStatusDeriver 同口径，改一处必须同步另一处。
 */
export function parseDisabilityCertificate(
  certificateNo: string | null | undefined,
): DisabilityCertificateInfo {
  const text = (certificateNo ?? '').trim()
  if (text.length < 2) return { type: '', level: '' }

  const typeIndex = Number(text[text.length - 2]) - 1 // '1' → 0 … '7' → 6
  const levelIndex = Number(text[text.length - 1]) - 1 // '1' → 0 … '4' → 3

  return {
    type:
      typeIndex >= 0 && typeIndex < DISABILITY_TYPE_OPTIONS.length
        ? DISABILITY_TYPE_OPTIONS[typeIndex].value
        : '',
    level:
      levelIndex >= 0 && levelIndex < DISABILITY_LEVEL_OPTIONS.length
        ? DISABILITY_LEVEL_OPTIONS[levelIndex].value
        : '',
  }
}

export interface DeriveHealthStatusInput {
  /** 一级疾病（疾病分类）表单值。 */
  diseaseCategory: string | null | undefined
  /** 残疾类型表单值（视力/听力/…）。 */
  disabilityType: string | null | undefined
  /** 残疾等级表单值（一级/二级/三级/四级）。 */
  disabilityLevel: string | null | undefined
  /** 年龄：取自身份证解析（validation.ts parseIdCard）；失败/null 视为 <60。 */
  age: number | null | undefined
  /** 人工/勾选的「是否重病」。 */
  isSevereDisease: boolean | null | undefined
  /** 当前 healthStatus：用于覆盖守卫（非可覆盖档位的历史值原样保留）。 */
  currentHealthStatus: string | null | undefined
}

/**
 * 派生身体状况（表单值口径）：
 * - hasDisease = diseaseCategory 非空且 ≠ '无任何疾病'；
 * - hasDisability = disabilityType 非空；重残 = 有残疾且等级 ∈ {一级, 二级}；
 * - 病+重残 → '重病且重残'；仅病 → '重病'；仅重残 → '重残'；
 *   有残疾非重残 / age ≥ 60 → FAIR（一般或较弱）；否则 → HEALTHY（健康或良好）；
 * - 地板：isSevereDisease = true 且结果 ∈ {HEALTHY, FAIR} → '重病'；
 * - 覆盖守卫：仅当前值 ∈ {'', HEALTHY, FAIR, '重病', '重残', '重病且重残'} 才写入结果，
 *   其它历史值（患病/残疾等）原样返回。
 *
 * ⚠️ 与后端 Core HealthStatusDeriver 同口径，改一处必须同步另一处。
 */
export function deriveHealthStatus(input: DeriveHealthStatusInput): string {
  const current = normalizeHealthStatus(input.currentHealthStatus)
  const diseaseCategory = (input.diseaseCategory ?? '').trim()
  const disabilityType = normalizeDisabilityType(input.disabilityType)
  const disabilityLevel = normalizeDisabilityLevel(input.disabilityLevel)
  const isSevere = Boolean(input.isSevereDisease)
  const age =
    typeof input.age === 'number' && Number.isFinite(input.age) ? input.age : null

  const hasDisease = diseaseCategory !== '' && diseaseCategory !== NO_DISEASE_CATEGORY
  const hasDisability = disabilityType !== ''
  const hasSevereDisability =
    hasDisability && SEVERE_DISABILITY_LEVELS.includes(disabilityLevel)
  const hasNonSevereDisability = hasDisability && !hasSevereDisability

  let derived: string
  if (hasDisease && hasSevereDisability) derived = SEVERE_DISEASE_AND_DISABILITY
  else if (hasDisease) derived = SEVERE_DISEASE
  else if (hasSevereDisability) derived = SEVERE_DISABILITY
  else if (hasNonSevereDisability) derived = FAIR
  else if (age !== null && age >= 60) derived = FAIR
  else derived = HEALTHY

  // 人工判定「是否重病」优先：勾选时推导结果不低于「重病」。
  if (isSevere && (derived === HEALTHY || derived === FAIR)) derived = SEVERE_DISEASE

  return OVERWRITABLE_HEALTH_STATUSES.has(current) ? derived : current
}
