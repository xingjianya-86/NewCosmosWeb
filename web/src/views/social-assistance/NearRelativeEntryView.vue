<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Check, Delete, Plus, Refresh } from '@element-plus/icons-vue'
import {
  createNearRelative,
  deleteNearRelative,
  fillNearRelativeArchive,
  getNearRelative,
  lookupNearRelativeStaff,
  updateNearRelative,
} from '@/api/nearRelatives'
import { ApiError, getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type {
  NearRelativeArchiveFill,
  NearRelativeEntry,
  NearRelativeLink,
  NearRelativeLinkInput,
  NearRelativeSavePayload,
  NearRelativeStaff,
  NearRelativeStaffLookup,
} from '@/types/nearRelatives'

const route = useRoute()
const router = useRouter()

const LIST_PATH = '/social-assistance/near-relatives'

interface LinkRow {
  key: number
  id: number
  applicationId: string
  relation: string
  name: string
  idCard: string
  gender: string
  birthDate: string
  familyAddress: string
  hukouAddress: string
  residenceAddress: string
  familySize: string
  helpType: string
  monthAmount: string
  reportAmount: string
  startTime: string
  familyRelation: string
  applyReason: string
  applyTime: string
  familyDifficulty: string
  economyInvestigate: string
  townOpinion: string
  dynamicRecord: string
  isArchiveLinked: boolean
  archiveSourceText: string
  lastLookupIdCard: string
  lookupBusy: boolean
}

const ID_CARD_18 = /^\d{17}[\dXx]$/
const ID_CARD_WEIGHTS = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2]
const ID_CARD_CHECK_CODES = ['1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2']

const form = reactive({
  staffName: '',
  staffIdCard: '',
  staffPhone: '',
  workUnit: '',
  position: '',
  town: '',
})

const links = ref<LinkRow[]>([])
const editingId = ref(0)
const loadedUpdatedAt = ref<string | null>(null)
const entryLoaded = ref(false)
const lastStaffLookupIdCard = ref('')
const staffLookupBusy = ref(false)

const loading = ref(false)
const saving = ref(false)
const deleting = ref(false)
const pageError = ref('')
const statusText = ref('新增备案模式')
const conflictError = ref('')

let rowSeed = 0
let staffLookupSeq = 0
let loadTicket = 0

const routeId = computed(() => {
  const raw = route.params.id
  const value = Array.isArray(raw) ? raw[0] : raw
  return typeof value === 'string' ? value : ''
})

const isEditing = computed(() => editingId.value > 0)
const canSave = computed(
  () => !loading.value && (!routeId.value || entryLoaded.value),
)
const pageTitle = computed(() => (isEditing.value ? '编辑近亲属备案' : '新增近亲属备案'))
const editableRowCount = computed(() => links.value.filter((row) => row.name.trim()).length)

function emptyLinkRow(): LinkRow {
  rowSeed += 1
  return {
    key: rowSeed,
    id: 0,
    applicationId: '',
    relation: '',
    name: '',
    idCard: '',
    gender: '',
    birthDate: '',
    familyAddress: '',
    hukouAddress: '',
    residenceAddress: '',
    familySize: '',
    helpType: '',
    monthAmount: '',
    reportAmount: '',
    startTime: '',
    familyRelation: '',
    applyReason: '',
    applyTime: '',
    familyDifficulty: '',
    economyInvestigate: '',
    townOpinion: '',
    dynamicRecord: '',
    isArchiveLinked: false,
    archiveSourceText: '',
    lastLookupIdCard: '',
    lookupBusy: false,
  }
}

function normalizeIdCard(value: string | null | undefined): string {
  return (value ?? '').trim().toUpperCase()
}

/** 与 Core Helpers/IdCardValidator.IsValid 同口径：18 位 + 校验码。 */
function isValidIdCard(value: string | null | undefined): boolean {
  const text = normalizeIdCard(value)
  if (!ID_CARD_18.test(text)) return false
  let sum = 0
  for (let index = 0; index < 17; index += 1) {
    sum += Number(text[index]) * (ID_CARD_WEIGHTS[index] ?? 0)
  }
  return text[17] === ID_CARD_CHECK_CODES[sum % 11]
}

