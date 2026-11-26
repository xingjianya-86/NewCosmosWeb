<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowDown,
  ArrowLeft,
  ArrowUp,
  Download,
  Plus,
  Refresh,
  Upload,
} from '@element-plus/icons-vue'
import {
  createDutyLeave,
  createDutyMember,
  deleteDutyLeave,
  deleteDutyMember,
  downloadDutyMemberTemplate,
  fillDutyMonths,
  getDutyLeaveAffectedMonths,
  importDutyMembers,
  listDutyLeaves,
  listDutyMembers,
  moveDutyMember,
  previewDutyMemberImport,
  updateDutyMember,
} from '@/api/duty'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import type {
  DutyGroupCode,
  DutyImportPreview,
  DutyImportResult,
  DutyLeave,
  DutyMember,
  DutyMemberInput,
} from '@/types/duty'
import {
  DUTY_GROUP_ORDER,
  GROUP_META,
  dutyError,
  formatDate,
  groupDisplayName,
  saveBlob,
  todayText,
} from './display'

type GroupFilter = 'ALL' | DutyGroupCode

const router = useRouter()

function routerBack(): void {
  void router.push('/duty')
}

interface MemberForm {
  name: string
  phone: string
  remark: string
  isActive: boolean
  hasJoinDate: boolean
  joinDate: string
  hasExitDate: boolean
  exitDate: string
  groupCodes: DutyGroupCode[]
}

const members = ref<DutyMember[]>([])
const loading = ref(false)
const loadError = ref('')
const groupFilter = ref<GroupFilter>('ALL')

const editorVisible = ref(false)
const editingId = ref<number | null>(null)
const saving = ref(false)
const form = reactive<MemberForm>({
  name: '',
  phone: '',
  remark: '',
  isActive: true,
  hasJoinDate: false,
  joinDate: todayText(),
  hasExitDate: false,
  exitDate: todayText(),
  groupCodes: [],
})

const leaveDialogVisible = ref(false)
const leaveMember = ref<DutyMember | null>(null)
const leaveRecords = ref<DutyLeave[]>([])
const leaveLoading = ref(false)
const leaveSaving = ref(false)
const leaveStart = ref(todayText())
const leaveEnd = ref(todayText())
const leaveReason = ref('')
const leaveAffectedMonths = ref<string[]>([])
const leaveConfirmVisible = ref(false)
const leaveRegenerateNow = ref(true)

const importVisible = ref(false)
const importFile = ref<File | null>(null)
const importPreview = ref<DutyImportPreview | null>(null)
const importResult = ref<DutyImportResult | null>(null)
const importPreviewing = ref(false)
const importing = ref(false)
const importError = ref('')
const fileInput = ref<HTMLInputElement | null>(null)

const downloadingTemplate = ref(false)
let loadTicket = 0

const filterOptions = computed(() => [
  { value: 'ALL' as const, label: '全部成员' },
  ...DUTY_GROUP_ORDER.map((code) => ({
    value: code,
    label: GROUP_META[code].label,
  })),
])

const filteredMembers = computed(() => {
  if (groupFilter.value === 'ALL') return members.value
  return members.value.filter((member) =>
    member.groups.some((group) => group.groupCode === groupFilter.value),
  )
})

const activeMembers = computed(() => members.value.filter((member) => member.isActive))

const groupCounts = computed(() => {
  const counts: Record<DutyGroupCode, number> = {
    LEADER: 0,
    MIDDLE: 0,
    MALE: 0,
    FEMALE: 0,
  }
  for (const member of activeMembers.value) {
    for (const group of member.groups) counts[group.groupCode] += 1
  }
  return counts
})

const editorTitle = computed(() =>
  editingId.value === null ? '新增成员' : `编辑成员：${form.name || '—'}`,
)

const canMoveInCurrentGroup = computed(
  () => groupFilter.value !== 'ALL' || members.value.some((member) => member.groups.length > 0),
)

const isBackfill = computed(() => leaveStart.value < todayText())

const leaveDialogTitle = computed(() =>
  leaveMember.value ? `${leaveMember.value.name} 的请假记录` : '请假记录',
)

const emptyDescription = computed(() =>
  groupFilter.value === 'ALL'
    ? '暂无值班成员，请新增或批量导入'
    : `${GROUP_META[groupFilter.value].label}暂无成员`,
)

