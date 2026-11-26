<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, Document, Refresh } from '@element-plus/icons-vue'
import { ApiError, getErrorMessage } from '@/api/http'
import {
  createPublicityJobs,
  getPublicityFamilies,
  getPublicityGroups,
} from '@/api/publicity'
import type {
  PublicityFamily,
  PublicityFamilyListResponse,
  PublicityJobResponse,
  PublicityVillage,
} from '@/types/publicity'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { archiveJobStatusLabel } from './archiveDisplay'

const router = useRouter()

/* ───────────────────────── 公示年月 ───────────────────────── */

const today = new Date()
const selectedYear = ref(today.getFullYear())
const selectedMonth = ref(today.getMonth() + 1)

/** 与旧版一致：当前年前 5 年至后 1 年。 */
const years = Array.from({ length: 7 }, (_, index) => today.getFullYear() - 5 + index)
const months = Array.from({ length: 12 }, (_, index) => index + 1)

/* ───────────────────────── 预览名单 ───────────────────────── */

const report = ref<PublicityFamilyListResponse | null>(null)
const loading = ref(false)
const loadError = ref('')
const permissionDenied = ref(false)
const selectedVillageName = ref('')

/** 按户数升序；同户数按村名排序，保证列表稳定。 */
const villages = computed<PublicityVillage[]>(() =>
  [...(report.value?.villages ?? [])].sort(
    (left, right) =>
      left.familyCount - right.familyCount ||
      left.village.localeCompare(right.village, 'zh-CN'),
  ),
)

const totalFamilies = computed(() => report.value?.totalFamilies ?? 0)

const selectedVillage = computed<PublicityVillage | null>(
  () => villages.value.find((item) => item.village === selectedVillageName.value) ?? null,
)

interface PublicityDisplayRow extends PublicityFamily {
  index: number
}

/** 村内顺序保持接口返回顺序（旧版按类别 + 姓名排序由后端完成）。 */
const familyRows = computed<PublicityDisplayRow[]>(() =>
  (selectedVillage.value?.families ?? []).map((family, index) => ({
    ...family,
    index: index + 1,
  })),
)

/* ───────────────────────── 输出任务 ───────────────────────── */

const generating = ref(false)
const generateError = ref('')
const jobResult = ref<PublicityJobResponse | null>(null)
const generatedAt = ref<Date | null>(null)

const outputHint = computed(() => jobResult.value?.outputHint?.trim() ?? '')

/* ───────────────────────── 格式化 ───────────────────────── */

const moneyFormatter = new Intl.NumberFormat('zh-CN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return moneyFormatter.format(value)
}

function formatCount(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return String(value)
}

