<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Plus, Refresh } from '@element-plus/icons-vue'
import {
  createDutyAdjustment,
  createDutyLeave,
  deleteDutyLeave,
  fillDutyMonths,
  getDutyLeaveAffectedMonths,
  listDutyAdjustments,
  listDutyLeaves,
  listDutyMemberFutureShifts,
  listDutyMembers,
  rescheduleDutyAdjustment,
  revokeDutyAdjustment,
} from '@/api/duty'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type {
  DutyAdjustment,
  DutyFutureShift,
  DutyGroupCode,
  DutyLeave,
  DutyMember,
} from '@/types/duty'
import {
  DUTY_GROUP_ORDER,
  adjustmentDirection,
  dateTypeLabel,
  dutyError,
  formatDate,
  formatDateTime,
  formatShortDate,
  groupDisplayName,
  statusMeta,
  todayText,
} from './display'

type AdjustMode = 'leave' | 'swap' | 'substitute'

const route = useRoute()
const router = useRouter()

const members = ref<DutyMember[]>([])
const leaves = ref<DutyLeave[]>([])
const adjustments = ref<DutyAdjustment[]>([])
const loading = ref(false)
const loadError = ref('')
const showCancelled = ref(false)

const leaveVisible = ref(false)
const leaveMemberId = ref<number | null>(null)
const leaveStart = ref(todayText())
const leaveEnd = ref(todayText())
const leaveReason = ref('')
const leaveAffected = ref<string[]>([])
const leaveAffectedError = ref('')
const leaveRegenerate = ref(true)
const leaveSaving = ref(false)
const leaveAffectedLoading = ref(false)

const swapVisible = ref(false)
const swapFromId = ref<number | null>(null)
const swapFromShifts = ref<DutyFutureShift[]>([])
const swapFromScheduleId = ref(0)
const swapToId = ref<number | null>(null)
const swapToShifts = ref<DutyFutureShift[]>([])
const swapToScheduleId = ref(0)
const swapReason = ref('')
const swapSaving = ref(false)
const swapFromLoading = ref(false)
const swapToLoading = ref(false)

const substituteVisible = ref(false)
const substituteFromId = ref<number | null>(null)
const substituteFromShifts = ref<DutyFutureShift[]>([])
const substituteScheduleId = ref(0)
const substituteToId = ref<number | null>(null)
const substituteReason = ref('')
const substituteSaving = ref(false)
const substituteLoading = ref(false)

const rescheduleVisible = ref(false)
const rescheduleTarget = ref<DutyAdjustment | null>(null)
const rescheduleDate = ref(todayText())
const rescheduleSaving = ref(false)

let loadTicket = 0
let affectedTicket = 0
let swapFromTicket = 0
let swapToTicket = 0
let substituteTicket = 0
let prefillApplied = false

const sortedMembers = computed(() =>
  [...members.value].sort((a, b) => a.name.localeCompare(b.name, 'zh-CN')),
)

const leaveRows = computed(() =>
  [...leaves.value].sort((a, b) => b.startDate.localeCompare(a.startDate)),
)

const swapRows = computed(() =>
  adjustments.value
    .filter(
      (item) =>
        item.changeType === 'SWAP' && (showCancelled.value || !item.isCancelled),
    )
    .sort((a, b) => b.dutyDate.localeCompare(a.dutyDate)),
)

const substituteRows = computed(() =>
  adjustments.value
    .filter(
      (item) =>
        item.changeType === 'SUBSTITUTE' && (showCancelled.value || !item.isCancelled),
    )
    .sort((a, b) => b.dutyDate.localeCompare(a.dutyDate)),
)

const selectedSwapFromShift = computed(() =>
  swapFromShifts.value.find((shift) => shift.scheduleId === swapFromScheduleId.value),
)

const selectedSwapToShift = computed(() =>
  swapToShifts.value.find((shift) => shift.scheduleId === swapToScheduleId.value),
)

const selectedSubstituteShift = computed(() =>
  substituteFromShifts.value.find(
    (shift) => shift.scheduleId === substituteScheduleId.value,
  ),
)

const swapGroup = computed<DutyGroupCode | ''>(() => {
  if (selectedSwapFromShift.value?.groupCode) return selectedSwapFromShift.value.groupCode
  const member = members.value.find((item) => item.id === swapFromId.value)
  return member?.groups[0]?.groupCode ?? ''
})

const substituteGroup = computed<DutyGroupCode | ''>(() => {
  if (selectedSubstituteShift.value?.groupCode) return selectedSubstituteShift.value.groupCode
  const member = members.value.find((item) => item.id === substituteFromId.value)
  return member?.groups[0]?.groupCode ?? ''
})

const swapTargetOptions = computed(() =>
  targetsInGroup(swapFromId.value, swapGroup.value),
)

const substituteTargetOptions = computed(() =>
  targetsInGroup(substituteFromId.value, substituteGroup.value),
)

const isBackfill = computed(() => leaveStart.value < todayText())

const prefillMode = ref<AdjustMode | null>(null)