function resetForm(): void {
  editingId.value = null
  form.name = ''
  form.phone = ''
  form.remark = ''
  form.isActive = true
  form.hasJoinDate = false
  form.joinDate = todayText()
  form.hasExitDate = false
  form.exitDate = todayText()
  form.groupCodes = []
}

function startAdd(): void {
  resetForm()
  editorVisible.value = true
}

function startEdit(member: DutyMember): void {
  editingId.value = member.id
  form.name = member.name
  form.phone = member.phone
  form.remark = member.remark
  form.isActive = member.isActive
  form.hasJoinDate = Boolean(member.joinDate)
  form.joinDate = member.joinDate ?? todayText()
  form.hasExitDate = Boolean(member.exitDate)
  form.exitDate = member.exitDate ?? todayText()
  form.groupCodes = member.groups.map((group) => group.groupCode)
  editorVisible.value = true
}

function cancelEdit(): void {
  editorVisible.value = false
}

async function load(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''
  try {
    const rows = await listDutyMembers()
    if (ticket !== loadTicket) return
    members.value = rows
  } catch (error) {
    if (ticket !== loadTicket) return
    members.value = []
    loadError.value = dutyError(error, '成员列表加载失败', '查看值班成员', 'DUTY_VIEW')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

function buildMemberInput(): DutyMemberInput | null {
  const name = form.name.trim()
  if (!name) {
    ElMessage.warning('姓名不能为空')
    return null
  }
  if (form.groupCodes.length === 0) {
    ElMessage.warning('请至少选择一个所属组')
    return null
  }
  if (form.hasJoinDate && form.hasExitDate && form.exitDate < form.joinDate) {
    ElMessage.warning('离职时间不能早于入职时间')
    return null
  }
  return {
    name,
    phone: form.phone.trim(),
    remark: form.remark.trim(),
    isActive: form.isActive,
    joinDate: form.hasJoinDate ? form.joinDate : null,
    exitDate: form.hasExitDate ? form.exitDate : null,
    groupCodes: [...form.groupCodes],
  }
}

async function saveMember(): Promise<void> {
  const input = buildMemberInput()
  if (!input || saving.value) return

  saving.value = true
  try {
    if (editingId.value !== null) {
      await updateDutyMember(editingId.value, input)
      ElMessage.success('成员已保存')
    } else {
      await createDutyMember(input)
      ElMessage.success('成员已添加')
    }
    editorVisible.value = false
    await load()
  } catch (error) {
    ElMessage.error(dutyError(error, '保存成员失败', '保存值班成员'))
  } finally {
    saving.value = false
  }
}

function targetGroup(member: DutyMember): DutyGroupCode | null {
  if (groupFilter.value !== 'ALL') return groupFilter.value
  return member.groups[0]?.groupCode ?? null
}

async function moveMember(member: DutyMember, direction: 'up' | 'down'): Promise<void> {
  const group = targetGroup(member)
  if (!group) {
    ElMessage.warning('该成员尚未分组，无法调整顺序')
    return
  }
  try {
    await moveDutyMember(member.id, direction, group)
    await load()
  } catch (error) {
    ElMessage.error(dutyError(error, '调整顺序失败', '调整成员顺序'))
  }
}

async function removeMember(member: DutyMember): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定要删除成员「${member.name}」吗？\n（其分组记录将一并删除，已生成班次中的姓名快照保留）`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await deleteDutyMember(member.id)
    ElMessage.success('成员已删除')
    await load()
  } catch (error) {
    ElMessage.error(dutyError(error, '删除成员失败', '删除值班成员'))
  }
}

/* ── 请假 ───────────────────────────────────────────────────── */

async function loadLeaveRecords(memberId: number): Promise<void> {
  leaveLoading.value = true
  try {
    leaveRecords.value = await listDutyLeaves(memberId)
  } catch (error) {
    leaveRecords.value = []
    ElMessage.error(dutyError(error, '加载请假记录失败', '查看请假记录', 'DUTY_VIEW'))
  } finally {
    leaveLoading.value = false
  }
}

async function openLeave(member: DutyMember): Promise<void> {
  leaveMember.value = member
  leaveStart.value = todayText()
  leaveEnd.value = todayText()
  leaveReason.value = ''
  leaveAffectedMonths.value = []
  leaveDialogVisible.value = true
  await loadLeaveRecords(member.id)
}

function closeLeave(): void {
  leaveDialogVisible.value = false
  leaveMember.value = null
  leaveRecords.value = []
}

async function prepareLeave(): Promise<void> {
  if (!leaveMember.value || leaveSaving.value) return
  if (!leaveStart.value || !leaveEnd.value) {
    ElMessage.warning('请选择请假起止日期')
    return
  }
  if (leaveEnd.value < leaveStart.value) {
    ElMessage.warning('请假结束日期不能早于开始日期')
    return
  }
  if (isBackfill.value && !leaveReason.value.trim()) {
    ElMessage.warning('补登记过去日期的请假必须填写事由')
    return
  }

  leaveLoading.value = true
  try {
    leaveAffectedMonths.value = await getDutyLeaveAffectedMonths(leaveStart.value)
    leaveRegenerateNow.value = true
    leaveConfirmVisible.value = true
  } catch (error) {
    ElMessage.error(dutyError(error, '读取受影响月份失败', '登记请假'))
  } finally {
    leaveLoading.value = false
  }
}

async function confirmLeave(): Promise<void> {
  if (!leaveMember.value || leaveSaving.value) return
  const member = leaveMember.value
  leaveSaving.value = true
  try {
    const months = await createDutyLeave({
      memberId: member.id,
      startDate: leaveStart.value,
      endDate: leaveEnd.value,
      reason: leaveReason.value.trim(),
    })
    const affected = months.length > 0 ? months : leaveAffectedMonths.value

    let regenerated = 0
    let regenerateFailed = false
    if (leaveRegenerateNow.value && affected.length > 0) {
      try {
        regenerated = await fillDutyMonths(affected)
      } catch (error) {
        regenerateFailed = true
        ElMessage.error(dutyError(error, '重新生成失败', '重新生成值班表'))
      }
    }

    leaveConfirmVisible.value = false
    leaveReason.value = ''
    ElMessage.success(
      leaveRegenerateNow.value && !regenerateFailed
        ? `请假已登记，已重新生成 ${regenerated} 个月份`
        : `请假已登记，已删除 ${affected.length} 个月份排班，请到值班表页重新生成`,
    )
    await Promise.all([loadLeaveRecords(member.id), load()])
  } catch (error) {
    ElMessage.error(dutyError(error, '登记请假失败', '登记请假'))
  } finally {
    leaveSaving.value = false
  }
}

async function removeLeave(leave: DutyLeave): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定要删除 ${leave.memberName} 的请假记录（${formatDate(leave.startDate)} ~ ${formatDate(leave.endDate)}）吗？\n删除后将删除受影响月份的排班数据。`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    const months = await deleteDutyLeave(leave.id)
    ElMessage.success(
      months.length > 0
        ? `请假记录已删除，已删除 ${months.length} 个月份排班，请到值班表页重新生成`
        : '请假记录已删除',
    )
    if (leaveMember.value) await loadLeaveRecords(leaveMember.value.id)
    await load()
  } catch (error) {
    ElMessage.error(dutyError(error, '删除请假记录失败', '删除请假记录'))
  }
}

