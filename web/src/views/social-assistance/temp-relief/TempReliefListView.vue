<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Clock, CircleCheck, Plus, Search, Warning } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  confirmTempReliefApplication,
  deleteTempReliefApplication,
  fileTempReliefApplication,
  getTempReliefSummary,
  listTempReliefApplications,
  rejectTempReliefApplication,
  returnTempReliefApplication,
  stopTempReliefApplication,
  submitTempReliefApplication,
} from '@/api/tempRelief'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  TempReliefListItem,
  TempReliefPaymentChannel,
  TempReliefPopulationStatus,
  TempReliefReliefType,
  TempReliefStatus,
  TempReliefSummary,
} from '@/types/tempRelief'
import TempReliefListPhotoDialog from './TempReliefListPhotoDialog.vue'
import TempReliefPrintDialog from './TempReliefPrintDialog.vue'

type PopulationFilter = '' | TempReliefPopulationStatus
type CategoryFilter = '' | 'large-county' | 'large-reserve' | 'small'
type ButtonTone = 'primary' | 'success' | 'warning' | 'danger' | 'info'
type ActionKey =
  | 'view'
  | 'edit'
  | 'submit'
  | 'return'
  | 'reject'
  | 'confirm'
  | 'file'
  | 'stop'
  | 'delete'
  | 'photo'
  | 'reprint'

interface RowAction {
  key: ActionKey
  label: string
  type: ButtonTone
  plain?: boolean
}

interface StatusTabDefinition {
  value: number
  label: string
  statuses: string
  emptyText: string
  pageSize: number
}

const STATUS_TABS: StatusTabDefinition[] = [
  {
    value: 0,
    label: '办理中',
    statuses: 'Draft,Submitted,Returned',
    emptyText: '暂无办理中记录',
    pageSize: 20,
  },
  {
    value: 1,
    label: '已确认',
    statuses: 'Confirmed',
    emptyText: '暂无已确认记录',
    pageSize: 10,
  },
  {
    value: 2,
    label: '已终结',
    statuses: 'Stopped,Rejected',
    emptyText: '暂无已终结记录',
    pageSize: 20,
  },
]

const POPULATION_OPTIONS: Array<{ value: PopulationFilter; label: string }> = [
  { value: '', label: '全部人群' },
  { value: 'Insured', label: '在保障人群内' },
  { value: 'Uninsured', label: '不在保障人群内' },
]

const CATEGORY_OPTIONS: Array<{ value: CategoryFilter; label: string }> = [
  { value: '', label: '全部小类' },
  { value: 'large-county', label: '常规·上级发放' },
  { value: 'large-reserve', label: '常规·备用金发放' },
  { value: 'small', label: '小额快救' },
]

const STATUS_TEXT_FALLBACK: Record<TempReliefStatus, string> = {
  Draft: '草稿',
  Submitted: '已提交',
  Confirmed: '已确认',
  Returned: '退回补正',
  Rejected: '不予救助',
  Stopped: '已终止',
}

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

/** PC 默认选中第 2 个 Tab「已确认」。 */
const activeTab = ref(1)
const keyword = ref('')
const populationFilter = ref<PopulationFilter>('')
const categoryFilter = ref<CategoryFilter>('')

const page = ref(1)
const items = ref<TempReliefListItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')

const counts = ref<{ draft: number | null; confirmed: number | null; stopped: number | null }>({
  draft: null,
  confirmed: null,
  stopped: null,
})
const summary = ref<TempReliefSummary | null>(null)

const photoVisible = ref(false)
const photoTarget = ref<TempReliefListItem | null>(null)
const printVisible = ref(false)
const printTarget = ref<TempReliefListItem | null>(null)

let requestTicket = 0
let countsTicket = 0

