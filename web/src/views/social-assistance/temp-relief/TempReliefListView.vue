<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { MoreFilled, Plus, Printer, Refresh, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  changeTempReliefApplyDate,
  confirmTempReliefApplication,
  deleteTempReliefApplication,
  fileTempReliefApplication,
  getArchivedTempReliefApplications,
  getTempReliefApplication,
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
  TempReliefDetail,
  TempReliefListItem,
  TempReliefPaymentChannel,
  TempReliefPopulationStatus,
  TempReliefReliefType,
  TempReliefStatus,
  TempReliefSummary,
} from '@/types/tempRelief'
import TempReliefPrintDialog from './TempReliefPrintDialog.vue'

type StatusTab = 'pending' | 'submitted' | 'confirmed' | 'closed' | 'all'
type ViewMode = 'active' | 'archive'
type PopulationFilter = '' | TempReliefPopulationStatus
type CategoryFilter = '' | 'large-county' | 'large-reserve' | 'small'
type PrintDialogMode = 'print' | 'reprint'
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
  | 'change-date'
  | 'delete'
  | 'print'
  | 'reprint'

interface RowAction {
  key: ActionKey
  label: string
  type: ButtonTone
}

interface StatusTabDefinition {
  value: StatusTab
  label: string
  status?: string
}

const STATUS_TABS: StatusTabDefinition[] = [
  { value: 'pending', label: '待办', status: 'Draft,Returned' },
  { value: 'submitted', label: '已提交', status: 'Submitted' },
  { value: 'confirmed', label: '已确认', status: 'Confirmed' },
  { value: 'closed', label: '已终止·不予', status: 'Stopped,Rejected' },
  { value: 'all', label: '全部' },
]

const POPULATION_OPTIONS: Array<{ value: PopulationFilter; label: string }> = [
  { value: '', label: '全部大类' },
  { value: 'Insured', label: '在册' },
  { value: 'Uninsured', label: '非在册' },
]

const CATEGORY_OPTIONS: Array<{ value: CategoryFilter; label: string }> = [
  { value: '', label: '全部小类' },
  { value: 'large-county', label: '常规·上级' },
  { value: 'large-reserve', label: '常规·备用金' },
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

const mode = ref<ViewMode>('active')
const activeTab = ref<StatusTab>('pending')
const keyword = ref('')
const populationFilter = ref<PopulationFilter>('')
const categoryFilter = ref<CategoryFilter>('')
const townFilter = ref('')

const currentYear = new Date().getFullYear()
const yearOptions = Array.from({ length: 11 }, (_, index) => currentYear + 1 - index)
const monthOptions = Array.from({ length: 12 }, (_, index) => index + 1)
const yearFilter = ref<number | ''>(currentYear)
const monthFilter = ref<number | ''>('')

const page = ref(1)
const pageSize = ref(20)
const items = ref<TempReliefListItem[]>([])
const summary = ref<TempReliefSummary | null>(null)
/** 待办理精确件数（Draft + Returned），独立计数避免与「已提交」重叠。 */
const pendingTodoCount = ref<number | null>(null)
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const summaryLoading = ref(false)
const loadError = ref('')

const printVisible = ref(false)
const printMode = ref<PrintDialogMode>('print')
const printTarget = ref<TempReliefListItem | null>(null)

let requestTicket = 0

/**
 * 列表契约目前未返回「遇困人数 / 备案状态」，按页懒补详情；
 * 服务端列表补齐这些字段后 needsRowDetail 自动跳过，不再产生额外请求。
 */
const detailCache = new Map<number, TempReliefDetail>()
const detailPromises = new Map<number, Promise<TempReliefDetail>>()

const isArchive = computed(() => mode.value === 'archive')
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const hasKeyword = computed(() => keyword.value.trim().length > 0)
const hasFilter = computed(() => {
  if (isArchive.value) {
    return hasKeyword.value || monthFilter.value !== '' || yearFilter.value !== currentYear
  }
  return (
    hasKeyword.value ||
    populationFilter.value !== '' ||
    categoryFilter.value !== '' ||
    townFilter.value.trim().length > 0
  )
})
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)
const currentTab = computed(
  () => STATUS_TABS.find((tab) => tab.value === activeTab.value) ?? STATUS_TABS[0],
)
const currentTabLabel = computed(() => currentTab.value.label)

const tabCounts = computed<Record<StatusTab, number | null>>(() => {
  const value = summary.value
  if (!value) {
    return {
      pending: pendingTodoCount.value,
      submitted: null,
      confirmed: null,
      closed: null,
      all: null,
    }
  }
  const pending = pendingTodoCount.value ?? value.pendingCount
  const submitted = value.submittedCount
  const confirmed = value.confirmedCount
  const closed = value.stoppedCount
  const all =
    pending !== null && submitted !== null && confirmed !== null && closed !== null
      ? pending + submitted + confirmed + closed
      : null
  return { pending, submitted, confirmed, closed, all }
})