/* ── 导入 ───────────────────────────────────────────────────── */

function openImport(): void {
  importFile.value = null
  importPreview.value = null
  importResult.value = null
  importError.value = ''
  importVisible.value = true
}

function closeImport(): void {
  importVisible.value = false
  importFile.value = null
  importPreview.value = null
  importResult.value = null
  importError.value = ''
  if (fileInput.value) fileInput.value.value = ''
}

function pickFile(): void {
  fileInput.value?.click()
}

function onFileChange(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  importPreview.value = null
  importResult.value = null
  importError.value = ''
  if (!file) {
    importFile.value = null
    return
  }
  if (!/\.(xlsx|xls)$/i.test(file.name)) {
    ElMessage.warning('仅支持 .xlsx / .xls 文件')
    input.value = ''
    importFile.value = null
    return
  }
  importFile.value = file
}

async function previewImport(): Promise<void> {
  if (!importFile.value || importPreviewing.value) {
    ElMessage.warning('请先选择导入文件')
    return
  }
  importPreviewing.value = true
  importError.value = ''
  importResult.value = null
  try {
    importPreview.value = await previewDutyMemberImport(importFile.value)
  } catch (error) {
    importPreview.value = null
    importError.value = dutyError(error, '解析导入文件失败', '解析导入文件')
  } finally {
    importPreviewing.value = false
  }
}