const currentTab = computed(() => STATUS_TABS[activeTab.value] ?? STATUS_TABS[0])
const pageSize = computed(() => currentTab.value.pageSize)
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = 'prev, pager, next'
const tabCounts = computed<number[]>(() => [
  counts.value.draft ?? 0,
  counts.value.confirmed ?? 0,
  counts.value.stopped ?? 0,
])

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const text = value.slice(0, 10)
  return /^\d{4}-\d{2}-\d{2}$/.test(text) ? text : '—'
}

function formatInt(value: number): string {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 0 }).format(value)
}

function statText(value: number | null | undefined): string | number {
  return value === null || value === undefined ? '—' : formatInt(value)
}

/** 对齐 PC ConfirmAmountDisplay：`{0:F2}元`，空值不伪造 0。 */
function confirmAmountText(row: TempReliefListItem): string {
  return row.confirmAmount === null || row.confirmAmount === undefined
    ? ''
    : `${row.confirmAmount.toFixed(2)}元`
}

function statusText(row: TempReliefListItem): string {
  return row.statusText || STATUS_TEXT_FALLBACK[row.status] || row.status
}

function deadlineText(row: TempReliefListItem): string {
  return (row.deadlineText ?? '').trim()
}

function hasFlag(value: boolean | undefined): value is boolean {
  return typeof value === 'boolean'
}

function canSubmitRow(row: TempReliefListItem): boolean {
  return hasFlag(row.canSubmit)
    ? row.canSubmit
    : row.status === 'Draft' || row.status === 'Returned'
}

function canConfirmRow(row: TempReliefListItem): boolean {
  return hasFlag(row.canConfirm)
    ? row.canConfirm
    : row.status === 'Draft' || row.status === 'Submitted'
}

function canReturnRow(row: TempReliefListItem): boolean {
  return hasFlag(row.canReturn) ? row.canReturn : row.status === 'Submitted'
}

function canRejectRow(row: TempReliefListItem): boolean {
  return hasFlag(row.canReject)
    ? row.canReject
    : row.status === 'Draft' || row.status === 'Submitted'
}

function canEditRow(row: TempReliefListItem): boolean {
  return row.status === 'Draft' || row.status === 'Returned'
}

function canDeleteRow(row: TempReliefListItem): boolean {
  return row.status === 'Draft'
}

function canMarkFiledRow(row: TempReliefListItem): boolean {
  if (hasFlag(row.canMarkFiled)) return row.canMarkFiled
  if (hasFlag(row.canFile)) return row.canFile
  return (
    row.status === 'Confirmed' &&
    row.paymentChannel === 'County' &&
    (row.filingStatus === null || row.filingStatus === undefined || row.filingStatus === 'Pending')
  )
}

/** 按钮集合严格按 PC 的 Tab / 状态顺序；不可用动作直接不渲染。 */
function actionsFor(row: TempReliefListItem): RowAction[] {
  if (activeTab.value === 1) {
    const actions: RowAction[] = [
      { key: 'view', label: '查看档案', type: 'primary' },
      { key: 'photo', label: '照片', type: 'info', plain: true },
      { key: 'reprint', label: '补打', type: 'info' },
    ]
    if (canMarkFiledRow(row)) actions.push({ key: 'file', label: '备案', type: 'success' })
    actions.push({ key: 'stop', label: '终止', type: 'warning' })
    return actions
  }

  if (activeTab.value === 2) {
    return [
      { key: 'view', label: '查看档案', type: 'primary' },
      { key: 'photo', label: '照片', type: 'info', plain: true },
      { key: 'reprint', label: '补打', type: 'info' },
    ]
  }

  const actions: RowAction[] = []
  if (canSubmitRow(row)) actions.push({ key: 'submit', label: '提交', type: 'primary' })
  if (canConfirmRow(row)) actions.push({ key: 'confirm', label: '确认', type: 'success' })
  if (canReturnRow(row)) actions.push({ key: 'return', label: '退回', type: 'warning' })
  if (canRejectRow(row)) actions.push({ key: 'reject', label: '不予', type: 'danger' })
  if (canEditRow(row)) actions.push({ key: 'edit', label: '编辑', type: 'warning' })
  if (canDeleteRow(row)) actions.push({ key: 'delete', label: '删除', type: 'danger' })
  return actions
}