function targetsInGroup(excludeId: number | null, group: DutyGroupCode | ''): DutyMember[] {
  return sortedMembers.value.filter((member) => {
    if (!member.isActive || member.id === excludeId) return false
    if (!group) return true
    return member.groups.some((item) => item.groupCode === group)
  })
}

function readQueryText(value: unknown): string {
  if (Array.isArray(value)) return String(value[0] ?? '')
  return value === null || value === undefined ? '' : String(value)
}

function parseGroup(value: unknown): DutyGroupCode | '' {
  const code = readQueryText(value).toUpperCase()
  return (DUTY_GROUP_ORDER as readonly string[]).includes(code)
    ? (code as DutyGroupCode)
    : ''
}

function shiftLabel(shift: DutyFutureShift): string {
  return `${formatDate(shift.date)} ${dateTypeLabel(shift.dateType)}${shift.isAdjusted ? '（已调整）' : ''}`
}

async function loadAll(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''
  try {
    const [leaveResult, adjustResult, memberResult] = await Promise.allSettled([
      listDutyLeaves(),
      listDutyAdjustments(true),
      listDutyMembers(),
    ])
    if (ticket !== loadTicket) return

    leaves.value = leaveResult.status === 'fulfilled' ? leaveResult.value : []
    adjustments.value = adjustResult.status === 'fulfilled' ? adjustResult.value : []
    members.value = memberResult.status === 'fulfilled' ? memberResult.value : []

    const rejected = [leaveResult, adjustResult, memberResult].find(
      (result) => result.status === 'rejected',
    )
    if (rejected && rejected.status === 'rejected') {
      loadError.value = dutyError(
        rejected.reason,
        '班务调整数据加载失败',
        '查看班务调整',
        'DUTY_VIEW',
      )
    }
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

function goBack(): void {
  void router.push('/duty')
}

function memberName(id: number | null): string {
  return members.value.find((member) => member.id === id)?.name ?? ''
}

/* ── 请假 ───────────────────────────────────────────────────── */

async function loadLeaveAffected(): Promise<void> {
  if (!leaveStart.value) {
    leaveAffected.value = []
    return
  }
  const ticket = ++affectedTicket
  leaveAffectedLoading.value = true
  leaveAffectedError.value = ''
  try {
    const months = await getDutyLeaveAffectedMonths(leaveStart.value)
    if (ticket !== affectedTicket) return
    leaveAffected.value = months
  } catch (error) {
    if (ticket !== affectedTicket) return
    leaveAffected.value = []
    leaveAffectedError.value = dutyError(error, '读取受影响月份失败', '登记请假')
  } finally {
    if (ticket === affectedTicket) leaveAffectedLoading.value = false
  }
}

function openLeave(): void {
  leaveMemberId.value = null
  leaveStart.value = todayText()
  leaveEnd.value = todayText()
  leaveReason.value = ''
  leaveAffected.value = []
  leaveAffectedError.value = ''
  leaveRegenerate.value = true
  leaveVisible.value = true
  void loadLeaveAffected()
}

async function saveLeave(): Promise<void> {
  if (leaveSaving.value) return
  if (leaveMemberId.value === null) {
    ElMessage.warning('请选择请假人员')
    return
  }
  if (!leaveStart.value || !leaveEnd.value) {
    ElMessage.warning('请选择请假起止日期')
    return
  }
  if (leaveEnd.value < leaveStart.value) {
    ElMessage.warning('结束日期不能早于开始日期')
    return
  }
  if (isBackfill.value && !leaveReason.value.trim()) {
    ElMessage.warning('补登记过去日期的请假必须填写事由')
    return
  }

  leaveSaving.value = true
  try {
    const months = await createDutyLeave({
      memberId: leaveMemberId.value,
      startDate: leaveStart.value,
      endDate: leaveEnd.value,
      reason: leaveReason.value.trim(),
    })
    const affected = months.length > 0 ? months : leaveAffected.value

    let regenerated = 0
    let regenerateFailed = false
    if (leaveRegenerate.value && affected.length > 0) {
      try {
        regenerated = await fillDutyMonths(affected)
      } catch (error) {
        regenerateFailed = true
        ElMessage.error(dutyError(error, '重新生成失败', '重新生成值班表'))
      }
    }

    ElMessage.success(
      leaveRegenerate.value && !regenerateFailed
        ? `请假已登记，已重新生成 ${regenerated} 个月份`
        : `请假已登记，已删除 ${affected.length} 个月份排班，请到值班表页重新生成`,
    )
    leaveVisible.value = false
    await loadAll()
  } catch (error) {
    ElMessage.error(dutyError(error, '登记请假失败', '登记请假'))
  } finally {
    leaveSaving.value = false
  }
}

async function removeLeave(leave: DutyLeave): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定撤销 ${leave.memberName} 的请假（${formatDate(leave.startDate)} ~ ${formatDate(leave.endDate)}）吗？\n撤销后将删除受影响月份的排班数据。`,
      '撤销请假',
      { confirmButtonText: '撤销', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    const months = await deleteDutyLeave(leave.id)
    ElMessage.success(
      months.length > 0
        ? `请假已撤销，已删除 ${months.length} 个月份排班，请到值班表页重新生成`
        : '请假已撤销',
    )
    await loadAll()
  } catch (error) {
    ElMessage.error(dutyError(error, '撤销请假失败', '撤销请假'))
  }
}

/* ── 串班 ───────────────────────────────────────────────────── */

function resetSwapForm(): void {
  swapFromId.value = null
  swapFromShifts.value = []
  swapFromScheduleId.value = 0
  swapToId.value = null
  swapToShifts.value = []
  swapToScheduleId.value = 0
  swapReason.value = ''
}

function openSwap(): void {
  resetSwapForm()
  swapVisible.value = true
}

async function loadSwapFromShifts(
  memberId: number,
  preferredDate = '',
  preferredGroup: DutyGroupCode | '' = '',
): Promise<void> {
  const ticket = ++swapFromTicket
  swapFromLoading.value = true
  swapFromShifts.value = []
  swapFromScheduleId.value = 0
  swapToId.value = null
  swapToShifts.value = []
  swapToScheduleId.value = 0
  try {
    const rows = await listDutyMemberFutureShifts(memberId)
    if (ticket !== swapFromTicket) return
    swapFromShifts.value = preferredGroup
      ? rows.filter((shift) => !shift.groupCode || shift.groupCode === preferredGroup)
      : rows
    const match = preferredDate
      ? swapFromShifts.value.find(
          (shift) =>
            shift.date === preferredDate &&
            (!preferredGroup || !shift.groupCode || shift.groupCode === preferredGroup),
        )
      : null
    if (match) swapFromScheduleId.value = match.scheduleId
  } catch (error) {
    if (ticket !== swapFromTicket) return
    ElMessage.error(dutyError(error, '加载甲方班次失败', '加载未来班次'))
  } finally {
    if (ticket === swapFromTicket) swapFromLoading.value = false
  }
}

async function loadSwapToShifts(memberId: number): Promise<void> {
  const ticket = ++swapToTicket
  swapToLoading.value = true
  swapToShifts.value = []
  swapToScheduleId.value = 0
  try {
    const rows = await listDutyMemberFutureShifts(memberId)
    if (ticket !== swapToTicket) return
    const group = swapGroup.value
    swapToShifts.value = group
      ? rows.filter((shift) => !shift.groupCode || shift.groupCode === group)
      : rows
  } catch (error) {
    if (ticket !== swapToTicket) return
    ElMessage.error(dutyError(error, '加载乙方班次失败', '加载未来班次'))
  } finally {
    if (ticket === swapToTicket) swapToLoading.value = false
  }
}

function handleSwapFromChange(): void {
  swapToId.value = null
  swapToShifts.value = []
  swapToScheduleId.value = 0
  if (swapFromId.value === null) {
    swapFromShifts.value = []
    swapFromScheduleId.value = 0
    return
  }
  void loadSwapFromShifts(swapFromId.value)
}

function handleSwapFromScheduleChange(): void {
  swapToId.value = null
  swapToShifts.value = []
  swapToScheduleId.value = 0
}

function handleSwapToChange(): void {
  if (swapToId.value === null) {
    swapToShifts.value = []
    swapToScheduleId.value = 0
    return
  }
  void loadSwapToShifts(swapToId.value)
}

function handleSwapToScheduleChange(): void {
  if (
    swapToScheduleId.value !== 0 &&
    swapFromScheduleId.value !== 0 &&
    selectedSwapToShift.value?.date === selectedSwapFromShift.value?.date
  ) {
    ElMessage.warning('串班的两个班次不能是同一天，请重新选择乙方的班次')
    swapToScheduleId.value = 0
  }
}

async function saveSwap(): Promise<void> {
  if (swapSaving.value) return
  if (swapFromId.value === null) {
    ElMessage.warning('请选择甲方（对调方一）')
    return
  }
  if (!selectedSwapFromShift.value) {
    ElMessage.warning('请选择甲方的班次')
    return
  }
  if (swapToId.value === null) {
    ElMessage.warning('请选择乙方（对调方二，同组成员）')
    return
  }
  if (!selectedSwapToShift.value) {
    ElMessage.warning('请选择乙方的班次（要与甲方对调的班日）')
    return
  }
  if (selectedSwapFromShift.value.date === selectedSwapToShift.value.date) {
    ElMessage.warning('串班的两个班次不能是同一天')
    return
  }
  if (
    selectedSwapFromShift.value.groupCode &&
    selectedSwapToShift.value.groupCode &&
    selectedSwapFromShift.value.groupCode !== selectedSwapToShift.value.groupCode
  ) {
    ElMessage.warning('串班双方必须属于同一组')
    return
  }

  const fromId = swapFromId.value
  const toId = swapToId.value
  const fromShift = selectedSwapFromShift.value
  const toShift = selectedSwapToShift.value

  swapSaving.value = true
  try {
    await createDutyAdjustment({
      changeType: 'SWAP',
      fromMemberId: fromId,
      dutyDate: fromShift.date,
      toMemberId: toId,
      toDutyDate: toShift.date,
      reason: swapReason.value.trim(),
    })
    ElMessage.success(
      `串班已生效：${memberName(fromId)}（${formatShortDate(fromShift.date)}） ↔ ${memberName(toId)}（${formatShortDate(toShift.date)}）`,
    )
    swapVisible.value = false
    await loadAll()
  } catch (error) {
    ElMessage.error(dutyError(error, '保存串班失败', '保存串班'))
  } finally {
    swapSaving.value = false
  }
}

/* ── 代班 ───────────────────────────────────────────────────── */

function resetSubstituteForm(): void {
  substituteFromId.value = null
  substituteFromShifts.value = []
  substituteScheduleId.value = 0
  substituteToId.value = null
  substituteReason.value = ''
}

function openSubstitute(): void {
  resetSubstituteForm()
  substituteVisible.value = true
}

async function loadSubstituteShifts(
  memberId: number,
  preferredDate = '',
  preferredGroup: DutyGroupCode | '' = '',
): Promise<void> {
  const ticket = ++substituteTicket
  substituteLoading.value = true
  substituteFromShifts.value = []
  substituteScheduleId.value = 0
  substituteToId.value = null
  try {
    const rows = await listDutyMemberFutureShifts(memberId)
    if (ticket !== substituteTicket) return
    substituteFromShifts.value = preferredGroup
      ? rows.filter((shift) => !shift.groupCode || shift.groupCode === preferredGroup)
      : rows
    const match = preferredDate
      ? substituteFromShifts.value.find(
          (shift) =>
            shift.date === preferredDate &&
            (!preferredGroup || !shift.groupCode || shift.groupCode === preferredGroup),
        )
      : null
    if (match) substituteScheduleId.value = match.scheduleId
  } catch (error) {
    if (ticket !== substituteTicket) return
    ElMessage.error(dutyError(error, '加载被代班人班次失败', '加载未来班次'))
  } finally {
    if (ticket === substituteTicket) substituteLoading.value = false
  }
}

function handleSubstituteFromChange(): void {
  substituteToId.value = null
  if (substituteFromId.value === null) {
    substituteFromShifts.value = []
    substituteScheduleId.value = 0
    return
  }
  void loadSubstituteShifts(substituteFromId.value)
}

function handleSubstituteScheduleChange(): void {
  substituteToId.value = null
}

async function saveSubstitute(): Promise<void> {
  if (substituteSaving.value) return
  if (substituteFromId.value === null) {
    ElMessage.warning('请选择被代班人（让出班次的人员）')
    return
  }
  if (!selectedSubstituteShift.value) {
    ElMessage.warning('请选择要代班的班次')
    return
  }
  if (substituteToId.value === null) {
    ElMessage.warning('请选择代班人（同组成员）')
    return
  }

  const fromId = substituteFromId.value
  const toId = substituteToId.value
  const fromShift = selectedSubstituteShift.value

  substituteSaving.value = true
  try {
    await createDutyAdjustment({
      changeType: 'SUBSTITUTE',
      fromMemberId: fromId,
      dutyDate: fromShift.date,
      toMemberId: toId,
      reason: substituteReason.value.trim(),
    })
    ElMessage.success(
      `代班已生效：${memberName(toId)} 代 ${memberName(fromId)}（${formatShortDate(fromShift.date)}）`,
    )
    substituteVisible.value = false
    await loadAll()
  } catch (error) {
    ElMessage.error(dutyError(error, '保存代班失败', '保存代班'))
  } finally {
    substituteSaving.value = false
  }
}

/* ── 改期 / 撤销 ─────────────────────────────────────────────── */

function openReschedule(item: DutyAdjustment): void {
  if (item.isCancelled) {
    ElMessage.warning('已撤销的记录不能改期')
    return
  }
  rescheduleTarget.value = item
  rescheduleDate.value =
    item.changeType === 'SWAP' ? (item.toDutyDate ?? item.dutyDate) : item.dutyDate
  rescheduleVisible.value = true
}

async function saveReschedule(): Promise<void> {
  const target = rescheduleTarget.value
  if (!target || rescheduleSaving.value) return
  if (!rescheduleDate.value) {
    ElMessage.warning('请选择新的班日')
    return
  }
  rescheduleSaving.value = true
  try {
    await rescheduleDutyAdjustment(target.id, rescheduleDate.value)
    ElMessage.success(
      `${target.changeType === 'SWAP' ? '串班' : '代班'}已改期`,
    )
    rescheduleVisible.value = false
    await loadAll()
  } catch (error) {
    ElMessage.error(dutyError(error, '改期失败', '改期班务调整'))
  } finally {
    rescheduleSaving.value = false
  }
}

async function revoke(item: DutyAdjustment): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定撤销 ${item.dutyDate ? formatDate(item.dutyDate) : ''} 的${item.changeType === 'SWAP' ? '串班' : '代班'}（${item.fromMemberName} → ${item.toMemberName}）吗？\n撤销后所在月值班表将自动重排恢复。`,
      `撤销${item.changeType === 'SWAP' ? '串班' : '代班'}`,
      { confirmButtonText: '撤销', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await revokeDutyAdjustment(item.id)
    ElMessage.success('记录已撤销，值班表已重排恢复')
    await loadAll()
  } catch (error) {
    ElMessage.error(dutyError(error, '撤销失败', '撤销班务调整'))
  }
}

/* ── 从值班表带参数进入 ─────────────────────────────────────── */

async function applyPrefill(): Promise<void> {
  if (prefillApplied) return
  prefillApplied = true

  const modeText = readQueryText(route.query.mode)
  const mode: AdjustMode | null =
    modeText === 'leave' || modeText === 'swap' || modeText === 'substitute'
      ? (modeText as AdjustMode)
      : modeText === 'Leave'
        ? 'leave'
        : modeText === 'Swap'
          ? 'swap'
          : modeText === 'Substitute'
            ? 'substitute'
            : null
  if (!mode) return

  const memberId = Number(readQueryText(route.query.memberId))
  const date = readQueryText(route.query.date)
  const group = parseGroup(route.query.group)
  prefillMode.value = mode

  if (!Number.isFinite(memberId) || memberId <= 0) {
    ElMessage.warning('未收到有效的成员信息，无法预填班务调整')
    return
  }

  if (mode === 'leave') {
    leaveMemberId.value = members.value.some((member) => member.id === memberId)
      ? memberId
      : null
    if (leaveMemberId.value === null) {
      ElMessage.warning('成员不存在或已删除，无法预填请假')
      return
    }
    if (date) {
      leaveStart.value = date
      leaveEnd.value = date
    }
    leaveVisible.value = true
    await loadLeaveAffected()
    return
  }

  if (mode === 'swap') {
    const member = members.value.find((item) => item.id === memberId)
    if (!member) {
      ElMessage.warning('成员不存在或已删除，无法预填串班')
      return
    }
    resetSwapForm()
    swapFromId.value = memberId
    swapVisible.value = true
    await loadSwapFromShifts(memberId, date, group)
    return
  }

  const member = members.value.find((item) => item.id === memberId)
  if (!member) {
    ElMessage.warning('成员不存在或已删除，无法预填代班')
    return
  }
  resetSubstituteForm()
  substituteFromId.value = memberId
  substituteVisible.value = true
  await loadSubstituteShifts(memberId, date, group)
}

watch(leaveStart, () => {
  if (leaveVisible.value) void loadLeaveAffected()
})

onMounted(async () => {
  await loadAll()
  await applyPrefill()
})
</script>

<template>
  <div class="nc-page duty-adjust-page">
    <NcPageHeader
      title="班务调整管理"
      description="请假 · 串班（相互对调）· 代班（单向顶班）· 撤销后值班表自动重排"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回值班表</el-button>
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
      <el-button v-if="!loadError.includes('无权限')" link type="primary" @click="loadAll()">
        重新加载
      </el-button>
    </el-alert>

    <NcCard padding="sm" class="toolbar-card">
      <div class="toolbar">
        <div class="toolbar-left">
          <el-switch v-model="showCancelled" />
          <span class="switch-label">显示已撤销记录</span>
          <el-button :icon="Refresh" text :loading="loading" @click="loadAll()">
            刷新
          </el-button>
        </div>
        <div class="toolbar-actions">
          <el-button :icon="Plus" @click="openLeave">+ 请假</el-button>
          <el-button :icon="Plus" @click="openSwap">+ 串班</el-button>
          <el-button type="primary" :icon="Plus" @click="openSubstitute">+ 代班</el-button>
        </div>
      </div>
    </NcCard>

    <div v-if="prefillMode" class="prefill-note">
      已从值班表带入预填信息：{{ prefillMode === 'leave' ? '请假' : prefillMode === 'swap' ? '串班' : '代班' }}
    </div>

    <!-- 请假 -->
    <NcCard title="请假管理" class="section-card">
      <template #actions>
        <span class="section-count">{{ leaveRows.length }} 条</span>
      </template>
      <p class="section-desc">
        请假区间内生成值班表时顺延跳过并消耗轮次；撤销请假后受影响月份自动重排。
      </p>
      <div v-loading="loading && leaves.length === 0" class="record-list">
        <article v-for="leave in leaveRows" :key="leave.id" class="record-row">
          <div class="record-main">
            <div class="record-title">
              <strong>{{ leave.memberName }}</strong>
              <span class="record-range">{{ formatDate(leave.startDate) }} ~ {{ formatDate(leave.endDate) }}</span>
              <span class="day-chip">{{ leave.days }} 天</span>
            </div>
            <p v-if="leave.reason" class="record-reason">{{ leave.reason }}</p>
          </div>
          <el-button size="small" type="danger" plain @click="removeLeave(leave)">撤销</el-button>
        </article>
        <el-empty
          v-if="!loading && leaveRows.length === 0"
          description="暂无请假记录"
          :image-size="64"
        />
      </div>
    </NcCard>

    <!-- 串班 -->
    <NcCard title="串班管理（两天互相对调）" class="section-card">
      <template #actions>
        <span class="section-count">{{ swapRows.length }} 条</span>
      </template>
      <p class="section-desc">
        串班=双方各让出一个班日并互相对调（限同组、两班不同日、限今天及以后）；撤销或改期后所在月自动重排恢复。
      </p>
      <div v-loading="loading && adjustments.length === 0" class="record-list">
        <article v-for="item in swapRows" :key="item.id" class="record-row">
          <div class="record-main">
            <div class="record-title">
              <span class="type-badge type-badge--swap">串班</span>
              <strong>{{ adjustmentDirection(item) }}</strong>
              <span
                class="status-badge"
                :class="`status-badge--${statusMeta(item).tone}`"
                :title="item.statusText"
              >
                {{ statusMeta(item).label }}
              </span>
            </div>
            <p class="record-meta">
              {{ formatDateTime(item.createdAt) }}
              <template v-if="item.groupCode"> · {{ groupDisplayName(item.groupCode) }}</template>
            </p>
            <p v-if="item.reason" class="record-reason">{{ item.reason }}</p>
          </div>
          <div v-if="!item.isCancelled" class="record-actions">
            <el-button size="small" @click="openReschedule(item)">改期</el-button>
            <el-button size="small" type="danger" plain @click="revoke(item)">撤销</el-button>
          </div>
        </article>
        <el-empty
          v-if="!loading && swapRows.length === 0"
          description="暂无串班记录"
          :image-size="64"
        />
      </div>
    </NcCard>

    <!-- 代班 -->
    <NcCard title="代班管理" class="section-card">
      <template #actions>
        <span class="section-count">{{ substituteRows.length }} 条</span>
      </template>
      <p class="section-desc">
        代班=某人代替另一人上班一次（限同组、限今天及以后）；撤销或改期后所在月自动重排恢复。
      </p>
      <div v-loading="loading && adjustments.length === 0" class="record-list">
        <article v-for="item in substituteRows" :key="item.id" class="record-row">
          <div class="record-main">
            <div class="record-title">
              <span class="type-badge type-badge--substitute">代班</span>
              <strong>{{ adjustmentDirection(item) }}</strong>
              <span
                class="status-badge"
                :class="`status-badge--${statusMeta(item).tone}`"
                :title="item.statusText"
              >
                {{ statusMeta(item).label }}
              </span>
            </div>
            <p class="record-meta">
              {{ formatDateTime(item.createdAt) }}
              <template v-if="item.groupCode"> · {{ groupDisplayName(item.groupCode) }}</template>
            </p>
            <p v-if="item.reason" class="record-reason">{{ item.reason }}</p>
          </div>
          <div v-if="!item.isCancelled" class="record-actions">
            <el-button size="small" @click="openReschedule(item)">改期</el-button>
            <el-button size="small" type="danger" plain @click="revoke(item)">撤销</el-button>
          </div>
        </article>
        <el-empty
          v-if="!loading && substituteRows.length === 0"
          description="暂无代班记录"
          :image-size="64"
        />
      </div>
    </NcCard>

    <!-- 新增请假 -->
    <el-dialog v-model="leaveVisible" title="新增请假" width="520px">
      <div class="dialog-form">
        <label class="field">
          <span>请假人员 <em>*</em></span>
          <el-select v-model="leaveMemberId" placeholder="请选择请假人员" filterable>
            <el-option
              v-for="member in sortedMembers"
              :key="member.id"
              :label="member.name"
              :value="member.id"
            />
          </el-select>
        </label>
        <div class="date-range">
          <label class="field">
            <span>开始 <em>*</em></span>
            <el-date-picker
              v-model="leaveStart"
              type="date"
              value-format="YYYY-MM-DD"
              placeholder="开始日期"
            />
          </label>
          <label class="field">
            <span>结束 <em>*</em></span>
            <el-date-picker
              v-model="leaveEnd"
              type="date"
              value-format="YYYY-MM-DD"
              placeholder="结束日期"
            />
          </label>
        </div>
        <label class="field">
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
          title="补登记：开始日期早于今天，将补登记并删除受影响月份的排班数据；已打印的旧表需重新打印。"
        />

        <div class="affected-box">
          <span class="affected-label">受影响月份</span>
          <span v-if="leaveAffectedLoading" class="affected-muted">读取中…</span>
          <template v-else-if="leaveAffected.length > 0">
            <span v-for="month in leaveAffected" :key="month" class="month-chip">
              {{ month }}
            </span>
          </template>
          <span v-else class="affected-muted">
            {{ leaveAffectedError || '当前没有已生成的值班表' }}
          </span>
        </div>

        <el-radio-group v-model="leaveRegenerate" class="regen-choice">
          <el-radio :value="true">删除并立即重新生成</el-radio>
          <el-radio :value="false">仅删除（稍后手动生成）</el-radio>
        </el-radio-group>

        <p class="dialog-hint">
          已登记的串班/代班将在重新生成时按记录重放（不匹配或命中请假的会跳过）。
        </p>
      </div>

      <template #footer>
        <el-button @click="leaveVisible = false">取消</el-button>
        <el-button type="primary" :loading="leaveSaving" @click="saveLeave">登记</el-button>
      </template>
    </el-dialog>

    <!-- 新增串班 -->
    <el-dialog v-model="swapVisible" title="新增串班（两天互相对调）" width="660px">
      <div class="dialog-form">
        <label class="field">
          <span>甲方 <em>*</em></span>
          <el-select
            v-model="swapFromId"
            placeholder="请选择甲方（对调方一）"
            filterable
            @change="handleSwapFromChange"
          >
            <el-option
              v-for="member in sortedMembers"
              :key="member.id"
              :label="member.name"
              :value="member.id"
            />
          </el-select>
        </label>

        <div class="shift-section">
          <span class="shift-label">甲方的班次（选择要换出的 X 日）</span>
          <div v-loading="swapFromLoading" class="shift-picker">
            <el-radio-group
              v-if="swapFromShifts.length > 0"
              v-model="swapFromScheduleId"
              class="shift-options"
              @change="handleSwapFromScheduleChange"
            >
              <el-radio
                v-for="shift in swapFromShifts"
                :key="shift.scheduleId"
                :value="shift.scheduleId"
                border
                class="shift-option"
              >
                {{ shiftLabel(shift) }}
              </el-radio>
            </el-radio-group>
            <p v-else-if="!swapFromLoading" class="empty-hint">
              请先选择甲方，或该成员今天及以后没有班次
            </p>
          </div>
        </div>

        <label class="field">
          <span>乙方 <em>*</em></span>
          <el-select
            v-model="swapToId"
            placeholder="请选择乙方（同组成员）"
            filterable
            :disabled="swapFromId === null"
            @change="handleSwapToChange"
          >
            <el-option
              v-for="member in swapTargetOptions"
              :key="member.id"
              :label="member.name"
              :value="member.id"
            />
          </el-select>
          <small class="field-hint">同组人员（不含甲方本人）；选定甲方班次后按该班次所属组收窄。</small>
        </label>

        <div class="shift-section">
          <span class="shift-label">乙方的班次（选择要换出的 Y 日，与甲方班次同组）</span>
          <div v-loading="swapToLoading" class="shift-picker">
            <el-radio-group
              v-if="swapToShifts.length > 0"
              v-model="swapToScheduleId"
              class="shift-options"
              @change="handleSwapToScheduleChange"
            >
              <el-radio
                v-for="shift in swapToShifts"
                :key="shift.scheduleId"
                :value="shift.scheduleId"
                border
                class="shift-option"
              >
                {{ shiftLabel(shift) }}
              </el-radio>
            </el-radio-group>
            <p v-else-if="!swapToLoading" class="empty-hint">
              请先选择乙方，或该成员今天及以后没有班次
            </p>
          </div>
        </div>

        <label class="field">
          <span>事由</span>
          <el-input v-model="swapReason" maxlength="100" placeholder="选填" />
        </label>

        <p class="dialog-hint">
          串班为相互对调：X 日乙方上、Y 日甲方上（同组、两日不可相同、对调后双方无双班）；值班表即时刷新，撤销后自动恢复。
        </p>
      </div>

      <template #footer>
        <el-button @click="swapVisible = false">取消</el-button>
        <el-button type="primary" :loading="swapSaving" @click="saveSwap">保存</el-button>
      </template>
    </el-dialog>

    <!-- 新增代班 -->
    <el-dialog v-model="substituteVisible" title="新增代班" width="620px">
      <div class="dialog-form">
        <label class="field">
          <span>被代班人 <em>*</em></span>
          <el-select
            v-model="substituteFromId"
            placeholder="请选择被代班人（让出班次的人员）"
            filterable
            @change="handleSubstituteFromChange"
          >
            <el-option
              v-for="member in sortedMembers"
              :key="member.id"
              :label="member.name"
              :value="member.id"
            />
          </el-select>
        </label>

        <div class="shift-section">
          <span class="shift-label">选择要代班的班次（限今天及以后）</span>
          <div v-loading="substituteLoading" class="shift-picker">
            <el-radio-group
              v-if="substituteFromShifts.length > 0"
              v-model="substituteScheduleId"
              class="shift-options"
              @change="handleSubstituteScheduleChange"
            >
              <el-radio
                v-for="shift in substituteFromShifts"
                :key="shift.scheduleId"
                :value="shift.scheduleId"
                border
                class="shift-option"
              >
                {{ shiftLabel(shift) }}
              </el-radio>
            </el-radio-group>
            <p v-else-if="!substituteLoading" class="empty-hint">
              请先选择被代班人，或该成员今天及以后没有班次
            </p>
          </div>
        </div>

        <label class="field">
          <span>代班人 <em>*</em></span>
          <el-select
            v-model="substituteToId"
            placeholder="请选择代班人（同组成员）"
            filterable
            :disabled="substituteFromId === null"
          >
            <el-option
              v-for="member in substituteTargetOptions"
              :key="member.id"
              :label="member.name"
              :value="member.id"
            />
          </el-select>
          <small class="field-hint">同组人员（不含被代班人本人）；选定班次后按该班次所属组收窄。</small>
        </label>

        <label class="field">
          <span>事由</span>
          <el-input v-model="substituteReason" maxlength="100" placeholder="选填" />
        </label>

        <p class="dialog-hint">
          代班为单人顶班一次：代班人代替被代班人上班；值班表即时刷新，撤销后自动恢复。
        </p>
      </div>

      <template #footer>
        <el-button @click="substituteVisible = false">取消</el-button>
        <el-button type="primary" :loading="substituteSaving" @click="saveSubstitute">
          保存
        </el-button>
      </template>
    </el-dialog>

    <!-- 改期 -->
    <el-dialog v-model="rescheduleVisible" title="改期" width="460px">
      <div class="dialog-form">
        <p v-if="rescheduleTarget" class="reschedule-title">
          {{ rescheduleTarget.changeType === 'SWAP' ? '串班' : '代班' }}：{{ adjustmentDirection(rescheduleTarget) }}
        </p>
        <label class="field">
          <span>
            {{ rescheduleTarget?.changeType === 'SWAP' ? '乙方的新班日' : '被代的新班日' }}
          </span>
          <el-date-picker
            v-model="rescheduleDate"
            type="date"
            value-format="YYYY-MM-DD"
            placeholder="选择新日期"
          />
        </label>
        <p class="dialog-hint">
          改期后自动重排新旧日期所在月：新日期由记录中的顶班人顶上，旧日期恢复轮转原值；一条记录始终，不会产生重复。
        </p>
      </div>
      <template #footer>
        <el-button @click="rescheduleVisible = false">取消</el-button>
        <el-button type="primary" :loading="rescheduleSaving" @click="saveReschedule">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.duty-adjust-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.toolbar-card,
.section-card {
  overflow: hidden;
}

.load-alert {
  margin: 0;
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

.switch-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  cursor: pointer;
}

.prefill-note {
  padding: 7px 12px;
  border-left: 3px solid var(--nc-violet);
  background-color: var(--nc-violet-soft);
  color: var(--nc-violet);
  font-size: var(--nc-text-sm);
}

.section-count {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.section-desc {
  margin: -2px 0 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.75;
}

.record-list {
  min-height: 60px;
}

.record-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.record-row + .record-row {
  margin-top: 7px;
}

.record-main {
  min-width: 0;
}

.record-title {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 7px;
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
}

.record-title strong {
  font-weight: 600;
}

.record-range {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.record-meta {
  margin-top: 2px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.record-reason {
  margin-top: 3px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.record-actions {
  display: flex;
  flex: none;
  gap: 6px;
}

.record-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.day-chip,
.month-chip {
  display: inline-block;
  padding: 0 6px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  line-height: 1.8;
  white-space: nowrap;
}

.month-chip {
  margin: 0 4px 4px 0;
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
}

.type-badge,
.status-badge {
  display: inline-block;
  padding: 0 7px;
  border-radius: var(--nc-radius-xs);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  line-height: 1.8;
  white-space: nowrap;
}

.type-badge--swap {
  background-color: #e0f2fe;
  color: #0369a1;
}

.type-badge--substitute {
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.status-badge--pine {
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
}

.status-badge--amber {
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.status-badge--seal {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.status-badge--muted {
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
}

/* ── 弹窗表单 ───────────────────────────────────────────────── */
.dialog-form {
  display: flex;
  flex-direction: column;
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

.field em {
  color: var(--nc-seal);
  font-style: normal;
}

.field-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.date-range {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.affected-box {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 5px;
  padding: 8px 10px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.affected-label {
  margin-right: 4px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.affected-muted {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.regen-choice {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.dialog-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.75;
}

.reschedule-title {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.shift-section {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.shift-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.shift-picker {
  min-height: 42px;
}

.shift-options {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 5px;
  max-height: 180px;
  padding: 2px;
  overflow-y: auto;
}

.shift-option {
  display: flex;
  width: 100%;
  height: auto;
  margin: 0 !important;
  padding: 6px 10px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
}

.shift-option :deep(.el-radio__label) {
  font-size: var(--nc-text-sm);
}

.empty-hint {
  padding: 8px 2px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

@media (max-width: 767px) {
  .toolbar {
    align-items: stretch;
    flex-direction: column;
  }

  .toolbar-left,
  .toolbar-actions {
    width: 100%;
  }

  .toolbar-actions {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .toolbar-actions :deep(.el-button) {
    margin-left: 0;
  }

  .record-row {
    flex-direction: column;
  }

  .record-actions {
    justify-content: flex-end;
    width: 100%;
  }

  .date-range {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