const summaryAmountText = computed(() => {
  const amount = summary.value?.yearConfirmedAmount
  return amount === null || amount === undefined ? '—' : formatNumber(amount)
})

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取临时救助记录…'
  if (isArchive.value) {
    const period =
      yearFilter.value === ''
        ? '全部年份'
        : monthFilter.value === ''
          ? `${yearFilter.value} 年`
          : `${yearFilter.value} 年 ${monthFilter.value} 月`
    return `${period} · 已完结 ${totalCount.value} 条`
  }
  const suffix = hasKeyword.value ? ' · 已应用关键字筛选' : ''
  return `${currentTabLabel.value} · 共 ${totalCount.value} 条记录${suffix}`
})

const emptyText = computed(() => {
  if (isArchive.value) {
    if (hasKeyword.value) return '没有找到匹配的已完结临时救助档案'
    return monthFilter.value === ''
      ? '该年份暂无已完结的临时救助档案'
      : '该月份暂无已完结的临时救助档案'
  }
  if (hasKeyword.value) return '没有找到匹配的临时救助申请'
  switch (activeTab.value) {
    case 'pending':
      return '暂无待办理的临时救助申请'
    case 'submitted':
      return '暂无已提交、等待县确认的申请'
    case 'confirmed':
      return '暂无已确认的临时救助记录'
    case 'closed':
      return '暂无已终止或不予救助的记录'
    default:
      return '暂无临时救助记录'
  }
})

