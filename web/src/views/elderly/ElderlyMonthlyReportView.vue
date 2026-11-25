<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, FolderOpened, Printer, Refresh, View } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { createElderlyReportJob, getElderlyMonthlyReport } from '@/api/elderly'
import type {
  ElderlyMonthlyReport,
  ElderlyReportJobMode,
  ElderlyReportJobResult,
} from '@/types/elderly'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'

/* ───────────────────────── 基础状态 ───────────────────────── */

const router = useRouter()

const today = new Date()
const selectedYear = ref(today.getFullYear())
const selectedMonth = ref(today.getMonth() + 1)

/** 近 3 年 + 当前年（旧版为当前年前两年起，这里覆盖到近三年）。 */
const years = Array.from({ length: 4 }, (_, index) => today.getFullYear() - 3 + index)
const months = Array.from({ length: 12 }, (_, index) => index + 1)

const report = ref<ElderlyMonthlyReport | null>(null)
const loading = ref(false)
const loadError = ref('')

const selectedKeys = ref<string[]>([])
const copies = ref(1)

const forms = computed(() => report.value?.forms ?? [])
const hasSelection = computed(() => selectedKeys.value.length > 0)

/** 旧版按钮可用性：预览/打印/保存选中需勾选；全部保存与打开目录始终可用。 */
type ReportJobAction = 'preview' | 'print' | 'save-selected' | 'save-all' | 'open-folder'

const submittingAction = ref<ReportJobAction | ''>('')
const isSubmitting = computed(() => submittingAction.value !== '')

interface ReportJobRecord {
  job: ElderlyReportJobResult
  mode: ElderlyReportJobMode
  formKeys: string[]
  submittedAt: Date
}

const lastJob = ref<ReportJobRecord | null>(null)

let loadTicket = 0

