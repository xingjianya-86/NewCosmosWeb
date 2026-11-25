<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Plus } from '@element-plus/icons-vue'
import NcCard from '@/components/NcCard.vue'
import { formatMoney } from '../format'
import { createMember, createSupporter } from './formModel'
import {
  DISABILITY_LEVEL_OPTIONS,
  DISABILITY_TYPE_OPTIONS,
  DISEASE_CATEGORY_OPTIONS,
  EDUCATION_LEVEL_OPTIONS,
  EMPLOYMENT_STATUS_OPTIONS,
  ETHNICITY_OPTIONS,
  GENDER_OPTIONS,
  HEALTH_STATUS_OPTIONS,
  HUKOU_TYPE_OPTIONS,
  MARITAL_STATUS_OPTIONS,
  PERSON_TYPE_OPTIONS,
  POLITICAL_STATUS_OPTIONS,
  RELATION_OPTIONS,
  SELF_CARE_ABILITY_OPTIONS,
  SUPPORT_RELATION_OPTIONS,
} from './options'
import type {
  ApplicationFormMember,
  ApplicationFormModel,
  ApplicationFormSupporter,
} from './types'
import { ageFromBirthDate, getIdCardError, normalizeIdCard, parseIdCard } from './validation'

const props = defineProps<{
  form: ApplicationFormModel
}>()

/**
 * 赡养卡新增字段（supportMonths/familySize）由后端 DTO 末尾追加，
 * 共享 types.ts 不在本次改动范围，这里做局部扩展读取。
 */
type SupporterRow = ApplicationFormSupporter & {
  supportMonths?: number | null
  familySize?: number | null
  personType?: string | null
  monthlyIncomeCapacity?: number | null
  phone?: string | null
  address?: string | null
}

const members = computed(() => props.form.members)
const supporters = computed(() => props.form.supporters as SupporterRow[])

/**
 * 申请表家庭人口（含户主，年赡养费乘数）：
 * 优先使用表单上已有的 familySize；否则按共同生活成员数计算，无户主行时 +1。
 * 后端 form.members 已排除赡养行，户主 + 共同生活成员都在其中。
 */
const householdFamilySize = computed(() => {
  const declared = (props.form as ApplicationFormModel & { familySize?: number | null }).familySize
  if (typeof declared === 'number' && Number.isFinite(declared) && declared > 0) {
    return Math.trunc(declared)
  }

  const list = members.value
  if (list.length === 0) return 1
  const hasHouseholdHead = list.some((item) => item.isHouseholdHead || item.isApplicant)
  return hasHouseholdHead ? list.length : list.length + 1
})

/** 月数缺省 12（旧版默认口径）。 */
function supportMonthsOf(supporter: SupporterRow): number {
  const value = Number(supporter.supportMonths)
  return Number.isFinite(value) && value > 0 ? Math.trunc(value) : 12
}

/** 年赡养费只读派生：round(月费 × 月数 × 申请表家庭人口, 2)，与 legacy Supporter 同口径。 */
function annualSupportFeeOf(supporter: SupporterRow): number {
  const monthly = Number(supporter.monthlySupportFee ?? 0)
  if (!Number.isFinite(monthly) || monthly <= 0) return 0
  return Math.round(monthly * supportMonthsOf(supporter) * householdFamilySize.value * 100) / 100
}

/** 记录关闭赡养能力前的月费，重新打开时恢复（不覆盖用户重填值）。 */
const supportFeeBackup = new WeakMap<object, number>()

function syncSupporterFees(supporter: SupporterRow): void {
  // 旧数据兼容：没有月数字段且年费>0、月费=0 → 月费=round(年费/12,2)、月数=12。
  if (!(Number(supporter.supportMonths) > 0)) {
    if (
      Number(supporter.monthlySupportFee ?? 0) <= 0
      && Number(supporter.annualSupportFee ?? 0) > 0
    ) {
      supporter.monthlySupportFee =
        Math.round((Number(supporter.annualSupportFee) / 12) * 100) / 100
    }
    supporter.supportMonths = 12
  }

  const annual = annualSupportFeeOf(supporter)
  if (supporter.annualSupportFee !== annual) supporter.annualSupportFee = annual
}

function handleMonthlySupportFeeChange(
  supporter: SupporterRow,
  value: number | null | undefined,
): void {
  supporter.monthlySupportFee = value ?? null
  syncSupporterFees(supporter)
}