function formatNumber(value: number): string {
  return new Intl.NumberFormat('zh-CN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value)
}

function formatMoney(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : `${formatNumber(value)} 元`
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const text = value.slice(0, 10)
  return /^\d{4}-\d{2}-\d{2}$/.test(text) ? text : '—'
}

function statValue(value: number | null | undefined): string | number {
  return value ?? '—'
}

function rowNumber(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1
}

function isTerminal(status: TempReliefStatus): boolean {
  return status === 'Confirmed' || status === 'Stopped' || status === 'Rejected'
}

function categoryText(row: TempReliefListItem): string {
  if (row.categoryDisplayName) return row.categoryDisplayName
  const population =
    row.populationStatusText ||
    (row.populationStatus === 'Insured' ? '在保障人群内' : '不在保障人群内')
  const channel =
    row.paymentChannelText || (row.paymentChannel === 'County' ? '上级部门发放' : '备用金发放')
  const sub = row.reliefType === 'Small' ? '小额快救' : `常规救助（${channel}）`
  return `${population} · ${sub}`
}

function personText(row: TempReliefListItem): string {
  const count = row.reliefPersonCount
  return count === null || count === undefined || count <= 0 ? '—' : `${count} 人`
}

function statusText(row: TempReliefListItem): string {
  return row.statusText || STATUS_TEXT_FALLBACK[row.status] || row.status
}

function statusClass(row: TempReliefListItem): string {
  const map: Record<TempReliefStatus, string> = {
    Draft: 'is-draft',
    Submitted: 'is-submitted',
    Confirmed: 'is-confirmed',
    Returned: 'is-returned',
    Rejected: 'is-rejected',
    Stopped: 'is-stopped',
  }
  return map[row.status] ?? 'is-draft'
}

function filingText(row: TempReliefListItem): string {
  if (row.filingStatusText) return row.filingStatusText
  if (row.filingStatus === 'Filed') return '已备案'
  if (row.filingStatus === 'Pending') return '待备案'
  if (row.status === 'Confirmed') {
    return row.paymentChannel === 'County' ? '待备案' : '无需备案'
  }
  return '—'
}

function filingClass(row: TempReliefListItem): string {
  const text = filingText(row)
  if (text === '已备案') return 'is-filed'
  if (text === '待备案') return 'is-pending'
  if (text === '无需备案') return 'is-none'
  return 'is-none'
}

function deadlineLabel(row: TempReliefListItem): string {
  if (isTerminal(row.status)) return '—'
  if (row.deadlineText) return row.deadlineText
  if (!row.deadlineDate) return '—'
  const due = new Date(`${row.deadlineDate.slice(0, 10)}T00:00:00`)
  if (Number.isNaN(due.getTime())) return '—'
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const days = Math.round((due.getTime() - today.getTime()) / 86_400_000)
  if (days < 0) return `已逾期 ${-days} 天`
  if (days === 0) return '今日到期'
  return `剩余 ${days} 天`
}

function isOverdueRow(row: TempReliefListItem): boolean {
  return row.isOverdue || deadlineLabel(row).startsWith('已逾期')
}

function hasFlag(value: boolean | undefined): value is boolean {
  return typeof value === 'boolean'
}

function canEditRow(row: TempReliefListItem): boolean {
  return row.status === 'Draft' || row.status === 'Returned'
}

function canSubmitRow(row: TempReliefListItem): boolean {
  return hasFlag(row.canSubmit) ? row.canSubmit : row.status === 'Draft' || row.status === 'Returned'
}

function canReturnRow(row: TempReliefListItem): boolean {
  return hasFlag(row.canReturn) ? row.canReturn : row.status === 'Submitted'
}

function canRejectRow(row: TempReliefListItem): boolean {
  return hasFlag(row.canReject)
    ? row.canReject
    : row.status === 'Draft' || row.status === 'Submitted'
}

function canConfirmRow(row: TempReliefListItem): boolean {
  return hasFlag(row.canConfirm)
    ? row.canConfirm
    : row.status === 'Draft' || row.status === 'Submitted'
}

function canFileRow(row: TempReliefListItem): boolean {
  if (hasFlag(row.canFile)) return row.canFile
  if (hasFlag(row.canMarkFiled)) return row.canMarkFiled
  return (
    row.status === 'Confirmed' &&
    row.paymentChannel === 'County' &&
    (row.filingStatus === null || row.filingStatus === undefined || row.filingStatus === 'Pending')
  )
}

function canStopRow(row: TempReliefListItem): boolean {
  return row.status === 'Confirmed'
}

function canDeleteRow(row: TempReliefListItem): boolean {
  return row.status === 'Draft'
}

function confirmLabel(row: TempReliefListItem): string {
  return row.paymentChannel === 'County' ? '县确认' : '乡镇确认'
}

function actionsFor(row: TempReliefListItem): RowAction[] {
  const actions: RowAction[] = [{ key: 'view', label: '查看', type: 'primary' }]
  if (canEditRow(row)) actions.push({ key: 'edit', label: '编辑', type: 'warning' })
  if (canSubmitRow(row)) actions.push({ key: 'submit', label: '提交', type: 'primary' })
  if (canConfirmRow(row)) {
    actions.push({ key: 'confirm', label: confirmLabel(row), type: 'success' })
  }
  if (canFileRow(row)) actions.push({ key: 'file', label: '备案', type: 'success' })
  if (canReturnRow(row)) actions.push({ key: 'return', label: '退回补正', type: 'warning' })
  if (canRejectRow(row)) actions.push({ key: 'reject', label: '不予救助', type: 'danger' })
  if (canStopRow(row)) actions.push({ key: 'stop', label: '终止', type: 'warning' })
  if (canEditRow(row)) actions.push({ key: 'change-date', label: '改期', type: 'info' })
  if (!isArchive.value && !isTerminal(row.status)) {
    actions.push({ key: 'print', label: '打印', type: 'info' })
  }
  if (isArchive.value || isTerminal(row.status)) {
    actions.push({ key: 'reprint', label: '补打', type: 'primary' })
  }
  if (canDeleteRow(row)) actions.push({ key: 'delete', label: '删除', type: 'danger' })
  return actions
}

function inlineActions(row: TempReliefListItem): RowAction[] {
  return actionsFor(row).slice(0, 3)
}

function overflowActions(row: TempReliefListItem): RowAction[] {
  return actionsFor(row).slice(3)
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
    let response
    if (isArchive.value) {
      response = await getArchivedTempReliefApplications({
        page: page.value,
        pageSize: pageSize.value,
        keyword: keyword.value.trim() || undefined,
        year: yearFilter.value === '' ? undefined : yearFilter.value,
        month: monthFilter.value === '' ? undefined : monthFilter.value,
      })
    } else {
      const category = resolveCategoryFilter()
      response = await listTempReliefApplications({
        page: page.value,
        pageSize: pageSize.value,
        keyword: keyword.value.trim() || undefined,
        status: currentTab.value.status,
        populationStatus: populationFilter.value || undefined,
        reliefType: category.reliefType,
        paymentChannel: category.paymentChannel,
        town: townFilter.value.trim() || undefined,
      })
    }
    if (ticket !== requestTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0
    void enrichRows(ticket)
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

function mergeDetail(row: TempReliefListItem, detail: TempReliefDetail): TempReliefListItem {
  const sameStatus = detail.status === row.status
  return {
    ...row,
    reliefPersonCount:
      row.reliefPersonCount !== null && row.reliefPersonCount !== undefined && row.reliefPersonCount > 0
        ? row.reliefPersonCount
        : detail.reliefPersonCount,
    filingStatus: row.filingStatus ?? (sameStatus ? detail.filingStatus : null),
    filingStatusText: row.filingStatusText || (sameStatus ? detail.filingStatusText : ''),
  }
}

function applyCachedDetails(rows: TempReliefListItem[]): TempReliefListItem[] {
  return rows.map((row) => {
    const detail = detailCache.get(row.id)
    return detail ? mergeDetail(row, detail) : row
  })
}

function loadDetail(id: number): Promise<TempReliefDetail> {
  const existing = detailPromises.get(id)
  if (existing) return existing

  const promise = getTempReliefApplication(id)
    .then((detail) => {
      detailCache.set(id, detail)
      return detail
    })
    .finally(() => {
      detailPromises.delete(id)
    })
  detailPromises.set(id, promise)
  return promise
}

async function enrichRows(ticket: number): Promise<void> {
  items.value = applyCachedDetails(items.value)

  const targets = items.value.filter((row) => {
    if (row.reliefPersonCount === null || row.reliefPersonCount === undefined) return true
    if (row.status !== 'Confirmed' || row.paymentChannel !== 'County') return false
    if (row.filingStatus !== null && row.filingStatus !== undefined) return false
    const cached = detailCache.get(row.id)
    return cached === undefined || cached.status !== row.status
  })
  if (targets.length === 0) return

  await Promise.allSettled(targets.map((row) => loadDetail(row.id)))

  if (ticket !== requestTicket) return
  items.value = applyCachedDetails(items.value)
}

async function loadSummary(): Promise<void> {
  summaryLoading.value = true
  try {
    const [summaryResult, pendingResult] = await Promise.allSettled([
      getTempReliefSummary(),
      listTempReliefApplications({ page: 1, pageSize: 1, status: 'Draft,Returned' }),
    ])
    summary.value = summaryResult.status === 'fulfilled' ? summaryResult.value : null
    pendingTodoCount.value =
      pendingResult.status === 'fulfilled' ? (pendingResult.value.totalCount ?? null) : null
  } finally {
    summaryLoading.value = false
  }
}

async function refreshAll(invalidateId?: number): Promise<void> {
  if (invalidateId === undefined) {
    detailCache.clear()
  } else {
    detailCache.delete(invalidateId)
  }
  await Promise.all([load(), loadSummary()])
}

function switchMode(next: ViewMode): void {
  if (mode.value === next) return
  mode.value = next
  page.value = 1
  keyword.value = ''
  if (next === 'archive' && yearFilter.value === '') {
    yearFilter.value = currentYear
  }
  void load()
}

function toggleArchive(): void {
  switchMode(isArchive.value ? 'active' : 'archive')
}

function switchTab(next: StatusTab): void {
  if (activeTab.value === next) return
  activeTab.value = next
  page.value = 1
  void load()
}

function handleSearch(): void {
  if (isArchive.value && monthFilter.value !== '' && yearFilter.value === '') {
    ElMessage.warning('按月补打时请先选择年份')
    return
  }
  page.value = 1
  void load()
}

function handleReset(): void {
  keyword.value = ''
  page.value = 1
  if (isArchive.value) {
    yearFilter.value = currentYear
    monthFilter.value = ''
  } else {
    populationFilter.value = ''
    categoryFilter.value = ''
    townFilter.value = ''
  }
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

function handleCreate(): void {
  void router.push('/social-assistance/temp-relief/new')
}

function openView(row: TempReliefListItem): void {
  void router.push(`/social-assistance/temp-relief/${row.id}/edit?mode=view`)
}

function openEdit(row: TempReliefListItem): void {
  void router.push(`/social-assistance/temp-relief/${row.id}/edit`)
}

function openPrint(row: TempReliefListItem, nextMode: PrintDialogMode): void {
  printTarget.value = row
  printMode.value = nextMode
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
    await refreshAll(row.id)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '提交申请失败'))
  }
}

async function handleReturn(row: TempReliefListItem): Promise<void> {
  let reason = ''
  try {
    const result = await ElMessageBox.prompt(
      `请填写 [${row.applicationNo}] ${row.applicantName} 的退回补正原因：`,
      '退回补正',
      {
        confirmButtonText: '退回',
        cancelButtonText: '取消',
        inputPlaceholder: '如：材料不全 / 信息需核实',
        inputValidator: (value: string) => Boolean(value && value.trim()) || '请填写退回补正原因',
        type: 'warning',
      },
    )
    reason = result.value.trim()
  } catch (error) {
    if (isDialogCancel(error)) return
    ElMessage.error(getErrorMessage(error, '退回补正失败'))
    return
  }

  try {
    await returnTempReliefApplication(row.id, reason)
    ElMessage.success('已退回补正')
    await refreshAll(row.id)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '退回补正失败'))
  }
}