async function confirmImport(): Promise<void> {
  if (!importFile.value || !importPreview.value || importing.value) return
  const preview = importPreview.value
  if (!preview.isValid) {
    ElMessage.warning('导入文件存在校验错误，请修正后重新选择文件')
    return
  }

  const confirmLines = [
    `全量对齐：新增 ${preview.newCount} 人 / 对齐 ${preview.updateCount} 人 / 移出 ${preview.removeCount} 处归属`,
  ]
  if (preview.clearedGroupNames.length > 0) {
    confirmLines.push(
      '',
      `警告：${preview.clearedGroupNames.join('、')} 名单为空，该组现有成员将被全部移出！`,
    )
  }
  confirmLines.push('', '确定开始导入吗？')

  try {
    await ElMessageBox.confirm(confirmLines.join('\n'), '确认导入（全量对齐）', {
      confirmButtonText: '开始导入',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }

  importing.value = true
  try {
    const result = await importDutyMembers(importFile.value)
    importResult.value = result
    ElMessage({
      type: successType(result),
      message: result.summaryText || result.message || '导入完成',
    })
    await load()
  } catch (error) {
    ElMessage.error(dutyError(error, '批量导入成员失败', '批量导入成员'))
  } finally {
    importing.value = false
  }
}

function successType(result: DutyImportResult): 'success' | 'warning' {
  return result.success && result.errors.length === 0 ? 'success' : 'warning'
}

async function downloadTemplate(): Promise<void> {
  if (downloadingTemplate.value) return
  downloadingTemplate.value = true
  try {
    const blob = await downloadDutyMemberTemplate()
    saveBlob(blob, '值班成员导入模板.xlsx')
    ElMessage.success('导入模板已下载')
  } catch (error) {
    ElMessage.error(dutyError(error, '下载导入模板失败', '下载导入模板'))
  } finally {
    downloadingTemplate.value = false
  }
}

function groupTags(member: DutyMember): DutyGroupCode[] {
  return member.groups.map((group) => group.groupCode)
}

function rosterNames(preview: DutyImportPreview, groupCode: DutyGroupCode): string {
  const roster = preview.rosters.find((item) => item.groupCode === groupCode)
  if (!roster || roster.entries.length === 0) return '（空 → 将清空该组）'
  return roster.entries
    .slice(0, 10)
    .map((entry) => `${entry.seq}.${entry.name}`)
    .join('、')
}

function rosterCount(preview: DutyImportPreview, groupCode: DutyGroupCode): number {
  return preview.rosters.find((item) => item.groupCode === groupCode)?.memberCount ?? 0
}

function currentSeqLines(preview: DutyImportPreview): string[] {
  const lines: string[] = []
  for (const [group, line] of Object.entries(preview.currentSeqByLine)) {
    const parts = Object.entries(line)
      .filter(([, name]) => Boolean(name))
      .map(([dateType, name]) => {
        const label =
          dateType === 'HOLIDAY'
            ? '法定节假日'
            : dateType === 'RESTDAY'
              ? '休息日'
              : '工作日'
        return `${label}=${name}`
      })
    if (parts.length > 0) {
      lines.push(`${groupDisplayName(group as DutyGroupCode)}：${parts.join(' / ')}`)
    }
  }
  return lines
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page duty-member-page">
    <NcPageHeader
      title="值班成员管理"
      description="四组名单维护 · 一人可属多组 · 组内顺序即轮转顺序"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="routerBack">返回值班表</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      class="load-alert"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button v-if="!loadError.includes('无权限')" link type="primary" @click="load()">
        重新加载
      </el-button>
    </el-alert>

    <div class="stat-grid">
      <NcStat label="总人数" :value="members.length" tone="ink" />
      <NcStat label="启用" :value="activeMembers.length" tone="pine" />
      <NcStat
        v-for="group in DUTY_GROUP_ORDER"
        :key="group"
        :label="GROUP_META[group].label"
        :value="groupCounts[group]"
        :tone="GROUP_META[group].tone === 'violet' ? 'seal' : GROUP_META[group].tone"
        :hint="`${GROUP_META[group].label}启用人数`"
      />
    </div>

    <NcCard padding="sm" class="toolbar-card">
      <div class="toolbar">
        <div class="toolbar-left">
          <el-select v-model="groupFilter" class="group-filter" aria-label="按组筛选">
            <el-option
              v-for="option in filterOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
          <el-button :icon="Refresh" text :loading="loading" @click="load()">刷新</el-button>
        </div>

        <div class="toolbar-actions">
          <el-button
            :icon="Download"
            :loading="downloadingTemplate"
            @click="downloadTemplate"
          >
            下载导入模板
          </el-button>
          <el-button :icon="Upload" @click="openImport">批量导入</el-button>
          <el-button type="primary" :icon="Plus" @click="startAdd">新增成员</el-button>
        </div>
      </div>
    </NcCard>

    <NcCard v-if="editorVisible" :title="editorTitle" class="editor-card">
      <form class="member-form" @submit.prevent="saveMember">
        <div class="form-grid">
          <label class="field">
            <span>姓名 <em>*</em></span>
            <el-input v-model="form.name" maxlength="30" placeholder="请输入姓名" />
          </label>
          <label class="field">
            <span>电话</span>
            <el-input v-model="form.phone" maxlength="20" placeholder="选填" />
          </label>
          <label class="field field--wide">
            <span>备注</span>
            <el-input v-model="form.remark" maxlength="100" placeholder="选填" />
          </label>
          <div class="field field--switch">
            <span>启用</span>
            <el-switch v-model="form.isActive" active-text="参与轮转" />
          </div>
        </div>

        <div class="date-row">
          <el-checkbox v-model="form.hasJoinDate">入职时间</el-checkbox>
          <el-date-picker
            v-model="form.joinDate"
            type="date"
            value-format="YYYY-MM-DD"
            :disabled="!form.hasJoinDate"
            placeholder="选择入职日期"
          />
          <el-checkbox v-model="form.hasExitDate">离职时间</el-checkbox>
          <el-date-picker
            v-model="form.exitDate"
            type="date"
            value-format="YYYY-MM-DD"
            :disabled="!form.hasExitDate"
            placeholder="选择离职日期"
          />
        </div>

        <p class="form-hint">
          入职/离职时间按日精确参与轮转：值班日当天须已入职且未离职；历史班次不受影响。长期请假请在成员列表点「请假」登记。
        </p>

        <div class="group-field">
          <span class="group-field__label">所属组 <em>*</em></span>
          <el-checkbox-group v-model="form.groupCodes" class="group-checks">
            <el-checkbox
              v-for="group in DUTY_GROUP_ORDER"
              :key="group"
              :value="group"
            >
              {{ GROUP_META[group].label }}
            </el-checkbox>
          </el-checkbox-group>
        </div>

        <div class="form-actions">
          <el-button @click="cancelEdit">取消</el-button>
          <el-button type="primary" native-type="submit" :loading="saving">保存</el-button>
        </div>
      </form>
    </NcCard>

    <NcCard padding="none" class="list-card">
      <div v-loading="loading" class="member-list">
        <el-skeleton v-if="loading && members.length === 0" :rows="5" animated class="skeleton" />

        <template v-else-if="filteredMembers.length > 0">
          <article v-for="member in filteredMembers" :key="member.id" class="member-card">
            <div class="member-main">
              <div class="member-name-row">
                <strong class="member-name">{{ member.name }}</strong>
                <span
                  class="member-badge"
                  :class="member.isActive ? 'is-active' : 'is-inactive'"
                >
                  {{ member.isActive ? '启用' : '停用' }}
                </span>
                <span
                  v-for="group in groupTags(member)"
                  :key="group"
                  class="group-tag"
                  :class="`group-tag--${GROUP_META[group].tone}`"
                >
                  {{ GROUP_META[group].label }}
                </span>
              </div>
              <p v-if="member.phone" class="member-phone">{{ member.phone }}</p>
              <p class="member-dates">
                入职 {{ formatDate(member.joinDate) }} · 离职 {{ formatDate(member.exitDate) }}
              </p>
              <p v-if="member.remark" class="member-remark">{{ member.remark }}</p>
            </div>

            <div class="member-actions">
              <el-button
                size="small"
                :icon="ArrowUp"
                :disabled="!canMoveInCurrentGroup"
                @click="moveMember(member, 'up')"
              >
                上移
              </el-button>
              <el-button
                size="small"
                :icon="ArrowDown"
                :disabled="!canMoveInCurrentGroup"
                @click="moveMember(member, 'down')"
              >
                下移
              </el-button>
              <el-button size="small" @click="openLeave(member)">请假</el-button>
              <el-button size="small" type="primary" plain @click="startEdit(member)">
                编辑
              </el-button>
              <el-button size="small" type="danger" plain @click="removeMember(member)">
                删除
              </el-button>
            </div>
          </article>
        </template>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="emptyDescription">
            <el-button type="primary" :icon="Plus" @click="startAdd">新增成员</el-button>
          </el-empty>
        </div>
      </div>
    </NcCard>

    <!-- 请假面板 -->
    <el-dialog
      v-model="leaveDialogVisible"
      :title="leaveDialogTitle"
      width="620px"
      @closed="closeLeave"
    >
      <div class="leave-panel">
        <div class="leave-list-head">
          <span>已有请假记录</span>
          <el-button size="small" text :loading="leaveLoading" @click="leaveMember && loadLeaveRecords(leaveMember.id)">
            刷新
          </el-button>
        </div>

        <div v-loading="leaveLoading" class="leave-list">
          <div v-for="leave in leaveRecords" :key="leave.id" class="leave-row">
            <div class="leave-row__main">
              <strong>{{ formatDate(leave.startDate) }} ~ {{ formatDate(leave.endDate) }}</strong>
              <span class="leave-days">{{ leave.days }} 天</span>
              <p v-if="leave.reason">{{ leave.reason }}</p>
            </div>
            <el-button size="small" type="danger" plain @click="removeLeave(leave)">
              删除
            </el-button>
          </div>
          <el-empty
            v-if="!leaveLoading && leaveRecords.length === 0"
            description="暂无请假记录"
            :image-size="70"
          />
        </div>

        <div class="leave-form">
          <p class="leave-form__hint">
            登记请假会删除受影响月份排班、需重新生成；支持补登记过去日期，须填写事由。
          </p>
          <div class="leave-date-row">
            <label>
              <span>开始</span>
              <el-date-picker
                v-model="leaveStart"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="开始日期"
              />
            </label>
            <label>
              <span>结束</span>
              <el-date-picker
                v-model="leaveEnd"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="结束日期"
              />
            </label>
          </div>
          <label class="leave-reason">
            <span>事由</span>
            <el-input
              v-model="leaveReason"
              maxlength="100"
              :placeholder="isBackfill ? '补登记必填' : '选填'"
            />
          </label>
          <el-alert
            v-if="isBackfill"
            type="warning"
            :closable="false"
            show-icon
            title="补登记：将删除起始月起至最后已生成月的排班数据（需重新生成）；已打印的旧表需重打。"
          />
        </div>
      </div>

      <template #footer>
        <el-button @click="leaveDialogVisible = false">关闭</el-button>
        <el-button type="primary" :loading="leaveLoading || leaveSaving" @click="prepareLeave">
          登记请假
        </el-button>
      </template>
    </el-dialog>

    <!-- 请假影响范围确认 -->
    <el-dialog
      v-model="leaveConfirmVisible"
      title="确认请假影响范围"
      width="460px"
      append-to-body
    >
      <p class="confirm-text">
        登记后将从以下已生成月份删除排班数据：
      </p>
      <div v-if="leaveAffectedMonths.length > 0" class="month-chips">
        <span v-for="item in leaveAffectedMonths" :key="item" class="month-chip">
          {{ item }}
        </span>
      </div>
      <p v-else class="confirm-muted">当前没有已生成的值班表</p>

      <el-radio-group v-model="leaveRegenerateNow" class="regen-choice">
        <el-radio :value="true">删除并立即重新生成</el-radio>
        <el-radio :value="false">仅删除（稍后手动生成）</el-radio>
      </el-radio-group>

      <p class="confirm-muted">
        已登记的串班/代班将在重新生成时按记录重放（不匹配或命中请假的会跳过）；已打印的旧表需重新打印。
      </p>

      <template #footer>
        <el-button @click="leaveConfirmVisible = false">取消</el-button>
        <el-button type="primary" :loading="leaveSaving" @click="confirmLeave">
          确认登记
        </el-button>
      </template>
    </el-dialog>

    <!-- 批量导入面板 -->
    <el-dialog v-model="importVisible" title="批量导入值班成员" width="680px" @closed="closeImport">
      <div class="import-panel">
        <p class="import-desc">
          队列名单模式：每组「序号 + 姓名」成对填写，序号即该组轮转顺序（按值升序、组内唯一、允许跳号）。导入为全量对齐——名单里没列到的库内成员将从其所在组移除（某组全空 = 清空该组）。请使用「下载导入模板」生成的标准模板填写。
        </p>

        <input
          ref="fileInput"
          class="file-input"
          type="file"
          accept=".xlsx,.xls"
          @change="onFileChange"
        />
        <div class="file-row">
          <span class="file-name" :title="importFile?.name">
            {{ importFile?.name || '尚未选择文件' }}
          </span>
          <el-button @click="pickFile">选择文件</el-button>
          <el-button
            :loading="importPreviewing"
            :disabled="!importFile"
            @click="previewImport"
          >
            预览
          </el-button>
        </div>

        <el-alert
          v-if="importError"
          type="error"
          :title="importError"
          :closable="false"
          show-icon
        />

        <template v-if="importPreview">
          <div class="preview-summary">
            <span>名单共 {{ importPreview.totalMembers }} 人</span>
            <span class="preview-chip preview-chip--new">新增 {{ importPreview.newCount }}</span>
            <span class="preview-chip preview-chip--update">对齐 {{ importPreview.updateCount }}</span>
            <span class="preview-chip preview-chip--remove">移出 {{ importPreview.removeCount }} 处归属</span>
            <span
              class="preview-chip"
              :class="importPreview.isValid ? 'preview-chip--ok' : 'preview-chip--error'"
            >
              {{ importPreview.isValid ? '校验通过' : `${importPreview.errors.length} 条错误` }}
            </span>
          </div>

          <div class="roster-grid">
            <section v-for="group in DUTY_GROUP_ORDER" :key="group" class="roster-block">
              <header>
                <span>{{ GROUP_META[group].label }}</span>
                <strong>{{ rosterCount(importPreview, group) }} 人</strong>
              </header>
              <p class="roster-names">{{ rosterNames(importPreview, group) }}</p>
            </section>
          </div>

          <div v-if="importPreview.clearedGroupNames.length > 0" class="clear-warning">
            警告：{{ importPreview.clearedGroupNames.join('、') }} 名单为空，导入后该组现有成员将被全部移出。
          </div>

          <div v-if="importPreview.anchorDate" class="anchor-note">
            轮转锚点：{{ importPreview.anchorDate }} 起按名单轮转（生成该月值班表时将从此日起）。
            <div v-for="line in currentSeqLines(importPreview)" :key="line" class="anchor-line">
              {{ line }}
            </div>
          </div>
          <p v-else class="confirm-muted">未填写起始日期：仅导入名单，不调整轮转起点。</p>

          <div v-if="importPreview.errors.length > 0" class="error-list">
            <p class="error-list__title">校验错误（最多显示 50 条）</p>
            <p v-for="(error, index) in importPreview.errors.slice(0, 50)" :key="index">
              ✗ {{ error }}
            </p>
            <p v-if="importPreview.errors.length > 50" class="confirm-muted">
              …… 其余 {{ importPreview.errors.length - 50 }} 条校验错误略
            </p>
          </div>
        </template>

        <div v-if="importResult" class="import-result">
          <p class="import-result__title">
            {{ importResult.summaryText || importResult.message || '导入完成' }}
          </p>
          <p v-for="(error, index) in importResult.errors.slice(0, 50)" :key="index">
            ✗ {{ error }}
          </p>
          <p v-if="importResult.errors.length === 0" class="confirm-muted">
            导入成功，成员列表已刷新。
          </p>
        </div>
      </div>

      <template #footer>
        <el-button @click="importVisible = false">关闭</el-button>
        <el-button
          type="primary"
          :loading="importing"
          :disabled="!importPreview || !importPreview.isValid || Boolean(importResult)"
          @click="confirmImport"
        >
          开始导入
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.duty-member-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.stat-grid {
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  gap: 10px;
}

.toolbar-card,
.editor-card,
.list-card {
  overflow: hidden;
}

.toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 2px 4px;
}

.toolbar-left,
.toolbar-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.group-filter {
  width: 150px;
}

.load-alert {
  margin: 0;
}

/* ── 内联编辑 ───────────────────────────────────────────────── */
.member-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.field--wide {
  grid-column: 1 / -1;
}

.field--switch {
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
  padding: 6px 2px;
}

.field em,
.group-field em {
  color: var(--nc-seal);
  font-style: normal;
}

.date-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px 16px;
}

.form-hint,
.leave-form__hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.75;
}

