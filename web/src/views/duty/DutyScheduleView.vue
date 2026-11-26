<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Calendar,
  Files,
  Printer,
  Refresh,
  Setting,
  User,
  UserFilled,
} from '@element-plus/icons-vue'
import {
  clearDutyMonth,
  exportDutyGovRange,
  exportDutyMonthPdf,
  fillDutyMonths,
  generateDutyMonth,
  getDutyMonth,
  listDutyAdjustments,
  listDutyMembers,
  printDutyMonth,
  rebuildDownstreamMonths,
  saveDutySettings,
} from '@/api/duty'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  DutyAdjustment,
  DutyDayRow,
  DutyGenerationSummary,
  DutyGroupCode,
  DutyMember,
  DutyMonthSchedule,
  DutyScheduleCell,
} from '@/types/duty'
import {
  DATE_TYPE_META,
  DUTY_GROUP_ORDER,
  GROUP_META,
  cellOf,
  dutyError,
  formatMonthDay,
  formatShortDate,
  monthCount,
  monthKey,
  monthTitle,
  parseMonthKey,
  saveBlob,
  shiftMonth,
} from './display'

type ShiftMode = 'leave' | 'swap' | 'substitute'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const now = new Date()
const year = ref(now.getFullYear())
const month = ref(now.getMonth() + 1)

function emptyMonth(y: number, m: number): DutyMonthSchedule {
  return {
    year: y,
    month: m,
    generated: false,
    shiftCount: 0,
    days: [],
    settings: { restdayLeaderSamePair: false },
  }
}

const schedule = ref<DutyMonthSchedule>(emptyMonth(year.value, month.value))
const members = ref<DutyMember[]>([])
const adjustments = ref<DutyAdjustment[]>([])
const loading = ref(false)
const generating = ref(false)
const clearing = ref(false)
const printing = ref(false)
const pdfExporting = ref(false)
const settingsSaving = ref(false)
const loadError = ref('')

const exportVisible = ref(false)
const exportLoading = ref(false)
const exportStartYear = ref(now.getFullYear())
const exportStartMonth = ref(now.getMonth() + 1)
const exportEndYear = ref(now.getFullYear())
const exportEndMonth = ref(now.getMonth() + 1)

let loadTicket = 0

const monthText = computed(() => monthTitle(year.value, month.value))
const summaryText = computed(() =>
  schedule.value.generated
    ? `已生成 · ${schedule.value.shiftCount} 个班次`
    : '未生成',
)
const days = computed(() => schedule.value.days)
const hasDays = computed(() => days.value.length > 0)
const isPermissionDenied = computed(() => loadError.value.includes('无权限'))

const exportYearOptions = computed(() =>
  Array.from({ length: 8 }, (_, index) => now.getFullYear() - 2 + index),
)
const exportMonthOptions = Array.from({ length: 12 }, (_, index) => index + 1)
const exportTotalMonths = computed(() =>
  monthCount(
    exportStartYear.value,
    exportStartMonth.value,
    exportEndYear.value,
    exportEndMonth.value,
  ),
)
const exportRangeError = computed(() => {
  if (exportTotalMonths.value <= 0) return '结束月份不能早于起始月份'
  if (exportTotalMonths.value > 36) return '一次最多导出 36 个月'
  return ''
})

const memberIdByName = computed(() => {
  const map = new Map<string, number>()
  for (const member of members.value) map.set(member.name, member.id)
  return map
})

const memberPhoneByName = computed(() => {
  const map = new Map<string, string>()
  for (const member of members.value) {
    if (member.phone) map.set(member.name, member.phone)
  }
  return map
})

const adjustedCellKeys = computed(() => {
  const keys = new Set<string>()
  for (const item of adjustments.value) {
    if (item.isCancelled) continue
    if (item.changeType === 'SWAP') {
      if (item.dutyDate) {
        keys.add(`${item.dutyDate}|${item.groupCode}|${item.toMemberName}`)
      }
      if (item.toDutyDate) {
        keys.add(`${item.toDutyDate}|${item.groupCode}|${item.fromMemberName}`)
      }
    } else if (item.dutyDate) {
      keys.add(`${item.dutyDate}|${item.groupCode}|${item.toMemberName}`)
    }
  }
  return keys
})

const adjustedDates = computed(() => {
  const dates = new Set<string>()
  for (const item of adjustments.value) {
    if (item.isCancelled) continue
    if (item.dutyDate) dates.add(item.dutyDate)
    if (item.toDutyDate) dates.add(item.toDutyDate)
  }
  return dates
})

