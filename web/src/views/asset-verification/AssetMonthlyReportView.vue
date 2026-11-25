<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, Refresh } from '@element-plus/icons-vue'
import { listAssetTasks } from '@/api/assetVerification'
import { getErrorMessage } from '@/api/http'
import {
  MONTHLY_REPORT_EXPORT_PENDING_MESSAGE,
  exportMonthlyReport,
  isMonthlyReportExportPending,
} from '@/api/assetMonthlyReport'
import type {
  MonthlyReportExportScope,
  MonthlyReportPeriod,
  MonthlyReportStats,
  MonthlyReportStatus,
} from '@/api/assetMonthlyReport'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'

/* 旧版 Helpers/ReportPeriodHelper：2026-08-01 起由月报切换为周报。 */
const WEEKLY_SWITCH_YEAR = 2026
const WEEKLY_SWITCH_MONTH = 8

interface WeekOption {
  /** ISO 周序号；过渡周为 0。 */
  weekNumber: number
  startDate: Date
  /** 排他上界（下周一 00:00）。 */
  endDateExclusive: Date
  displayText: string
}

const router = useRouter()
const today = new Date()

const selectedYear = ref(today.getFullYear())
const selectedMonth = ref(today.getMonth() + 1)
const selectedWeek = ref<number | null>(null)
const weekOptions = ref<WeekOption[]>([])
/** 两个导出区共用，与旧版 OnlyCurrentStatusPersons 一致。 */
const onlyCurrentStatusPersons = ref(false)

const yearOptions = Array.from({ length: 11 }, (_, index) => today.getFullYear() - 5 + index)
const monthOptions = Array.from({ length: 12 }, (_, index) => index + 1)

const stats = ref<MonthlyReportStats>(emptyStats())
const loading = ref(false)
const loaded = ref(false)
const loadError = ref('')
const exporting = ref(false)
let requestTicket = 0

const isWeeklyMode = computed(() =>
  isWeeklyMonth(selectedYear.value, selectedMonth.value),
)
const reportTypeLabel = computed(() => (isWeeklyMode.value ? '周报' : '月报'))
const pageTitle = computed(() => `核查${reportTypeLabel.value}表`)
const totalLabel = computed(() => (isWeeklyMode.value ? '本周合计' : '本月合计'))

const activeWeek = computed(
  () => weekOptions.value.find((item) => item.weekNumber === selectedWeek.value) ?? null,
)

const period = computed<MonthlyReportPeriod>(() => {
  const year = selectedYear.value
  const month = selectedMonth.value

  if (isWeeklyMode.value && activeWeek.value) {
    const week = activeWeek.value
    const weekEnd = addDays(week.endDateExclusive, -1)
    const label =
      week.weekNumber === 0
        ? `${year}年过渡周(${week.startDate.getMonth() + 1}.${week.startDate.getDate()}~${weekEnd.getMonth() + 1}.${weekEnd.getDate()})`
        : `${year}年第${week.weekNumber}周`

    return {
      mode: 'weekly',
      year,
      month,
      weekNumber: week.weekNumber,
      startDate: formatDate(week.startDate),
      endDate: formatDate(weekEnd),
      label,
    }
  }

  const { start, endExclusive } = getALinePeriod(year, month)
  return {
    mode: 'monthly',
    year,
    month,
    weekNumber: null,
    startDate: formatDate(start),
    endDate: formatDate(addDays(endExclusive, -1)),
    label: `${year}年${month}月`,
  }
})

const periodText = computed(() => {
  const current = period.value
  if (current.mode === 'weekly') {
    const prefix =
      current.weekNumber === 0 ? 'A线 核查周期（过渡周）' : 'A线 核查周期（周报）'
    return `${prefix}：${current.startDate} ~ ${current.endDate}`
  }
  return `A线 核查周期：${current.startDate} ~ ${current.endDate}`
})

const periodHint = computed(() =>
  isWeeklyMode.value
    ? '2026 年 8 月起按周报送，请选择需要导出的自然周；过渡周单独列出。'
    : '按月报送期间，统计区间为 A 线核查周期（上月 11 日至本月 10 日）。',
)

const totalText = computed(() => {
  const value = stats.value.totalCount
  return value === null ? '—' : `${value.toLocaleString('zh-CN')} 条`
})