function handleSupportMonthsChange(
  supporter: SupporterRow,
  value: number | null | undefined,
): void {
  supporter.supportMonths = value !== null && value !== undefined && value > 0 ? Math.trunc(value) : 12
  syncSupporterFees(supporter)
}

function handleSupportAbilityChange(
  supporter: SupporterRow,
  value: boolean | string | number,
): void {
  const enabled = value === true
  supporter.isSupportAbility = enabled

  if (enabled) {
    if (Number(supporter.monthlySupportFee ?? 0) <= 0) {
      const backup = supportFeeBackup.get(supporter)
      if (backup !== undefined) supporter.monthlySupportFee = backup
    }
    supportFeeBackup.delete(supporter)
  } else {
    const monthly = Number(supporter.monthlySupportFee ?? 0)
    if (monthly > 0) supportFeeBackup.set(supporter, monthly)
    supporter.monthlySupportFee = 0
  }

  syncSupporterFees(supporter)
}

/** 户主（申请人）信息只在第 1 步维护，旧版共同生活成员列表排除户主。 */
const visibleMembers = computed(() =>
  members.value.filter((item) => !item.isHouseholdHead && !item.isApplicant),
)

const duplicateIdCards = computed<Set<string>>(() => {
  const counts = new Map<string, number>()
  for (const member of members.value) {
    const idCard = normalizeIdCard(member.idCard)
    if (!idCard) continue
    counts.set(idCard, (counts.get(idCard) ?? 0) + 1)
  }
  const duplicates = new Set<string>()
  counts.forEach((count, idCard) => {
    if (count > 1) duplicates.add(idCard)
  })
  return duplicates
})

/* 行 key：避免新增/删除时复用错误输入框；WeakMap 保证同一成员稳定。 */
let keySeed = 0
const keyMap = new WeakMap<object, number>()
function rowKey(row: object): number {
  let key = keyMap.get(row)
  if (key === undefined) {
    keySeed += 1
    key = keySeed
    keyMap.set(row, key)
  }
  return key
}

/** 记录每个户主上次从第 1 步自动同步的值；页面内切步骤时不覆盖手工修改。 */
const headSyncState = new WeakMap<object, { name: string; idCard: string }>()

function syncIdentityFromIdCard(member: ApplicationFormMember): boolean {
  const info = parseIdCard(member.idCard)
  if (!info) return false
  member.gender = info.gender
  member.birthDate = info.birthDate
  member.age = info.age
  return true
}

function handleMemberIdBlur(member: ApplicationFormMember): void {
  member.idCard = normalizeIdCard(member.idCard)
  syncIdentityFromIdCard(member)
  const issue = memberIdIssue(member)
  if (issue) ElMessage.warning(issue)
}

function handleBirthDateChange(member: ApplicationFormMember): void {
  const age = ageFromBirthDate(member.birthDate)
  if (age !== null) member.age = age
}

function memberIdIssue(member: ApplicationFormMember): string {
  const idCard = normalizeIdCard(member.idCard)
  if (!idCard) return ''
  const error = getIdCardError(idCard, false)
  if (error) return error
  if (duplicateIdCards.value.has(idCard)) return '身份证号与家庭其他成员重复'
  return ''
}

function setLaborAbility(member: ApplicationFormMember, value: string): void {
  member.isLaborExempt = value === 'none'
}

function handleDisabilityTypeChange(member: ApplicationFormMember, value: string): void {
  member.hasDisability = Boolean(value)
  if (!value) member.disabilityLevel = ''
}

function handleDiseaseInput(member: ApplicationFormMember): void {
  member.isSevereDisease = Boolean(member.diseaseName.trim())
}

function addMember(): void {
  members.value.push(createMember({ relation: '其他' }))
}