function getQueryString(name: string): string {
  const raw = route.query[name]
  const value = Array.isArray(raw) ? raw[0] : raw
  return typeof value === 'string' ? value : ''
}

function formatVersion(value: string | null): string {
  if (!value) return '未载入'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  const pad = (part: number): string => String(part).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(
    date.getHours(),
  )}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

function formatFillSource(fill: NearRelativeArchiveFill): string {
  const no = fill.applicationNo || `#${fill.applicationId}`
  const date = fill.applicationCreatedAt
    ? String(fill.applicationCreatedAt).slice(0, 10)
    : '-'
  return `申请 ${no}（${date}，${fill.statusDisplay || fill.status || '—'}）`
}

function buildStaffStatus(lookup: NearRelativeStaffLookup): string {
  const name = lookup.staff?.staffName || '未命名'
  const foundCaregiver =
    lookup.foundInCaregivers || lookup.foundInDestituteArchive
  if (lookup.foundInUsers) {
    return foundCaregiver
      ? `已从系统用户带出经办人员工作信息（${name}）；档案照料护理人另有记录，已补齐空缺信息`
      : `已从系统用户带出经办人员工作信息（${name}）`
  }
  return `已从档案照料护理人带出工作信息（${name}）`
}

function hasAnyOtherContent(): boolean {
  if (
    form.staffName.trim() ||
    form.staffPhone.trim() ||
    form.workUnit.trim() ||
    form.position.trim() ||
    form.town.trim()
  ) {
    return true
  }
  return links.value.some((row) => !isRowEmpty(row))
}

function isRowEmpty(row: LinkRow): boolean {
  const values = [
    row.applicationId,
    row.relation,
    row.name,
    row.idCard,
    row.gender,
    row.birthDate,
    row.familyAddress,
    row.hukouAddress,
    row.residenceAddress,
    row.familySize,
    row.helpType,
    row.monthAmount,
    row.reportAmount,
    row.startTime,
    row.familyRelation,
    row.applyReason,
    row.applyTime,
    row.familyDifficulty,
    row.economyInvestigate,
    row.townOpinion,
    row.dynamicRecord,
  ]
  return values.every((value) => !value.trim())
}

function applyStaffInfo(staff: NearRelativeStaff): void {
  form.staffName = staff.staffName ?? ''
  form.staffPhone = staff.staffPhone ?? ''
  form.workUnit = staff.workUnit ?? ''
  form.position = staff.position ?? ''
  form.town = staff.town ?? ''
}

function applyArchiveFill(row: LinkRow, fill: NearRelativeArchiveFill): void {
  const link = fill.link
  row.applicationId = String(fill.applicationId || link.applicationId || '')
  row.name = link.name ?? ''
  row.idCard = link.idCard ?? ''
  row.gender = link.gender ?? ''
  row.birthDate = link.birthDate ?? ''
  row.hukouAddress = link.hukouAddress ?? ''
  row.residenceAddress = link.residenceAddress ?? ''
  row.familyAddress = link.familyAddress ?? ''
  row.familySize = link.familySize !== null && link.familySize !== undefined
    ? String(link.familySize)
    : ''
  row.helpType = link.helpType ?? ''
  row.monthAmount =
    link.monthAmount !== null && link.monthAmount !== undefined
      ? link.monthAmount.toFixed(2)
      : ''
  row.reportAmount =
    link.reportAmount !== null && link.reportAmount !== undefined
      ? link.reportAmount.toFixed(2)
      : ''
  row.startTime = link.startTime ?? ''
  row.applyReason = link.applyReason ?? ''
  row.applyTime = link.applyTime ?? ''
  row.isArchiveLinked = true
  row.archiveSourceText = `已关联：${formatFillSource(fill)}`
  row.lastLookupIdCard = normalizeIdCard(row.idCard)
}