function resolveCategoryFilter(): {
  reliefType?: TempReliefReliefType
  paymentChannel?: TempReliefPaymentChannel
} {
  switch (categoryFilter.value) {
    case 'large-county':
      return { reliefType: 'Large', paymentChannel: 'County' }
    case 'large-reserve':
      return { reliefType: 'Large', paymentChannel: 'Reserve' }
    case 'small':
      return { reliefType: 'Small', paymentChannel: 'Reserve' }
    default:
      return {}
  }
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const category = resolveCategoryFilter()
    const response = await listTempReliefApplications({
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      status: currentTab.value.statuses,
      populationStatus: populationFilter.value || undefined,
      reliefType: category.reliefType,
      paymentChannel: category.paymentChannel,
    })
    if (ticket !== requestTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0
  } catch (error) {
    if (ticket !== requestTicket) return
    items.value = []
    totalCount.value = 0
    totalPages.value = 0
    loadError.value = getErrorMessage(error, '临时救助列表加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

/** Tab 计数与 PC LoadAllCountsAsync 同口径：按当前人群 / 小类筛选，关键字不参与。 */
async function loadCounts(): Promise<void> {
  const ticket = ++countsTicket
  const category = resolveCategoryFilter()
  const base = {
    page: 1,
    pageSize: 1,
    populationStatus: populationFilter.value || undefined,
    reliefType: category.reliefType,
    paymentChannel: category.paymentChannel,
  }

  const [draft, confirmed, stopped] = await Promise.allSettled([
    listTempReliefApplications({ ...base, status: 'Draft,Submitted,Returned' }),
    listTempReliefApplications({ ...base, status: 'Confirmed' }),
    listTempReliefApplications({ ...base, status: 'Stopped,Rejected' }),
  ])
  if (ticket !== countsTicket) return

  counts.value = {
    draft: draft.status === 'fulfilled' ? (draft.value.totalCount ?? null) : null,
    confirmed: confirmed.status === 'fulfilled' ? (confirmed.value.totalCount ?? null) : null,
    stopped: stopped.status === 'fulfilled' ? (stopped.value.totalCount ?? null) : null,
  }
}

/** 页头「本年确认 / 本年确认金额」；失败保持 null 显示「—」。 */
async function loadSummary(): Promise<void> {
  try {
    summary.value = await getTempReliefSummary()
  } catch {
    summary.value = null
  }
}

async function refreshAll(): Promise<void> {
  await Promise.all([load(), loadCounts(), loadSummary()])
}

function switchTab(next: number): void {
  if (activeTab.value === next) return
  activeTab.value = next
  keyword.value = ''
  page.value = 1
  items.value = []
  totalCount.value = 0
  totalPages.value = 0
  void load()
}

function handleFilterChange(): void {
  page.value = 1
  void Promise.all([load(), loadCounts()])
}

function handleSearch(): void {
  page.value = 1
  void load()
}

function handlePageChange(value: number): void {
  page.value = value
  void load()
}

function handleCreate(): void {
  void router.push('/social-assistance/temp-relief/new')
}

function openView(row: TempReliefListItem): void {
  void router.push(`/social-assistance/temp-relief/${row.id}/edit?mode=view`)
}

function openEdit(row: TempReliefListItem): void {
  void router.push(`/social-assistance/temp-relief/${row.id}/edit`)
}

function openPhotos(row: TempReliefListItem): void {
  photoTarget.value = row
  photoVisible.value = true
}

function openReprint(row: TempReliefListItem): void {
  printTarget.value = row
  printVisible.value = true
}

function isDialogCancel(error: unknown): boolean {
  return error === 'cancel' || error === 'close'
}

async function askConfirm(
  message: string,
  title: string,
  confirmText = '确定',
  type: 'warning' | 'info' = 'warning',
): Promise<boolean> {
  try {
    await ElMessageBox.confirm(message, title, {
      confirmButtonText: confirmText,
      cancelButtonText: '取消',
      type,
    })
    return true
  } catch {
    return false
  }
}

async function handleSubmit(row: TempReliefListItem): Promise<void> {
  const confirmed = await askConfirm(
    `确定提交 [${row.applicationNo}] ${row.applicantName} 的临时救助申请吗？\n提交后进入办理时限。`,
    '提交申请',
    '提交',
  )
  if (!confirmed) return

  try {
    await submitTempReliefApplication(row.id)
    ElMessage.success('临时救助申请已提交')
    await refreshAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '提交申请失败'))
  }
}

async function handleConfirm(row: TempReliefListItem): Promise<void> {
  const confirmed = await askConfirm(
    `确定要确认 [${row.applicationNo}] ${row.applicantName} 的临时救助申请生效吗？`,
    '确认生效',
    '确认',
  )
  if (!confirmed) return

  try {
    await confirmTempReliefApplication(row.id)
    ElMessage.success('临时救助申请已确认生效')
    await refreshAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '确认生效失败'))
  }
}

