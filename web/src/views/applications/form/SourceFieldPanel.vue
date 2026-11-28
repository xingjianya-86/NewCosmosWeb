<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { FolderOpened } from '@element-plus/icons-vue'
import { getAssetSource } from '@/api/applicationForm'
import { getErrorMessage } from '@/api/http'
import { getVillageByCode } from '@/api/regions'
import type { AssetTaskDetail } from '@/types/assetVerification'
import { createMember } from './formModel'
import { APPLICATION_REASON_OPTIONS } from './options'
import type { ApplicationFormModel } from './types'
import { normalizeIdCard, parseIdCard } from './validation'

/**
 * 五步表单右侧「来源数据」侧栏：按路由 query 的 assetCheckId 拉取资产核对详情，
 * 展示 7 个可回填字段并支持逐项/批量填充、一键导入同批次家庭成员。
 *
 * 口径（对齐桌面 ApplicationFormViewModel）：
 * - **只填空**：目标字段已有值时提示不覆盖；
 * - 申请原因落到第 4 步 survey（Web 端该字段在 Step4，非 Step1）；
 * - 社区列存的是**村编码**，先经 by-code 反查「乡镇 + 村社区」名称再写级联，
 *   解析失败只提示、不把编码塞进 AddressCascader；
 * - 申请日期在 Web 端是「编号日期段」只读展示 + 第 1 步改期对话框，此处仅引导不直接改写；
 * - 成员导入排除 isHead 户主行（户主即第 1 步申请人），并按身份证去重。
 */
const props = defineProps<{
  /** 路由 query 传入的资产核对记录 ID；为空时显示原空态。 */
  assetCheckId: string
  form: ApplicationFormModel
  /** 查看/复核模式或表单尚未加载完成时禁用全部写入动作。 */
  disabled?: boolean
}>()

const emit = defineEmits<{ gotoStep: [step: number] }>()

type SourceFieldKey =
  | 'applicantName'
  | 'applicantIdCard'
  | 'contactPhone'
  | 'applicationReason'
  | 'applicationDate'
  | 'familyAddress'
  | 'community'

interface SourceField {
  key: SourceFieldKey
  label: string
  /** 来源原始值（空 = 无值，仅显示「无」并提供“去填写”）。 */
  raw: string
  /** 展示值：申请原因换显示文案、社区换解析后的村名。 */
  display: string
  source: string
  target: string
  step: number
  selected: boolean
}

const loading = ref(false)
const loadError = ref('')
const filling = ref(false)
const fields = ref<SourceField[]>([])
const detail = ref<AssetTaskDetail | null>(null)
/** 社区（村编码）解析结果；null = 非编码或解析失败。 */
const community = ref<{ villageName: string; townName: string } | null>(null)

let loadTicket = 0

const hasSource = computed(() => Boolean(props.assetCheckId.trim()))
const selectableFields = computed(() => fields.value.filter((field) => field.raw))
const allSelected = computed(
  () =>
    selectableFields.value.length > 0 &&
    selectableFields.value.every((field) => field.selected),
)
const selectedCount = computed(
  () => fields.value.filter((field) => field.selected && field.raw).length,
)
const memberCount = computed(
  () => (detail.value?.members ?? []).filter((member) => !member.isHead).length,
)

/** 资产核对的申请原因存的是 code（ToApplicationReasonCode），换算成选项显示文案。 */
function reasonLabel(raw: string): string {
  const value = (raw ?? '').trim()
  if (!value) return ''
  const option = APPLICATION_REASON_OPTIONS.find(
    (item) => item.value === value || item.label === value,
  )
  return option ? option.label : value
}

/** 6~12 位纯数字 = 村编码；已是村名/空值原样返回。 */
function isVillageCode(value: string): boolean {
  return value.length >= 6 && value.length <= 12 && /^\d+$/.test(value)
}