function rowFromLink(link: NearRelativeLink): LinkRow {
  const row = emptyLinkRow()
  row.id = link.id
  row.applicationId = link.applicationId ? String(link.applicationId) : ''
  row.relation = link.relation ?? ''
  row.name = link.name ?? ''
  row.idCard = link.idCard ?? ''
  row.gender = link.gender ?? ''
  row.birthDate = link.birthDate ?? ''
  row.familyAddress = link.familyAddress ?? ''
  row.hukouAddress = link.hukouAddress ?? ''
  row.residenceAddress = link.residenceAddress ?? ''
  row.familySize = link.familySize !== null && link.familySize !== undefined
    ? String(link.familySize)
    : ''
  row.helpType = link.helpType ?? ''
  row.monthAmount =
    link.monthAmount !== null && link.monthAmount !== undefined
      ? link.monthAmount.toFixed(2)
      : ''
  row.reportAmount =
    link.reportAmount !== null && link.reportAmount !== undefined
      ? link.reportAmount.toFixed(2)
      : ''
  row.startTime = link.startTime ?? ''
  row.familyRelation = link.familyRelation ?? ''
  row.applyReason = link.applyReason ?? ''
  row.applyTime = link.applyTime ?? ''
  row.familyDifficulty = link.familyDifficulty ?? ''
  row.economyInvestigate = link.economyInvestigate ?? ''
  row.townOpinion = link.townOpinion ?? ''
  row.dynamicRecord = link.dynamicRecord ?? ''
  if (link.applicationId) {
    row.isArchiveLinked = true
    row.archiveSourceText = `已关联申请 #${link.applicationId}`
  }
  return row
}

function applyEntry(entry: NearRelativeEntry): void {
  editingId.value = entry.staff.id
  loadedUpdatedAt.value = entry.staff.updatedAt ?? null
  entryLoaded.value = true
  lastStaffLookupIdCard.value = normalizeIdCard(entry.staff.staffIdCard)

  form.staffName = entry.staff.staffName ?? ''
  form.staffIdCard = entry.staff.staffIdCard ?? ''
  form.staffPhone = entry.staff.staffPhone ?? ''
  form.workUnit = entry.staff.workUnit ?? ''
  form.position = entry.staff.position ?? ''
  form.town = entry.staff.town ?? ''

  links.value = entry.links.map(rowFromLink)
  if (links.value.length === 0) {
    links.value = [emptyLinkRow()]
  }

  statusText.value = `编辑备案：${entry.staff.staffName}（${entry.links.length} 名对象）`
}

function resetNew(): void {
  loadTicket += 1
  loading.value = false
  staffLookupSeq += 1
  staffLookupBusy.value = false
  editingId.value = 0
  loadedUpdatedAt.value = null
  entryLoaded.value = false
  lastStaffLookupIdCard.value = ''
  form.staffName = ''
  form.staffIdCard = ''
  form.staffPhone = ''
  form.workUnit = ''
  form.position = ''
  form.town = ''
  links.value = [emptyLinkRow()]
  pageError.value = ''
  conflictError.value = ''
  statusText.value = '新增备案模式'
}

async function loadEntry(id: number): Promise<void> {
  const ticket = ++loadTicket
  staffLookupSeq += 1
  staffLookupBusy.value = false
  loading.value = true
  pageError.value = ''
  conflictError.value = ''
  entryLoaded.value = false

  if (!Number.isSafeInteger(id) || id <= 0) {
    pageError.value = '备案 ID 无效'
    statusText.value = '备案加载失败'
    loading.value = false
    return
  }

  try {
    const entry = await getNearRelative(id)
    if (ticket !== loadTicket) return
    applyEntry(entry)
  } catch (error) {
    if (ticket !== loadTicket) return
    pageError.value = getErrorMessage(error, '备案加载失败')
    statusText.value = '备案加载失败'
  } finally {
    if (ticket === loadTicket) {
      loading.value = false
    }
  }
}

async function reloadCurrent(): Promise<void> {
  if (editingId.value > 0) {
    await loadEntry(editingId.value)
  } else if (routeId.value) {
    await loadEntry(Number(routeId.value))
  }
}