async function handleReturn(row: TempReliefListItem): Promise<void> {
  let reason = ''
  try {
    const result = await ElMessageBox.prompt('请填写退回原因：', '退回补正', {
      confirmButtonText: '确定',
      cancelButtonText: '取消',
      inputPlaceholder: '如：材料不全/信息需核实等',
      inputValidator: (value: string) => Boolean(value && value.trim()) || '请填写退回原因',
      type: 'warning',
    })
    reason = result.value.trim()
  } catch (error) {
    if (isDialogCancel(error)) return
    ElMessage.error(getErrorMessage(error, '退回补正失败'))
    return
  }

  try {
    await returnTempReliefApplication(row.id, reason)
    ElMessage.success('已退回补正')
    await refreshAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '退回补正失败'))
  }
}

async function handleReject(row: TempReliefListItem): Promise<void> {
  let reason = ''
  try {
    const result = await ElMessageBox.prompt('请填写不予救助理由：', '不予救助', {
      confirmButtonText: '确定',
      cancelButtonText: '取消',
      inputPlaceholder: '如：不符合救助条件等',
      inputValidator: (value: string) => Boolean(value && value.trim()) || '请填写不予救助理由',
      type: 'warning',
    })
    reason = result.value.trim()
  } catch (error) {
    if (isDialogCancel(error)) return
    ElMessage.error(getErrorMessage(error, '登记不予救助失败'))
    return
  }

  try {
    await rejectTempReliefApplication(row.id, {
      reason,
      notifiedAt: new Date().toISOString(),
      noticeMethod: '书面',
    })
    ElMessage.success('已登记不予救助')
    await refreshAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '登记不予救助失败'))
  }
}

async function handleFile(row: TempReliefListItem): Promise<void> {
  const confirmed = await askConfirm(
    `确定 [${row.applicationNo}] ${row.applicantName} 已完成备案登记吗？`,
    '登记备案',
    '已备案',
    'info',
  )
  if (!confirmed) return

  try {
    await fileTempReliefApplication(row.id)
    ElMessage.success('已登记备案')
    await refreshAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '登记备案失败'))
  }
}

async function handleStop(row: TempReliefListItem): Promise<void> {
  const confirmed = await askConfirm(
    `确定要终止 [${row.applicationNo}] ${row.applicantName} 的临时救助申请吗？\n终止后本年度仍占用唯一申请名额。`,
    '终止申请',
    '终止',
  )
  if (!confirmed) return

  let reason = ''
  try {
    const result = await ElMessageBox.prompt('请填写终止原因：', '终止原因', {
      confirmButtonText: '确定',
      cancelButtonText: '取消',
      inputPlaceholder: '如：信息录入错误/不符合救助条件等',
      inputValidator: (value: string) => Boolean(value && value.trim()) || '请填写终止原因',
      type: 'warning',
    })
    reason = result.value.trim()
  } catch (error) {
    if (isDialogCancel(error)) return
    ElMessage.error(getErrorMessage(error, '终止申请失败'))
    return
  }

  try {
    await stopTempReliefApplication(row.id, reason)
    ElMessage.success('临时救助申请已终止')
    await refreshAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '终止申请失败'))
  }
}