const isEmpty = computed(
  () =>
    loaded.value &&
    !loading.value &&
    !loadError.value &&
    (stats.value.totalCount ?? 0) === 0,
)

function emptyStats(): MonthlyReportStats {
  return {
    submittedCount: null,
    hasReportCount: null,
    archivedCount: null,
    rejectedCount: null,
    totalCount: null,
  }
}

function countText(value: number | null): string {
  return value === null ? '—' : value.toLocaleString('zh-CN')
}

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function formatDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function formatMonthDay(date: Date): string {
  return `${date.getMonth() + 1}月${date.getDate()}日`
}

function addDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days)
}

/** 周一为一周起点。 */
function startOfWeek(date: Date): Date {
  const offset = (date.getDay() + 6) % 7
  return addDays(date, -offset)
}

function isoWeekNumber(date: Date): number {
  const target = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  const day = target.getUTCDay() || 7
  target.setUTCDate(target.getUTCDate() + 4 - day)
  const yearStart = Date.UTC(target.getUTCFullYear(), 0, 1)
  return Math.ceil(((target.getTime() - yearStart) / 86_400_000 + 1) / 7)
}

function isWeeklyMonth(year: number, month: number): boolean {
  const monthStart = new Date(year, month - 1, 1)
  return monthStart >= new Date(WEEKLY_SWITCH_YEAR, WEEKLY_SWITCH_MONTH - 1, 1)
}

/** A 线月周期：[上月 11 日, 本月 11 日)。 */
function getALinePeriod(year: number, month: number): { start: Date; endExclusive: Date } {
  return {
    start: new Date(year, month - 2, 11),
    endExclusive: new Date(year, month - 1, 11),
  }
}

function firstMondayOfMonth(year: number, month: number): Date {
  let cursor = new Date(year, month - 1, 1)
  while (cursor.getDay() !== 1) {
    cursor = addDays(cursor, 1)
  }
  return cursor
}

/**
 * 与旧版 ReportPeriodHelper.GetWeeksInMonth 一致：只取周一起始日落在本月的自然周；
 * 切换月额外追加过渡周（上月 11 日 ~ 本月第一个周一前一日）。
 */
function buildWeeksInMonth(year: number, month: number): WeekOption[] {
  const weeks: WeekOption[] = []
  const monthStart = new Date(year, month - 1, 1)
  const monthEnd = new Date(year, month, 1)
  let cursor = startOfWeek(monthStart)

  while (cursor < monthEnd) {
    const weekStart = cursor
    const weekEndExclusive = addDays(weekStart, 7)

    if (weekStart >= monthStart && weekStart < monthEnd) {
      const weekNumber = isoWeekNumber(weekStart)
      weeks.push({
        weekNumber,
        startDate: weekStart,
        endDateExclusive: weekEndExclusive,
        displayText: `第${weekNumber}周: ${formatMonthDay(weekStart)}~${formatMonthDay(addDays(weekEndExclusive, -1))}`,
      })
    }

    cursor = weekEndExclusive
  }

  if (year === WEEKLY_SWITCH_YEAR && month === WEEKLY_SWITCH_MONTH) {
    const transitionStart = new Date(year, month - 2, 11)
    const firstMonday = firstMondayOfMonth(year, month)
    weeks.push({
      weekNumber: 0,
      startDate: transitionStart,
      endDateExclusive: firstMonday,
      displayText: `过渡周: ${formatMonthDay(transitionStart)}~${formatMonthDay(addDays(firstMonday, -1))}`,
    })
  }

  return weeks
}

function syncWeeks(): void {
  if (!isWeeklyMode.value) {
    weekOptions.value = []
    selectedWeek.value = null
    return
  }

  const options = buildWeeksInMonth(selectedYear.value, selectedMonth.value)
  weekOptions.value = options
  // 旧版默认选中第一周（自然周）；过渡周保留在列表末尾供手动选择。
  selectedWeek.value = options[0]?.weekNumber ?? null
}

/** 统计按人员记录聚合：列表接口的 totalCount，按各状态分别取数。 */
async function fetchStatusCount(
  current: MonthlyReportPeriod,
  status?: MonthlyReportStatus,
): Promise<number> {
  const response = await listAssetTasks({
    page: 1,
    pageSize: 1,
    status,
    dateFrom: current.startDate,
    dateTo: current.endDate,
  })
  return response.totalCount ?? 0
}