function formatTime(date: Date): string {
  const pad = (part: number): string => String(part).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function jobStatusLabel(status: string | undefined): string {
  return archiveJobStatusLabel(status || '已提交')
}

function jobStatusClass(status: string | undefined): string {
  const value = status ?? ''
  if (value === 'Failed' || value === 'Cancelled') return 'is-failed'
  if (value === 'Succeeded' || value === 'Completed') return 'is-done'
  if (value === 'Pending' || value === 'Running' || value === 'Processing') return 'is-pending'
  return 'is-submitted'
}

/* ───────────────────────── 加载 ───────────────────────── */

let loadTicket = 0

async function loadFamilies(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''
  permissionDenied.value = false
  generateError.value = ''
  jobResult.value = null
  generatedAt.value = null

  try {
    const result = await getPublicityFamilies(selectedYear.value, selectedMonth.value)
    if (ticket !== loadTicket) return

    report.value = result

    const list = [...(result.villages ?? [])].sort(
      (left, right) =>
        left.familyCount - right.familyCount ||
        left.village.localeCompare(right.village, 'zh-CN'),
    )
    const keepCurrent = list.some((item) => item.village === selectedVillageName.value)
    selectedVillageName.value = keepCurrent
      ? selectedVillageName.value
      : (list[0]?.village ?? '')

    // 预览接口不含近亲属标记；后台用生成口径名单补充标记，失败不影响预览。
    void enrichNearRelative(ticket, selectedYear.value, selectedMonth.value)
  } catch (error) {
    if (ticket !== loadTicket) return
    report.value = null
    selectedVillageName.value = ''
    if (error instanceof ApiError && error.status === 403) {
      permissionDenied.value = true
      loadError.value = '当前账号无档案管理权限（ARCHIVE_MANAGE），请联系管理员'
    } else {
      loadError.value = getErrorMessage(error, '公示名单加载失败')
    }
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

/**
 * 近亲属标记只做增强：按身份证匹配生成口径名单的 nearRelative，
 * 不改变预览名单成员（预览仍只展示导入库）。
 */
async function enrichNearRelative(
  ticket: number,
  year: number,
  month: number,
): Promise<void> {
  try {
    const groups = await getPublicityGroups(year, month)
    if (ticket !== loadTicket || report.value === null) return

    const flagged = new Set<string>()
    for (const village of groups.villages ?? []) {
      for (const family of village.families ?? []) {
        if (family.nearRelative && family.idCard) flagged.add(family.idCard)
      }
    }
    if (flagged.size === 0) return

    for (const village of report.value.villages ?? []) {
      for (const family of village.families ?? []) {
        if (flagged.has(family.idCard)) family.nearRelative = true
      }
    }
  } catch {
    // 增强信息失败不提示，避免干扰预览主流程。
  }
}

/* ───────────────────────── 生成公示文档 ───────────────────────── */

async function generateDocuments(): Promise<void> {
  if (generating.value) return

  generating.value = true
  generateError.value = ''
  jobResult.value = null
  generatedAt.value = null

  try {
    // 先取生成口径名单：确认有数据，并把本次生成范围显式传给任务接口。
    const groups = await getPublicityGroups(selectedYear.value, selectedMonth.value)
    const targets = groups.villages ?? []
    if (targets.length === 0) {
      ElMessage.warning('生成口径名单为空，无法生成公示文档')
      return
    }

    const result = await createPublicityJobs({
      year: selectedYear.value,
      month: selectedMonth.value,
      villages: targets.map((village) => village.village),
    })

    jobResult.value = {
      jobs: (result.jobs ?? []).map((job) => ({
        ...job,
        status: job.status || '已提交',
      })),
      outputHint: result.outputHint ?? '',
    }
    generatedAt.value = new Date()
    ElMessage.success(result.outputHint?.trim() || '公示文档生成任务已提交')
  } catch (error) {
    if (error instanceof ApiError && error.status === 403) {
      permissionDenied.value = true
      generateError.value = '当前账号无档案管理权限（ARCHIVE_MANAGE），请联系管理员'
    } else {
      generateError.value = getErrorMessage(error, '生成公示文档失败')
    }
  } finally {
    generating.value = false
  }
}

/* ───────────────────────── 年月变化 ───────────────────────── */

// 公示年月只影响生成文档标题与月度口径，不自动重查预览；清掉旧任务结果避免误读。
watch([selectedYear, selectedMonth], () => {
  generateError.value = ''
  jobResult.value = null
  generatedAt.value = null
})

onMounted(() => {
  void loadFamilies()
})
</script>

<template>
  <div class="nc-page publicity-page">
    <NcPageHeader
      title="每月公示文档输出"
      description="公示人员以导入库为准（不参考当前库）"
      kicker="救助档案 · 每月公示"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/')">返回工作台</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadFamilies()">刷新</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      class="page-alert"
      :type="permissionDenied ? 'warning' : 'error'"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button v-if="!permissionDenied" link type="primary" @click="loadFamilies()">
        重新加载
      </el-button>
    </el-alert>

    <div class="publicity-layout">
      <!-- 左栏：参数 + 按村分组 -->
      <div class="publicity-left">
        <NcCard title="公示参数">
          <div class="param-field">
            <span class="param-label">公示年月</span>
            <div class="param-controls">
              <el-select v-model="selectedYear" class="param-select" aria-label="选择年份">
                <el-option
                  v-for="year in years"
                  :key="year"
                  :label="String(year)"
                  :value="year"
                />
              </el-select>
              <span class="param-unit">年</span>
              <el-select v-model="selectedMonth" class="param-select" aria-label="选择月份">
                <el-option
                  v-for="month in months"
                  :key="month"
                  :label="String(month)"
                  :value="month"
                />
              </el-select>
              <span class="param-unit">月</span>
            </div>
          </div>

          <p class="param-note">
            数据源：农村低保 / 城市低保 / 低保边缘 / 特困供养 / 刚性支出（导入库全部在保家庭，不分月）
          </p>

          <el-button
            type="primary"
            class="block-button"
            :loading="loading"
            @click="loadFamilies()"
          >
            加载公示名单
          </el-button>

          <div v-if="report" class="summary-chip">
            <strong>共 {{ totalFamilies }} 户</strong>
            <span>/ {{ villages.length }} 个村</span>
          </div>
        </NcCard>

        <NcCard title="按村分组">
          <el-skeleton v-if="loading" :rows="5" animated />

          <p v-else-if="villages.length === 0" class="village-empty">
            点击上方「加载公示名单」查看
          </p>

          <div v-else class="village-list">
            <button
              v-for="village in villages"
              :key="village.village"
              type="button"
              class="village-item"
              :class="{ 'is-active': village.village === selectedVillageName }"
              :aria-pressed="village.village === selectedVillageName"
              @click="selectedVillageName = village.village"
            >
              {{ village.village }}（{{ village.familyCount }} 户）
            </button>
          </div>
        </NcCard>
      </div>

      <!-- 右栏：公示名单预览 + 输出 -->
      <div class="publicity-right">
        <el-alert
          class="scope-alert"
          type="warning"
          :closable="false"
          show-icon
          title="预览名单与生成名单口径不同"
        >
          预览只取 5 个导入库在保家庭；生成会合并当前库在保家庭，并排除已退出人员（月度口径）。
        </el-alert>

        <NcCard title="公示名单预览" padding="none">
          <template #actions>
            <span v-if="selectedVillage" class="village-badge">
              {{ selectedVillage.village }} 村
            </span>
          </template>

          <el-skeleton v-if="loading" class="table-skeleton" :rows="8" animated />

          <el-table
            v-else
            :data="familyRows"
            class="family-table"
            table-layout="auto"
          >
            <el-table-column label="序号" width="64" align="center">
              <template #default="{ row }">{{ row.index }}</template>
            </el-table-column>

            <el-table-column label="户主姓名" min-width="120">
              <template #default="{ row }">
                <span class="name-cell">
                  <strong>{{ row.headName || '—' }}</strong>
                  <el-tag
                    v-if="row.nearRelative"
                    size="small"
                    type="warning"
                    effect="plain"
                    class="near-tag"
                  >
                    近亲属
                  </el-tag>
                </span>
              </template>
            </el-table-column>

            <el-table-column
              label="救助类别"
              min-width="130"
              prop="category"
              show-overflow-tooltip
            />

            <el-table-column label="保障人数" width="90" align="center">
              <template #default="{ row }">{{ formatCount(row.familySize) }}</template>
            </el-table-column>

            <el-table-column label="基础保障金额" width="124" align="right">
              <template #default="{ row }">{{ formatMoney(row.baseAmount) }}</template>
            </el-table-column>

            <el-table-column label="分类施保" width="110" align="right">
              <template #default="{ row }">{{ formatMoney(row.classifiedAmount) }}</template>
            </el-table-column>

            <el-table-column label="合计金额" width="110" align="right">
              <template #default="{ row }">
                <strong class="total-amount">{{ formatMoney(row.totalAmount) }}</strong>
              </template>
            </el-table-column>

            <el-table-column label="备注" min-width="160" show-overflow-tooltip>
              <template #default="{ row }">
                <span class="remark-text">{{ row.remark || '—' }}</span>
              </template>
            </el-table-column>

            <template #empty>
              <div class="table-empty">请先在左侧选择村</div>
            </template>
          </el-table>
        </NcCard>

        <NcCard
          title="输出公示文档"
          subtitle="按村逐份生成《公共_每月公示名单》（每页 22 户，超出自动拆页）"
        >
          <div class="output-bar">
            <div class="output-copy">
              <p class="output-hint">保存到本机输出目录并打开（由本机 Agent 执行）</p>
              <p class="output-note">生成范围：全部村，按导入库 + 当前库在保合并口径输出。</p>
            </div>
            <el-button
              type="primary"
              :icon="Document"
              :loading="generating"
              :disabled="permissionDenied || loading"
              @click="generateDocuments()"
            >
              生成公示文档
            </el-button>
          </div>

          <el-alert
            v-if="generateError"
            class="output-alert"
            :type="permissionDenied ? 'warning' : 'error'"
            :title="generateError"
            :closable="false"
            show-icon
          >
            <el-button
              v-if="!permissionDenied"
              link
              type="primary"
              @click="generateDocuments()"
            >
              重新生成
            </el-button>
          </el-alert>

          <div v-if="jobResult" class="job-result">
            <div class="job-result-head">
              <span class="job-result-title">生成任务</span>
              <span v-if="generatedAt" class="job-result-time">
                提交于 {{ formatTime(generatedAt) }}
              </span>
            </div>

            <div class="job-table">
              <div class="job-row job-row--head">
                <span>任务号</span>
                <span>村</span>
                <span>页数</span>
                <span>状态</span>
              </div>
              <div
                v-for="job in jobResult.jobs"
                :key="`${job.id}-${job.village}`"
                class="job-row"
              >
                <span class="job-no">{{ job.jobNo || `#${job.id}` }}</span>
                <span class="job-village">{{ job.village }}</span>
                <span>{{ job.pageCount }} 页</span>
                <span>
                  <span class="job-status" :class="jobStatusClass(job.status)">
                    {{ jobStatusLabel(job.status) }}
                  </span>
                </span>
              </div>
            </div>

            <p v-if="outputHint" class="job-output-hint">{{ outputHint }}</p>
          </div>

          <p v-else-if="!generating" class="job-empty">
            尚未生成。点击「生成公示文档」按村提交任务。
          </p>
        </NcCard>
      </div>
    </div>
  </div>
</template>

<style scoped>
.publicity-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.page-alert,
.scope-alert {
  margin: 0;
}

/* 桌面左右两栏：左栏固定 380px（与旧版一致），右栏自适应；窄屏上下堆叠。 */
.publicity-layout {
  display: grid;
  grid-template-columns: 380px minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

.publicity-left,
.publicity-right {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}

/* ── 参数 ─────────────────────────────────────────────────────── */

.param-field {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.param-label {
  font-size: var(--nc-text-sm);
  font-weight: 600;
  color: var(--nc-muted-2);
}

.param-controls {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 8px;
}

.param-select {
  width: 100%;
}

.param-unit {
  font-size: var(--nc-text-base);
  color: var(--nc-muted-2);
}

.param-note {
  margin-top: 12px;
  font-size: var(--nc-text-sm);
  line-height: 1.7;
  color: var(--nc-muted);
}

.block-button {
  width: 100%;
  margin-top: 12px;
  margin-left: 0;
}

.summary-chip {
  display: inline-flex;
  align-items: baseline;
  gap: 8px;
  margin-top: 12px;
  padding: 3px 10px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal-soft);
  font-size: var(--nc-text-sm);
  color: var(--nc-seal-dark);
}

.summary-chip strong {
  font-weight: 700;
}

/* ── 村列表 ───────────────────────────────────────────────────── */

.village-list {
  display: flex;
  flex-direction: column;
  gap: 3px;
  max-height: 520px;
  overflow-y: auto;
}

.village-item {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid transparent;
  border-radius: var(--nc-radius-sm);
  background-color: transparent;
  font: inherit;
  font-size: var(--nc-text-base);
  line-height: 1.6;
  color: var(--nc-ink);
  text-align: left;
  cursor: pointer;
  transition:
    background-color var(--nc-transition),
    border-color var(--nc-transition),
    color var(--nc-transition);
}

.village-item:hover {
  background-color: var(--nc-paper-3);
}

.village-item:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.village-item.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
  font-weight: 600;
  color: var(--nc-seal-dark);
}

.village-empty {
  padding: 8px 0;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted);
}

/* ── 预览表格 ─────────────────────────────────────────────────── */

.village-badge {
  padding: 1px 9px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-amber-soft);
  font-size: var(--nc-text-sm);
  font-weight: 700;
  color: var(--nc-amber-dark);
  white-space: nowrap;
}

.table-skeleton {
  padding: 16px 20px;
}

.family-table {
  width: 100%;
}

.family-table :deep(.el-table__header th) {
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  font-weight: 600;
}

.family-table :deep(.el-table__cell) {
  padding: 8px 0;
}

.name-cell {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.name-cell strong {
  font-weight: 700;
  color: var(--nc-ink);
}

.near-tag {
  flex: none;
}

.total-amount {
  font-weight: 700;
  color: var(--nc-amber-dark);
}

.remark-text {
  color: var(--nc-muted-2);
}

.table-empty {
  padding: 28px 0;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted);
}

/* ── 输出 ─────────────────────────────────────────────────────── */

.output-bar {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 14px;
  align-items: center;
}

.output-copy {
  min-width: 0;
}

.output-hint {
  font-size: var(--nc-text-base);
  font-weight: 600;
  line-height: 1.6;
  color: var(--nc-amber-dark);
}

.output-note {
  margin-top: 3px;
  font-size: var(--nc-text-sm);
  line-height: 1.6;
  color: var(--nc-muted);
}

.output-bar :deep(.el-button) {
  margin-left: 0;
}

.output-alert {
  margin-top: 14px;
}

.job-result {
  margin-top: 16px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.job-result-head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 10px;
}

.job-result-title {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  color: var(--nc-ink);
}

.job-result-time {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  color: var(--nc-muted);
}

.job-table {
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  overflow: hidden;
}

.job-row {
  display: grid;
  grid-template-columns: minmax(0, 1.25fr) minmax(0, 1fr) 64px 88px;
  gap: 12px;
  align-items: center;
  padding: 8px 12px;
  border-bottom: 1px solid var(--nc-line-faint);
  font-size: var(--nc-text-sm);
  color: var(--nc-ink-2);
}

.job-row:last-child {
  border-bottom: 0;
}

.job-row--head {
  background-color: var(--nc-paper-3);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.04em;
  color: var(--nc-muted-2);
}

.job-no {
  overflow: hidden;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  color: var(--nc-ink);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.job-village {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.job-status {
  display: inline-flex;
  padding: 1px 7px;
  border-radius: var(--nc-radius-xs);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.7;
  white-space: nowrap;
}

.job-status.is-submitted {
  background-color: var(--nc-violet-soft);
  color: var(--nc-violet);
}

.job-status.is-pending {
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.job-status.is-done {
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.job-status.is-failed {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.job-output-hint {
  margin-top: 10px;
  padding: 8px 11px;
  border-left: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
  color: var(--nc-seal-dark);
  word-break: break-all;
}

.job-empty {
  margin-top: 14px;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted);
}

/* ── 响应式：窄屏上下堆叠 ─────────────────────────────────────── */

@media (max-width: 960px) {
  .publicity-layout {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 560px) {
  .output-bar {
    grid-template-columns: minmax(0, 1fr);
  }

  .output-bar :deep(.el-button) {
    width: 100%;
  }

  .job-row {
    grid-template-columns: minmax(0, 1.1fr) minmax(0, 0.9fr) 52px 78px;
    gap: 8px;
    padding: 8px 10px;
  }
}
</style>
