<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  Document,
  FolderOpened,
  Picture,
  Printer,
  Refresh,
  Search,
  Upload,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  getMonthlyAuditOverview,
  listMonthlyAuditTasks,
  type MonthlyAuditOverview,
} from '@/api/assetMonthlyAudit'
import type { AssetCheckStatus, AssetTaskListItem } from '@/types/assetVerification'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import AssetStatusTag from './AssetStatusTag.vue'
import AssetTaskDetailDrawer from './AssetTaskDetailDrawer.vue'
import { formatDate, relationshipText, textOrDash } from './format'
import { ASSET_STATUS_OPTIONS } from './status'

const isNarrow = useMediaQuery('(max-width: 960px)')

const now = new Date()
const year = ref(now.getFullYear())
const month = ref(now.getMonth() + 1)

const yearOptions = computed(() => {
  const base = new Date().getFullYear()
  return Array.from({ length: 11 }, (_, index) => base - 5 + index)
})
const monthOptions = Array.from({ length: 12 }, (_, index) => index + 1)

const overview = ref<MonthlyAuditOverview | null>(null)
const overviewLoading = ref(false)
const overviewNote = ref('')

const keyword = ref('')
const status = ref<AssetCheckStatus | ''>('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<AssetTaskListItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const listLoading = ref(false)
const listError = ref('')

const selectedTask = ref<AssetTaskListItem | null>(null)
const drawerVisible = ref(false)
const activeTaskId = ref<number | null>(null)

let overviewTicket = 0
let listTicket = 0

const pageLoading = computed(() => listLoading.value || overviewLoading.value)
const hasFilters = computed(() => keyword.value.trim().length > 0 || status.value !== '')
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

const monthLabel = computed(() => `${year.value} 年 ${month.value} 月`)
const periodText = computed(() => {
  const lastDay = new Date(year.value, month.value, 0).getDate()
  const prefix = `${year.value}-${pad(month.value)}`
  return `统计期间：${prefix}-01 ~ ${prefix}-${pad(lastDay)}`
})

const overviewCards = computed<
  Array<{ label: string; value: string; tone: 'ink' | 'seal' | 'pine' | 'amber' }>
>(() => {
  const value = overview.value
  return [
    { label: '已提交', value: value ? String(value.submitted) : '—', tone: 'ink' },
    { label: '有报告未建档', value: value ? String(value.hasReport) : '—', tone: 'amber' },
    { label: '已建档', value: value ? String(value.archived) : '—', tone: 'pine' },
    { label: '不予认定', value: value ? String(value.rejected) : '—', tone: 'seal' },
  ]
})

const overviewTotal = computed(() =>
  overview.value ? `总计 ${overview.value.total} 户` : '总计 — 户',
)

const listSubtitle = computed(() => {
  if (listError.value) return '数据加载失败'
  if (listLoading.value && items.value.length === 0) return '正在读取月度待核对人员…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `${monthLabel.value} · 共 ${totalCount.value} 条记录${suffix}`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的待核对人员' : `${monthLabel.value}暂无待核对人员`,
)

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

async function loadOverview(): Promise<void> {
  const ticket = ++overviewTicket
  overviewLoading.value = true
  overviewNote.value = ''

  try {
    const result = await getMonthlyAuditOverview(year.value, month.value)
    if (ticket !== overviewTicket) return

    if (!result.complete) {
      overview.value = null
      overviewNote.value = `当月共 ${result.totalCount} 条任务，超过 ${result.scannedCount} 条的概览聚合上限，统计暂以“—”显示。`
      return
    }

    overview.value = result.overview
  } catch (error) {
    if (ticket !== overviewTicket) return
    overview.value = null
    overviewNote.value = getErrorMessage(error, '核对概览加载失败')
  } finally {
    if (ticket === overviewTicket) overviewLoading.value = false
  }
}

async function loadList(): Promise<void> {
  const ticket = ++listTicket
  listLoading.value = true
  listError.value = ''

  try {
    const response = await listMonthlyAuditTasks({
      year: year.value,
      month: month.value,
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      status: status.value || undefined,
    })
    if (ticket !== listTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    pageSize.value = response.pageSize || pageSize.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0

    if (selectedTask.value && !items.value.some((item) => item.id === selectedTask.value?.id)) {
      selectedTask.value = null
    }
  } catch (error) {
    if (ticket !== listTicket) return
    items.value = []
    totalCount.value = 0
    totalPages.value = 0
    selectedTask.value = null
    listError.value = getErrorMessage(error, '月度待核对人员加载失败')
  } finally {
    if (ticket === listTicket) listLoading.value = false
  }
}

function loadAll(): void {
  void loadList()
  void loadOverview()
}

function handlePeriodChange(): void {
  page.value = 1
  selectedTask.value = null
  loadAll()
}

function handleSearch(): void {
  page.value = 1
  void loadList()
}

function handleStatusChange(): void {
  page.value = 1
  selectedTask.value = null
  void loadList()
}

function handleReset(): void {
  keyword.value = ''
  status.value = ''
  page.value = 1
  selectedTask.value = null
  void loadList()
}

function handlePageChange(value: number): void {
  page.value = value
  void loadList()
}

function handleSizeChange(value: number): void {
  pageSize.value = value
  page.value = 1
  void loadList()
}

function selectTask(item: AssetTaskListItem): void {
  selectedTask.value = item
}

function openDetail(item: AssetTaskListItem): void {
  activeTaskId.value = item.id
  drawerVisible.value = true
}

onMounted(() => {
  loadAll()
})
</script>

<template>
  <div class="nc-page asset-monthly-page">
    <NcPageHeader
      title="月度审核"
      description="按月核查资产状况与核对报告。当前开放只读查询：任务名单、状态与详情可用；照片、报告保存/打印、批量上传、PDF 预览与不予认定等待接入。"
      kicker="家庭经济状况核对 · 月度审核"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="pageLoading" @click="loadAll">刷新</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      class="readonly-note"
      type="info"
      :closable="false"
      show-icon
      title="只读模式：报告上传与审核写入尚未开放，相关按钮保留位置并禁用（悬停可见“待接入”）。"
    />

    <div class="audit-layout">
      <div class="audit-main">
        <NcCard title="报表周期" subtitle="按申请日期所在年月筛选月度任务">
          <div class="period-row">
            <el-select v-model="year" class="period-select" @change="handlePeriodChange">
              <el-option
                v-for="option in yearOptions"
                :key="option"
                :label="`${option} 年`"
                :value="option"
              />
            </el-select>
            <el-select v-model="month" class="period-select" @change="handlePeriodChange">
              <el-option
                v-for="option in monthOptions"
                :key="option"
                :label="`${option} 月`"
                :value="option"
              />
            </el-select>
          </div>
          <p class="period-text">{{ periodText }}</p>
          <p class="card-note">
            Web 月度接口按申请日期所在自然月统计；旧桌面端的 A 线周期（上月 11 日 ~ 本月 10
            日）不在此页生效。
          </p>
        </NcCard>

        <NcCard title="核对概览" subtitle="按户主身份证号去重聚合，统计按户">
          <div v-loading="overviewLoading" class="overview-body">
            <div class="overview-grid">
              <NcStat
                v-for="card in overviewCards"
                :key="card.label"
                :label="card.label"
                :value="card.value"
                :tone="card.tone"
              />
            </div>

            <div class="overview-foot">
              <span class="overview-total">{{ overviewTotal }}</span>
              <span class="card-note">统计自月度任务列表聚合，与名单“按人”计数口径不同。</span>
            </div>

            <el-alert
              v-if="overviewNote"
              class="overview-note"
              type="warning"
              :title="overviewNote"
              :closable="false"
              show-icon
            >
              <el-button link type="primary" @click="loadOverview">重新统计</el-button>
            </el-alert>
          </div>
        </NcCard>

        <NcCard title="待核对人员" :subtitle="listSubtitle" padding="none" class="people-card">
          <template #actions>
            <span class="count-chip">{{ totalCount }}</span>
            <span class="pending-wrap" title="待接入">
              <el-button size="small" :icon="Picture" disabled>照片</el-button>
            </span>
            <span class="pending-wrap" title="待接入">
              <el-button size="small" disabled>保存报告</el-button>
            </span>
            <span class="pending-wrap" title="待接入">
              <el-button size="small" :icon="Printer" disabled>打印报告</el-button>
            </span>
          </template>

          <el-alert
            v-if="listError"
            class="people-error"
            type="error"
            :title="listError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadList">重新加载</el-button>
          </el-alert>

          <form class="people-toolbar" @submit.prevent="handleSearch">
            <el-input
              v-model="keyword"
              class="people-search"
              clearable
              :prefix-icon="Search"
              placeholder="搜索姓名/身份证号"
              @clear="handleSearch"
            />
            <el-select
              v-model="status"
              class="people-status"
              clearable
              placeholder="全部状态"
              @change="handleStatusChange"
            >
              <el-option
                v-for="option in ASSET_STATUS_OPTIONS"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
            <el-button type="primary" :icon="Search" native-type="submit" :loading="listLoading">
              搜索
            </el-button>
            <span class="pending-wrap" title="待接入">
              <el-button :icon="FolderOpened" disabled>打开输出目录</el-button>
            </span>
            <el-button v-if="hasFilters" text native-type="button" @click="handleReset">
              重置
            </el-button>
          </form>

          <div v-loading="listLoading && items.length > 0" class="people-list">
            <el-skeleton
              v-if="listLoading && items.length === 0"
              :rows="4"
              animated
              class="list-skeleton"
            />

            <template v-else-if="items.length > 0">
              <article
                v-for="item in items"
                :key="item.id"
                class="person-row"
                :class="{ 'is-active': selectedTask?.id === item.id }"
                role="button"
                tabindex="0"
                @click="selectTask(item)"
                @keydown.enter="selectTask(item)"
              >
                <div class="person-ident">
                  <strong class="person-name">{{ textOrDash(item.archiveName) }}</strong>
                  <span class="person-id mono">{{ textOrDash(item.idCardMasked) }}</span>
                </div>

                <div class="person-meta">
                  <span>
                    {{ relationshipText(item.relationship) }}
                    <em v-if="item.isHead" class="head-badge">户主</em>
                  </span>
                  <span class="batch">{{ textOrDash(item.batchId) }}</span>
                  <span>申请 {{ formatDate(item.applicationDate) }}</span>
                </div>

                <div class="person-status">
                  <AssetStatusTag :status="item.status" :status-text="item.statusText" />
                </div>

                <div class="person-actions">
                  <span
                    v-if="item.status === '1'"
                    class="pending-wrap"
                    title="待接入"
                  >
                    <el-button size="small" disabled>不予认定</el-button>
                  </span>
                  <el-button link type="primary" @click.stop="openDetail(item)">详情</el-button>
                </div>
              </article>
            </template>

            <div v-else-if="!listError" class="empty-wrap">
              <el-empty :description="emptyDescription">
                <el-button v-if="hasFilters" :icon="Refresh" @click="handleReset">
                  清除筛选
                </el-button>
              </el-empty>
            </div>
          </div>

          <template #footer>
            <div v-if="items.length > 0" class="pagination">
              <span class="pagination-note">
                {{ monthLabel }} · 第 {{ page }} / {{ displayTotalPages }} 页 · 共
                {{ totalCount }} 条
              </span>
              <el-pagination
                :current-page="page"
                :page-size="pageSize"
                :page-sizes="[10, 20, 50]"
                :total="totalCount"
                :layout="paginationLayout"
                :pager-count="isNarrow ? 5 : 7"
                :small="isNarrow"
                background
                @current-change="handlePageChange"
                @size-change="handleSizeChange"
              />
            </div>
          </template>
        </NcCard>

        <NcCard title="报告上传" subtitle="批量上传核查报告 PDF（待接入）">
          <div class="upload-stats">
            <div class="upload-stat">
              <span class="upload-label">待上传</span>
              <strong class="upload-value">—</strong>
              <span class="card-note">上传统计待接入</span>
            </div>
            <div class="upload-stat upload-stat--danger">
              <span class="upload-label">失败</span>
              <strong class="upload-value">—</strong>
              <span class="card-note">上传统计待接入</span>
            </div>
          </div>

          <span class="pending-wrap pending-block" title="待接入">
            <el-button type="primary" class="upload-button" :icon="Upload" disabled>
              选择文件夹并批量上传
            </el-button>
          </span>

          <div class="upload-options">
            <el-checkbox disabled>包含子文件夹</el-checkbox>
            <span class="pending-wrap" title="待接入">
              <el-button size="small" disabled>取消上传</el-button>
            </span>
          </div>

          <div class="upload-progress" title="待接入">
            <div
              class="upload-progress-track"
              role="progressbar"
              aria-label="上传进度"
              aria-valuemin="0"
              aria-valuemax="100"
              aria-valuenow="0"
            >
              <span class="upload-progress-fill" />
            </div>
            <span class="card-note">上传进度待接入</span>
          </div>

          <p class="card-note upload-footnote">
            后端尚未提供报告上传、进度与取消接口，按钮与进度条仅保留位置。
          </p>
        </NcCard>

        <NcCard v-if="selectedTask" title="比对信息">
          <p class="match-line">
            申请人：<strong>{{ textOrDash(selectedTask.archiveName) }}</strong>
            <span class="match-divider">|</span>
            身份证：<span class="mono">{{ textOrDash(selectedTask.idCardMasked) }}</span>
          </p>
          <p class="card-note">
            批次 {{ textOrDash(selectedTask.batchId) }} · 申请日期
            {{ formatDate(selectedTask.applicationDate) }} · 状态
            {{ textOrDash(selectedTask.statusText) }}
          </p>
        </NcCard>
      </div>

      <aside class="audit-preview">
        <NcCard
          title="报告预览"
          subtitle="选中待核对人员后查看其核查报告 PDF"
          class="preview-card"
        >
          <div v-if="!selectedTask" class="preview-empty">
            <el-icon :size="34"><Document /></el-icon>
            <p>选择左侧人员后显示 PDF 预览</p>
          </div>

          <div v-else class="preview-placeholder">
            <span class="preview-badge" aria-hidden="true">PDF</span>
            <strong class="preview-title">
              {{ textOrDash(selectedTask.archiveName) }} · 核查报告
            </strong>
            <p class="preview-note">
              PDF 预览接口尚未接入，当前无法在页面内查看或打印报告原件。
            </p>
            <span class="pending-wrap" title="待接入">
              <el-button :icon="Printer" disabled>打印旧版本</el-button>
            </span>
          </div>
        </NcCard>

        <NcCard title="报告版本（只读）" subtitle="查看与打印历史核查报告版本">
          <div class="versions-empty">暂无报告版本数据（待接入）</div>
          <div class="versions-foot">
            <span class="card-note">版本列表与历史版本预览接口待接入。</span>
            <span class="pending-wrap" title="待接入">
              <el-button size="small" :icon="Printer" disabled>打印此版本</el-button>
            </span>
          </div>
        </NcCard>
      </aside>
    </div>

    <AssetTaskDetailDrawer v-model="drawerVisible" :task-id="activeTaskId" />
  </div>
</template>

<style scoped>
.asset-monthly-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.readonly-note {
  margin: 0;
}

.audit-layout {
  display: grid;
  grid-template-columns: minmax(0, 430px) minmax(0, 1fr);
  align-items: start;
  gap: 16px;
}

.audit-main,
.audit-preview {
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: 15px;
}

.audit-preview {
  position: sticky;
  top: 12px;
}

.period-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.period-select {
  width: 132px;
}

.period-text {
  margin-top: 10px;
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.02em;
}

.overview-body {
  min-height: 120px;
}

.overview-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.overview-foot {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 3px;
  margin-top: 12px;
  text-align: right;
}

.overview-total {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.overview-note {
  margin-top: 10px;
}

.card-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.people-card {
  overflow: hidden;
}

.people-card :deep(.nc-card__head) {
  flex-wrap: wrap;
  gap: 10px;
}

.people-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.count-chip {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 28px;
  height: 22px;
  padding: 0 8px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
}

.pending-wrap {
  display: inline-flex;
}

.people-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 14px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.people-error {
  margin: 12px 16px 0;
}

.people-search {
  flex: 1 1 220px;
  max-width: 340px;
}

.people-status {
  width: 160px;
}

.people-list {
  display: flex;
  flex-direction: column;
  gap: 7px;
  min-height: 150px;
  padding: 12px 16px 14px;
}

.list-skeleton {
  padding: 8px 2px;
}

.person-row {
  display: grid;
  grid-template-columns: minmax(0, 1.25fr) minmax(0, 1.1fr) auto auto;
  grid-template-areas: 'ident meta status actions';
  align-items: center;
  gap: 10px;
  min-width: 0;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.person-row:hover {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-xs);
}

.person-row:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.person-row.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-paper-2);
  box-shadow: inset 3px 0 0 var(--nc-seal);
}

.person-ident {
  display: flex;
  grid-area: ident;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.person-name {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.02em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.person-id {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.person-meta {
  display: flex;
  grid-area: meta;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.person-meta .batch {
  overflow: hidden;
  font-family: var(--nc-font-mono);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.head-badge {
  margin-left: 4px;
  padding: 0 4px;
  border: 1px solid rgba(171, 50, 39, 0.42);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-style: normal;
  line-height: 1.5;
}

.person-status {
  grid-area: status;
}

.person-actions {
  display: flex;
  grid-area: actions;
  align-items: center;
  gap: 4px;
}

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.empty-wrap {
  padding: 8px 2px;
}

.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.pagination-note {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.upload-stats {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.upload-stat {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  padding: 11px 14px;
  border: 1px solid var(--nc-line-faint);
  border-left: 3px solid var(--nc-amber);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-amber-soft);
}

.upload-stat--danger {
  border-left-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
}

.upload-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.upload-stat--danger .upload-label {
  color: var(--nc-seal-dark);
}

.upload-value {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 26px;
  line-height: 1.2;
}

.pending-block {
  display: flex;
  margin-top: 12px;
}

.upload-button {
  width: 100%;
}

.upload-options {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 8px;
}

.upload-progress {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 10px;
  opacity: 0.65;
}

.upload-progress-track {
  flex: 1 1 auto;
  height: 10px;
  overflow: hidden;
  border-radius: 999px;
  background-color: var(--nc-paper-3);
}

.upload-progress-fill {
  display: block;
  width: 0;
  height: 100%;
  background-color: var(--nc-amber);
}

.upload-footnote {
  margin-top: 10px;
}

.match-line {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-md);
  line-height: 1.8;
}

.match-line strong {
  color: var(--nc-ink);
}

.match-divider {
  margin: 0 8px;
  color: var(--nc-line-strong);
}

.preview-card :deep(.nc-card__body) {
  min-height: 280px;
}

.preview-empty,
.preview-placeholder {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 10px;
  min-height: 280px;
  padding: 24px 18px;
  text-align: center;
}

.preview-empty {
  color: var(--nc-muted);
}

.preview-empty .el-icon {
  color: var(--nc-muted-2);
}

.preview-empty p {
  font-size: var(--nc-text-sm);
}

.preview-placeholder {
  border: 1px dashed var(--nc-line-strong);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.preview-badge {
  display: grid;
  place-items: center;
  width: 54px;
  height: 64px;
  border: 1px solid var(--nc-line-strong);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  letter-spacing: 0.12em;
}

.preview-title {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.02em;
}

.preview-note {
  max-width: 340px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.versions-empty {
  padding: 18px 14px;
  border: 1px dashed var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.versions-foot {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 10px;
}

@media (max-width: 1080px) {
  .audit-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .audit-preview {
    position: static;
  }
}

@media (max-width: 767px) {
  .period-select {
    flex: 1 1 calc(50% - 5px);
    width: auto;
  }

  .overview-grid,
  .upload-stats {
    gap: 8px;
  }

  .people-toolbar {
    padding: 12px;
  }

  .people-search {
    flex: 1 1 100%;
    max-width: none;
  }

  .people-status {
    flex: 1 1 calc(50% - 4px);
    width: auto;
  }

  .people-toolbar .el-button {
    flex: 0 0 auto;
  }

  .people-list {
    padding: 10px 12px 12px;
  }

  .person-row {
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas:
      'ident status'
      'meta meta'
      'actions actions';
    gap: 6px 10px;
  }

  .person-actions {
    justify-content: flex-end;
    padding-top: 4px;
    border-top: 1px solid var(--nc-line-faint);
  }

  .pagination {
    justify-content: center;
    padding: 12px;
  }

  .pagination-note {
    width: 100%;
    text-align: center;
  }

  .preview-card :deep(.nc-card__body),
  .preview-empty,
  .preview-placeholder {
    min-height: 200px;
  }
}
</style>