.group-field {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.group-field__label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.group-checks {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 14px;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 4px;
}

/* ── 成员卡片 ───────────────────────────────────────────────── */
.member-list {
  min-height: 200px;
  padding: 10px;
}

.skeleton {
  padding: 12px;
}

.member-card {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 14px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.member-card + .member-card {
  margin-top: 8px;
}

.member-main {
  min-width: 0;
}

.member-name-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.member-name {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.member-badge,
.group-tag {
  display: inline-block;
  padding: 1px 7px;
  border-radius: var(--nc-radius-xs);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.7;
  white-space: nowrap;
}

.member-badge.is-active {
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
}

.member-badge.is-inactive {
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
}

.group-tag--violet {
  background-color: var(--nc-violet-soft);
  color: var(--nc-violet);
}

.group-tag--amber {
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.group-tag--pine {
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
}

.group-tag--seal {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.member-phone {
  margin-top: 3px;
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.member-dates {
  margin-top: 3px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.member-remark {
  margin-top: 3px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.member-actions {
  display: flex;
  flex: none;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 6px;
  max-width: 340px;
}

.member-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.empty-wrap {
  padding: 20px 8px;
}

/* ── 请假 ───────────────────────────────────────────────────── */
.leave-panel {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.leave-list-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.leave-list {
  max-height: 240px;
  overflow-y: auto;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.leave-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
  padding: 9px 12px;
}

.leave-row + .leave-row {
  border-top: 1px solid var(--nc-line-faint);
}

.leave-row__main {
  min-width: 0;
}

.leave-row__main strong {
  font-size: var(--nc-text-sm);
}

.leave-row__main p {
  margin-top: 2px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.leave-days {
  margin-left: 7px;
  padding: 0 6px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: 10px;
  white-space: nowrap;
}

.leave-form {
  display: flex;
  flex-direction: column;
  gap: 9px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.leave-date-row {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.leave-date-row label,
.leave-reason {
  display: flex;
  flex: 1 1 200px;
  align-items: center;
  gap: 8px;
  min-width: 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.leave-date-row label > span,
.leave-reason > span {
  flex: none;
  width: 34px;
}

.leave-date-row :deep(.el-date-editor),
.leave-reason :deep(.el-input) {
  flex: 1;
  min-width: 0;
}

.confirm-text {
  margin-bottom: 8px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.month-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 12px;
}

.month-chip {
  padding: 2px 8px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.confirm-muted {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.7;
}

.regen-choice {
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin: 10px 0;
}

/* ── 导入 ───────────────────────────────────────────────────── */
.import-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.import-desc {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.8;
}

.file-input {
  display: none;
}

.file-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.file-name {
  flex: 1;
  overflow: hidden;
  min-width: 0;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.preview-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 7px;
  padding: 9px 11px;
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  font-size: var(--nc-text-sm);
}

.preview-chip {
  padding: 0 7px;
  border-radius: var(--nc-radius-xs);
  font-size: var(--nc-text-xs);
  line-height: 1.8;
}

.preview-chip--new {
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
}

.preview-chip--update {
  background-color: var(--nc-violet-soft);
  color: var(--nc-violet);
}

.preview-chip--remove {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.preview-chip--ok {
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
}

.preview-chip--error {
  background: #fde8e8;
  color: #c62828;
}

.roster-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.roster-block {
  padding: 9px 11px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.roster-block header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.roster-block header strong {
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.roster-names {
  margin-top: 3px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.7;
}

.clear-warning {
  padding: 8px 11px;
  border-left: 3px solid var(--nc-seal);
  background-color: #fde8e8;
  color: #9b1c1c;
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.anchor-note {
  padding: 8px 11px;
  border-left: 3px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.anchor-line {
  margin-top: 2px;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.error-list {
  max-height: 170px;
  padding: 8px 11px;
  overflow-y: auto;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-xs);
  line-height: 1.7;
}

.error-list__title {
  margin-bottom: 3px;
  font-weight: 700;
}

.import-result {
  padding: 9px 11px;
  border-left: 3px solid var(--nc-pine);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.import-result__title {
  font-weight: 700;
}

@media (max-width: 1100px) {
  .stat-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

@media (max-width: 860px) {
  .member-card {
    flex-direction: column;
  }

  .member-actions {
    justify-content: flex-start;
    max-width: none;
  }

  .roster-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .form-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .field--wide {
    grid-column: auto;
  }
}

@media (max-width: 560px) {
  .stat-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .toolbar-left,
  .toolbar-actions {
    width: 100%;
  }

  .group-filter {
    flex: 1;
    width: auto;
  }

  .toolbar-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .toolbar-actions :deep(.el-button) {
    margin-left: 0;
  }

  .toolbar-actions :deep(.el-button:last-child) {
    grid-column: 1 / -1;
  }

  .member-actions {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    width: 100%;
  }

  .member-actions :deep(.el-button) {
    margin-left: 0;
  }

  .file-row {
    flex-wrap: wrap;
  }

  .file-name {
    flex: 1 1 100%;
  }
}
</style>