async function removeMember(member: ApplicationFormMember): Promise<void> {
  if (member.isHouseholdHead || member.isApplicant) {
    ElMessage.warning('户主信息请在第 1 步修改，不能在此删除')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确定删除成员「${member.name || '未命名'}」吗？删除后需重新保存草稿。`,
      '删除成员',
      {
        type: 'warning',
        confirmButtonText: '删除',
        cancelButtonText: '取消',
        confirmButtonClass: 'el-button--danger',
      },
    )
  } catch {
    return
  }

  const index = members.value.indexOf(member)
  if (index < 0) return

  members.value.splice(index, 1)
}

/** 自动/手动把第 1 步申请人同步到户主。 */
function syncHeadFromApplicant(): void {
  const basic = props.form.basic
  const name = basic.applicantName.trim()
  const idCard = normalizeIdCard(basic.applicantIdCard)

  for (const member of members.value) {
    if (!member.isHouseholdHead && !member.isApplicant) continue
    const previous = headSyncState.get(member) ?? { name: '', idCard: '' }
    /** 申请人成员以第 1 步为准；普通户主仅在未被手工改名时跟随。 */
    const followApplicant = member.isApplicant

    if (followApplicant || !member.name || member.name === previous.name) {
      member.name = name
    }
    if (followApplicant || !member.idCard || member.idCard === previous.idCard) {
      member.idCard = idCard
      syncIdentityFromIdCard(member)
    }
    if (!member.gender && basic.gender) member.gender = basic.gender
    if (!member.phone && basic.phone) member.phone = basic.phone

    headSyncState.set(member, { name: member.name, idCard: member.idCard })
  }
}

/** 载入的旧表单若没有户主，则把申请人或第一位成员补为户主。 */
function ensureHouseholdHead(): void {
  if (members.value.length === 0) return
  if (members.value.some((item) => item.isHouseholdHead)) return
  const head = members.value.find((item) => item.isApplicant) ?? members.value[0]
  head.isHouseholdHead = true
  if (!head.relation || head.relation === '其他') head.relation = '户主'
}

function addSupporter(): void {
  supporters.value.push(createSupporter())
}

async function removeSupporter(supporter: ApplicationFormSupporter): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定删除赡养人「${supporter.name || '未命名'}」吗？`,
      '删除赡养人',
      {
        type: 'warning',
        confirmButtonText: '删除',
        cancelButtonText: '取消',
        confirmButtonClass: 'el-button--danger',
      },
    )
  } catch {
    return
  }

  const index = supporters.value.indexOf(supporter)
  if (index >= 0) supporters.value.splice(index, 1)
}

/** 月费/月数/家庭人口任一变化都重算年费；外部换表单（加载/分类回填）也走同一入口。 */
watch(
  [supporters, householdFamilySize],
  () => {
    for (const supporter of supporters.value) syncSupporterFees(supporter)
  },
  { deep: true, immediate: true },
)

watch(
  () => [props.form.basic.applicantName, props.form.basic.applicantIdCard],
  () => syncHeadFromApplicant(),
)

onMounted(() => {
  ensureHouseholdHead()
  syncHeadFromApplicant()
})
</script>