async function buildFields(data: AssetTaskDetail): Promise<void> {
  const rawCommunity = (data.community ?? '').trim()
  let communityDisplay = rawCommunity
  community.value = null
  if (isVillageCode(rawCommunity)) {
    try {
      const village = await getVillageByCode(rawCommunity)
      if (village?.villageName) {
        community.value = {
          villageName: village.villageName,
          townName: (village.townName ?? '').trim(),
        }
        communityDisplay = village.villageName
      }
    } catch {
      // 解析失败：字段仍展示原编码，填充时按“解析失败”提示，不静默丢来源值。
    }
  }

  const applicationDate = String(data.applicationDate ?? '').slice(0, 10)
  const reasonRaw = (data.applicationReason ?? '').trim()

  fields.value = [
    {
      key: 'applicantName',
      label: '申请人姓名',
      raw: (data.applicantName ?? '').trim(),
      display: (data.applicantName ?? '').trim(),
      source: '资产核对',
      target: '第 1 步 基本信息',
      step: 1,
      selected: true,
    },
    {
      key: 'applicantIdCard',
      label: '身份证号',
      raw: (data.applicantIdCard ?? '').trim(),
      display: (data.applicantIdCard ?? '').trim(),
      source: '资产核对',
      target: '第 1 步 基本信息',
      step: 1,
      selected: true,
    },
    {
      key: 'contactPhone',
      label: '联系电话',
      raw: (data.contactPhone ?? '').trim(),
      display: (data.contactPhone ?? '').trim(),
      source: '资产核对',
      target: '第 1 步 基本信息',
      step: 1,
      selected: true,
    },
    {
      key: 'applicationReason',
      label: '申请原因',
      raw: reasonRaw,
      display: reasonLabel(reasonRaw),
      source: '资产核对',
      target: '第 4 步 入户调查',
      step: 4,
      selected: true,
    },
    {
      key: 'applicationDate',
      label: '申请日期',
      raw: applicationDate,
      display: applicationDate,
      source: '资产核对',
      target: '第 1 步 基本信息',
      step: 1,
      selected: true,
    },
    {
      key: 'familyAddress',
      label: '家庭地址',
      raw: (data.familyAddress ?? '').trim(),
      display: (data.familyAddress ?? '').trim(),
      source: '资产核对',
      target: '第 1 步 基本信息',
      step: 1,
      selected: true,
    },
    {
      key: 'community',
      label: '社区',
      raw: rawCommunity,
      display: communityDisplay,
      source: '资产核对',
      target: '第 1 步 地址信息',
      step: 1,
      selected: true,
    },
  ]
}