async function handleReject(row: TempReliefListItem): Promise<void> {
  let reason = ''
  try {
    const result = await ElMessageBox.prompt(
      `请填写 [${row.applicationNo}] ${row.applicantName} 的不予救助理由。\n将以书面方式告知，告知时间记为当前时间。`,
      '不予救助',
      {
        confirmButtonText: '确定',
        cancelButtonText: '取消',
        inputPlaceholder: '如：不符合救助条件',
        inputValidator: (value: string) => Boolean(value && value.trim()) || '请填写不予救助理由',
        type: 'warning',
      },
    )
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
    await refreshAll(row.id)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '登记不予救助失败'))
  }
}

async function handleConfirm(row: TempReliefListItem): Promise<void> {
  const label = confirmLabel(row)
  const confirmed = await askConfirm(
    `确定要${label} [${row.applicationNo}] ${row.applicantName} 的临时救助申请吗？\n${
      label === '县确认' ? '县确认后进入待备案。' : '乡镇确认后即生效。'
    }`,
    label,
    '确认',
  )
  if (!confirmed) return

  try {
    await confirmTempReliefApplication(row.id)
    ElMessage.success(`已${label}`)
    await refreshAll(row.id)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, `${label}失败`))
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
    await refreshAll(row.id)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '登记备案失败'))
  }
}

