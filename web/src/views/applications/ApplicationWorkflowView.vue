<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
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
import { changeApplicationDate, completeApplication } from '@/api/applicationForm'
import { deleteApplication, getWorkflowSummary, listWorkflow } from '@/api/applicationWorkflow'
import type { WorkflowItem, WorkflowSummary } from '@/types/applicationWorkflow'
import NcPageHeader from '@/components/NcPageHeader.vue'
import WorkflowPhotosDialog from './WorkflowPhotosDialog.vue'

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

/* ── 照片对话框 ────────────────────────────────────────────── */
const photosVisible = ref(false)
const photosSubject = ref('')
const photosApplicationId = ref<number | null>(null)
const photosAssetCheckId = ref<number | null>(null)

/* ── 改期对话框 ────────────────────────────────────────────── */
const changeDateVisible = ref(false)
const changeDateItem = ref<WorkflowItem | null>(null)
const newDate = ref('')
const changeDateSubmitting = ref(false)

/* ── 已建档未提交：批量归档 ────────────────────────────────── */
const selectedIds = ref<number[]>([])
const batchArchiving = ref(false)
const batchProgress = ref(0)
const batchProgressText = ref('')

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
    selectedIds.value = []
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
  selectedIds.value = []
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

type ActionKey =
  | 'nearRelative'
  | 'view'
  | 'photo'
  | 'archive'
  | 'output'
  | 'changeDate'
  | 'edit'
  | 'delete'

interface CardAction {
  key: ActionKey
  label: string
  type: 'primary' | 'info' | 'warning' | 'danger' | 'success' | 'default'
}

/**
 * 旧版 WorkflowCardItem 的显示条件（XAML 79-140 行 / WorkflowCardItem.cs）：
 * - 资产核查两 Tab：照片 + 档案制作；
 * - 草稿：近亲属备案 + 照片 + 改期 + 编辑 + 删除；
 * - 已建档未提交：近亲属备案 + 照片 + 编辑 + 出文书 + 删除；
 * - 已完结：近亲属备案 + 查看档案 + 照片。
 */
function actionsFor(item: WorkflowItem): CardAction[] {
  if (item.kind === 'asset') {
    return [
      { key: 'photo', label: '照片', type: 'info' },
      { key: 'archive', label: '档案制作', type: 'primary' },
    ]
  }

  if (activeTab.value === 2) {
    return [
      { key: 'nearRelative', label: '近亲属备案', type: 'default' },
      { key: 'photo', label: '照片', type: 'info' },
      { key: 'changeDate', label: '改期', type: 'default' },
      { key: 'edit', label: '编辑', type: 'warning' },
      { key: 'delete', label: '删除', type: 'danger' },
    ]
  }

  if (activeTab.value === 3) {
    return [
      { key: 'nearRelative', label: '近亲属备案', type: 'default' },
      { key: 'photo', label: '照片', type: 'info' },
      { key: 'edit', label: '编辑', type: 'warning' },
      { key: 'output', label: '出文书', type: 'success' },
      { key: 'delete', label: '删除', type: 'danger' },
    ]
  }

  return [
    { key: 'nearRelative', label: '近亲属备案', type: 'default' },
    { key: 'view', label: '查看档案', type: 'primary' },
    { key: 'photo', label: '照片', type: 'info' },
  ]
}

function handleAction(action: CardAction, item: WorkflowItem): void {
  if (action.key === 'nearRelative') {
    if (item.kind === 'application') {
      void router.push(`/social-assistance/near-relatives/new?applicationId=${item.id}`)
    }
    return
  }

  if (action.key === 'view') {
    if (item.kind === 'application') {
      void router.push({ name: 'application-detail', params: { id: item.id } })
    }
    return
  }

  if (action.key === 'photo') {
    openPhotos(item)
    return
  }

  if (action.key === 'archive' || action.key === 'output') {
    openArchiveProduction(item)
    return
  }

  if (action.key === 'changeDate') {
    openChangeDate(item)
    return
  }

  if (action.key === 'edit' && item.kind === 'application') {
    void router.push(`/applications/${item.id}/edit`)
    return
  }

  if (action.key === 'delete' && item.kind === 'application') {
    void handleDelete(item)
  }
}

/** 档案制作/出文书：申请卡片直达档案制作页；资产核查卡片沿用旧版“从核查发起申请”。 */
function openArchiveProduction(item: WorkflowItem): void {
  if (item.kind === 'asset') {
    void router.push({
      path: '/applications/new',
      query: { assetCheckId: String(item.id) },
    })
    return
  }

  void router.push({
    path: '/archives/production',
    query: { applicationId: String(item.id) },
  })
}

/** 照片：申请卡片按申请 ID 取（服务端解析身份证），资产核查卡片走已有核查照片接口。 */
function openPhotos(item: WorkflowItem): void {
  photosSubject.value = item.name
  photosApplicationId.value = item.kind === 'application' ? item.id : null
  photosAssetCheckId.value = item.kind === 'asset' ? item.id : null
  photosVisible.value = true
}