async function handleDelete(row: TempReliefListItem): Promise<void> {
  const confirmed = await askConfirm(
    `确定要删除申请 [${row.applicationNo}] ${row.applicantName} 吗？\n此操作不可恢复。`,
    '确认删除',
    '删除',
  )
  if (!confirmed) return

  try {
    await deleteTempReliefApplication(row.id)
    ElMessage.success('临时救助申请已删除')
    await refreshAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除申请失败'))
  }
}

function runAction(key: ActionKey, row: TempReliefListItem): void {
  switch (key) {
    case 'view':
      openView(row)
      return
    case 'edit':
      openEdit(row)
      return
    case 'submit':
      void handleSubmit(row)
      return
    case 'confirm':
      void handleConfirm(row)
      return
    case 'return':
      void handleReturn(row)
      return
    case 'reject':
      void handleReject(row)
      return
    case 'file':
      void handleFile(row)
      return
    case 'stop':
      void handleStop(row)
      return
    case 'delete':
      void handleDelete(row)
      return
    case 'photo':
      openPhotos(row)
      return
    case 'reprint':
      openReprint(row)
  }
}

onMounted(() => {
  void refreshAll()
})
</script>

<template>
  <div class="nc-page temp-relief-list-page">
    <NcPageHeader title="临时救助" description="急难型 / 支出型临时救助申请办理">
      <div class="header-stats">
        <NcStat label="办理中（件）" :value="statText(counts.draft)" tone="amber" compact />
        <NcStat label="已确认累计（件）" :value="statText(counts.confirmed)" tone="pine" compact />
        <NcStat
          label="本年确认（人次）"
          :value="statText(summary?.yearConfirmed)"
          tone="seal"
          compact
        />
        <NcStat
          label="本年确认金额（元）"
          :value="statText(summary?.yearConfirmedAmount)"
          tone="ink"
          compact
        />
      </div>

      <template #actions>
        <el-button type="primary" :icon="Plus" @click="handleCreate">新增申请</el-button>
      </template>
    </NcPageHeader>

    <NcCard padding="none" class="list-card">
      <div class="status-tabs" role="tablist" aria-label="按办理状态筛选">
        <button
          v-for="tab in STATUS_TABS"
          :key="tab.value"
          type="button"
          role="tab"
          class="status-tab"
          :class="{ 'is-active': activeTab === tab.value }"
          :aria-selected="activeTab === tab.value"
          @click="switchTab(tab.value)"
        >
          <el-icon class="status-tab__icon">
            <Clock v-if="tab.value === 0" />
            <CircleCheck v-else-if="tab.value === 1" />
            <Warning v-else />
          </el-icon>
          <span>{{ tab.label }}</span>
          <span
            v-if="tabCounts[tab.value] > 0"
            class="status-tab__count"
            :class="`status-tab__count--${tab.value}`"
          >
            {{ tabCounts[tab.value] }}
          </span>
        </button>
      </div>

      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-select
          v-model="populationFilter"
          aria-label="人群筛选"
          placeholder="全部人群"
          @change="handleFilterChange"
        >
          <el-option
            v-for="option in POPULATION_OPTIONS"
            :key="option.value || 'all'"
            :label="option.label"
            :value="option.value"
          />
        </el-select>

        <el-select
          v-model="categoryFilter"
          aria-label="小类筛选"
          placeholder="全部小类"
          @change="handleFilterChange"
        >
          <el-option
            v-for="option in CATEGORY_OPTIONS"
            :key="option.value || 'all'"
            :label="option.label"
            :value="option.value"
          />
        </el-select>

        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索姓名、身份证号..."
          @clear="handleSearch"
        />

        <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
          查询
        </el-button>
      </form>

      <el-alert
        v-if="loadError"
        class="load-error"
        type="error"
        :title="loadError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="load()">重新加载</el-button>
      </el-alert>

      <div v-loading="loading && items.length > 0" class="list-area">
        <el-skeleton v-if="loading && items.length === 0" :rows="5" animated class="list-skeleton" />

        <div v-else-if="items.length > 0" class="relief-cards">
          <article
            v-for="(row, index) in items"
            :key="row.id"
            class="relief-card"
            :class="{ 'is-alt': index % 2 === 1 }"
          >
            <header class="card-head">
              <div class="card-person">
                <strong class="card-name">{{ row.applicantName || '—' }}</strong>
                <span
                  class="status-chip"
                  :class="activeTab === 1 ? 'is-success' : 'is-danger'"
                >
                  {{ statusText(row) }}
                </span>
              </div>

              <div class="card-actions">
                <el-button
                  v-for="action in actionsFor(row)"
                  :key="action.key"
                  size="small"
                  :type="action.type"
                  :plain="action.plain"
                  @click="runAction(action.key, row)"
                >
                  {{ action.label }}
                </el-button>
              </div>
            </header>

            <p class="card-id-card">{{ row.applicantIdCardMasked || '—' }}</p>

            <div class="card-lines">
              <span class="card-no">编号: {{ row.applicationNo || '—' }}</span>
              <span
                v-if="deadlineText(row)"
                class="card-deadline"
                :class="{ 'is-overdue': row.isOverdue }"
              >
                {{ deadlineText(row) }}
              </span>
            </div>

            <dl class="card-meta" :class="`card-meta--tab${activeTab}`">
              <div>
                <dt>救助类型</dt>
                <dd :title="row.categoryDisplayName">{{ row.categoryDisplayName || '—' }}</dd>
              </div>
              <div>
                <dt>申请日期</dt>
                <dd>{{ formatDate(row.applyDate) }}</dd>
              </div>

              <template v-if="activeTab === 1">
                <div>
                  <dt>确定金额</dt>
                  <dd class="amount">{{ confirmAmountText(row) || '—' }}</dd>
                </div>
              </template>

              <template v-else-if="activeTab === 2">
                <div>
                  <dt>终止时间</dt>
                  <dd>{{ formatDate(row.stoppedAt) }}</dd>
                </div>
                <div class="card-meta__wide">
                  <dt>终止原因</dt>
                  <dd :title="row.stopReason">{{ row.stopReason || '—' }}</dd>
                </div>
              </template>
            </dl>
          </article>
        </div>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="currentTab.emptyText" />
        </div>
      </div>

      <template #footer>
        <div v-if="totalCount > 0" class="pagination">
          <span class="pagination-note">
            第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 条
          </span>
          <el-pagination
            :current-page="page"
            :page-size="pageSize"
            :total="totalCount"
            :layout="paginationLayout"
            :pager-count="isNarrow ? 5 : 7"
            :small="isNarrow"
            background
            @current-change="handlePageChange"
          />
        </div>
      </template>
    </NcCard>

    <TempReliefListPhotoDialog
      v-model="photoVisible"
      :application-id="photoTarget?.id ?? null"
      :applicant-name="photoTarget?.applicantName ?? ''"
    />

    <TempReliefPrintDialog
      v-model="printVisible"
      :application-id="printTarget?.id ?? null"
      :application-no="printTarget?.applicationNo ?? ''"
      :applicant-name="printTarget?.applicantName ?? ''"
      :relief-type="printTarget?.reliefType"
      mode="reprint"
    />
  </div>
