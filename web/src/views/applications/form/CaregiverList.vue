<script setup lang="ts">
import { computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete } from '@element-plus/icons-vue'
import NcCard from '@/components/NcCard.vue'
import {
  EDUCATION_LEVEL_OPTIONS,
  EMPLOYMENT_STATUS_OPTIONS,
  ETHNICITY_OPTIONS,
  HEALTH_STATUS_OPTIONS,
  MARITAL_STATUS_OPTIONS,
  POLITICAL_STATUS_OPTIONS,
  RELATION_OPTIONS,
  type SelectOption,
} from './options'
import type { ApplicationFormCaregiver, ApplicationFormModel } from './types'
import { getIdCardError, normalizeIdCard, parseIdCard } from './validation'

const props = defineProps<{ form: ApplicationFormModel; busy?: boolean }>()

const caregivers = computed<ApplicationFormCaregiver[]>(() => props.form.caregivers ?? [])

/** 旧数据可能存英文关系 key（Head/Son/Spouse…），仅做只读显示映射，不改后端数据。 */
const RELATION_KEY_LABELS: Record<string, string> = {
  Head: '本人/户主',
  Spouse: '配偶',
  Husband: '配偶',
  Wife: '配偶',
  Son: '子/婿',
  Daughter: '女/媳',
  Child: '子/婿',
  Grandchild: '孙子女/外孙子女',
  Parent: '父母/岳父母/公婆',
  Father: '父母/岳父母/公婆',
  Mother: '父母/岳父母/公婆',
  Grandparent: '祖父母/外祖父母',
  Sibling: '兄弟姐妹',
  Brother: '兄弟姐妹',
  Sister: '兄弟姐妹',
  Other: '其他',
  // 旧中文口径（历史数据/默认值）也归一为新版显示值
  户主: '本人/户主',
  本人: '本人/户主',
  子女: '子/婿',
  儿子: '子/婿',
  女儿: '女/媳',
  孙子女: '孙子女/外孙子女',
  父母: '父母/岳父母/公婆',
  祖父母: '祖父母/外祖父母',
}

const relationKeyLabelsLower = Object.fromEntries(
  Object.entries(RELATION_KEY_LABELS).map(([key, label]) => [key.toLowerCase(), label]),
)

function relationDisplay(value: string | null | undefined): string {
  const text = (value ?? '').trim()
  if (!text) return ''
  return RELATION_KEY_LABELS[text] ?? relationKeyLabelsLower[text.toLowerCase()] ?? text
}

/**
 * 关系下拉与家庭成员保持一致；若回显值是英文 key，补一个中文 label 的临时选项，
 * 避免 el-select 直接显示 “Head/Son” 这类代码。
 */
function relationOptions(caregiver: ApplicationFormCaregiver): SelectOption[] {
  const current = (caregiver.relationship ?? '').trim()
  if (!current) return RELATION_OPTIONS
  if (RELATION_OPTIONS.some((item) => item.value === current)) return RELATION_OPTIONS
  return [...RELATION_OPTIONS, { label: relationDisplay(current), value: current }]
}

/** 性别 + 年龄合并为只读展示，由身份证号自动带出。 */
function genderAgeDisplay(caregiver: ApplicationFormCaregiver): string {
  const parts: string[] = []
  if (caregiver.gender) parts.push(caregiver.gender)
  if (caregiver.age !== null && caregiver.age !== undefined) {
    parts.push(`${caregiver.age} 岁`)
  }
  return parts.length > 0 ? parts.join(' ') : '—'
}

/* 行 key：避免新增/删除时复用错误的输入框。 */
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

/** 新增照料人默认 caredMemberId=0，表示暂不指定被照料成员。 */
function createCaregiver(): ApplicationFormCaregiver {
  return {
    id: 0,
    caredMemberId: 0,
    name: '',
    idCard: '',
    phone: '',
    relationship: '子/婿',
    gender: '',
    age: null,
    ethnicity: 'Han',
    maritalStatus: '',
    hukouType: 'Rural',
    educationLevel: '',
    politicalStatus: '',
    healthStatus: '健康或良好',
    employmentStatus: '',
    mainIncomeSource: '',
    workUnit: '',
    position: '',
    address: '',
    bankName: '',
    bankAccount: '',
  }
}

function addCaregiver(): void {
  caregivers.value.push(createCaregiver())
}