function onStaffIdCardInput(value?: string): void {
  const raw = typeof value === 'string' ? value : form.staffIdCard
  const card = normalizeIdCard(raw)
  if (form.staffIdCard !== card) {
    form.staffIdCard = card
  }
  void runStaffLookup(card)
}

async function runStaffLookup(card: string, allowSame = false): Promise<void> {
  if (!isValidIdCard(card)) return
  if (!allowSame && card === lastStaffLookupIdCard.value) return
  lastStaffLookupIdCard.value = card

  const seq = ++staffLookupSeq
  staffLookupBusy.value = true
  try {
    const result = await lookupNearRelativeStaff(card)
    if (seq !== staffLookupSeq) return
    if (normalizeIdCard(form.staffIdCard) !== card) return

    if (result.existing) {
      if (editingId.value === result.existing.id) {
        statusText.value = `正在编辑该身份证已有备案：${result.existing.staffName}（${result.existing.linkCount} 名对象）`
        return
      }

      if (hasAnyOtherContent()) {
        try {
          await ElMessageBox.confirm(
            `该身份证已有备案（工作人员：${result.existing.staffName}，${result.existing.linkCount} 名对象），是否载入编辑？当前已填写内容将被覆盖。`,
            '提示',
            { type: 'info', confirmButtonText: '载入', cancelButtonText: '取消' },
          )
        } catch {
          statusText.value = `该身份证已有备案：${result.existing.staffName}（未载入，可修改身份证号或手工填写）`
          return
        }
      }

      await router.replace(`${LIST_PATH}/${result.existing.id}/edit`)
      return
    }

    if (result.staff) {
      applyStaffInfo(result.staff)
      statusText.value = buildStaffStatus(result)
    } else {
      statusText.value = '未在系统用户及档案照料护理人中找到该身份证，请手工填写工作信息'
    }
  } catch (error) {
    if (seq !== staffLookupSeq) return
    statusText.value = getErrorMessage(error, '查询经办人员工作信息失败')
  } finally {
    if (seq === staffLookupSeq) {
      staffLookupBusy.value = false
    }
  }
}

function onLinkIdCardInput(row: LinkRow, value?: string): void {
  const raw = typeof value === 'string' ? value : row.idCard
  const card = normalizeIdCard(raw)
  if (row.idCard !== card) {
    row.idCard = card
  }

  if (row.isArchiveLinked || row.lookupBusy) return
  if (!isValidIdCard(card)) return
  if (card === row.lastLookupIdCard) return
  row.lastLookupIdCard = card
  void lookupLink(row, card)
}

async function lookupLink(row: LinkRow, card: string): Promise<void> {
  row.lookupBusy = true
  try {
    const fill = await fillNearRelativeArchive({
      idCard: card,
      excludeStaffId: editingId.value > 0 ? editingId.value : undefined,
    })
    if (!links.value.includes(row)) return
    if (normalizeIdCard(row.idCard) !== card) return

    if (!fill) {
      statusText.value = '未在本系统档案中找到该身份证的救助申请，请手工录入'
      return
    }

    applyArchiveFill(row, fill)
    statusText.value = fill.occupiedByOtherStaff
      ? `已从档案带出：${formatFillSource(fill)}；注意：该救助对象已由 [${fill.occupiedByStaffName}] 备案，保存将被拦截`
      : `已从档案带出：${formatFillSource(fill)}`
  } catch (error) {
    if (!links.value.includes(row)) return
    statusText.value = getErrorMessage(error, '档案查询失败')
  } finally {
    row.lookupBusy = false
  }
}

function unlinkArchive(row: LinkRow): void {
  row.isArchiveLinked = false
  row.archiveSourceText = ''
  row.applicationId = ''
  row.lastLookupIdCard = ''
}

function addLink(): void {
  links.value.push(emptyLinkRow())
}

function removeLink(row: LinkRow): void {
  const index = links.value.indexOf(row)
  if (index >= 0) {
    links.value.splice(index, 1)
  }
  if (links.value.length === 0) {
    links.value.push(emptyLinkRow())
  }
}

