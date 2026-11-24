<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { Component } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Calendar,
  CircleCheck,
  Document,
  DocumentChecked,
  EditPen,
  Refresh,
  Search,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { deleteApplication, getWorkflowSummary, listWorkflow } from '@/api/applicationWorkflow'
import type { WorkflowItem, WorkflowSummary } from '@/types/applicationWorkflow'
import NcPageHeader from '@/components/NcPageHeader.vue'

interface WorkflowTab {
  index: number
  label: string
  icon: Component
  countKey: keyof WorkflowSummary
  emptyText: string
}

const router = useRouter()

const tabs: WorkflowTab[] = [
  {
    index: 0,
    label: '已提交经济核对',
    icon: Calendar,
    countKey: 'submittedAssetChecks',
    emptyText: '暂无已提交的经济核对记录',
  },
  {
    index: 1,
    label: '有报告未建档',
    icon: Document,
    countKey: 'assetChecksWithReport',
    emptyText: '暂无有报告未建档的记录',
  },
  {
    index: 2,
    label: '草稿',
    icon: EditPen,
    countKey: 'drafts',
    emptyText: '暂无草稿记录',
  },
  {
    index: 3,
    label: '已建档未提交',
    icon: DocumentChecked,
    countKey: 'archiveBuilt',
    emptyText: '暂无已建档未提交记录',
  },
  {
    index: 4,
    label: '已完结',
    icon: CircleCheck,
    countKey: 'archived',
    emptyText: '暂无已完结档案',
  },
]

const activeTab = ref(0)
const keyword = ref('')
const page = ref(1)
const pageSize = ref(20)
const items = ref<WorkflowItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')
const summary = ref<WorkflowSummary | null>(null)
const summaryError = ref('')

let requestTicket = 0

const visibleTabs = computed(() =>
  tabs.filter((tab) => tab.index > 1 || summary.value === null || summary.value.assetAccess),
)

const currentTab = computed(() => tabs.find((tab) => tab.index === activeTab.value) ?? tabs[0])
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() => 'total, sizes, prev, pager, next, jumper')

function countOf(tab: WorkflowTab): number | null {
  if (!summary.value) return null
  const value = summary.value[tab.countKey]
  return typeof value === 'number' ? value : null
}