async function handleStop(row: TempReliefListItem): Promise<void> {
  let reason = ''
  try {
    const result = await ElMessageBox.prompt(
      `确定要终止 [${row.applicationNo}] ${row.applicantName} 的临时救助申请吗？\n终止后本年度仍占用唯一申请名额。`,
      '终止申请',
      {
        confirmButtonText: '终止',
        cancelButtonText: '取消',
        inputPlaceholder: '请填写终止原因',
        inputValidator: (value: string) => Boolean(value && value.trim()) || '请填写终止原因',
        type: 'warning',
      },
    )
    reason = result.value.trim()
  } catch (error) {
    if (isDialogCancel(error)) return
    ElMessage.error(getErrorMessage(error, '终止申请失败'))
    return
  }

  try {
    await stopTempReliefApplication(row.id, reason)
    ElMessage.success('临时救助申请已终止')
    await refreshAll(row.id)
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
    await refreshAll(row.id)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除申请失败'))
  }
}

function dateToIso(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function maxApplyDateText(): string {
  const today = new Date()
  const target = new Date(today.getFullYear(), today.getMonth() + 1, 1)
  const lastDay = new Date(target.getFullYear(), target.getMonth() + 1, 0).getDate()
  target.setDate(Math.min(today.getDate(), lastDay))
  return dateToIso(target)
}

function validateApplyDate(value: string): true | string {
  const text = value.trim()
  if (!/^\d{4}-\d{2}-\d{2}$/.test(text)) return '日期格式应为 yyyy-MM-dd'
  const date = new Date(`${text}T00:00:00`)
  if (Number.isNaN(date.getTime()) || dateToIso(date) !== text) return '日期无效'
  const max = maxApplyDateText()
  if (text > max) return `申请日期最晚为 ${max}`
  return true
}

async function handleChangeDate(row: TempReliefListItem): Promise<void> {
  let applyDate = ''
  try {
    const result = await ElMessageBox.prompt(
      `请输入 [${row.applicationNo}] 的新申请日期（yyyy-MM-dd）。\n改期后申请编号将按新日期重排。`,
      '改期',
      {
        confirmButtonText: '改期',
        cancelButtonText: '取消',
        inputValue: row.applyDate?.slice(0, 10) ?? '',
        inputPlaceholder: 'yyyy-MM-dd',
        inputValidator: (value: string) => validateApplyDate(value),
        type: 'warning',
      },
    )
    applyDate = result.value.trim()
  } catch (error) {
    if (isDialogCancel(error)) return
    ElMessage.error(getErrorMessage(error, '改期失败'))
    return
  }

  try {
    const result = await changeTempReliefApplyDate(row.id, applyDate)
    ElMessage.success(
      result.applicationNo ? `已改期，新申请编号 ${result.applicationNo}` : '申请日期已修改',
    )
    await refreshAll(row.id)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '改期失败'))
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
    case 'return':
      void handleReturn(row)
      return
    case 'reject':
      void handleReject(row)
      return
    case 'confirm':
      void handleConfirm(row)
      return
    case 'file':
      void handleFile(row)
      return
    case 'stop':
      void handleStop(row)
      return
    case 'change-date':
      void handleChangeDate(row)
      return
    case 'delete':
      void handleDelete(row)
      return
    case 'print':
      openPrint(row, 'print')
      return
    case 'reprint':
      openPrint(row, 'reprint')
  }
}

function handleRowCommand(command: string, row: TempReliefListItem): void {
  runAction(command as ActionKey, row)
}

watch(yearFilter, (next) => {
  if (next === '') monthFilter.value = ''
})

onMounted(() => {
  void load()
  void loadSummary()
})
</script>