async function removeCaregiver(caregiver: ApplicationFormCaregiver): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定删除照料人「${caregiver.name || '未命名'}」吗？保存草稿后生效。`,
      '删除照料人',
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

  const index = caregivers.value.indexOf(caregiver)
  if (index >= 0) caregivers.value.splice(index, 1)
}

/** 身份证号失焦时归一格式，并自动带出性别、年龄。 */
function handleIdCardBlur(caregiver: ApplicationFormCaregiver): void {
  caregiver.idCard = normalizeIdCard(caregiver.idCard)
  if (!caregiver.idCard) return

  const error = getIdCardError(caregiver.idCard, false)
  if (error) {
    ElMessage.warning(`身份证号：${error}`)
    return
  }

  const info = parseIdCard(caregiver.idCard)
  if (!info) return
  caregiver.gender = info.gender
  caregiver.age = info.age
}
</script>

<template>
  <NcCard title="🤝 照料人信息">
    <template #actions>
      <el-button type="primary" :disabled="busy" @click="addCaregiver">
        + 照料人
      </el-button>
    </template>

    <el-empty
      v-if="caregivers.length === 0"
      description="暂无照料人，点击右上角“+ 照料人”添加"
      :image-size="64"
    />

    <div v-else class="caregiver-list">
      <article v-for="(caregiver, index) in caregivers" :key="rowKey(caregiver)" class="caregiver-card">
        <header class="caregiver-card__head">
          <div class="caregiver-card__title">
            <span class="caregiver-index">照料人 {{ index + 1 }}</span>
            <span v-if="caregiver.relationship" class="badge">
              {{ relationDisplay(caregiver.relationship) }}
            </span>
            <strong class="caregiver-name">{{ caregiver.name || '未命名照料人' }}</strong>
          </div>
          <el-button
            size="small"
            text
            type="danger"
            :icon="Delete"
            :disabled="busy"
            @click="removeCaregiver(caregiver)"
          >
            删除
          </el-button>
        </header>

        <h3 class="caregiver-section">个人信息</h3>
        <div class="caregiver-grid">
          <label class="field">
            <span class="field__label">家庭关系</span>
            <el-select
              v-model="caregiver.relationship"
              filterable
              allow-create
              default-first-option
              clearable
              :disabled="busy"
            >
              <el-option
                v-for="item in relationOptions(caregiver)"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </label>

          <label class="field">
            <span class="field__label">姓名 <i>*</i></span>
            <el-input
              v-model="caregiver.name"
              maxlength="30"
              placeholder="姓名"
              clearable
              :disabled="busy"
            />
          </label>

          <label class="field">
            <span class="field__label">身份证号</span>
            <el-input
              v-model="caregiver.idCard"
              maxlength="18"
              placeholder="18 位身份证号"
              clearable
              :disabled="busy"
              @blur="handleIdCardBlur(caregiver)"
            />
          </label>

          <label class="field">
            <span class="field__label">性别+年龄</span>
            <el-input
              :model-value="genderAgeDisplay(caregiver)"
              readonly
              placeholder="身份证自动带出"
            />
          </label>

          <label class="field">
            <span class="field__label">联系方式</span>
            <el-input
              v-model="caregiver.phone"
              maxlength="20"
              placeholder="手机或固定电话"
              clearable
              :disabled="busy"
            />
          </label>

          <label class="field">
            <span class="field__label">民族</span>
            <el-select v-model="caregiver.ethnicity" :disabled="busy">
              <el-option
                v-for="item in ETHNICITY_OPTIONS"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </label>

          <label class="field">
            <span class="field__label">婚姻状况</span>
            <el-select
              v-model="caregiver.maritalStatus"
              filterable
              clearable
              :disabled="busy"
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
            <span class="field__label">文化程度</span>
            <el-select
              v-model="caregiver.educationLevel"
              filterable
              clearable
              :disabled="busy"
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
            <span class="field__label">政治面貌</span>
            <el-select
              v-model="caregiver.politicalStatus"
              filterable
              clearable
              :disabled="busy"
            >
              <el-option
                v-for="item in POLITICAL_STATUS_OPTIONS"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </label>
        </div>

        <h3 class="caregiver-section">健康状况</h3>
        <div class="caregiver-grid">
          <label class="field">
            <span class="field__label">健康状况</span>
            <el-select
              v-model="caregiver.healthStatus"
              filterable
              allow-create
              default-first-option
              clearable
              :disabled="busy"
            >
              <el-option
                v-for="item in HEALTH_STATUS_OPTIONS"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </label>

          <label class="field">
            <span class="field__label">就业状况</span>
            <el-select
              v-model="caregiver.employmentStatus"
              filterable
              allow-create
              default-first-option
              clearable
              :disabled="busy"
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
            <span class="field__label">收入来源</span>
            <el-input
              v-model="caregiver.mainIncomeSource"
              maxlength="60"
              placeholder="如：务农、务工、养老金"
              clearable
              :disabled="busy"
            />
          </label>

          <label class="field">
            <span class="field__label">工作单位</span>
            <el-input
              v-model="caregiver.workUnit"
              maxlength="120"
              placeholder="工作单位名称"
              clearable
              :disabled="busy"
            />
          </label>

          <label class="field">
            <span class="field__label">开户行</span>
            <el-input
              v-model="caregiver.bankName"
              maxlength="120"
              placeholder="银行及支行名称"
              clearable
              :disabled="busy"
            />
          </label>

          <label class="field">
            <span class="field__label">银行卡号</span>
            <el-input
              v-model="caregiver.bankAccount"
              maxlength="40"
              placeholder="补贴发放账号"
              clearable
              :disabled="busy"
            />
          </label>

          <label class="field field--full">
            <span class="field__label">家庭住址</span>
            <el-input
              v-model="caregiver.address"
              maxlength="200"
              placeholder="照料人家庭住址"
              clearable
              :disabled="busy"
            />
          </label>
        </div>
      </article>
    </div>
  </NcCard>
</template>

<style scoped>
.caregiver-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.caregiver-card {
  padding: 14px 15px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-amber);
  background-color: var(--nc-paper-soft);
}

.caregiver-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.caregiver-card__title {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.caregiver-index {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.caregiver-name {
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

.caregiver-section {
  margin: 14px 0 10px;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--nc-line-faint);
  color: var(--nc-ink-2);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-sm);
  font-weight: 700;
  letter-spacing: 0.04em;
}

.caregiver-grid {
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

.field :deep(.el-select),
.field :deep(.el-input-number) {
  width: 100%;
}

@media (max-width: 767px) {
  .caregiver-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .field--full {
    grid-column: auto;
  }

  .caregiver-card__head {
    flex-direction: column;
  }
}
</style>