function parsePositiveInt(value: string): number | null {
  const text = value.trim()
  if (!/^\d+$/.test(text)) return null
  const parsed = Number(text)
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null
}

function parseDecimal(value: string): number | null {
  const text = value.trim()
  if (!text) return null
  const parsed = Number(text)
  return Number.isFinite(parsed) ? parsed : null
}

function toLinkInput(row: LinkRow, sortOrder: number): NearRelativeLinkInput {
  return {
    id: row.id,
    applicationId: parsePositiveInt(row.applicationId),
    relation: row.relation.trim(),
    name: row.name.trim(),
    idCard: normalizeIdCard(row.idCard),
    gender: row.gender.trim(),
    birthDate: row.birthDate.trim(),
    familyAddress: row.familyAddress.trim(),
    hukouAddress: row.hukouAddress.trim(),
    residenceAddress: row.residenceAddress.trim(),
    familySize: parsePositiveInt(row.familySize),
    helpType: row.helpType.trim(),
    monthAmount: parseDecimal(row.monthAmount),
    reportAmount: parseDecimal(row.reportAmount),
    startTime: row.startTime.trim(),
    familyRelation: row.familyRelation.trim(),
    applyReason: row.applyReason.trim(),
    applyTime: row.applyTime.trim(),
    familyDifficulty: row.familyDifficulty.trim(),
    economyInvestigate: row.economyInvestigate.trim(),
    townOpinion: row.townOpinion.trim(),
    dynamicRecord: row.dynamicRecord.trim(),
    sortOrder,
  }
}

async function applyApplicationContext(applicationId: number): Promise<void> {
  let row =
    links.value.find((item) => !item.idCard.trim() && !item.isArchiveLinked) ??
    null
  if (!row) {
    row = emptyLinkRow()
    links.value.push(row)
  }

  try {
    const fill = await fillNearRelativeArchive({
      applicationId,
      excludeStaffId: editingId.value > 0 ? editingId.value : undefined,
    })
    if (!fill) {
      row.applicationId = String(applicationId)
      statusText.value = `已关联申请 #${applicationId}（档案带出失败，请手工补全）`
      return
    }

    applyArchiveFill(row, fill)
    statusText.value = fill.occupiedByOtherStaff
      ? `已关联申请 #${applicationId} 并带出对象信息；注意：该对象已由 [${fill.occupiedByStaffName}] 备案，保存将被拦截`
      : `已关联申请 #${applicationId} 并按档案带出对象信息（补全与工作人员关系后保存）`
  } catch (error) {
    row.applicationId = String(applicationId)
    statusText.value = `已关联申请 #${applicationId}（${getErrorMessage(
      error,
      '档案带出失败',
    )}，请手工补全）`
  }
}

async function initNewContext(): Promise<void> {
  resetNew()

  const applicationId = Number(getQueryString('applicationId'))
  if (Number.isSafeInteger(applicationId) && applicationId > 0) {
    await applyApplicationContext(applicationId)
  }

  const staffIdCard = normalizeIdCard(getQueryString('staffIdCard'))
  if (staffIdCard) {
    form.staffIdCard = staffIdCard
    await runStaffLookup(staffIdCard, true)
  }
}

function startNew(): void {
  if (!routeId.value) {
    resetNew()
    if (Object.keys(route.query).length > 0) {
      void router.replace(`${LIST_PATH}/new`)
    }
    return
  }
  void router.push(`${LIST_PATH}/new`)
}

function cancel(): void {
  void router.push(LIST_PATH)
}