async function loadSource(): Promise<void> {
  const id = props.assetCheckId.trim()
  const ticket = ++loadTicket
  detail.value = null
  fields.value = []
  community.value = null
  loadError.value = ''
  if (!id) {
    loading.value = false
    return
  }

  loading.value = true
  try {
    const data = await getAssetSource(id)
    if (ticket !== loadTicket) return
    detail.value = data
    await buildFields(data)
  } catch (error) {
    if (ticket !== loadTicket) return
    loadError.value = getErrorMessage(error, '来源数据加载失败')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

function reload(): void {
  void loadSource()
}

/** 申请日期：Web 端由申请编号日期段派生，只读展示 + 第 1 步改期对话框，这里只引导。 */
function hintApplicationDate(): void {
  ElMessage.info(
    '申请日期由申请编号决定，请到第 1 步「申请日期」右侧“修改”按钮变更（需先保存草稿）',
  )
  emit('gotoStep', 1)
}

function warnFilled(label: string, silent: boolean): void {
  if (!silent) {
    ElMessage.warning(`「${label}」已有值，未覆盖来源数据`)
  }
}

/** 单字段填充：只填空；成功返回 true。silent=true 时由调用方汇总提示（批量填充）。 */
async function fillField(field: SourceField, silent = false): Promise<boolean> {
  if (props.disabled || !field.raw) return false
  const basic = props.form.basic

  switch (field.key) {
    case 'applicantName':
      if (basic.applicantName.trim()) {
        warnFilled(field.label, silent)
        return false
      }
      basic.applicantName = field.raw
      break
    case 'applicantIdCard':
      if (basic.applicantIdCard.trim()) {
        warnFilled(field.label, silent)
        return false
      }
      basic.applicantIdCard = field.raw
      break
    case 'contactPhone':
      if (basic.phone.trim()) {
        warnFilled(field.label, silent)
        return false
      }
      basic.phone = field.raw
      break
    case 'familyAddress':
      if (basic.address.trim()) {
        warnFilled(field.label, silent)
        return false
      }
      basic.address = field.raw
      break
    case 'applicationReason': {
      if (props.form.survey.applicationReason.trim()) {
        warnFilled(field.label, silent)
        return false
      }
      const option = APPLICATION_REASON_OPTIONS.find(
        (item) => item.value === field.raw || item.label === field.raw,
      )
      if (!option) {
        if (!silent) {
          ElMessage.warning(`申请原因「${field.raw}」不在选项中，未填充`)
        }
        return false
      }
      props.form.survey.applicationReason = option.value
      break
    }
    case 'community': {
      const resolved = community.value
      if (!resolved?.villageName || !resolved.townName) {
        if (!silent) {
          ElMessage.warning('社区编码未能解析为「乡镇 + 村社区」，未填充；请在第 1 步手工选择')
        }
        return false
      }
      if (basic.town.trim() || basic.village.trim()) {
        warnFilled('社区（乡镇/村社区）', silent)
        return false
      }
      basic.town = resolved.townName
      basic.village = resolved.villageName
      break
    }
    case 'applicationDate':
      if (!silent) hintApplicationDate()
      return false
  }

  return true
}

async function handleFill(field: SourceField): Promise<void> {
  if (filling.value) return
  filling.value = true
  try {
    if (await fillField(field)) {
      ElMessage.success(`已填充「${field.label}」，记得保存草稿`)
    }
  } finally {
    filling.value = false
  }
}

function toggleAll(): void {
  const next = !allSelected.value
  for (const field of selectableFields.value) {
    field.selected = next
  }
}

/** 批量填充勾选项：只填空，逐项跳过已有值/不可填字段。 */
async function handleFillSelected(): Promise<void> {
  if (filling.value) return
  const targets = fields.value.filter((field) => field.selected && field.raw)
  if (targets.length === 0) {
    ElMessage.warning('请先勾选要填充的字段')
    return
  }

  filling.value = true
  try {
    let filled = 0
    for (const field of targets) {
      if (await fillField(field, true)) filled += 1
    }
    if (filled > 0) {
      const skipped = targets.length - filled
      ElMessage.success(
        `已填充 ${filled} 项${skipped > 0 ? `（跳过 ${skipped} 项：已有值或需到对应步骤处理）` : ''}，记得保存草稿`,
      )
    } else {
      ElMessage.warning('没有可填充的字段（已有值或需先到对应步骤处理）')
    }
  } finally {
    filling.value = false
  }
}

/**
 * 资产核对关系是 code（Spouse/Son/Grandson…）；换算成表单关系选项的中文值，
 * 避免保存回读时被 ToFormRelationship 归并成「其他」。
 */
function toRelationLabel(value: string): string {
  const text = (value ?? '').trim()
  switch (text) {
    case 'Head':
    case '本人/户主':
      return '本人/户主'
    case 'Spouse':
      return '配偶'
    case 'Son':
      return '子/婿'
    case 'Daughter':
      return '女/媳'
    case 'Grandson':
    case 'Granddaughter':
    case 'Grandchild':
      return '孙子女/外孙子女'
    case 'Father':
    case 'Mother':
      return '父母/岳父母/公婆'
    case 'Brother':
    case 'Sister':
      return '兄弟姐妹'
    case 'Other':
      return '其他'
    default:
      return text
  }
}

/** 导入全部家庭成员：排除 isHead 户主行，按身份证去重后追加到第 2 步。 */
async function importMembers(): Promise<void> {
  if (props.disabled) return
  const members = (detail.value?.members ?? []).filter((member) => !member.isHead)
  if (members.length === 0) {
    ElMessage.warning('来源记录没有可导入的家庭成员（户主已在第 1 步填写）')
    return
  }

  try {
    await ElMessageBox.confirm(
      `将把资产核对记录 #${props.assetCheckId.trim()} 的 ${members.length} 位家庭成员导入第 2 步；` +
        '户主已在第 1 步（申请人）不重复导入，已存在的同身份证成员会自动跳过。',
      '导入全部家庭成员',
      {
        type: 'warning',
        confirmButtonText: '导入',
        cancelButtonText: '取消',
      },
    )
  } catch {
    return
  }

  const existingIds = new Set(
    props.form.members.map((member) => normalizeIdCard(member.idCard)).filter(Boolean),
  )
  const existingNames = new Set(
    props.form.members.map((member) => member.name.trim()).filter(Boolean),
  )

  let added = 0
  let skipped = 0
  for (const member of members) {
    const idCard = normalizeIdCard(member.idCard)
    const name = (member.applicantName ?? '').trim()
    const duplicated = idCard
      ? existingIds.has(idCard)
      : Boolean(name) && existingNames.has(name)
    if (duplicated) {
      skipped += 1
      continue
    }

    const parsed = parseIdCard(idCard)
    props.form.members.push(
      createMember({
        name,
        idCard,
        relation: toRelationLabel(member.relationship),
        gender: parsed?.gender ?? '',
        birthDate: parsed?.birthDate ?? '',
        age: parsed?.age ?? null,
      }),
    )
    if (idCard) existingIds.add(idCard)
    if (name) existingNames.add(name)
    added += 1
  }

  ElMessage.success(
    `已导入 ${added} 位家庭成员${skipped > 0 ? `（跳过已存在 ${skipped} 位）` : ''}，记得保存草稿`,
  )
}

watch(
  () => props.assetCheckId,
  () => {
    void loadSource()
  },
)

onMounted(() => {
  void loadSource()
})
</script>

<template>
  <aside class="form-aside" aria-label="来源数据">
    <div class="aside-head">
      <el-icon :size="18"><FolderOpened /></el-icon>
      <strong>来源数据</strong>
      <span v-if="hasSource" class="aside-head__no">#{{ assetCheckId }}</span>
    </div>

    <div class="aside-body">
      <p v-if="hasSource" class="aside-note">已关联资产核对记录 #{{ assetCheckId }}</p>

      <div v-if="loading" class="aside-loading">
        <el-skeleton :rows="4" animated />
      </div>

      <el-alert
        v-else-if="loadError"
        class="aside-alert"
        type="error"
        :title="loadError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="reload">重试</el-button>
      </el-alert>

      <div v-else-if="!hasSource" class="aside-empty">
        <el-icon :size="38"><FolderOpened /></el-icon>
        <span>暂无来源数据</span>
      </div>

      <ul v-else class="field-list">
        <li v-for="field in fields" :key="field.key" class="field-item">
          <div class="field-item__head">
            <el-checkbox v-model="field.selected" :aria-label="`选择${field.label}`" />
            <span class="field-item__label">{{ field.label }}</span>
            <span class="field-item__source">{{ field.source }}</span>
          </div>
          <div class="field-item__meta">
            <span class="field-item__target">{{ field.target }}</span>
            <span class="field-item__value" :class="{ 'is-empty': !field.raw }">
              {{ field.display || '无' }}
            </span>
          </div>
          <div class="field-item__action">
            <el-button
              v-if="field.key === 'applicationDate'"
              size="small"
              link
              type="primary"
              :disabled="disabled"
              title="申请日期由申请编号决定，请在第 1 步改期"
              @click="hintApplicationDate"
            >
              去修改
            </el-button>
            <el-button
              v-else-if="field.raw"
              size="small"
              type="primary"
              plain
              :disabled="disabled"
              :title="disabled ? '查看模式不可填充' : `填充到${field.target}`"
              @click="handleFill(field)"
            >
              填充
            </el-button>
            <el-button
              v-else
              size="small"
              link
              type="primary"
              :disabled="disabled"
              @click="emit('gotoStep', field.step)"
            >
              去填写
            </el-button>
          </div>
        </li>
      </ul>
    </div>

    <div class="aside-actions">
      <strong class="aside-subtitle">字段选择</strong>
      <el-button plain :disabled="disabled || selectableFields.length === 0" @click="toggleAll">
        {{ allSelected ? '取消全选' : '全选' }}
      </el-button>
      <el-button
        type="primary"
        :disabled="disabled || selectedCount === 0"
        :loading="filling"
        @click="handleFillSelected"
      >
        填充选中（{{ selectedCount }}）
      </el-button>
      <div class="aside-divider" aria-hidden="true" />
      <strong class="aside-subtitle">家庭成员</strong>
      <el-button type="primary" :disabled="disabled || memberCount === 0" @click="importMembers">
        导入全部家庭成员（{{ memberCount }}）
      </el-button>
    </div>
  </aside>
</template>

<style scoped>
.form-aside {
  display: flex;
  flex-direction: column;
  min-width: 0;
  border-left: 1px solid var(--nc-line);
  background-color: #fff;
}

.aside-head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--nc-line);
  background-color: var(--nc-paper-soft);
  color: var(--nc-seal);
}