async function loadReport(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''

  try {
    const result = await getElderlyMonthlyReport(selectedYear.value, selectedMonth.value)
    if (ticket !== loadTicket) return

    report.value = result
    // 保留仍然存在的勾选项，避免换月后勾选状态与清单不一致。
    const validKeys = new Set(result.forms.map((form) => form.key))
    selectedKeys.value = selectedKeys.value.filter((key) => validKeys.has(key))
  } catch (error) {
    if (ticket !== loadTicket) return
    report.value = null
    selectedKeys.value = []
    loadError.value = getErrorMessage(error, '月报统计加载失败')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

watch([selectedYear, selectedMonth], () => {
  lastJob.value = null
  void loadReport()
})

onMounted(() => {
  void loadReport()
})

/* ───────────────────────── 勾选 ───────────────────────── */

function selectAll(): void {
  selectedKeys.value = forms.value.map((form) => form.key)
}

function clearSelection(): void {
  selectedKeys.value = []
}

/* ───────────────────────── 任务提交 ───────────────────────── */

const MODE_LABELS: Record<ElderlyReportJobMode, string> = {
  preview: '预览',
  print: '打印',
  save: '保存',
  'open-folder': '打开输出目录',
}

const MODE_DESCRIPTIONS: Record<ElderlyReportJobMode, string> = {
  preview: '已推送到 PC 端预览',
  print: '已推送到 PC 端打印',
  save: '保存到本机输出目录并打开',
  'open-folder': '已请求本机打开输出目录',
}

const MODE_SUCCESS_MESSAGES: Record<ElderlyReportJobMode, string> = {
  preview: '已推送到 PC 端预览',
  print: '已推送打印任务',
  save: '已保存到本机输出目录并打开',
  'open-folder': '已请求本机打开输出目录',
}

const MODE_ERROR_FALLBACKS: Record<ElderlyReportJobMode, string> = {
  preview: '推送预览失败',
  print: '推送打印任务失败',
  save: '保存报表失败',
  'open-folder': '打开输出目录失败',
}

const JOB_STATUS_LABELS: Record<string, string> = {
  Pending: '排队中',
  Running: '执行中',
  Processing: '执行中',
  Succeeded: '已完成',
  Completed: '已完成',
  Failed: '失败',
  Cancelled: '已取消',
}

function modeLabel(mode: ElderlyReportJobMode): string {
  return MODE_LABELS[mode]
}

function statusLabel(status: string): string {
  return JOB_STATUS_LABELS[status] ?? status
}

function jobStatusClass(status: string): string {
  if (status === 'Failed') return 'is-failed'
  if (status === 'Succeeded' || status === 'Completed') return 'is-done'
  return 'is-pending'
}

function formatTime(date: Date): string {
  const pad = (value: number): string => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

const formLabelMap = computed(
  () => new Map(forms.value.map((form) => [form.key, form.label] as const)),
)

function keyLabel(key: string): string {
  return formLabelMap.value.get(key) ?? key
}

function normalizedCopies(): number {
  const value = Number.isFinite(copies.value) ? Math.trunc(copies.value) : 1
  return Math.min(99, Math.max(1, value))
}

async function submitJob(
  action: ReportJobAction,
  mode: ElderlyReportJobMode,
  keys: string[],
): Promise<void> {
  if (mode !== 'open-folder' && keys.length === 0) {
    ElMessage.warning(
      mode === 'save' ? '没有可保存的明细表' : '请先勾选要操作的明细表',
    )
    return
  }

  submittingAction.value = action
  try {
    const job = await createElderlyReportJob({
      year: selectedYear.value,
      month: selectedMonth.value,
      formKeys: keys,
      mode,
      copies: mode === 'print' ? normalizedCopies() : undefined,
      isDuplex: false,
    })
    lastJob.value = {
      job,
      mode,
      formKeys: [...keys],
      submittedAt: new Date(),
    }
    ElMessage.success(MODE_SUCCESS_MESSAGES[mode])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, MODE_ERROR_FALLBACKS[mode]))
  } finally {
    submittingAction.value = ''
  }
}

function handlePreview(): void {
  void submitJob('preview', 'preview', [...selectedKeys.value])
}

function handlePrint(): void {
  void submitJob('print', 'print', [...selectedKeys.value])
}

function handleSaveSelected(): void {
  void submitJob('save-selected', 'save', [...selectedKeys.value])
}

function handleSaveAll(): void {
  void submitJob(
    'save-all',
    'save',
    forms.value.map((form) => form.key),
  )
}

function handleOpenFolder(): void {
  void submitJob('open-folder', 'open-folder', [])
}
</script>

<template>
  <div class="nc-page elderly-report-page">
    <NcPageHeader
      title="高龄津贴月报表"
      description="按月统计高龄津贴新增与停发数据"
      kicker="普惠高龄 · 月报表"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/elderly')">返回工作台</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadReport()">刷新</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      class="agent-alert"
      type="info"
      :closable="false"
      show-icon
      title="预览/打印/保存由本机 Agent 执行，请确保 Agent 已绑定打印机/设备"
    />

    <el-alert
      v-if="loadError"
      class="page-alert"
      type="error"
      :title="`月报统计加载失败：${loadError}`"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadReport()">重新加载</el-button>
    </el-alert>

    <div class="report-layout">
      <!-- 左栏：统计周期 / 汇总 / 明细表 / 操作 -->
      <div class="report-left">
        <NcCard title="统计周期">
          <div class="cycle-fields">
            <el-select v-model="selectedYear" class="cycle-select" aria-label="选择年份">
              <el-option
                v-for="year in years"
                :key="year"
                :label="String(year)"
                :value="year"
              />
            </el-select>
            <span class="cycle-unit">年</span>
            <el-select v-model="selectedMonth" class="cycle-select" aria-label="选择月份">
              <el-option
                v-for="month in months"
                :key="month"
                :label="String(month)"
                :value="month"
              />
            </el-select>
            <span class="cycle-unit">月</span>
          </div>

          <p class="cycle-range">{{ report?.cycleRangeText || '—' }}</p>
          <p class="report-title">{{ report?.reportTitleText || '—' }}</p>

          <el-button
            class="block-button"
            type="primary"
            :loading="loading"
            @click="loadReport()"
          >
            刷新数据
          </el-button>

          <p v-if="report" class="status-text">{{ report.statusText }}</p>
          <p v-else-if="loading" class="status-text is-muted">正在加载…</p>
          <p v-else class="status-text is-muted">尚未加载统计</p>
        </NcCard>

        <NcCard title="汇总">
          <el-skeleton v-if="loading && !report" :rows="2" animated />
          <div v-else class="summary-grid">
            <div class="summary-tile summary-tile--new">
              <span class="summary-label">新增</span>
              <strong class="summary-value">{{ report ? report.newCount : '—' }}</strong>
            </div>
            <div class="summary-tile summary-tile--stop">
              <span class="summary-label">停止</span>
              <strong class="summary-value">{{ report ? report.stopCount : '—' }}</strong>
            </div>
          </div>
        </NcCard>

        <NcCard title="明细表">
          <div class="form-toolbar">
            <el-button size="small" plain @click="selectAll">全选</el-button>
            <el-button size="small" plain @click="clearSelection">清空</el-button>
            <span class="form-selected">已选 {{ selectedKeys.length }} / {{ forms.length }}</span>
          </div>

          <el-skeleton v-if="loading && forms.length === 0" :rows="5" animated />
          <el-empty
            v-else-if="forms.length === 0"
            description="暂无明细表"
            :image-size="60"
          />
          <el-checkbox-group v-else v-model="selectedKeys" class="form-list">
            <el-checkbox
              v-for="form in forms"
              :key="form.key"
              :value="form.key"
              class="form-item"
            >
              <span class="form-label">{{ form.label }}</span>
              <span v-if="form.hasData" class="form-count">{{ form.count }}</span>
            </el-checkbox>
          </el-checkbox-group>
        </NcCard>

        <NcCard title="操作">
          <div class="copies-row">
            <span class="copies-label">打印份数</span>
            <el-input-number
              v-model="copies"
              class="copies-input"
              :min="1"
              :max="99"
              :step="1"
              step-strictly
              size="small"
              controls-position="right"
              aria-label="打印份数"
            />
            <span class="copies-hint">1-99</span>
          </div>

          <div class="action-stack">
            <el-button
              type="primary"
              :icon="View"
              class="block-button"
              :loading="submittingAction === 'preview'"
              :disabled="!hasSelection || isSubmitting"
              @click="handlePreview"
            >
              预览选中
            </el-button>
            <el-button
              :icon="Printer"
              class="block-button"
              :loading="submittingAction === 'print'"
              :disabled="!hasSelection || isSubmitting"
              @click="handlePrint"
            >
              打印选中
            </el-button>
            <div class="action-pair">
              <el-button
                type="success"
                :loading="submittingAction === 'save-selected'"
                :disabled="!hasSelection || isSubmitting"
                @click="handleSaveSelected"
              >
                保存选中
              </el-button>
              <el-button
                type="success"
                plain
                :loading="submittingAction === 'save-all'"
                :disabled="isSubmitting"
                @click="handleSaveAll"
              >
                全部保存
              </el-button>
            </div>
            <el-button
              :icon="FolderOpened"
              class="block-button"
              :loading="submittingAction === 'open-folder'"
              :disabled="isSubmitting"
              @click="handleOpenFolder"
            >
              打开输出目录
            </el-button>
          </div>
        </NcCard>
      </div>

      <!-- 右栏：Web 不能内嵌本机 PDF，改为任务状态面板 -->
      <div class="report-right">
        <NcCard
          title="预览 / 任务状态"
          subtitle="Web 端不内嵌本机 PDF；预览、打印与保存均由 PC Agent 执行"
          padding="lg"
        >
          <el-skeleton v-if="loading && !lastJob" :rows="6" animated />

          <div v-else-if="lastJob" class="job-panel">
            <div class="job-head">
              <span class="job-mode">{{ modeLabel(lastJob.mode) }}</span>
              <span class="job-status" :class="jobStatusClass(lastJob.job.status)">
                {{ statusLabel(lastJob.job.status) }}
              </span>
            </div>

            <dl class="job-meta">
              <div class="job-meta-row">
                <dt>任务号</dt>
                <dd class="job-no">{{ lastJob.job.jobNo }}</dd>
              </div>
              <div class="job-meta-row">
                <dt>模式</dt>
                <dd>{{ modeLabel(lastJob.mode) }}</dd>
              </div>
              <div class="job-meta-row">
                <dt>状态</dt>
                <dd>{{ statusLabel(lastJob.job.status) }}（{{ lastJob.job.status }}）</dd>
              </div>
              <div class="job-meta-row">
                <dt>提交时间</dt>
                <dd>{{ formatTime(lastJob.submittedAt) }}</dd>
              </div>
            </dl>

            <p class="job-desc">{{ MODE_DESCRIPTIONS[lastJob.mode] }}</p>

            <div v-if="lastJob.formKeys.length > 0" class="job-forms">
              <span class="job-forms-label">明细表</span>
              <div class="job-form-tags">
                <el-tag
                  v-for="key in lastJob.formKeys"
                  :key="key"
                  size="small"
                  effect="plain"
                >
                  {{ keyLabel(key) }}
                </el-tag>
              </div>
            </div>
          </div>

          <el-empty
            v-else
            class="preview-empty"
            description="选择月份后勾选明细表并点击 预览 生成报表预览"
            :image-size="120"
          />

          <div v-if="report" class="report-meta">
            <p class="report-meta-title">{{ report.reportTitleText }}</p>
            <p class="report-meta-line">统计周期 {{ report.cycleRangeText }}</p>
            <p class="report-meta-line">{{ report.statusText }}</p>
          </div>
        </NcCard>
      </div>
    </div>
  </div>
</template>

<style scoped>
.elderly-report-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.agent-alert,
.page-alert {
  margin: 0;
}

/* 桌面左右两栏：左栏 440px（与旧版一致），右栏自适应；窄屏上下堆叠。 */
.report-layout {
  display: grid;
  grid-template-columns: 440px minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

.report-left {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}

.report-right {
  min-width: 0;
}

/* ── 统计周期 ─────────────────────────────────────────────────── */

.cycle-fields {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 8px;
}

.cycle-select {
  width: 100%;
}

.cycle-unit {
  font-size: var(--nc-text-base);
  color: var(--nc-muted-2);
}

.cycle-range {
  margin-top: 12px;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-md);
  font-weight: 600;
  color: var(--nc-seal);
}

.report-title {
  margin-top: 6px;
  font-size: var(--nc-text-base);
  line-height: 1.6;
  color: var(--nc-muted-2);
}

.block-button {
  width: 100%;
  margin-top: 12px;
  margin-left: 0;
}

.status-text {
  margin-top: 10px;
  font-size: var(--nc-text-base);
  font-weight: 600;
  color: var(--nc-seal);
}

.status-text.is-muted {
  font-weight: 400;
  color: var(--nc-muted);
}

/* ── 汇总（旧版 InfoSoft / WarningSoft 两块） ─────────────────── */

.summary-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.summary-tile {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  padding: 10px 12px 12px;
  border-radius: var(--nc-radius-sm);
}

.summary-tile--new {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.summary-tile--stop {
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.summary-label {
  font-size: var(--nc-text-base);
}

.summary-value {
  font-family: var(--nc-font-serif);
  font-size: 30px;
  font-weight: 700;
  line-height: 1.15;
}

/* ── 明细表 ───────────────────────────────────────────────────── */

.form-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.form-selected {
  margin-left: auto;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.06em;
  color: var(--nc-muted);
}

.form-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
  max-height: 320px;
  padding: 8px 10px;
  margin-top: 10px;
  overflow-y: auto;
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.form-item {
  width: 100%;
  height: auto;
  min-height: 30px;
  margin-right: 0;
  padding: 2px 0;
}

.form-item :deep(.el-checkbox__label) {
  display: flex;
  flex: 1;
  align-items: center;
  gap: 8px;
  min-width: 0;
  font-size: var(--nc-text-base);
  color: var(--nc-ink);
}

.form-label {
  min-width: 0;
  line-height: 1.5;
  white-space: normal;
}

.form-count {
  flex: none;
  min-width: 22px;
  padding: 0 6px;
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  line-height: 18px;
  text-align: center;
  color: var(--nc-seal-dark);
}

/* ── 操作 ─────────────────────────────────────────────────────── */

.copies-row {
  display: flex;
  align-items: center;
  gap: 10px;
}

.copies-label {
  font-size: var(--nc-text-base);
  color: var(--nc-muted-2);
}

.copies-input {
  width: 110px;
}

.copies-hint {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  color: var(--nc-muted);
}

.action-stack {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 12px;
}

.action-stack .block-button {
  margin-top: 0;
}

.action-pair {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.action-pair :deep(.el-button) {
  width: 100%;
  margin-left: 0;
}

/* ── 右栏：任务状态 ───────────────────────────────────────────── */

.preview-empty {
  min-height: 300px;
  padding: 24px 0;
}

.preview-empty :deep(.el-empty__description p) {
  max-width: 340px;
  color: var(--nc-muted-2);
  line-height: 1.7;
}

.job-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.job-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.job-mode {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  font-weight: 700;
  letter-spacing: 0.04em;
  color: var(--nc-ink);
}

.job-status {
  padding: 2px 8px;
  border-radius: var(--nc-radius-sm);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.08em;
}

.job-status.is-pending {
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.job-status.is-done {
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
}

.job-status.is-failed {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.job-meta {
  display: flex;
  flex-direction: column;
  gap: 0;
  margin: 0;
  border-top: 1px solid var(--nc-line-faint);
}

.job-meta-row {
  display: flex;
  align-items: baseline;
  gap: 16px;
  padding: 9px 0;
  border-bottom: 1px solid var(--nc-line-faint);
}

.job-meta-row dt {
  flex: none;
  width: 72px;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted);
}

.job-meta-row dd {
  min-width: 0;
  margin: 0;
  font-size: var(--nc-text-base);
  line-height: 1.6;
  color: var(--nc-ink-2);
  word-break: break-all;
}

.job-meta-row dd.job-no {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  color: var(--nc-ink);
}

.job-desc {
  padding: 10px 12px;
  border-left: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  font-size: var(--nc-text-base);
  font-weight: 600;
  color: var(--nc-seal-dark);
}

.job-forms {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.job-forms-label {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.12em;
  color: var(--nc-muted);
}

.job-form-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.report-meta {
  margin-top: 20px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.report-meta-title {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  line-height: 1.6;
  color: var(--nc-ink);
}

.report-meta-line {
  margin-top: 4px;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted-2);
}

/* ── 响应式：窄屏上下堆叠 ─────────────────────────────────────── */

@media (max-width: 960px) {
  .report-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .report-right {
    order: 2;
  }

  .preview-empty {
    min-height: 200px;
  }
}

@media (max-width: 480px) {
  .summary-value {
    font-size: 26px;
  }

  .copies-input {
    width: 100px;
  }
}
</style>