async function loadStats(): Promise<void> {
  const ticket = ++requestTicket
  const current = period.value
  loading.value = true
  loadError.value = ''

  try {
    const [totalCount, submittedCount, hasReportCount, archivedCount, rejectedCount] =
      await Promise.all([
        fetchStatusCount(current),
        fetchStatusCount(current, '0'),
        fetchStatusCount(current, '1'),
        fetchStatusCount(current, '2'),
        fetchStatusCount(current, '3'),
      ])

    if (ticket !== requestTicket) return

    stats.value = {
      totalCount,
      submittedCount,
      hasReportCount,
      archivedCount,
      rejectedCount,
    }
    loaded.value = true
  } catch (error) {
    if (ticket !== requestTicket) return
    // 与旧版一致：失败不置 0，保留上次统计并显式提示，避免被误读为“本期无人”。
    loadError.value = getErrorMessage(error, '核查统计加载失败')
  } finally {
    if (ticket === requestTicket) {
      loading.value = false
    }
  }
}

function handlePeriodChange(): void {
  syncWeeks()
  void loadStats()
}

function handleWeekChange(): void {
  void loadStats()
}

async function handleExport(
  scope: MonthlyReportExportScope,
  status: MonthlyReportStatus | null,
  label: string,
): Promise<void> {
  const current = period.value
  if (current.mode === 'weekly' && current.weekNumber === null) {
    ElMessage.warning('请先选择周')
    return
  }

  exporting.value = true
  try {
    await exportMonthlyReport({
      period: current,
      scope,
      status,
      onlyCurrentStatusPersons: onlyCurrentStatusPersons.value,
    })
    ElMessage.success(`已开始导出${label}`)
  } catch (error) {
    if (isMonthlyReportExportPending(error)) {
      ElMessage.warning(`${MONTHLY_REPORT_EXPORT_PENDING_MESSAGE}，暂不能下载文件`)
      return
    }
    ElMessage.error(getErrorMessage(error, `导出${label}失败`))
  } finally {
    exporting.value = false
  }
}

/* 首帧即按当前年月确定月报/周报与默认周，避免先渲染月报再切周报。 */
syncWeeks()

onMounted(() => {
  void loadStats()
})
</script>