function effectiveMemberId(cell: DutyScheduleCell): number | null {
  if (cell.memberId !== null) return cell.memberId
  return memberIdByName.value.get(cell.name) ?? null
}

/** 月表契约只返回姓名；电话从成员列表按姓名补齐。 */
function cellPhone(cell: DutyScheduleCell): string {
  if (cell.phone) return cell.phone
  if (!cell.name) return ''
  return memberPhoneByName.value.get(cell.name) ?? ''
}

/** 月表契约只返回当天是否有调整；有调整记录时按班次精确到单元格。 */
function cellIsAdjusted(
  day: DutyDayRow,
  group: DutyGroupCode,
  cell: DutyScheduleCell,
): boolean {
  if (!cell.name) return false
  if (adjustedDates.value.has(day.date)) {
    return (
      adjustedCellKeys.value.has(`${day.date}|${group}|${cell.name}`) ||
      adjustedCellKeys.value.has(`${day.date}||${cell.name}`)
    )
  }
  return cell.isAdjusted
}

function cellText(cell: DutyScheduleCell): string {
  if (cell.name) return cell.name
  return cell.isRequired ? '—' : '·'
}

function cellClass(group: DutyGroupCode, cell: DutyScheduleCell): string[] {
  return [
    `duty-cell--${group.toLowerCase()}`,
    {
      'is-empty': !cell.name && cell.isRequired,
      'is-off': !cell.isRequired,
      'is-adjusted': cell.name && cell.isAdjusted,
    },
  ].filter(Boolean) as string[]
}

function groupCell(day: DutyDayRow, group: DutyGroupCode): DutyScheduleCell {
  return cellOf(day, group)
}