async function loadSummary(): Promise<void> {
  summaryError.value = ''
  try {
    summary.value = await getWorkflowSummary()
    if (
      summary.value.assetAccess === false &&
      (activeTab.value === 0 || activeTab.value === 1)
    ) {
      activeTab.value = 2
      page.value = 1
      void load()
    }
  } catch (error) {
    summaryError.value = getErrorMessage(error, '阶段计数加载失败')
  }
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listWorkflow({
      tab: activeTab.value,
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
    })
    if (ticket !== requestTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    pageSize.value = response.pageSize || pageSize.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0
  } catch (error) {
    if (ticket !== requestTicket) return
    items.value = []
    totalCount.value = 0
    totalPages.value = 0
    loadError.value = getErrorMessage(error, '工作流列表加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function refresh(): void {
  void loadSummary()
  void load()
}

function selectTab(index: number): void {
  if (activeTab.value === index) return
  activeTab.value = index
  page.value = 1
  void load()
}

function handleSearch(): void {
  page.value = 1
  void load()
}

function handleReset(): void {
  keyword.value = ''
  page.value = 1
  void load()
}

function handlePageChange(value: number): void {
  page.value = value
  void load()
}

function handleSizeChange(value: number): void {
  pageSize.value = value
  page.value = 1
  void load()
}

type ActionKey = 'view' | 'photo' | 'archive' | 'changeDate' | 'edit' | 'delete'

interface CardAction {
  key: ActionKey
  label: string
  type: 'primary' | 'info' | 'warning' | 'danger' | 'default'
}

function actionsFor(item: WorkflowItem): CardAction[] {
  if (item.kind === 'asset') {
    return [
      { key: 'photo', label: '照片', type: 'info' },
      { key: 'archive', label: '档案制作', type: 'primary' },
    ]
  }

  if (activeTab.value === 2) {
    return [
      { key: 'changeDate', label: '改期', type: 'info' },
      { key: 'edit', label: '编辑', type: 'warning' },
      { key: 'delete', label: '删除', type: 'danger' },
    ]
  }

  if (activeTab.value === 3) {
    return [
      { key: 'view', label: '查看档案', type: 'primary' },
      { key: 'photo', label: '照片', type: 'info' },
      { key: 'archive', label: '档案制作', type: 'primary' },
      { key: 'changeDate', label: '改期', type: 'info' },
      { key: 'edit', label: '编辑', type: 'warning' },
      { key: 'delete', label: '删除', type: 'danger' },
    ]
  }

  return [{ key: 'view', label: '查看档案', type: 'primary' }]
}

function handleAction(action: CardAction, item: WorkflowItem): void {
  if (action.key === 'view' && item.kind === 'application') {
    void router.push({ name: 'application-detail', params: { id: item.id } })
    return
  }

  if (action.key === 'edit' && item.kind === 'application') {
    void router.push(`/applications/${item.id}/edit`)
    return
  }

  if (action.key === 'archive') {
    if (item.kind === 'asset') {
      void router.push({
        path: '/applications/new',
        query: { assetCheckId: String(item.id) },
      })
    } else {
      void router.push(`/applications/${item.id}/edit`)
    }
    return
  }

  if (action.key === 'delete' && activeTab.value === 2 && item.kind === 'application') {
    void handleDelete(item)
    return
  }

  ElMessage.info(`「${action.label}」将在下一批开放`)
}

async function handleDelete(item: WorkflowItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定删除「${item.name}」的申请草稿吗？删除后不可恢复。`,
      '删除草稿',
      {
        confirmButtonText: '删除',
        cancelButtonText: '取消',
        type: 'warning',
      },
    )
  } catch {
    return
  }

  try {
    await deleteApplication(item.id)
    ElMessage.success('草稿已删除')
    if (items.value.length === 1 && page.value > 1) {
      page.value -= 1
    }
    await Promise.all([loadSummary(), load()])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除失败'))
  }
}

function formatTime(value: string | null): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function formatAmount(value: number | null): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—'
  return `${value.toLocaleString('zh-CN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} 元`
}

function addressText(item: WorkflowItem): string {
  // 旧版展示完整家庭地址；家庭地址已含村名时不再重复拼社区名。
  return item.familyAddress || item.community
}

onMounted(() => {
  void loadSummary()
  void load()
})
</script>

<template>
  <div class="nc-page workflow-page">
    <NcPageHeader
      title="业务申请工作流"
      description="查看各阶段业务状态，办理与档案制作"
      kicker="SOCIAL ASSISTANCE / 业务申请"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/social-assistance')">返回工作台</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="refresh">刷新</el-button>
      </template>
    </NcPageHeader>

    <nav
      class="workflow-tabs"
      :style="{ gridTemplateColumns: `repeat(${visibleTabs.length}, minmax(0, 1fr))` }"
      aria-label="业务申请阶段"
    >
      <button
        v-for="tab in visibleTabs"
        :key="tab.index"
        type="button"
        class="workflow-tab"
        :class="{ 'is-active': activeTab === tab.index }"
        @click="selectTab(tab.index)"
      >
        <el-icon :size="17"><component :is="tab.icon" /></el-icon>
        <span class="workflow-tab__label">{{ tab.label }}</span>
        <span
          v-if="(countOf(tab) ?? 0) > 0"
          class="workflow-tab__count"
          :class="`tone-${tab.index}`"
        >
          {{ countOf(tab) }}
        </span>
      </button>
    </nav>

    <div v-if="summaryError" class="workflow-summary-error">{{ summaryError }}</div>

    <form class="workflow-search" @submit.prevent="handleSearch">
      <el-input
        v-model="keyword"
        class="workflow-search__input"
        clearable
        :prefix-icon="Search"
        placeholder="搜索姓名、身份证号..."
        @clear="handleSearch"
      />
      <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
        查询
      </el-button>
      <el-button v-if="keyword" text @click="handleReset">重置</el-button>
    </form>

    <el-alert
      v-if="loadError"
      class="workflow-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="load">重新加载</el-button>
    </el-alert>

    <section class="workflow-list" aria-live="polite">
      <el-skeleton v-if="loading && items.length === 0" :rows="6" animated />

      <template v-else-if="items.length > 0">
        <article
          v-for="(item, index) in items"
          :key="item.key"
          class="workflow-card"
          :class="{ 'is-alt': index % 2 === 1 }"
        >
          <header class="workflow-card__head">
            <div class="workflow-card__identity">
              <strong class="workflow-card__name">{{ item.name }}</strong>
              <span class="workflow-status" :class="`tone-${item.statusTone}`">
                {{ item.statusText }}
              </span>
              <span v-if="item.isSingleRescue" class="workflow-chip">单人保</span>
              <span v-if="item.hasGraceNote" class="workflow-chip workflow-chip--warn">
                {{ item.graceNote }}
              </span>
              <span v-if="item.hasBatchHint" class="workflow-chip">
                {{ item.batchHintText }}
              </span>
            </div>

            <div class="workflow-card__actions">
              <el-button
                v-for="action in actionsFor(item)"
                :key="action.key"
                size="small"
                :type="action.type === 'default' ? undefined : action.type"
                :plain="action.key === 'photo' || action.key === 'changeDate'"
                @click="handleAction(action, item)"
              >
                {{ action.label }}
              </el-button>
            </div>
          </header>

          <p class="workflow-card__id">{{ item.idCardMasked }}</p>

          <div class="workflow-card__meta">
            <div v-if="item.kind === 'asset'">
              <span>与户主关系</span>
              <strong>{{ item.relation || '—' }}</strong>
            </div>
            <div v-else>
              <span>编号</span>
              <strong>{{ item.applicationNo || '—' }}</strong>
            </div>
            <div>
              <span>申请添加时间</span>
              <strong>{{ formatTime(item.createdAt) }}</strong>
            </div>
            <template v-if="item.kind === 'application'">
              <div v-if="activeTab === 4">
                <span>原分类</span>
                <strong>{{ item.classificationText || '—' }}</strong>
              </div>
              <div v-if="activeTab === 4">
                <span>状态</span>
                <strong>{{ item.archiveStatusText || '—' }}</strong>
              </div>
              <div v-if="activeTab === 4">
                <span>月保障金</span>
                <strong>{{ formatAmount(item.householdMonthlyGuaranteeAmount) }}</strong>
              </div>
              <div v-else-if="item.classificationText">
                <span>原分类</span>
                <strong>{{ item.classificationText }}</strong>
              </div>
            </template>
          </div>

          <div v-if="item.kind === 'asset'" class="workflow-card__address">
            <span>家庭地址</span>
            <strong>{{ addressText(item) || '—' }}</strong>
          </div>
        </article>
      </template>

      <el-empty v-else-if="!loading && !loadError" :description="currentTab.emptyText" />

      <div v-else class="workflow-empty-error">数据加载失败，请点击上方「重新加载」。</div>
    </section>

    <footer v-if="items.length > 0" class="workflow-footer">
      <span class="workflow-footer__note">
        第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 条
      </span>
      <el-pagination
        :current-page="page"
        :page-size="pageSize"
        :page-sizes="[10, 20, 50, 100]"
        :total="totalCount"
        :layout="paginationLayout"
        :pager-count="7"
        background
        @current-change="handlePageChange"
        @size-change="handleSizeChange"
      />
    </footer>
  </div>
</template>

<style scoped>
.workflow-page {
  display: flex;
  flex-direction: column;
  gap: 0;
}

/* ── 五阶段 Tab ─────────────────────────────────────────────── */
.workflow-tabs {
  display: grid;
  gap: 8px;
  padding: 12px 16px;
  border-top: 1px solid var(--nc-line);
  border-bottom: 1px solid var(--nc-line);
  background-color: var(--nc-paper-2);
}

.workflow-tab {
  display: flex;
  align-items: center;
  gap: 7px;
  min-width: 0;
  padding: 9px 12px;
  border: 1px solid var(--nc-line);
  border-radius: 1px;
  background-color: var(--nc-paper-2);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-align: left;
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition),
    color var(--nc-transition);
}

.workflow-tab:hover {
  border-color: var(--nc-line-strong);
  background-color: var(--nc-paper-3);
}

.workflow-tab.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-weight: 700;
}

.workflow-tab__label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.workflow-tab__count {
  flex: none;
  min-width: 24px;
  margin-left: auto;
  padding: 1px 6px;
  border-radius: 999px;
  background-color: var(--nc-seal);
  color: #fff;
  font-family: var(--nc-font-mono);
  font-size: 12px;
  font-weight: 700;
  line-height: 1.6;
  text-align: center;
}

.workflow-tab__count.tone-0 {
  background-color: #3b6f8f;
}

.workflow-tab__count.tone-1,
.workflow-tab__count.tone-3 {
  background-color: var(--nc-amber-dark);
}

.workflow-tab__count.tone-2 {
  background-color: var(--nc-seal-dark);
}

.workflow-tab__count.tone-4 {
  background-color: var(--nc-pine);
}

.workflow-summary-error {
  padding: 8px 16px;
  border-bottom: 1px solid var(--nc-line);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-sm);
}

/* ── 搜索 ───────────────────────────────────────────────────── */
.workflow-search {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--nc-line);
  background-color: var(--nc-paper-2);
}