<template>
  <div class="nc-page temp-relief-list-page">
    <NcPageHeader
      title="临时救助"
      description="两类人群 × 三种救助场景，按 2 / 5 / 15 日时限办理；已确认档案可随时补打。"
      kicker="低收入人口救助帮扶 · 临时救助"
    >
      <template #actions>
        <el-button :icon="Printer" :loading="loading" @click="toggleArchive">
          {{ isArchive ? '返回办理列表' : '已完结 / 补打' }}
        </el-button>
        <el-button :icon="Refresh" :loading="loading || summaryLoading" @click="refreshAll()">
          刷新
        </el-button>
        <el-button type="primary" :icon="Plus" @click="handleCreate">新增申请</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat
        label="待办理（件）"
        :value="statValue(pendingTodoCount ?? summary?.pendingCount)"
        hint="草稿 + 退回补正"
        tone="amber"
        compact
      />
      <NcStat
        label="已提交（件）"
        :value="statValue(summary?.submittedCount)"
        hint="等待县确认"
        tone="seal"
        compact
      />
      <NcStat
        label="本年已确认（件）"
        :value="statValue(summary?.yearConfirmed)"
        tone="pine"
        compact
      />
      <NcStat
        label="逾期件数"
        :value="statValue(summary?.overdueCount)"
        hint="待办中超时限"
        tone="seal"
        compact
      />
      <NcStat
        label="本年确认金额（元）"
        :value="summaryAmountText"
        tone="ink"
        compact
      />
      <span v-if="summaryLoading" class="summary-loading">统计更新中…</span>
    </div>

    <NcCard padding="none" class="filter-card">
      <div v-if="isArchive" class="archive-note">
        <div>
          <strong>已完结档案 · 补打</strong>
          <p>仅显示已确认、已终止、不予救助记录；可按年 / 月及姓名、身份证、编号检索后补打。</p>
        </div>
      </div>

      <div v-else class="status-tabs" role="tablist" aria-label="按状态筛选">
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
          <span>{{ tab.label }}</span>
          <span
            v-if="tabCounts[tab.value] !== null"
            class="status-tab__count"
            :class="`status-tab__count--${tab.value}`"
          >
            {{ tabCounts[tab.value] }}
          </span>
        </button>
      </div>

      <form v-if="isArchive" class="filter-bar archive-filter" @submit.prevent="handleSearch">
        <label class="filter-field">
          <span class="filter-field__label">年份</span>
          <el-select v-model="yearFilter" placeholder="全部年份">
            <el-option label="全部年份" value="" />
            <el-option
              v-for="year in yearOptions"
              :key="year"
              :label="`${year} 年`"
              :value="year"
            />
          </el-select>
        </label>
        <label class="filter-field">
          <span class="filter-field__label">月份</span>
          <el-select v-model="monthFilter" :disabled="yearFilter === ''" placeholder="全部月份">
            <el-option label="全部月份" value="" />
            <el-option
              v-for="month in monthOptions"
              :key="month"
              :label="`${month} 月`"
              :value="month"
            />
          </el-select>
        </label>
        <label class="filter-field filter-field--search">
          <span class="filter-field__label">关键字</span>
          <el-input
            v-model="keyword"
            clearable
            :prefix-icon="Search"
            placeholder="姓名 / 身份证 / 申请编号"
            @clear="handleSearch"
          />
        </label>
        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            查询
          </el-button>
          <el-button v-if="hasFilter" text native-type="button" @click="handleReset">
            重置
          </el-button>
        </div>
      </form>

      <form v-else class="filter-bar" @submit.prevent="handleSearch">
        <label class="filter-field">
          <span class="filter-field__label">大类</span>
          <el-select v-model="populationFilter" placeholder="全部大类">
            <el-option
              v-for="option in POPULATION_OPTIONS"
              :key="option.value || 'all'"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </label>
        <label class="filter-field">
          <span class="filter-field__label">小类</span>
          <el-select v-model="categoryFilter" placeholder="全部小类">
            <el-option
              v-for="option in CATEGORY_OPTIONS"
              :key="option.value || 'all'"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </label>
        <label class="filter-field filter-field--town">
          <span class="filter-field__label">乡镇</span>
          <el-input v-model="townFilter" clearable placeholder="乡镇 / 街道" />
        </label>
        <label class="filter-field filter-field--search">
          <span class="filter-field__label">关键字</span>
          <el-input
            v-model="keyword"
            clearable
            :prefix-icon="Search"
            placeholder="姓名 / 身份证 / 申请编号"
            @clear="handleSearch"
          />
        </label>
        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            查询
          </el-button>
          <el-button v-if="hasFilter" text native-type="button" @click="handleReset">
            重置
          </el-button>
        </div>
      </form>
    </NcCard>

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

    <NcCard
      :title="isArchive ? '已完结档案' : '临时救助列表'"
      :subtitle="listSubtitle"
      padding="none"
      class="list-card"
    >
      <div v-loading="loading && items.length > 0" class="list-area">
        <el-skeleton v-if="loading && items.length === 0" :rows="5" animated class="list-skeleton" />

        <template v-else-if="items.length > 0">
          <el-table v-if="!isNarrow" :data="items" row-key="id" class="relief-table">
            <el-table-column label="序号" width="58" align="center">
              <template #default="{ $index }">
                <span class="row-index">{{ rowNumber($index) }}</span>
              </template>
            </el-table-column>

            <el-table-column label="申请编号" min-width="132">
              <template #default="{ row }">
                <span class="app-no">{{ (row as TempReliefListItem).applicationNo || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="申请人" min-width="92">
              <template #default="{ row }">
                <strong class="applicant-name">
                  {{ (row as TempReliefListItem).applicantName || '—' }}
                </strong>
              </template>
            </el-table-column>

            <el-table-column label="身份证（脱敏）" min-width="152">
              <template #default="{ row }">
                <span class="id-card">
                  {{ (row as TempReliefListItem).applicantIdCardMasked || '—' }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="大类 / 小类" min-width="196">
              <template #default="{ row }">
                <span class="category" :title="categoryText(row as TempReliefListItem)">
                  {{ categoryText(row as TempReliefListItem) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="遇困人数" width="86" align="right">
              <template #default="{ row }">
                <span class="person-count">{{ personText(row as TempReliefListItem) }}</span>
              </template>
            </el-table-column>

            <el-table-column label="金额" width="108" align="right">
              <template #default="{ row }">
                <span class="amount">
                  {{ formatMoney((row as TempReliefListItem).confirmAmount) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="申请日期" width="108">
              <template #default="{ row }">
                {{ formatDate((row as TempReliefListItem).applyDate) }}
              </template>
            </el-table-column>

            <el-table-column label="办理时限" min-width="126">
              <template #default="{ row }">
                <div
                  class="deadline-cell"
                  :class="{ 'is-overdue': isOverdueRow(row as TempReliefListItem) }"
                >
                  <span>{{ deadlineLabel(row as TempReliefListItem) }}</span>
                  <small v-if="deadlineLabel(row as TempReliefListItem) !== '—'">
                    {{ formatDate((row as TempReliefListItem).deadlineDate) }}
                  </small>
                </div>
              </template>
            </el-table-column>

            <el-table-column label="状态" width="96">
              <template #default="{ row }">
                <span class="status" :class="statusClass(row as TempReliefListItem)">
                  {{ statusText(row as TempReliefListItem) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="备案状态" width="96">
              <template #default="{ row }">
                <span class="filing" :class="filingClass(row as TempReliefListItem)">
                  {{ filingText(row as TempReliefListItem) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="操作" width="252" fixed="right" align="right">
              <template #default="{ row }">
                <div class="row-actions">
                  <el-button
                    v-for="action in inlineActions(row as TempReliefListItem)"
                    :key="action.key"
                    link
                    :type="action.type"
                    @click="runAction(action.key, row as TempReliefListItem)"
                  >
                    {{ action.label }}
                  </el-button>
                  <el-dropdown
                    v-if="overflowActions(row as TempReliefListItem).length > 0"
                    trigger="click"
                    @command="handleRowCommand($event, row as TempReliefListItem)"
                  >
                    <el-button link :icon="MoreFilled" class="more-button">更多</el-button>
                    <template #dropdown>
                      <el-dropdown-menu>
                        <el-dropdown-item
                          v-for="action in overflowActions(row as TempReliefListItem)"
                          :key="action.key"
                          :command="action.key"
                        >
                          {{ action.label }}
                        </el-dropdown-item>
                      </el-dropdown-menu>
                    </template>
                  </el-dropdown>
                </div>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="relief-cards">
            <article v-for="(row, index) in items" :key="row.id" class="relief-card">
              <header class="card-head">
                <div class="card-person">
                  <span class="card-index">{{ rowNumber(index) }}</span>
                  <strong>{{ row.applicantName || '—' }}</strong>
                  <span class="status" :class="statusClass(row)">{{ statusText(row) }}</span>
                </div>
                <span class="app-no">{{ row.applicationNo || '—' }}</span>
              </header>

              <p class="id-card">{{ row.applicantIdCardMasked || '—' }}</p>

              <dl class="card-meta">
                <div class="card-meta__wide">
                  <dt>大类 / 小类</dt>
                  <dd :title="categoryText(row)">{{ categoryText(row) }}</dd>
                </div>
                <div>
                  <dt>遇困人数</dt>
                  <dd>{{ personText(row) }}</dd>
                </div>
                <div>
                  <dt>金额</dt>
                  <dd class="amount">{{ formatMoney(row.confirmAmount) }}</dd>
                </div>
                <div>
                  <dt>申请日期</dt>
                  <dd>{{ formatDate(row.applyDate) }}</dd>
                </div>
                <div>
                  <dt>办理时限</dt>
                  <dd class="deadline-cell" :class="{ 'is-overdue': isOverdueRow(row) }">
                    {{ deadlineLabel(row) }}
                  </dd>
                </div>
                <div>
                  <dt>备案状态</dt>
                  <dd>
                    <span class="filing" :class="filingClass(row)">{{ filingText(row) }}</span>
                  </dd>
                </div>
                <div v-if="row.stoppedAt || row.stopReason" class="card-meta__wide">
                  <dt>终止信息</dt>
                  <dd :title="row.stopReason">
                    {{ formatDate(row.stoppedAt) }} · {{ row.stopReason || '—' }}
                  </dd>
                </div>
              </dl>

              <div class="card-actions">
                <el-button
                  v-for="action in actionsFor(row)"
                  :key="action.key"
                  size="small"
                  :type="action.type"
                  plain
                  @click="runAction(action.key, row)"
                >
                  {{ action.label }}
                </el-button>
              </div>
            </article>
          </div>
        </template>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="emptyText">
            <el-button v-if="hasFilter" :icon="Refresh" @click="handleReset">
              清除筛选
            </el-button>
            <el-button v-else-if="isArchive" @click="switchMode('active')">返回办理列表</el-button>
          </el-empty>
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

    <TempReliefPrintDialog
      v-model="printVisible"
      :application-id="printTarget?.id ?? null"
      :application-no="printTarget?.applicationNo ?? ''"
      :applicant-name="printTarget?.applicantName ?? ''"
      :mode="printMode"
    />
  </div>
</template>

<style scoped>
.temp-relief-list-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.summary {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 10px;
}

.summary-loading {
  grid-column: 1 / -1;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.filter-card,
.list-card {
  overflow: hidden;
}

.archive-note {
  padding: 13px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: var(--nc-amber-soft);
}

.archive-note strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.04em;
}

.archive-note p {
  margin: 3px 0 0;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.status-tabs {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 8px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.status-tab {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 9px 10px;
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
  background-color: var(--nc-seal-soft);
  color: var(--nc-ink);
  font-weight: 700;
}

.status-tab__count {
  min-width: 20px;
  padding: 0 6px;
  border-radius: 999px;
  background-color: var(--nc-ink-2);
  color: #fff;
  font-family: var(--nc-font-mono);
  font-size: 11px;
  font-weight: 700;
  line-height: 18px;
  text-align: center;
}

.status-tab__count--pending {
  background-color: var(--nc-amber);
}

.status-tab__count--submitted {
  background-color: var(--nc-seal);
}

.status-tab__count--confirmed {
  background-color: var(--nc-pine);
}

.status-tab__count--closed {
  background-color: var(--nc-muted);
}

.filter-bar {
  display: grid;
  grid-template-columns: 150px 178px 152px minmax(200px, 1fr) auto;
  align-items: end;
  gap: 10px;
  padding: 14px 16px;
}

.archive-filter {
  grid-template-columns: 126px 122px minmax(220px, 1fr) auto;
}

.filter-field {
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: 4px;
}

.filter-field__label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.1em;
}

.filter-field :deep(.el-select),
.filter-field :deep(.el-input) {
  width: 100%;
}

.filter-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  padding-bottom: 1px;
}

.load-error {
  margin: 0;
}

.list-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.list-area {
  min-height: 220px;
}

.list-skeleton {
  padding: 22px;
}

.relief-table {
  width: 100%;
}

.row-index {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.app-no {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.02em;
  white-space: nowrap;
}

.applicant-name {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
}

.id-card {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.category {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.person-count {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.amount {
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  white-space: nowrap;
}

.deadline-cell {
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: 1px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.deadline-cell small {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.deadline-cell.is-overdue {
  color: var(--nc-seal);
  font-weight: 700;
}

.deadline-cell.is-overdue small {
  color: var(--nc-seal);
}

.status,
.filing {
  display: inline-flex;
  align-items: center;
  padding: 1px 7px;
  border: 1px solid transparent;
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.04em;
  white-space: nowrap;
}

.status.is-draft {
  border-color: rgba(150, 104, 28, 0.32);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.status.is-returned {
  border-style: dashed;
  border-color: rgba(150, 104, 28, 0.55);
  background-color: var(--nc-paper-2);
  color: var(--nc-amber-dark);
}

.status.is-submitted {
  border-color: var(--nc-line-strong);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
}

.status.is-confirmed {
  border-color: rgba(46, 71, 56, 0.35);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.status.is-rejected {
  border-color: rgba(171, 50, 39, 0.3);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.status.is-stopped {
  border-color: var(--nc-line-strong);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
}

.filing.is-filed {
  border-color: rgba(46, 71, 56, 0.28);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.filing.is-pending {
  border-color: rgba(150, 104, 28, 0.3);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.filing.is-none {
  color: var(--nc-muted);
}

.row-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 2px 4px;
}

.row-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.more-button {
  color: var(--nc-muted);
}

.relief-cards {
  display: grid;
  gap: 10px;
  padding: 10px;
}

.relief-card {
  padding: 14px 15px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.card-person {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.card-index {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 22px;
  height: 22px;
  padding: 0 4px;
  border: 1px solid var(--nc-line);
  border-radius: 999px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.card-person strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.card-head .app-no {
  flex: none;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 14px;
  margin: 10px 0 0;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
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
  font-size: 10px;
  letter-spacing: 0.08em;
}

.card-meta dd {
  margin: 2px 0 0;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-meta dd.amount {
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
}

.card-meta dd.deadline-cell {
  display: block;
}

.card-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px dashed var(--nc-line-faint);
}

.card-actions :deep(.el-button + .el-button) {
  margin-left: 0;
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
  .summary {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .filter-bar {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .filter-field--search {
    grid-column: 1 / -1;
  }

  .filter-actions {
    grid-column: 1 / -1;
  }

  .archive-filter {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 1100px) {
  .status-tabs {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

@media (max-width: 960px) {
  .summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .filter-bar,
  .archive-filter {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    padding: 12px;
  }
}

@media (max-width: 640px) {
  .status-tabs {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    padding: 12px;
  }

  .filter-bar,
  .archive-filter {
    grid-template-columns: 1fr;
  }

  .filter-field--search,
  .filter-actions {
    grid-column: auto;
  }

  .filter-actions {
    justify-content: stretch;
  }

  .filter-actions .el-button {
    flex: 1;
  }

  .card-head {
    flex-direction: column;
    align-items: stretch;
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

@media (max-width: 420px) {
  .summary {
    grid-template-columns: 1fr;
  }

  .summary :deep(.nc-stat:last-of-type) {
    grid-column: auto;
  }
}
</style>