.aside-head strong {
  color: var(--nc-ink);
  font-size: 17px;
  font-weight: 700;
}

.aside-head__no {
  margin-left: auto;
  color: var(--nc-muted-2, #666);
  font-family: var(--nc-font-mono);
  font-size: 12px;
}

.aside-body {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow-y: auto;
  scrollbar-width: thin;
}

.aside-note {
  margin: 0;
  padding: 10px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-size: 12px;
  line-height: 1.6;
}

.aside-loading {
  padding: 14px 16px;
}

.aside-alert {
  margin: 12px 16px;
}

.aside-empty {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 40px 20px;
  color: var(--nc-disabled);
  font-size: 14px;
}

.field-list {
  display: flex;
  flex-direction: column;
  gap: 0;
  margin: 0;
  padding: 0;
  list-style: none;
}

.field-item {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 10px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.field-item__head {
  display: flex;
  align-items: center;
  gap: 6px;
}

.field-item__head :deep(.el-checkbox) {
  margin-right: 0;
}

.field-item__label {
  color: var(--nc-ink);
  font-size: 14px;
  font-weight: 600;
}

.field-item__source {
  margin-left: auto;
  padding: 1px 6px;
  border: 1px solid var(--nc-line);
  border-radius: 2px;
  background-color: var(--nc-paper-2);
  color: var(--nc-muted-2, #666);
  font-size: 11px;
  white-space: nowrap;
}

.field-item__meta {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.field-item__target {
  color: var(--nc-disabled);
  font-size: 11px;
}

.field-item__value {
  overflow-wrap: anywhere;
  color: var(--nc-ink-2, #444);
  font-size: 13px;
  line-height: 1.5;
}

.field-item__value.is-empty {
  color: var(--nc-disabled);
}

.field-item__action {
  display: flex;
  justify-content: flex-end;
}

.field-item__action :deep(.el-button + .el-button) {
  margin-left: 8px;
}

.aside-actions {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.aside-actions :deep(.el-button) {
  width: 100%;
  margin-left: 0;
}

.aside-divider {
  height: 1px;
  margin: 4px 0;
  background-color: var(--nc-line);
}

.aside-subtitle {
  margin-top: 4px;
  color: var(--nc-ink);
  font-size: 16px;
  font-weight: 700;
}

/* 窄屏：侧栏改为内容区下方的单列块，字段列表独立滚动，不整页撑高。 */
@media (max-width: 1100px) {
  .form-aside {
    border-top: 1px solid var(--nc-line);
    border-left: none;
  }

  .aside-body {
    max-height: 45vh;
  }

  .aside-empty {
    flex: none;
    padding: 24px 20px;
  }
}
</style>