async function handleDelete(item: WorkflowItem): Promise<void> {
  const confirmMessage = h(
    'div',
    { style: 'white-space: pre-line; line-height: 1.7;' },
    [
      `确定删除「${item.name}」${item.applicationNo ? `（${item.applicationNo}）` : ''}的申请吗？`,
      '此操作不可恢复。',
    ].join('\n'),
  )

  try {
    await ElMessageBox.confirm(confirmMessage, '确认删除', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }

  try {
    await deleteApplication(item.id)
    ElMessage.success('申请已删除')
    if (items.value.length === 1 && page.value > 1) {
      page.value -= 1
    }
    await Promise.all([loadSummary(), load()])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除失败'))
  }
}

/* ── 改期 ─────────────────────────────────────────────────── */

function pad2(value: number): string {
  return String(value).padStart(2, '0')
}

function toDateOnly(value: string | null): string {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value.slice(0, 10)
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`
}

function isFutureDate(date: Date): boolean {
  const endOfToday = new Date()
  endOfToday.setHours(23, 59, 59, 999)
  return date.getTime() > endOfToday.getTime()
}

function openChangeDate(item: WorkflowItem): void {
  if (item.kind !== 'application') return
  changeDateItem.value = item
  newDate.value = toDateOnly(item.createdAt)
  changeDateVisible.value = true
}

async function confirmChangeDate(): Promise<void> {
  const item = changeDateItem.value
  if (!item || item.kind !== 'application') return

  const target = newDate.value.trim()
  if (!target) {
    ElMessage.warning('请选择新的申请日期')
    return
  }

  const current = toDateOnly(item.createdAt)
  if (current && target === current) {
    ElMessage.info('申请日期未变化，无需修改')
    return
  }

  const confirmMessage = h(
    'div',
    { style: 'white-space: pre-line; line-height: 1.7;' },
    [
      `申请日期：${current || '—'} → ${target}`,
      '',
      '修改后将改变该档案的公示窗口、卷号与文书上的申请日期（批次顺延提示随之重算）；已打印的文书不受影响，需重新出文书才会更新。',
      '',
      '注意：批次按入户调查日期锚定——若入户日期超出新窗口，系统会自动把它同步到「新申请日 ~ 会议日」之间，否则批次不变。',
    ].join('\n'),
  )

  try {
    await ElMessageBox.confirm(confirmMessage, '确认修改申请日期', {
      type: 'warning',
      confirmButtonText: '确认修改',
      cancelButtonText: '取消',
    })
  } catch {
    return
  }

  changeDateSubmitting.value = true
  try {
    const result = await changeApplicationDate(item.id, target)
    const syncedSurvey = result.surveyDate ? String(result.surveyDate).slice(0, 10) : ''
    ElMessage.success(
      syncedSurvey
        ? `申请日期已改为 ${target}，编号 ${result.applicationNo}；入户调查日期已同步为 ${syncedSurvey}`
        : `申请日期已改为 ${target}，编号 ${result.applicationNo}`,
    )
    changeDateVisible.value = false
    await Promise.all([loadSummary(), load()])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '修改申请日期失败'))
  } finally {
    changeDateSubmitting.value = false
  }
}

/* ── 已建档未提交：批量归档 ───────────────────────────────── */

function isSelected(id: number): boolean {
  return selectedIds.value.includes(id)
}

function toggleSelection(id: number, value: string | number | boolean): void {
  if (value === true) {
    if (!selectedIds.value.includes(id)) {
      selectedIds.value = [...selectedIds.value, id]
    }
    return
  }

  selectedIds.value = selectedIds.value.filter((itemId) => itemId !== id)
}

function selectAllBatch(): void {
  selectedIds.value = items.value
    .filter((item) => item.kind === 'application')
    .map((item) => item.id)
}

function clearBatchSelection(): void {
  selectedIds.value = []
}

async function handleBatchArchive(): Promise<void> {
  if (batchArchiving.value) return

  const targets = items.value.filter(
    (item) => item.kind === 'application' && isSelected(item.id),
  )
  if (targets.length === 0) {
    ElMessage.warning('请先勾选要归档的档案卡片')
    return
  }

  try {
    await ElMessageBox.confirm(
      `将逐户完成归档，共 ${targets.length} 户。归档后档案状态推进为「已完结」，是否继续？`,
      '确认批量归档',
      {
        confirmButtonText: '确认',
        cancelButtonText: '取消',
        type: 'warning',
      },
    )
  } catch {
    return
  }

  batchArchiving.value = true
  batchProgress.value = 0
  batchProgressText.value = ''
  const failures: string[] = []
  let successCount = 0

  try {
    // 逐条串行，避免并发归档压垮服务端；失败不中断后续条目。
    for (let index = 0; index < targets.length; index += 1) {
      const item = targets[index]
      batchProgressText.value = `正在归档 (${index + 1}/${targets.length})：${item.name}`

      try {
        await completeApplication(item.id)
        successCount += 1
        selectedIds.value = selectedIds.value.filter((id) => id !== item.id)
      } catch (error) {
        failures.push(`${item.name}：${getErrorMessage(error, '归档失败')}`)
      }

      batchProgress.value = Math.round(((index + 1) / targets.length) * 100)
    }
  } finally {
    batchArchiving.value = false
    batchProgressText.value = ''
  }

  await Promise.all([loadSummary(), load()])

  let message = `批量归档完成：成功 ${successCount} 户`
  if (failures.length > 0) {
    message +=
      `，失败 ${failures.length} 户\n\n` +
      failures.slice(0, 8).join('\n') +
      (failures.length > 8 ? `\n...等共 ${failures.length} 户` : '')
    await ElMessageBox.alert(
      h('div', { style: 'white-space: pre-line; line-height: 1.7;' }, message),
      '批量归档',
      { type: 'warning', confirmButtonText: '确定' },
    )
  } else {
    ElMessage.success(message)
  }
}

function formatTime(value: string | null): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())} ${pad2(date.getHours())}:${pad2(date.getMinutes())}`
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
      kicker="救助业务 / 业务申请"
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

    <div v-if="activeTab === 3" class="workflow-batch">
      <div class="workflow-batch__bar">
        <el-button size="small" :disabled="batchArchiving" @click="selectAllBatch">
          全选本页
        </el-button>
        <el-button size="small" :disabled="batchArchiving" @click="clearBatchSelection">
          清空选择
        </el-button>
        <span class="workflow-batch__hint">
          勾选后可批量归档（状态推进为已完结）
        </span>
        <el-progress
          v-if="batchArchiving"
          class="workflow-batch__progress"
          :percentage="batchProgress"
          :show-text="false"
          :stroke-width="8"
        />
        <el-button
          type="primary"
          size="small"
          :loading="batchArchiving"
          @click="handleBatchArchive"
        >
          批量归档
        </el-button>
      </div>
      <p v-if="batchArchiving" class="workflow-batch__text">{{ batchProgressText }}</p>
    </div>

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
              <el-checkbox
                v-if="activeTab === 3 && item.kind === 'application'"
                class="workflow-card__check"
                :model-value="isSelected(item.id)"
                :disabled="batchArchiving"
                @change="toggleSelection(item.id, $event)"
              />
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
                :plain="action.key === 'photo'"
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

    <el-dialog
      v-model="changeDateVisible"
      title="修改申请日期"
      width="min(420px, 92vw)"
      append-to-body
    >
      <p class="workflow-date__hint">
        修改申请日期会同步重算该档案的批次、公示窗口与文书日期；入户日期由服务端同步。
      </p>
      <el-date-picker
        v-model="newDate"
        class="workflow-date__picker"
        type="date"
        format="YYYY-MM-DD"
        value-format="YYYY-MM-DD"
        placeholder="选择申请日期"
        :disabled-date="isFutureDate"
        :disabled="changeDateSubmitting"
      />
      <template #footer>
        <el-button :disabled="changeDateSubmitting" @click="changeDateVisible = false">
          取消
        </el-button>
        <el-button type="primary" :loading="changeDateSubmitting" @click="confirmChangeDate">
          确认修改
        </el-button>
      </template>
    </el-dialog>

    <WorkflowPhotosDialog
      v-model="photosVisible"
      :subject="photosSubject"
      :application-id="photosApplicationId"
      :asset-check-id="photosAssetCheckId"
    />
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

/* ── 已建档未提交：批量归档工具条 ───────────────────────────── */
.workflow-batch {
  padding: 10px 16px 0;
  background-color: var(--nc-paper-2);
}

.workflow-batch__bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 9px 10px;
  background-color: #5b6770;
}

.workflow-batch__bar .el-button + .el-button {
  margin-left: 0;
}

.workflow-batch__hint {
  flex: 1;
  min-width: 160px;
  color: #fff;
  font-size: 13px;
  text-align: right;
}

.workflow-batch__progress {
  width: 110px;
  flex: none;
}

.workflow-batch__progress :deep(.el-progress-bar__outer) {
  background-color: rgba(255, 255, 255, 0.28);
}

.workflow-batch__text {
  margin: 6px 0 0;
  color: var(--nc-seal-dark);
  font-size: 13px;
}

.workflow-card__check {
  flex: none;
  margin-right: 2px;
}

/* ── 改期对话框 ─────────────────────────────────────────────── */
.workflow-date__hint {
  margin: 0 0 12px;
  color: var(--nc-muted);
  font-size: 13px;
  line-height: 1.7;
}

.workflow-date__picker {
  width: 100%;
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

  .workflow-batch__hint {
    flex-basis: 100%;
    text-align: left;
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