<template>
  <div class="step-members">
    <NcCard title="共同生活成员">
      <template #actions>
        <el-button type="primary" :icon="Plus" @click="addMember">新增人员</el-button>
      </template>

      <el-empty v-if="visibleMembers.length === 0" description="暂无共同生活成员，点击右上角“新增人员”添加">
        <el-button type="primary" :icon="Plus" @click="addMember">新增人员</el-button>
      </el-empty>

      <div v-else class="member-list">
        <article
          v-for="(member, index) in visibleMembers"
          :key="rowKey(member)"
          class="member-card"
        >
          <header class="member-card__head">
            <div class="member-card__title">
              <span class="member-index">成员 {{ index + 1 }}</span>
              <span v-if="member.hasDisability" class="badge badge--warn">残疾</span>
              <span v-if="member.isSevereDisease" class="badge badge--warn">重病</span>
              <strong class="member-name">{{ member.name || '未命名成员' }}</strong>
            </div>

            <div class="member-card__actions">
              <el-button
                size="small"
                text
                type="danger"
                :icon="Delete"
                @click="removeMember(member)"
              >
                删除
              </el-button>
            </div>
          </header>

          <section class="member-section">
            <h4 class="member-section__title">个人信息</h4>
            <div class="member-grid">
              <label class="field">
                <span class="field__label">姓名 <i>*</i></span>
                <el-input v-model="member.name" maxlength="30" placeholder="请输入成员姓名" clearable />
              </label>

              <label class="field" :class="{ 'field--error': Boolean(memberIdIssue(member)) }">
                <span class="field__label">身份证号</span>
                <el-input
                  v-model="member.idCard"
                  maxlength="18"
                  placeholder="请输入 18 位身份证号"
                  clearable
                  @blur="handleMemberIdBlur(member)"
                />
              </label>

              <label class="field">
                <span class="field__label">与户主关系</span>
                <el-select v-model="member.relation" filterable allow-create default-first-option>
                  <el-option
                    v-for="item in RELATION_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">性别</span>
                <el-select v-model="member.gender" clearable placeholder="身份证自动带出">
                  <el-option
                    v-for="item in GENDER_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">出生日期</span>
                <el-date-picker
                  v-model="member.birthDate"
                  type="date"
                  value-format="YYYY-MM-DD"
                  placeholder="请选择出生日期"
                  @change="handleBirthDateChange(member)"
                />
              </label>

              <label class="field">
                <span class="field__label">年龄</span>
                <el-input-number
                  :model-value="member.age"
                  :min="0"
                  :max="130"
                  :controls="false"
                  placeholder="岁"
                  @update:model-value="member.age = $event ?? null"
                />
              </label>

              <label class="field">
                <span class="field__label">民族</span>
                <el-select v-model="member.ethnicity" filterable allow-create default-first-option clearable>
                  <el-option
                    v-for="item in ETHNICITY_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">文化程度</span>
                <el-select
                  v-model="member.educationLevel"
                  filterable
                  allow-create
                  default-first-option
                  clearable
                  placeholder="请选择或输入文化程度"
                >
                  <el-option
                    v-for="item in EDUCATION_LEVEL_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">婚姻状况</span>
                <el-select
                  v-model="member.maritalStatus"
                  filterable
                  allow-create
                  default-first-option
                  clearable
                  placeholder="请选择或输入婚姻状况"
                >
                  <el-option
                    v-for="item in MARITAL_STATUS_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">政治面貌</span>
                <el-select
                  v-model="member.politicalStatus"
                  filterable
                  allow-create
                  default-first-option
                  clearable
                  placeholder="请选择或输入政治面貌"
                >
                  <el-option
                    v-for="item in POLITICAL_STATUS_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">联系电话</span>
                <el-input v-model="member.phone" maxlength="20" placeholder="请输入手机号或固定电话" clearable />
              </label>

              <label class="field">
                <span class="field__label">户口性质</span>
                <el-select v-model="member.hukouType" placeholder="请选择户口性质">
                  <el-option
                    v-for="item in HUKOU_TYPE_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>
            </div>
            <p v-if="memberIdIssue(member)" class="field-error">{{ memberIdIssue(member) }}</p>
          </section>

          <section class="member-section">
            <h4 class="member-section__title">健康、就业与收入</h4>
            <div class="member-grid">
              <label class="field">
                <span class="field__label">健康状态</span>
                <el-select v-model="member.healthStatus" filterable allow-create default-first-option>
                  <el-option
                    v-for="item in HEALTH_STATUS_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">就业状态</span>
                <el-select
                  v-model="member.employmentStatus"
                  filterable
                  allow-create
                  default-first-option
                  clearable
                  placeholder="请选择或输入就业状态"
                >
                  <el-option
                    v-for="item in EMPLOYMENT_STATUS_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">劳动能力</span>
                <el-select
                  :model-value="member.isLaborExempt ? 'none' : 'full'"
                  @change="setLaborAbility(member, $event)"
                >
                  <el-option label="有劳动能力" value="full" />
                  <el-option label="无劳动能力" value="none" />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">残疾类型</span>
                <el-select
                  v-model="member.disabilityType"
                  clearable
                  placeholder="无残疾可留空"
                  @change="handleDisabilityTypeChange(member, $event)"
                >
                  <el-option
                    v-for="item in DISABILITY_TYPE_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">残疾等级</span>
                <el-select
                  v-model="member.disabilityLevel"
                  clearable
                  :disabled="!member.hasDisability"
                  placeholder="无残疾可留空"
                >
                  <el-option
                    v-for="item in DISABILITY_LEVEL_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">残疾证号</span>
                <el-input
                  v-model="member.disabilityCertificateNo"
                  maxlength="30"
                  placeholder="无残疾可留空"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field__label">重病病种</span>
                <el-input
                  v-model="member.diseaseName"
                  maxlength="60"
                  placeholder="无重病可留空"
                  clearable
                  @input="handleDiseaseInput(member)"
                />
              </label>

              <label class="field">
                <span class="field__label">疾病分类</span>
                <el-select
                  v-model="member.diseaseCategory"
                  filterable
                  allow-create
                  default-first-option
                  clearable
                  placeholder="请选择或输入疾病分类"
                >
                  <el-option
                    v-for="item in DISEASE_CATEGORY_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">前 12 个月医疗自付（元）</span>
                <el-input-number
                  :model-value="member.medicalSelfPaid12m"
                  :min="0"
                  :precision="2"
                  :controls="false"
                  placeholder="0.00"
                  @update:model-value="member.medicalSelfPaid12m = $event ?? null"
                />
              </label>

              <label class="field">
                <span class="field__label">工作单位</span>
                <el-input v-model="member.workUnit" maxlength="60" placeholder="无工作单位可留空" clearable />
              </label>

              <label class="field">
                <span class="field__label">月收入（元）</span>
                <el-input-number
                  :model-value="member.monthlyIncomeCapacity"
                  :min="0"
                  :precision="2"
                  :controls="false"
                  placeholder="0.00"
                  @update:model-value="member.monthlyIncomeCapacity = $event ?? null"
                />
              </label>
            </div>
          </section>

          <section class="member-section">
            <h4 class="member-section__title">户籍与居住地址</h4>
            <div class="member-grid">
              <label class="field field--full">
                <span class="field__label">户籍地址</span>
                <el-input
                  v-model="member.hukouAddress"
                  maxlength="120"
                  placeholder="请输入户籍所在地址"
                  clearable
                />
              </label>

              <label class="field field--full">
                <span class="field__label">现居住地址</span>
                <el-input
                  v-model="member.homeAddress"
                  maxlength="120"
                  placeholder="请输入现居住地址"
                  clearable
                />
              </label>
            </div>
          </section>

          <section class="member-section">
            <h4 class="member-section__title">人员认定</h4>
            <div class="member-grid">
              <label class="field">
                <span class="field__label">人员类型</span>
                <el-select
                  v-model="member.personType"
                  filterable
                  allow-create
                  default-first-option
                  clearable
                  placeholder="请选择或输入人员类型"
                >
                  <el-option
                    v-for="item in PERSON_TYPE_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field field--switch">
                <span class="field__label">单人保</span>
                <el-switch
                  v-model="member.isSingleRescue"
                  active-text="是"
                  inactive-text="否"
                  inline-prompt
                />
              </label>

              <label class="field">
                <span class="field__label">自理能力</span>
                <el-select
                  v-model="member.selfCareAbility"
                  filterable
                  allow-create
                  default-first-option
                  clearable
                  placeholder="请选择或输入自理能力"
                >
                  <el-option
                    v-for="item in SELF_CARE_ABILITY_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>
            </div>
          </section>
        </article>
      </div>
    </NcCard>

    <NcCard title="赡养抚养扶养">
      <template #actions>
        <el-button type="primary" :icon="Plus" @click="addSupporter">新增人员</el-button>
      </template>

      <el-empty v-if="supporters.length === 0" description="暂无赡养抚养扶养人员，点击右上角“新增人员”添加">
        <el-button type="primary" :icon="Plus" @click="addSupporter">新增人员</el-button>
      </el-empty>

      <div v-else class="supporter-list">
        <article
          v-for="(supporter, index) in supporters"
          :key="rowKey(supporter)"
          class="supporter-card"
        >
          <header class="member-card__head">
            <div class="member-card__title">
              <span class="member-index">赡养人 {{ index + 1 }}</span>
              <strong class="member-name">{{ supporter.name || '未命名' }}</strong>
            </div>
            <el-button size="small" text type="danger" :icon="Delete" @click="removeSupporter(supporter)">
              删除
            </el-button>
          </header>

          <section class="member-section">
            <h4 class="member-section__title">个人信息</h4>
            <div class="supporter-grid">
              <label class="field">
                <span class="field__label">姓名</span>
                <el-input v-model="supporter.name" maxlength="30" placeholder="请输入姓名" clearable />
              </label>

              <label class="field">
                <span class="field__label">身份证号</span>
                <el-input v-model="supporter.idCard" maxlength="18" placeholder="请输入 18 位身份证号" clearable />
              </label>

              <label class="field">
                <span class="field__label">关系</span>
                <el-select v-model="supporter.relation" filterable allow-create default-first-option>
                  <el-option
                    v-for="item in SUPPORT_RELATION_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">性别</span>
                <el-select v-model="supporter.gender" clearable placeholder="请选择性别">
                  <el-option
                    v-for="item in GENDER_OPTIONS"
                    :key="item.value"
                    :label="item.label"
                    :value="item.value"
                  />
                </el-select>
              </label>

              <label class="field">
                <span class="field__label">年龄</span>
                <el-input-number
                  :model-value="supporter.age"
                  :min="0"
                  :max="130"
                  :controls="false"
                  placeholder="岁"
                  @update:model-value="supporter.age = $event ?? null"
                />
              </label>
            </div>
          </section>

          <section class="member-section">
            <h4 class="member-section__title">赡养费用</h4>
            <div class="supporter-grid">
              <label class="field">
                <span class="field__label">月赡养费（元）</span>
                <el-input-number
                  :model-value="supporter.monthlySupportFee"
                  :min="0"
                  :precision="2"
                  :controls="false"
                  placeholder="0.00"
                  @update:model-value="handleMonthlySupportFeeChange(supporter, $event)"
                />
              </label>

              <label class="field">
                <span class="field__label">月数</span>
                <el-input-number
                  :model-value="supportMonthsOf(supporter)"
                  :min="1"
                  :max="12"
                  :precision="0"
                  :controls="false"
                  placeholder="12"
                  @update:model-value="handleSupportMonthsChange(supporter, $event)"
                />
              </label>

              <label class="field">
                <span class="field__label">家庭人口（申请表）</span>
                <el-input :model-value="String(householdFamilySize)" readonly />
              </label>

              <label class="field">
                <span class="field__label">年赡养费（元，自动计算）</span>
                <el-input :model-value="formatMoney(supporter.annualSupportFee)" readonly />
              </label>

              <label class="field field--switch">
                <span class="field__label">是否有赡养能力</span>
                <el-switch
                  :model-value="supporter.isSupportAbility"
                  active-text="有"
                  inactive-text="无"
                  inline-prompt
                  @change="handleSupportAbilityChange(supporter, $event)"
                />
              </label>
            </div>
            <p v-if="!supporter.isSupportAbility" class="support-note">
              无赡养能力：月费已清零，不计入赡养收入；重新打开能力开关会恢复关闭前的月费。
            </p>
          </section>
        </article>
      </div>
    </NcCard>
  </div>