<template>
  <div class="nc-page asset-report-page">
    <NcPageHeader
      :title="pageTitle"
      description="核对名册 · 数据导出"
      kicker="救助业务 / 经济状况核对"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/asset-verification')">
          返回工作台
        </el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadStats()">刷新统计</el-button>
      </template>
    </NcPageHeader>

    <div class="period-banner">
      <span class="period-badge">核对周期</span>
      <span class="period-text">{{ periodText }}</span>
    </div>

    <NcCard title="报表周期">
      <div class="period-fields" :class="{ 'is-weekly': isWeeklyMode }">
        <el-select
          v-model="selectedYear"
          class="period-select"
          aria-label="选择年份"
          @change="handlePeriodChange"
        >
          <el-option v-for="year in yearOptions" :key="year" :label="`${year} 年`" :value="year" />
        </el-select>

        <el-select
          v-model="selectedMonth"
          class="period-select"
          aria-label="选择月份"
          @change="handlePeriodChange"
        >
          <el-option
            v-for="month in monthOptions"
            :key="month"
            :label="`${month} 月`"
            :value="month"
          />
        </el-select>

        <el-select
          v-if="isWeeklyMode"
          v-model="selectedWeek"
          class="period-select"
          aria-label="选择周"
          @change="handleWeekChange"
        >
          <el-option
            v-for="week in weekOptions"
            :key="week.weekNumber"
            :label="week.displayText"
            :value="week.weekNumber"
          />
        </el-select>
      </div>
      <p class="period-hint">{{ periodHint }}</p>
    </NcCard>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="`核查统计加载失败：${loadError}`"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadStats()">重新加载</el-button>
    </el-alert>

    <div v-loading="loading" class="stats-grid">
      <NcStat label="已提交申请" :value="countText(stats.submittedCount)" tone="ink" />
      <NcStat label="有报告未建档" :value="countText(stats.hasReportCount)" tone="amber" />
      <NcStat label="已建档" :value="countText(stats.archivedCount)" tone="pine" />
      <NcStat label="不予认定" :value="countText(stats.rejectedCount)" tone="seal" />
    </div>

    <p v-if="isEmpty" class="empty-note">所选周期暂无核对记录，导出的名册将为空。</p>

    <NcCard padding="sm" class="total-card">
      <div class="total-row">
        <span class="total-label">{{ totalLabel }}</span>
        <strong class="total-value">{{ totalText }}</strong>
      </div>
      <p class="total-note">统计按核对人员记录；导出名册按人。旧版按户统计接口待接入。</p>
    </NcCard>

    <NcCard :title="`${reportTypeLabel}表输出`">
      <p class="export-desc">选择时间周期后，点击下方按钮导出核查人员名册。</p>

      <el-checkbox v-model="onlyCurrentStatusPersons" class="roster-scope">
        仅导出当前状态人员（不展开整户）
      </el-checkbox>

      <div class="export-actions">
        <el-button
          type="primary"
          :loading="exporting"
          @click="handleExport('period', '0', '已提交申请')"
        >
          已提交申请
        </el-button>
        <el-button
          type="primary"
          :loading="exporting"
          @click="handleExport('period', '1', '有报告未建档')"
        >
          有报告未建档
        </el-button>
        <el-button type="primary" :loading="exporting" @click="handleExport('period', null, '全部导出')">
          全部导出
        </el-button>
      </div>

      <div class="export-actions export-actions--secondary">
        <el-button :loading="exporting" @click="handleExport('period', '2', '已建档')">
          导出已建档
        </el-button>
        <el-button :loading="exporting" @click="handleExport('period', '3', '不予认定')">
          导出不予认定
        </el-button>
      </div>

      <p class="export-note">导出接口待接入：按钮暂不生成文件。</p>
    </NcCard>

    <NcCard title="全局导出">
      <p class="export-desc">
        导出所有月份中指定状态的人员（默认含同户全部成员）；勾选“仅导出当前状态人员”时不展开整户。
      </p>

      <el-checkbox v-model="onlyCurrentStatusPersons" class="roster-scope">
        仅导出当前状态人员（不展开整户）
      </el-checkbox>

      <div class="global-actions">
        <el-button
          type="primary"
          :loading="exporting"
          @click="handleExport('all', '0', '全部已提交申请人员')"
        >
          导出全部已提交申请人员
        </el-button>
        <el-button
          type="primary"
          :loading="exporting"
          @click="handleExport('all', '1', '全部有报告未建档人员')"
        >
          导出全部有报告未建档人员
        </el-button>
      </div>

      <p class="export-note">导出接口待接入：按钮暂不生成文件。</p>
    </NcCard>
  </div>
</template>

<style scoped>
.asset-report-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.period-banner {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  padding: 10px 16px;
  border: 1px solid var(--nc-line-faint);
  border-left: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-seal-soft);
}

.period-badge {
  flex: none;
  padding: 3px 8px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal);
  color: var(--nc-paper-soft);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.12em;
}

.period-text {
  min-width: 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-md);
  line-height: 1.6;
  overflow-wrap: anywhere;
}

.period-fields {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
}

.period-fields:not(.is-weekly) {
  max-width: 440px;
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.period-select {
  width: 100%;
}

.period-hint {
  margin-top: 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

.load-error {
  margin: 0;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
  min-height: 88px;
}

.empty-note {
  margin: -2px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.total-card {
  border-left: 3px solid var(--nc-seal);
}

.total-row {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px 16px;
}

.total-label {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.total-value {
  color: var(--nc-seal);
  font-family: var(--nc-font-serif);
  font-size: 22px;
  font-weight: 700;
  letter-spacing: 0.03em;
}

.total-note {
  margin-top: 5px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.export-desc {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-base);
  line-height: 1.7;
}

.roster-scope {
  margin-top: 12px;
  height: auto;
  white-space: normal;
}

.export-actions {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
  margin-top: 14px;
}

.export-actions--secondary {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px dashed var(--nc-line-faint);
}

.export-actions :deep(.el-button),
.global-actions :deep(.el-button) {
  width: 100%;
  margin-left: 0;
}

.global-actions {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 14px;
}

.export-note {
  margin-top: 12px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

@media (max-width: 960px) {
  .stats-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .asset-report-page {
    gap: 13px;
  }

  .period-banner {
    padding: 10px 12px;
  }

  .period-fields,
  .period-fields:not(.is-weekly) {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .export-actions,
  .export-actions--secondary {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 480px) {
  .period-fields,
  .period-fields:not(.is-weekly),
  .stats-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .total-row {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