async function save(): Promise<void> {
  if (!form.staffName.trim()) {
    ElMessage.warning('请填写工作人员姓名')
    return
  }
  if (!form.staffIdCard.trim()) {
    ElMessage.warning('请填写工作人员身份证号')
    return
  }

  conflictError.value = ''
  const rows = links.value.filter((row) => row.name.trim())
  const payload: NearRelativeSavePayload = {
    staff: {
      id: editingId.value,
      staffName: form.staffName.trim(),
      staffIdCard: normalizeIdCard(form.staffIdCard),
      staffPhone: form.staffPhone.trim(),
      workUnit: form.workUnit.trim(),
      position: form.position.trim(),
      town: form.town.trim(),
      updatedAt: loadedUpdatedAt.value,
    },
    links: rows.map((row, index) => toLinkInput(row, index)),
  }

  saving.value = true
  try {
    const result = isEditing.value
      ? await updateNearRelative(editingId.value, payload)
      : await createNearRelative(payload)

    ElMessage.success('近亲属备案已保存')

    if (!isEditing.value) {
      editingId.value = result.id
      await router.replace(`${LIST_PATH}/${result.id}/edit`)
    }
    await loadEntry(result.id)
  } catch (error) {
    const message = getErrorMessage(error, '保存备案失败')
    if (error instanceof ApiError && error.status === 409) {
      conflictError.value = message
    }
    ElMessage.error(message)
  } finally {
    saving.value = false
  }
}