async function load(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''
  try {
    const [monthResult, memberResult, adjustResult] = await Promise.allSettled([
      getDutyMonth(year.value, month.value),
      listDutyMembers(),
      listDutyAdjustments(true),
    ])
    if (ticket !== loadTicket) return

    if (monthResult.status === 'rejected') throw monthResult.reason
    schedule.value = monthResult.value
    members.value = memberResult.status === 'fulfilled' ? memberResult.value : []
    adjustments.value = adjustResult.status === 'fulfilled' ? adjustResult.value : []
  } catch (error) {
    if (ticket !== loadTicket) return
    schedule.value = emptyMonth(year.value, month.value)
    members.value = []
    adjustments.value = []
    loadError.value = dutyError(error, '值班表加载失败', '查看值班表', 'DUTY_VIEW')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

async function changeMonth(offset: number): Promise<void> {
  if (loading.value || generating.value) return
  const next = shiftMonth(year.value, month.value, offset)
  year.value = next.year
  month.value = next.month
  await load()
}

function goBack(): void {
  void router.push('/')
}

function goMembers(): void {
  void router.push('/duty/members')
}

function goAdjustments(): void {
  void router.push('/duty/adjustments')
}

function goHolidays(): void {
  void router.push('/duty/holidays')
}

function openShiftAction(
  day: DutyDayRow,
  group: DutyGroupCode,
  command: string | number | object,
): void {
  const cell = groupCell(day, group)
  if (!cell.name) return

  const memberId = effectiveMemberId(cell)
  if (memberId === null) {
    ElMessage.warning('未找到该成员的档案，无法发起班务调整')
    return
  }

  const mode = String(command) as ShiftMode
  if (!['leave', 'swap', 'substitute'].includes(mode)) return
  void router.push({
    path: '/duty/adjustments',
    query: {
      mode,
      memberId: String(memberId),
      date: day.date,
      group,
    },
  })
}

async function handleSamePairChange(value: string | number | boolean): Promise<void> {
  const enabled = Boolean(value)
  const previous = schedule.value.settings.restdayLeaderSamePair
  if (settingsSaving.value || enabled === previous) return

  schedule.value.settings.restdayLeaderSamePair = enabled
  settingsSaving.value = true
  try {
    await saveDutySettings({ restdayLeaderSamePair: enabled })
    ElMessage.success(
      enabled
        ? '已开启：周六/周日由同一位带班领导值班（下次生成生效）'
        : '已关闭：周六/周日各自独立轮转（下次生成生效）',
    )
  } catch (error) {
    schedule.value.settings.restdayLeaderSamePair = previous
    ElMessage.error(dutyError(error, '保存开关失败', '修改值班设置'))
  } finally {
    settingsSaving.value = false
  }
}

function generationMessage(summary: DutyGenerationSummary): string {
  const lines: string[] = []
  if (summary.workdayCount > 0 || summary.restdayCount > 0 || summary.holidayCount > 0) {
    lines.push(
      `工作日 ${summary.workdayCount} 天 / 休息日 ${summary.restdayCount} 天 / 法定节假日 ${summary.holidayCount} 天`,
    )
  }
  lines.push(`共生成 ${summary.scheduleCount} 个班次`)
  if (summary.anchorDay > 1) {
    lines.push(`轮转锚点生效：本月自 ${summary.anchorDay} 日起生成`)
  }
  if (summary.conflictSkippedCount > 0) {
    lines.push(`同人冲突顺延 ${summary.conflictSkippedCount} 次`)
  }
  if (summary.unavailableSkippedCount > 0) {
    lines.push(`请假/离职边界顺延 ${summary.unavailableSkippedCount} 次`)
  }
  if (
    summary.replayedChangeCount > 0 ||
    summary.pendingChangeCount > 0 ||
    summary.skippedChangeCount > 0
  ) {
    let line = `串班/代班：生效 ${summary.replayedChangeCount} 条`
    if (summary.pendingChangeCount > 0) {
      line += `，待生效 ${summary.pendingChangeCount} 条`
      if (summary.pendingChangeMonths.length > 0) {
        line += `（缺 ${summary.pendingChangeMonths.join('、')}）`
      }
    }
    if (summary.skippedChangeCount > 0) {
      line += `，轮转变化跳过 ${summary.skippedChangeCount} 条（需处理）`
    }
    lines.push(line)
  }
  if (summary.warnings.length > 0) lines.push('', ...summary.warnings)
  return lines.join('\n')
}

/** 从待生效调整反查缺失月份：生成后 service 会实时计算 PendingUnbuilt。 */
async function findPendingMissingMonths(): Promise<string[]> {
  try {
    const adjustments = await listDutyAdjustments(true)
    const pendingDates = new Set<string>()
    for (const item of adjustments) {
      if (item.isCancelled || item.replayStatus !== 'PendingUnbuilt') continue
      if (item.dutyDate) {
        const parsed = parseMonthKey(item.dutyDate.slice(0, 7))
        if (parsed) pendingDates.add(monthKey(parsed.year, parsed.month))
      }
      if (item.toDutyDate) {
        const parsed = parseMonthKey(item.toDutyDate.slice(0, 7))
        if (parsed) pendingDates.add(monthKey(parsed.year, parsed.month))
      }
    }

    const missing: string[] = []
    for (const key of pendingDates) {
      const parsed = parseMonthKey(key)
      if (!parsed) continue
      const monthData = await getDutyMonth(parsed.year, parsed.month)
      if (!monthData.generated) missing.push(key)
    }
    return missing.sort()
  } catch {
    return []
  }
}

async function confirmMessage(message: string, title: string): Promise<void> {
  await ElMessageBox.alert(
    h('div', { style: 'white-space: pre-line; line-height: 1.8' }, message),
    title,
    { confirmButtonText: '知道了' },
  )
}

async function handleGenerate(): Promise<void> {
  if (generating.value) return

  let force = false
  if (schedule.value.generated) {
    try {
      await ElMessageBox.confirm(
        `${monthText.value}值班表已生成。\n重新生成将删除该月班次，并按本月之前最近一次真实班次接续推演（结果可复现，不会随机重摇），确定继续吗？`,
        '重新生成',
        {
          confirmButtonText: '重新生成',
          cancelButtonText: '取消',
          type: 'warning',
        },
      )
      force = true
    } catch {
      return
    }
  }

  generating.value = true
  try {
    const summary = await generateDutyMonth(year.value, month.value, force)
    await confirmMessage(generationMessage(summary), '生成完成')

    if (
      (summary.downstreamMonths.length > 0 || summary.hasDownstream) &&
      summary.rotationChanged !== false
    ) {
      const preview =
        summary.downstreamMonths.length > 0
          ? summary.downstreamMonths.slice(0, 6).join('、') +
            (summary.downstreamMonths.length > 6
              ? ` 等 ${summary.downstreamMonths.length} 个月`
              : '')
          : ''
      let reflow = false
      try {
        await ElMessageBox.confirm(
          `${preview ? `后续已生成月份（${preview}）` : '后续已生成月份'}是按旧轮转生成的，与本月重生成后的结果不衔接。\n\n是否按时间顺序一并重排？重排后串班/代班仍会自动重放。`,
          '后续月份需重排',
          { confirmButtonText: '一并重排', cancelButtonText: '暂不', type: 'warning' },
        )
        reflow = true
      } catch {
        reflow = false
      }
      if (reflow) {
        try {
          const count = await rebuildDownstreamMonths(
            year.value,
            month.value,
            summary.downstreamMonths,
          )
          ElMessage.success(`已按时间顺序重排 ${count} 个后续月份`)
        } catch (error) {
          ElMessage.error(dutyError(error, '重排后续月份失败', '重排后续月份'))
        }
      }
    }

    if (
      summary.pendingChangeMonths.length > 0 ||
      summary.hasPendingCrossMonth
    ) {
      let fill = false
      const pendingText =
        summary.pendingChangeMonths.length > 0
          ? `有 ${summary.pendingChangeCount} 条串班/代班因对方月份未生成而待生效（缺：${summary.pendingChangeMonths.join('、')}）。`
          : '存在跨月串班/代班因对方月份未生成而待生效。'
      try {
        await ElMessageBox.confirm(
          `${pendingText}\n\n是否立即补齐生成？生成后自动生效。`,
          '串班/代班待生效',
          { confirmButtonText: '补齐生成', cancelButtonText: '稍后', type: 'warning' },
        )
        fill = true
      } catch {
        fill = false
      }
      if (fill) {
        try {
          const detected = await findPendingMissingMonths()
          const months =
            detected.length > 0 ? detected : summary.pendingChangeMonths
          let count = 0
          if (months.length > 0) {
            count = await fillDutyMonths(months)
          }
          await confirmMessage(
            months.length > 0
              ? `已补齐 ${count} 个月份（${months.join('、')}），串班/代班已自动重放生效。`
              : '未发现可补齐的缺失月份；请到「班务调整」查看待生效记录的缺失月份。',
            '补齐完成',
          )
        } catch (error) {
          ElMessage.error(dutyError(error, '补齐生成失败', '补齐生成'))
        }
      }
    }

    await load()
  } catch (error) {
    ElMessage.error(dutyError(error, '生成值班表失败', '生成值班表'))
  } finally {
    generating.value = false
  }
}

async function handleClear(): Promise<void> {
  if (clearing.value || !schedule.value.generated) return
  try {
    await ElMessageBox.confirm(
      `确定要删除 ${monthText.value} 的全部班次吗？\n（清空后可重新生成：起点取本月之前最近一次真实班次）`,
      '清空值班表',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  clearing.value = true
  try {
    await clearDutyMonth(year.value, month.value)
    ElMessage.success('该月值班表已清空')
    await load()
  } catch (error) {
    ElMessage.error(dutyError(error, '清空值班表失败', '清空值班表'))
  } finally {
    clearing.value = false
  }
}

function openExportPanel(): void {
  exportStartYear.value = now.getFullYear()
  exportStartMonth.value = now.getMonth() + 1
  exportEndYear.value = now.getFullYear()
  exportEndMonth.value = now.getMonth() + 1
  exportVisible.value = true
}

function exportError(error: unknown, fallback: string, action: string): string {
  const message = dutyError(error, fallback, action)
  if (
    message.includes('数据校验未通过') ||
    message.includes('请求参数有误') ||
    message.includes('请求失败')
  ) {
    return `${fallback}：请确认已绑定打印机，并已在文书模板管理中上传「模板_政务值班表」。`
  }
  return message
}

function exportSuccessMessage(
  result: Awaited<ReturnType<typeof exportDutyGovRange>>,
  action: string,
): string {
  if (result.type === 'file') {
    saveBlob(result.blob, result.fileName)
    return `已导出：${result.fileName}`
  }
  const job = result.job
  return `${action}任务已提交：${job.jobNo || `#${job.id}`}（${job.status || '待处理'}，${job.fileCount || 1} 个文件）`
}

async function submitExport(): Promise<void> {
  if (exportRangeError.value) {
    ElMessage.warning(exportRangeError.value)
    return
  }
  exportLoading.value = true
  try {
    const result = await exportDutyGovRange({
      startYear: exportStartYear.value,
      startMonth: exportStartMonth.value,
      endYear: exportEndYear.value,
      endMonth: exportEndMonth.value,
    })
    ElMessage.success(exportSuccessMessage(result, '导出'))
    exportVisible.value = false
  } catch (error) {
    ElMessage.error(exportError(error, '导出政务值班表失败', '导出政务值班表'))
  } finally {
    exportLoading.value = false
  }
}

async function handleExportPdf(): Promise<void> {
  if (pdfExporting.value) return
  pdfExporting.value = true
  try {
    const result = await exportDutyMonthPdf(year.value, month.value)
    ElMessage.success(exportSuccessMessage(result, 'PDF 导出'))
  } catch (error) {
    ElMessage.error(exportError(error, '导出 PDF 失败', '导出 PDF'))
  } finally {
    pdfExporting.value = false
  }
}

async function handlePrint(): Promise<void> {
  if (printing.value) return
  printing.value = true
  try {
    const message = await printDutyMonth(year.value, month.value)
    ElMessage.success(message || '打印任务已提交')
  } catch (error) {
    ElMessage.error(dutyError(error, '打印值班表失败', '打印值班表'))
  } finally {
    printing.value = false
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page duty-page">
    <NcPageHeader
      title="值班管理"
      description="领导组 · 中层管理组 · 男生组 · 女生组 ｜ 工作日 / 休息日 / 法定节假日独立轮转"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回工作台</el-button>
      </template>
    </NcPageHeader>

    <NcCard padding="sm" class="month-card">
      <div class="month-bar">
        <div class="month-nav">
          <el-button
            class="month-arrow"
            :disabled="loading || generating"
            aria-label="上一个月"
            @click="changeMonth(-1)"
          >
            <el-icon><ArrowLeft /></el-icon>
          </el-button>
          <div class="month-title-wrap">
            <h2 class="month-title">{{ monthText }}</h2>
            <span class="month-status" :class="{ 'is-generated': schedule.generated }">
              {{ summaryText }}
            </span>
          </div>
          <el-button
            class="month-arrow month-arrow--next"
            :disabled="loading || generating"
            aria-label="下一个月"
            @click="changeMonth(1)"
          >
            <el-icon><ArrowLeft /></el-icon>
          </el-button>
          <el-button :icon="Refresh" text :loading="loading" @click="load()">刷新</el-button>
        </div>

        <div class="month-actions">
          <el-button type="primary" :loading="generating" @click="handleGenerate">
            生成值班表
          </el-button>
          <el-button
            :disabled="!schedule.generated || clearing"
            :loading="clearing"
            @click="handleClear"
          >
            清空
          </el-button>
        </div>
      </div>
    </NcCard>

    <el-alert
      v-if="loadError"
      class="load-alert"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <template v-if="!isPermissionDenied">
        <el-button link type="primary" @click="load()">重新加载</el-button>
      </template>
      <template v-else>
        <span>请联系管理员分配 DUTY_VIEW 权限后重新进入。</span>
      </template>
    </el-alert>

    <NcCard padding="sm" class="toolbar-card">
      <div class="toolbar">
        <div class="same-pair">
          <el-switch
            :model-value="schedule.settings.restdayLeaderSamePair"
            :loading="settingsSaving"
            @change="handleSamePairChange"
          />
          <span>周六周日同一带班领导</span>
        </div>

        <div class="toolbar-actions">
          <el-button :icon="Files" @click="openExportPanel">导出政务值班表</el-button>
          <el-button :loading="pdfExporting" @click="handleExportPdf">导出 PDF</el-button>
          <el-button :icon="Printer" :loading="printing" @click="handlePrint">打印</el-button>
          <el-button :icon="Setting" @click="goAdjustments">班务调整</el-button>
          <el-button :icon="User" @click="goMembers">成员管理</el-button>
          <el-button :icon="Calendar" @click="goHolidays">节假日维护</el-button>
        </div>
      </div>
    </NcCard>

    <NcCard padding="none" class="schedule-card">
      <div v-loading="loading" class="schedule-body">
        <div v-if="isNarrow && hasDays" class="day-cards">
          <article v-for="day in days" :key="day.date" class="day-card">
            <header class="day-card__head">
              <div class="day-card__date">
                <strong>{{ formatMonthDay(day.date) }}</strong>
                <span>{{ day.week }}</span>
              </div>
              <span
                class="type-pill"
                :class="`type-pill--${DATE_TYPE_META[day.dateType].tone}`"
              >
                {{ DATE_TYPE_META[day.dateType].label }}
              </span>
            </header>

            <div class="day-card__rows">
              <div v-for="group in DUTY_GROUP_ORDER" :key="group" class="day-card__row">
                <span class="day-card__label" :class="`group-label--${GROUP_META[group].tone}`">
                  {{ GROUP_META[group].shortLabel }}
                </span>
                <el-dropdown
                  trigger="click"
                  :disabled="!groupCell(day, group).name"
                  @command="(command: string) => openShiftAction(day, group, command)"
                >
                  <button
                    type="button"
                    class="cell-button"
                    :class="cellClass(group, groupCell(day, group))"
                    :title="
                      groupCell(day, group).name
                        ? '点击设置请假 / 串班 / 代班'
                        : '该组当天不出班或暂未安排'
                    "
                  >
                    <span class="cell-name">{{ cellText(groupCell(day, group)) }}</span>
                    <span
                      v-if="cellIsAdjusted(day, group, groupCell(day, group))"
                      class="adjust-badge"
                    >
                      调
                    </span>
                    <span
                      v-if="cellPhone(groupCell(day, group))"
                      class="cell-phone"
                    >
                      {{ cellPhone(groupCell(day, group)) }}
                    </span>
                  </button>
                  <template #dropdown>
                    <el-dropdown-menu>
                      <el-dropdown-item command="leave">请假</el-dropdown-item>
                      <el-dropdown-item command="swap">串班</el-dropdown-item>
                      <el-dropdown-item command="substitute">代班</el-dropdown-item>
                    </el-dropdown-menu>
                  </template>
                </el-dropdown>
              </div>
            </div>
          </article>
        </div>

        <div v-else-if="hasDays" class="table-scroll">
          <table class="duty-table">
            <colgroup>
              <col class="col-date" />
              <col class="col-week" />
              <col class="col-type" />
              <col />
              <col />
              <col />
              <col />
            </colgroup>
            <thead>
              <tr>
                <th>日期</th>
                <th>星期</th>
                <th>日期类型</th>
                <th>领导组</th>
                <th>中层管理组</th>
                <th>男生组</th>
                <th>女生组</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="day in days" :key="day.date">
                <td class="date-cell">
                  <strong>{{ formatMonthDay(day.date) }}</strong>
                  <small>{{ formatShortDate(day.date) }}</small>
                </td>
                <td class="week-cell">{{ day.week }}</td>
                <td>
                  <span
                    class="type-pill"
                    :class="`type-pill--${DATE_TYPE_META[day.dateType].tone}`"
                  >
                    {{ DATE_TYPE_META[day.dateType].label }}
                  </span>
                </td>
                <td v-for="group in DUTY_GROUP_ORDER" :key="group">
                  <el-dropdown
                    trigger="click"
                    :disabled="!groupCell(day, group).name"
                    @command="(command) => openShiftAction(day, group, command)"
                  >
                    <button
                      type="button"
                      class="cell-button duty-cell"
                      :class="cellClass(group, groupCell(day, group))"
                      :title="
                        groupCell(day, group).name
                          ? '点击设置请假 / 串班 / 代班'
                          : '该组当天不出班或暂未安排'
                      "
                    >
                      <span class="cell-name">{{ cellText(groupCell(day, group)) }}</span>
                      <span
                        v-if="cellIsAdjusted(day, group, groupCell(day, group))"
                        class="adjust-badge"
                      >
                        调
                      </span>
                      <span
                        v-if="cellPhone(groupCell(day, group))"
                        class="cell-phone"
                      >
                        {{ cellPhone(groupCell(day, group)) }}
                      </span>
                    </button>
                    <template #dropdown>
                      <el-dropdown-menu>
                        <el-dropdown-item command="leave">请假</el-dropdown-item>
                        <el-dropdown-item command="swap">串班</el-dropdown-item>
                        <el-dropdown-item command="substitute">代班</el-dropdown-item>
                      </el-dropdown-menu>
                    </template>
                  </el-dropdown>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div v-else class="empty-wrap">
          <el-empty
            :description="
              schedule.generated ? '本月值班表暂无排班数据' : '本月尚未生成值班表'
            "
          >
            <el-button v-if="!schedule.generated" type="primary" @click="handleGenerate">
              生成值班表
            </el-button>
          </el-empty>
        </div>
      </div>
    </NcCard>

    <NcCard title="轮转规则说明" class="rules-card">
      <ul class="rules-list">
        <li>工作日/休息日：领导组 + 男生组 + 女生组各 1 人；法定节假日：另加中层管理组 1 人</li>
        <li>每组在每种日期类型下独立轮转（领导组最多 3 条轮转线，中层仅节假日轮转）</li>
        <li>
          同一人同一天只值一个班：节假日若中层轮到的人同时也是男生/女生组当前轮到者，男生/女生组自动顺延下一位（被跳过者视为已消耗轮次）
        </li>
        <li>
          开启「周六周日同一带班领导」后，休息日领导以周末对轮转（周日复用周六人选）；男生/女生组仍每天轮转
        </li>
        <li>
          点击表格中的姓名可手工换人（不影响轮转）；重新生成整月将按本月之前最近一次真实班次接续推演
        </li>
      </ul>
    </NcCard>

    <el-dialog v-model="exportVisible" title="导出政务值班表" width="460px">
      <p class="dialog-hint">选择输出月份范围（每月一个文件；单月输出时起止相同即可）</p>

      <div class="range-form">
        <span class="range-label">起始</span>
        <el-select v-model="exportStartYear" class="range-year">
          <el-option
            v-for="item in exportYearOptions"
            :key="`start-${item}`"
            :label="`${item} 年`"
            :value="item"
          />
        </el-select>
        <el-select v-model="exportStartMonth" class="range-month">
          <el-option
            v-for="item in exportMonthOptions"
            :key="`start-${item}`"
            :label="`${item} 月`"
            :value="item"
          />
        </el-select>
      </div>

      <div class="range-form">
        <span class="range-label">结束</span>
        <el-select v-model="exportEndYear" class="range-year">
          <el-option
            v-for="item in exportYearOptions"
            :key="`end-${item}`"
            :label="`${item} 年`"
            :value="item"
          />
        </el-select>
        <el-select v-model="exportEndMonth" class="range-month">
          <el-option
            v-for="item in exportMonthOptions"
            :key="`end-${item}`"
            :label="`${item} 月`"
            :value="item"
          />
        </el-select>
      </div>

      <p v-if="exportRangeError" class="range-error">{{ exportRangeError }}</p>
      <p v-else class="range-tip">
        共 {{ exportTotalMonths }} 个月（最多 36 个月）
      </p>

      <template #footer>
        <el-button @click="exportVisible = false">取消</el-button>
        <el-button
          type="primary"
          :loading="exportLoading"
          :disabled="Boolean(exportRangeError)"
          @click="submitExport"
        >
          导出
        </el-button>
      </template>
    </el-dialog>

    <div class="mobile-fab">
      <el-button type="primary" :icon="UserFilled" circle @click="goAdjustments" />
    </div>
  </div>
</template>

<style scoped>
.duty-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* ── 月份条 ─────────────────────────────────────────────────── */
.month-card,
.toolbar-card,
.schedule-card,
.rules-card {
  overflow: hidden;
}

.month-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 4px 6px;
}

.month-nav {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.month-arrow {
  width: 34px;
  height: 34px;
  padding: 0;
}

.month-arrow--next .el-icon {
  transform: rotate(180deg);
}

.month-title-wrap {
  display: flex;
  align-items: baseline;
  gap: 12px;
  min-width: 0;
}

.month-title {
  font-size: var(--nc-text-xl);
  font-weight: 700;
  letter-spacing: 0.04em;
  white-space: nowrap;
}

.month-status {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.month-status.is-generated {
  color: var(--nc-pine);
}

.month-actions {
  display: flex;
  flex: none;
  gap: 8px;
}

/* ── 工具栏 ─────────────────────────────────────────────────── */
.toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 2px 4px;
}

.same-pair {
  display: flex;
  align-items: center;
  gap: 9px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-base);
}

.toolbar-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.load-alert {
  margin: 0;
}

/* ── 表格 ───────────────────────────────────────────────────── */
.schedule-body {
  min-height: 220px;
}

.table-scroll {
  overflow-x: auto;
}

.duty-table {
  width: 100%;
  min-width: 860px;
  border-collapse: collapse;
  table-layout: fixed;
}

.duty-table .col-date {
  width: 100px;
}

.duty-table .col-week {
  width: 64px;
}

.duty-table .col-type {
  width: 112px;
}

.duty-table th {
  padding: 11px 8px;
  border-bottom: 1px solid var(--nc-line);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.duty-table td {
  padding: 7px 6px;
  border-bottom: 1px solid var(--nc-line-faint);
  vertical-align: middle;
}

.duty-table tbody tr:hover td {
  background-color: var(--nc-seal-wash);
}

.date-cell {
  text-align: center;
}

.date-cell strong {
  display: block;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  white-space: nowrap;
}

.date-cell small {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.week-cell {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-base);
  text-align: center;
}

.type-pill {
  display: inline-block;
  min-width: 70px;
  padding: 2px 9px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.7;
  text-align: center;
  white-space: nowrap;
}

.type-pill--ink {
  border-color: var(--nc-line-strong);
  background-color: var(--nc-paper-2);
  color: var(--nc-ink-2);
}

.type-pill--pine {
  border-color: transparent;
  background-color: #e3f0fc;
  color: #1565c0;
}

.type-pill--seal {
  border-color: transparent;
  background-color: #fde8e8;
  color: #c62828;
}

/* ── 单元格 ─────────────────────────────────────────────────── */
.el-dropdown {
  display: block;
  width: 100%;
}

.cell-button {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1px;
  width: 100%;
  min-height: 50px;
  padding: 5px 7px;
  border: 1px solid transparent;
  border-radius: var(--nc-radius-sm);
  background-color: transparent;
  color: var(--nc-ink);
  font-family: inherit;
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition);
}

.cell-button:hover:not(:disabled) {
  border-color: var(--nc-line-strong);
}

.cell-button:disabled {
  cursor: default;
}

.cell-name {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  line-height: 1.45;
  letter-spacing: 0.02em;
}

.cell-phone {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  line-height: 1.4;
}

.adjust-badge {
  position: absolute;
  top: 3px;
  right: 3px;
  padding: 0 4px;
  border-radius: 2px;
  background-color: #fef3c7;
  color: #b45309;
  font-size: 10px;
  font-weight: 700;
  line-height: 15px;
}

.duty-cell .cell-button,
.duty-cell {
  position: relative;
}

.duty-cell--leader {
  background-color: #f6f4fb;
}

.duty-cell--middle {
  background-color: #fdf6ec;
}

.duty-cell--male {
  background-color: #edf7f1;
}

.duty-cell--female {
  background-color: #fdf0f5;
}

.cell-button.is-empty .cell-name,
.cell-button.is-off .cell-name {
  color: var(--nc-disabled);
  font-family: var(--nc-font-sans);
  font-weight: 400;
}

.cell-button.is-off {
  background-color: transparent;
}

.cell-button.is-adjusted {
  box-shadow: inset 0 0 0 1px rgba(180, 83, 9, 0.22);
}

/* ── 移动端卡片 ─────────────────────────────────────────────── */
.day-cards {
  display: flex;
  flex-direction: column;
  gap: 9px;
  padding: 10px;
}

.day-card {
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.day-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 9px 12px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.day-card__date {
  display: flex;
  align-items: baseline;
  gap: 7px;
}

.day-card__date strong {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
}

.day-card__date span {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.day-card__rows {
  display: flex;
  flex-direction: column;
}

.day-card__row {
  display: grid;
  grid-template-columns: 38px minmax(0, 1fr);
  align-items: center;
  gap: 8px;
  padding: 5px 8px;
}

.day-card__row + .day-card__row {
  border-top: 1px dashed var(--nc-line-faint);
}

.day-card__label {
  font-size: var(--nc-text-xs);
  font-weight: 700;
  text-align: center;
}

.group-label--violet {
  color: var(--nc-violet);
}

.group-label--amber {
  color: var(--nc-amber-dark);
}

.group-label--pine {
  color: var(--nc-pine);
}

.group-label--seal {
  color: var(--nc-seal);
}

.day-card .cell-button {
  flex-direction: row;
  justify-content: flex-start;
  gap: 8px;
  min-height: 38px;
  padding: 4px 9px;
}

.day-card .cell-name {
  font-size: var(--nc-text-base);
}

.day-card .cell-phone {
  margin-left: auto;
  font-size: var(--nc-text-xs);
}

.day-card .adjust-badge {
  position: static;
}

.empty-wrap {
  padding: 26px 10px;
}

/* ── 规则 ───────────────────────────────────────────────────── */
.rules-list {
  display: flex;
  flex-direction: column;
  gap: 5px;
  margin: 0;
  padding: 0 0 0 18px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.8;
}

.rules-list li::marker {
  color: var(--nc-seal);
}

/* ── 导出弹窗 ───────────────────────────────────────────────── */
.dialog-hint {
  margin-bottom: 14px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.range-form {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;
}

.range-label {
  width: 34px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-base);
}

.range-year {
  width: 122px;
}

.range-month {
  width: 96px;
}

.range-error {
  margin-top: 8px;
  color: var(--nc-seal);
  font-size: var(--nc-text-sm);
}

.range-tip {
  margin-top: 8px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.mobile-fab {
  display: none;
}

@media (max-width: 960px) {
  .month-bar {
    align-items: stretch;
    flex-direction: column;
  }

  .month-actions {
    justify-content: flex-end;
  }

  .month-actions .el-button {
    flex: 1;
  }

  .toolbar {
    align-items: stretch;
    flex-direction: column;
  }

  .toolbar-actions {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .toolbar-actions :deep(.el-button) {
    margin-left: 0;
  }

  .mobile-fab {
    position: fixed;
    right: 18px;
    bottom: calc(74px + env(safe-area-inset-bottom));
    z-index: 20;
    display: block;
  }
}

@media (max-width: 520px) {
  .month-nav {
    flex-wrap: wrap;
  }

  .month-title-wrap {
    flex: 1;
  }

  .month-title {
    font-size: var(--nc-text-lg);
  }

  .month-status {
    font-size: var(--nc-text-xs);
  }

  .toolbar-actions {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .range-year {
    flex: 1;
    width: auto;
  }

  .range-month {
    flex: 1;
    width: auto;
  }
}
</style>