</template>

<style scoped>
.temp-relief-list-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.temp-relief-list-page :deep(.nc-page-header__main) {
  flex: 1;
}

.header-stats {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
  margin-top: 14px;
}

.list-card {
  overflow: hidden;
}

.list-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.status-tabs {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.status-tab {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
  font-family: inherit;
  font-size: var(--nc-text-sm);
  cursor: pointer;
}

.status-tab.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-paper-2);
  color: var(--nc-ink);
  font-weight: 700;
  box-shadow: var(--nc-shadow-xs);
}

.status-tab__icon {
  color: var(--nc-seal);
  font-size: 15px;
}

.status-tab__count {
  min-width: 20px;
  padding: 0 6px;
  border-radius: 999px;
  color: #fff;
  font-family: var(--nc-font-mono);
  font-size: 11px;
  font-weight: 700;
  line-height: 18px;
  text-align: center;
}

.status-tab__count--0 {
  background-color: var(--nc-seal);
}

.status-tab__count--1 {
  background-color: var(--nc-pine);
}

.status-tab__count--2 {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.filter-bar {
  display: grid;
  grid-template-columns: 172px 190px minmax(180px, 1fr) auto;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.filter-bar :deep(.el-select),
.filter-bar :deep(.el-input) {
  width: 100%;
}

.load-error {
  margin: 10px 16px 0;
}

.list-area {
  min-height: 220px;
}

.list-skeleton {
  padding: 22px;
}

.relief-cards {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px;
}

.relief-card {
  padding: 14px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
}

.relief-card.is-alt {
  background-color: #f8f9fa;
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px 16px;
}

.card-person {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
}

.card-name {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 18px;
  font-weight: 700;
  letter-spacing: 0.03em;
}

.status-chip {
  display: inline-flex;
  align-items: center;
  padding: 1px 8px;
  border: 1px solid transparent;
  border-radius: 999px;
  font-size: var(--nc-text-xs);
  font-weight: 700;
  letter-spacing: 0.04em;
  white-space: nowrap;
}

.status-chip.is-danger {
  border-color: rgba(171, 50, 39, 0.28);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.status-chip.is-success {
  border-color: rgba(46, 71, 56, 0.28);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.card-actions {
  display: flex;
  flex: none;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 6px;
}

.card-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.card-id-card {
  margin: 4px 0 0;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: 15px;
  letter-spacing: 0.02em;
}

.card-lines {
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin-top: 8px;
}

.card-no {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: 15px;
  letter-spacing: 0.02em;
  word-break: break-all;
}

.card-deadline {
  color: var(--nc-amber-dark);
  font-size: 13px;
  font-weight: 600;
}

.card-deadline.is-overdue {
  color: var(--nc-seal);
}

.card-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 16px;
  margin: 10px 0 0;
}

.card-meta--tab1 {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.card-meta div {
  min-width: 0;
}

.card-meta__wide {
  grid-column: 1 / -1;
}

.card-meta dt {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 13px;
  letter-spacing: 0.04em;
}

.card-meta dd {
  margin: 2px 0 0;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: 14px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-meta dd.amount {
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-weight: 700;
}

.empty-wrap {
  padding: 10px 8px;
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

@media (max-width: 1280px) {
  .header-stats {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .filter-bar {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .filter-search {
    grid-column: 1 / -1;
  }
}

@media (max-width: 960px) {
  .card-head {
    flex-direction: column;
    align-items: stretch;
  }

  .card-actions {
    justify-content: flex-start;
  }

  .card-meta--tab1 {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 640px) {
  .header-stats {
    grid-template-columns: 1fr;
  }

  .status-tabs {
    padding: 12px;
  }

  .status-tab {
    padding: 9px 6px;
    font-size: var(--nc-text-xs);
  }

  .filter-bar {
    grid-template-columns: 1fr;
    padding: 12px;
  }

  .filter-search {
    grid-column: auto;
  }

  .card-id-card,
  .card-no {
    font-size: 13px;
  }

  .card-meta,
  .card-meta--tab1 {
    grid-template-columns: 1fr;
  }

  .pagination {
    justify-content: center;
    padding: 12px;
  }

  .pagination-note {
    width: 100%;
    text-align: center;
  }
}
</style>