async function removeCurrent(): Promise<void> {
  if (editingId.value === 0) {
    ElMessage.info('当前为新增模式，无需删除')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确定要删除 [${form.staffName}] 的备案及其 ${links.value.length} 名对象吗？此操作不可恢复。`,
      '确认删除',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  deleting.value = true
  try {
    await deleteNearRelative(editingId.value)
    ElMessage.success('备案已删除')
    await router.push(LIST_PATH)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除备案失败'))
  } finally {
    deleting.value = false
  }
}

onMounted(() => {
  if (routeId.value) {
    void loadEntry(Number(routeId.value))
  } else {
    void initNewContext()
  }
})

watch(routeId, (value) => {
  if (!value) {
    void initNewContext()
    return
  }
  const numeric = Number(value)
  if (!Number.isSafeInteger(numeric) || numeric <= 0) return
  if (numeric === editingId.value) return
  void loadEntry(numeric)
})
</script>

<template>
  <div class="nc-page near-relative-entry">
    <NcPageHeader
      :title="pageTitle"
      kicker="Near-Relative Filing"
      description="输入身份证号自动带出档案信息；一名工作人员对应多名救助对象。"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="cancel">返回列表</el-button>
        <el-button :icon="Plus" @click="startNew">新增备案</el-button>
      </template>
    </NcPageHeader>

    <div class="status-strip">
      <el-tag :type="isEditing ? 'success' : 'info'" size="small" effect="plain">
        {{ isEditing ? '编辑备案' : '新增备案' }}
      </el-tag>
      <span class="status-text">{{ statusText }}</span>
      <span v-if="isEditing" class="status-version">
        版本：{{ formatVersion(loadedUpdatedAt) }}
      </span>
    </div>

    <el-alert
      v-if="pageError"
      class="entry-alert"
      type="error"
      :title="pageError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" :icon="Refresh" @click="reloadCurrent">重新加载</el-button>
    </el-alert>

    <el-alert
      v-if="conflictError"
      class="entry-alert"
      type="warning"
      :title="`保存冲突：${conflictError}`"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" :icon="Refresh" @click="reloadCurrent">
        重新载入最新数据
      </el-button>
    </el-alert>

    <div v-loading="loading" class="entry-stack">
      <NcCard
        title="社会救助经办（工作）人员基本情况"
        subtitle="输入工作人员身份证号自动带出工作信息；已有备案可直接载入编辑。"
      >
        <div class="form-grid">
          <label class="field">
            <span class="field-label">姓名 <em>*</em></span>
            <el-input v-model="form.staffName" placeholder="工作人员姓名" clearable />
          </label>

          <label class="field">
            <span class="field-label">
              身份证号 <em>*</em>
              <small v-if="staffLookupBusy" class="field-busy">查询中…</small>
            </span>
            <el-input
              v-model="form.staffIdCard"
              placeholder="身份证号（满 18 位自动带出）"
              clearable
              @input="onStaffIdCardInput"
            />
          </label>

          <label class="field">
            <span class="field-label">联系方式</span>
            <el-input v-model="form.staffPhone" placeholder="联系电话" clearable />
          </label>

          <label class="field">
            <span class="field-label">工作单位</span>
            <el-input v-model="form.workUnit" placeholder="工作单位" clearable />
          </label>

          <label class="field">
            <span class="field-label">职务（职级）</span>
            <el-input v-model="form.position" placeholder="职务（职级）" clearable />
          </label>

          <label class="field">
            <span class="field-label">乡镇（街道）</span>
            <el-input v-model="form.town" placeholder="所在乡镇/街道" clearable />
          </label>
        </div>
      </NcCard>

      <NcCard
        title="近亲属享受社会救助情况"
        :subtitle="`一名工作人员可对应多名救助对象，行数不限；当前 ${links.length} 行`"
      >
        <template #actions>
          <el-button type="primary" plain :icon="Plus" @click="addLink">添加对象</el-button>
        </template>

        <div class="link-list">
          <article v-for="(row, index) in links" :key="row.key" class="link-card">
            <header class="link-head">
              <div class="link-heading">
                <strong>救助对象 {{ index + 1 }}</strong>
                <span v-if="row.isArchiveLinked" class="archive-source">
                  {{ row.archiveSourceText }}
                </span>
              </div>
              <div class="link-head-actions">
                <el-button
                  v-if="row.isArchiveLinked"
                  link
                  type="primary"
                  @click="unlinkArchive(row)"
                >
                  解除关联
                </el-button>
                <el-button link type="danger" @click="removeLink(row)">✕ 删除此行</el-button>
              </div>
            </header>

            <div class="form-grid link-grid">
              <label class="field">
                <span class="field-label">与工作人员关系</span>
                <el-input v-model="row.relation" placeholder="如：父女" clearable />
              </label>

              <label class="field">
                <span class="field-label">姓名</span>
                <el-input
                  v-model="row.name"
                  placeholder="救助对象姓名"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">
                  身份证号
                  <small v-if="row.lookupBusy" class="field-busy">查询中…</small>
                </span>
                <el-input
                  v-model="row.idCard"
                  placeholder="身份证号（满 18 位自动带出）"
                  :readonly="row.isArchiveLinked"
                  clearable
                  @input="onLinkIdCardInput(row, $event)"
                />
              </label>

              <label class="field">
                <span class="field-label">关联申请 ID（可选）</span>
                <el-input
                  v-model="row.applicationId"
                  placeholder="档案输出关联用"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">性别</span>
                <el-input
                  v-model="row.gender"
                  placeholder="性别"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">出生年月</span>
                <el-input
                  v-model="row.birthDate"
                  placeholder="如：1968年3月"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">户籍地址</span>
                <el-input
                  v-model="row.hukouAddress"
                  placeholder="户籍地址"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">居住地址</span>
                <el-input
                  v-model="row.residenceAddress"
                  placeholder="居住地址"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">家庭住址</span>
                <el-input
                  v-model="row.familyAddress"
                  placeholder="家庭住址"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">家庭人口/共同生活人数</span>
                <el-input
                  v-model="row.familySize"
                  placeholder="人数"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">社会救助种类/保障类别</span>
                <el-input
                  v-model="row.helpType"
                  placeholder="如：最低生活保障对象"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">月保障金（元）</span>
                <el-input
                  v-model="row.monthAmount"
                  placeholder="月保障金"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">保障金额（元）</span>
                <el-input
                  v-model="row.reportAmount"
                  placeholder="保障金额"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">救助起始时间</span>
                <el-input
                  v-model="row.startTime"
                  placeholder="如：2026年1月"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field">
                <span class="field-label">家庭关系</span>
                <el-input v-model="row.familyRelation" placeholder="家庭关系" clearable />
              </label>

              <label class="field">
                <span class="field-label">申请时间</span>
                <el-input
                  v-model="row.applyTime"
                  placeholder="申请时间"
                  :readonly="row.isArchiveLinked"
                  clearable
                />
              </label>

              <label class="field field--full">
                <span class="field-label">申请原因</span>
                <el-input
                  v-model="row.applyReason"
                  type="textarea"
                  :autosize="{ minRows: 2, maxRows: 5 }"
                  placeholder="申请原因"
                  :readonly="row.isArchiveLinked"
                />
              </label>

              <label class="field field--full">
                <span class="field-label">家庭主要困难</span>
                <el-input
                  v-model="row.familyDifficulty"
                  type="textarea"
                  :autosize="{ minRows: 2, maxRows: 5 }"
                  placeholder="家庭主要困难"
                />
              </label>

              <label class="field">
                <span class="field-label">家庭经济情况调查情况</span>
                <el-input
                  v-model="row.economyInvestigate"
                  type="textarea"
                  :autosize="{ minRows: 2, maxRows: 5 }"
                  placeholder="调查情况"
                />
              </label>

              <label class="field">
                <span class="field-label">乡镇（街道）意见</span>
                <el-input
                  v-model="row.townOpinion"
                  type="textarea"
                  :autosize="{ minRows: 2, maxRows: 5 }"
                  placeholder="乡镇意见"
                />
              </label>

              <label class="field field--full">
                <span class="field-label">动态管理记录</span>
                <el-input
                  v-model="row.dynamicRecord"
                  type="textarea"
                  :autosize="{ minRows: 2, maxRows: 5 }"
                  placeholder="动态管理记录"
                />
              </label>
            </div>
          </article>
        </div>
      </NcCard>
    </div>

    <div class="entry-footer">
      <span class="footer-note">
        {{ editableRowCount }} 名有效对象
        <template v-if="isEditing"> · 保存时校验版本，避免覆盖他人修改</template>
      </span>
      <div class="footer-actions">
        <el-button @click="cancel">取消</el-button>
        <el-button
          v-if="isEditing"
          type="danger"
          plain
          :icon="Delete"
          :loading="deleting"
          @click="removeCurrent"
        >
          删除本条
        </el-button>
        <el-button
          type="primary"
          :icon="Check"
          :loading="saving"
          :disabled="!canSave"
          @click="save"
        >
          保存备案
        </el-button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.near-relative-entry {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.status-strip {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  border: 1px solid var(--nc-line-faint);
  border-left: 3px solid var(--nc-seal);
  background-color: var(--nc-paper-soft);
}

.status-text {
  min-width: 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.status-version {
  margin-left: auto;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.04em;
}

.entry-alert {
  margin: 0;
}

.entry-stack {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-height: 200px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px 16px;
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

.field-label {
  display: flex;
  align-items: center;
  gap: 6px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.02em;
}

.field-label em {
  color: var(--nc-seal);
  font-style: normal;
}

.field-busy {
  color: var(--nc-amber);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.link-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.link-card {
  padding: 13px 14px 15px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.link-card:nth-child(even) {
  background-color: var(--nc-paper-2);
}

.link-head {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 11px;
  padding-bottom: 9px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.link-heading {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.link-heading strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.archive-source {
  color: var(--nc-pine-dark);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
  overflow-wrap: anywhere;
}

.link-head-actions {
  display: flex;
  flex: none;
  align-items: center;
  gap: 4px;
}

.link-head-actions :deep(.el-button) {
  margin-left: 0;
}

.link-grid {
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
}

.entry-footer {
  position: sticky;
  bottom: 0;
  z-index: 4;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 12px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-sm);
}

.footer-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.footer-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.footer-actions :deep(.el-button) {
  margin-left: 0;
}

@media (max-width: 767px) {
  .form-grid,
  .link-grid {
    grid-template-columns: 1fr;
  }

  .status-version {
    margin-left: 0;
    width: 100%;
  }

  .entry-footer {
    flex-direction: column;
    align-items: stretch;
    padding: 12px;
  }

  .footer-note {
    text-align: center;
  }

  .footer-actions {
    display: grid;
    grid-template-columns: 1fr;
  }

  .footer-actions :deep(.el-button) {
    width: 100%;
  }
}
</style>