.workflow-search__input {
  flex: 1;
  min-width: 0;
}

.workflow-search__input :deep(.el-input__wrapper) {
  border-radius: 999px;
  background-color: var(--nc-paper-3);
}

.workflow-error {
  margin: 10px 16px 0;
}

/* ── 卡片列表 ───────────────────────────────────────────────── */
.workflow-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px 16px;
}

.workflow-card {
  padding: 13px 14px;
  border: 1px solid var(--nc-line);
  background-color: #fff;
  box-shadow: 0 1px 4px rgba(23, 19, 16, 0.06);
}

.workflow-card.is-alt {
  background-color: #f8f9fa;
}

.workflow-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.workflow-card__identity {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.workflow-card__name {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 18px;
  font-weight: 700;
  line-height: 1.4;
}

.workflow-status {
  display: inline-flex;
  align-items: center;
  padding: 2px 9px;
  border: 1px solid transparent;
  border-radius: 999px;
  font-size: 13px;
  line-height: 1.6;
  white-space: nowrap;
}

.workflow-status.tone-info {
  border-color: rgba(49, 87, 110, 0.2);
  background-color: #e8f0f7;
  color: #31576e;
}

.workflow-status.tone-warning {
  border-color: rgba(150, 104, 28, 0.22);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.workflow-status.tone-danger {
  border-color: rgba(171, 50, 39, 0.28);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal);
}

.workflow-status.tone-success {
  border-color: rgba(46, 71, 56, 0.2);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
}

.workflow-chip {
  display: inline-flex;
  align-items: center;
  padding: 1px 7px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: 12px;
  line-height: 1.6;
  white-space: nowrap;
}

.workflow-chip--warn {
  border-color: rgba(150, 104, 28, 0.24);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.workflow-card__actions {
  display: flex;
  flex: none;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 6px;
}

.workflow-card__id {
  margin-top: 5px;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.02em;
}

.workflow-card__meta {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 6px 16px;
  margin-top: 8px;
}

.workflow-card__meta > div,
.workflow-card__address {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.workflow-card__meta span,
.workflow-card__address span {
  color: var(--nc-muted);
  font-size: 12px;
}

.workflow-card__meta strong,
.workflow-card__address strong {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 500;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.workflow-card__address {
  margin-top: 6px;
}

.workflow-empty-error {
  padding: 36px 0;
  color: var(--nc-muted);
  text-align: center;
}

/* ── 分页 ───────────────────────────────────────────────────── */
.workflow-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 4px 16px 12px;
}

.workflow-footer__note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

@media (max-width: 1023px) {
  .workflow-tabs {
    grid-template-columns: repeat(2, minmax(0, 1fr)) !important;
  }
}

@media (max-width: 767px) {
  .workflow-search {
    flex-wrap: wrap;
  }

  .workflow-search__input {
    flex-basis: 100%;
  }

  .workflow-card__head {
    flex-direction: column;
  }

  .workflow-card__actions {
    justify-content: flex-start;
  }

  .workflow-card__meta {
    grid-template-columns: minmax(0, 1fr);
  }

  .workflow-footer {
    flex-direction: column;
    align-items: stretch;
  }
}
</style>