</template>

<style scoped>
.step-members {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.member-list,
.supporter-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.member-card,
.supporter-card {
  padding: 14px 15px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-line-strong);
  background-color: var(--nc-paper-soft);
}

.member-card.is-head {
  border-left-color: var(--nc-seal);
}

.member-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.member-card__title {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.member-index {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.1em;
}

.member-name {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.badge {
  display: inline-flex;
  align-items: center;
  padding: 1px 7px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.6;
  white-space: nowrap;
}

.badge--head {
  border-color: rgba(171, 50, 39, 0.32);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.badge--warn {
  border-color: rgba(150, 104, 28, 0.3);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.member-card__actions {
  display: flex;
  flex: none;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 2px;
}

.member-section {
  padding-top: 12px;
}

.member-section + .member-section {
  margin-top: 4px;
  border-top: 1px dashed var(--nc-line-faint);
}

.member-section__title {
  margin-bottom: 10px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
}

.member-grid,
.supporter-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 13px 15px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
}

.field--full {
  grid-column: 1 / -1;
}

.field__label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  letter-spacing: 0.02em;
}

.field__label i {
  color: var(--nc-seal);
  font-style: normal;
}

.field--error :deep(.el-input__wrapper) {
  box-shadow: 0 0 0 1px var(--nc-seal) inset;
}

.field--switch {
  justify-content: flex-end;
}

.field :deep(.el-select),
.field :deep(.el-date-editor),
.field :deep(.el-input-number) {
  width: 100%;
}

.field-error {
  margin-top: 9px;
  color: var(--nc-seal);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.support-note {
  margin: 9px 0 0;
  padding: 7px 10px;
  border-left: 2px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

@media (min-width: 1440px) {
  .member-grid,
  .supporter-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .member-grid,
  .supporter-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .member-card__head {
    flex-direction: column;
  }

  .member-card__actions {
    justify-content: flex-start;
    margin-left: -6px;
  }
}
</style>
