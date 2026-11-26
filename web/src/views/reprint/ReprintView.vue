<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  Download,
  Files,
  InfoFilled,
  Printer,
  Refresh,
  Search,
  User,
  View,
} from '@element-plus/icons-vue'
import {
  listDocumentTemplates,
  listPrintJobs,
  type DocumentTemplate,
} from '@/api/archivePrint'
import { ApiError, getErrorMessage } from '@/api/http'
import { listPrinters } from '@/api/printers'
import {
  createReprintBatch,
  createReprintJob,
  fetchReprintOriginal,
  getReprintBatch,
  listReprintDomains,
  listReprintHistory,
  listReprintPersonDomains,
  listReprintPersons,
  listReprintRecords,
  listReprintTemplates,
} from '@/api/reprint'
import type {
  ReprintBatchCreated,
  ReprintBatchItem,
  ReprintBatchStatus,
  ReprintDomain,
  ReprintHistoryItem,
  ReprintJobItem,
  ReprintJobMode,
  ReprintPerson,
  ReprintPersonDomain,
  ReprintRecord,
  ReprintTemplate,
} from '@/types/reprint'
import type { PrinterBinding } from '@/types/api'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { maskIdCard } from '@/views/applications/format'

/* ───────────────────────── 通用 ───────────────────────── */

type TabKey = 'person' | 'batch'
type BatchMode = 'archive' | 'template'

interface SubmissionJob {
  jobNo: string
  status: string
  templateName: string
}

interface Submission {
  mode: ReprintJobMode | 'original'
  jobs: SubmissionJob[]
  templateNames: string[]
  submittedAt: Date
  note: string
}

interface BatchSubmissionSnapshot {
  batchNo: string
  domain: string
  year: number
  month: number
  templateIds: number[]
  copies: number
  isDuplex: boolean
  printerBindingId?: number
  printerName?: string
}

const CURRENT_YEAR = new Date().getFullYear()
const YEAR_OPTIONS = [CURRENT_YEAR - 1, CURRENT_YEAR, CURRENT_YEAR + 1]
const MONTH_OPTIONS = [
  { label: '全部月份', value: 0 },
  ...Array.from({ length: 12 }, (_, index) => ({
    label: `${index + 1} 月`,
    value: index + 1,
  })),
]

const activeTab = ref<TabKey>('person')
const permissionDenied = ref(false)

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function formatDay(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function maskName(value: string | null | undefined): string {
  const text = (value ?? '').trim()
  if (!text) return '—'
  if (text.length === 1) return text
  return `${text.slice(0, 1)}${'*'.repeat(Math.max(1, text.length - 1))}`
}

function handleError(error: unknown, fallback: string): string {
  if (error instanceof ApiError && error.status === 403) {
    permissionDenied.value = true
  }
  return getErrorMessage(error, fallback)
}

const MODE_LABELS: Record<ReprintJobMode | 'original', string> = {
  preview: '预览',
  print: '打印',
  save: '保存',
  original: '原样补打',
}

function modeLabel(mode: ReprintJobMode | 'original'): string {
  return MODE_LABELS[mode] ?? mode
}

const COMPLETED_STATUSES = ['completed', 'succeeded', 'success', 'done', 'finished']
const FAILED_STATUSES = ['failed', 'error', 'cancelled', 'canceled']

function isCompletedStatus(status: string): boolean {
  const text = (status ?? '').trim().toLowerCase()
  return COMPLETED_STATUSES.includes(text) || text.includes('完成') || text.includes('成功')
}

function isFailedStatus(status: string): boolean {
  const text = (status ?? '').trim().toLowerCase()
  return FAILED_STATUSES.includes(text) || text.includes('失败')
}

function isRunningStatus(status: string): boolean {
  const text = (status ?? '').trim().toLowerCase()
  return ['processing', 'running', 'printing', 'executing'].includes(text) || text.includes('执行')
}

function isTerminalStatus(status: string): boolean {
  return isCompletedStatus(status) || isFailedStatus(status)
}

function jobStatusLabel(status: string): string {
  if (isCompletedStatus(status)) return '已完成'
  if (isFailedStatus(status)) return '失败'
  if (isRunningStatus(status)) return '执行中'
  if (!status) return '排队中'
  const lower = status.toLowerCase()
  if (lower === 'pending' || lower === 'queued') return '排队中'
  return status
}

function jobStatusTone(status: string): 'online' | 'offline' | 'idle' | 'default' {
  if (isCompletedStatus(status)) return 'online'
  if (isFailedStatus(status)) return 'offline'
  if (isRunningStatus(status)) return 'idle'
  return 'default'
}

/* ───────────────────────── 业务域 / 打印机 ───────────────────────── */

const domains = ref<ReprintDomain[]>([])
const domainsLoading = ref(false)
const domainsError = ref('')

const printers = ref<PrinterBinding[]>([])
const printersLoading = ref(false)
const selectedPrinterId = ref<number | null>(null)

const selectedPrinter = computed(
  () => printers.value.find((printer) => printer.id === selectedPrinterId.value) ?? null,
)

const domainNameMap = computed(
  () => new Map(domains.value.map((domain) => [domain.key, domain.name])),
)

function domainName(key: string): string {
  return domainNameMap.value.get(key) ?? key
}

function hasMode(domain: ReprintDomain | null | undefined, keyword: string): boolean {
  if (!domain) return false
  return domain.modes.some((mode) => mode.toUpperCase().includes(keyword))
}

async function loadDomains(): Promise<void> {
  domainsLoading.value = true
  domainsError.value = ''
  try {
    domains.value = await listReprintDomains()
    if (!batchDomainKey.value) batchDomainKey.value = domains.value[0]?.key ?? ''
    if (activeTab.value === 'batch') void ensureBatchInitialized()
  } catch (error) {
    domains.value = []
    domainsError.value = handleError(error, '业务域加载失败')
  } finally {
    domainsLoading.value = false
  }
}

async function loadPrinters(): Promise<void> {
  printersLoading.value = true
  try {
    const list = await listPrinters()
    printers.value = list
    const current = list.find((printer) => printer.id === selectedPrinterId.value)
    if (current) return
    const preferred =
      list.find((printer) => printer.isDefault && printer.online) ??
      list.find((printer) => printer.isDefault) ??
      list.find((printer) => printer.online) ??
      list[0]
    selectedPrinterId.value = preferred?.id ?? null
  } catch (error) {
    printers.value = []
    selectedPrinterId.value = null
    ElMessage.error(handleError(error, '打印机列表加载失败，请检查打印机绑定'))
  } finally {
    printersLoading.value = false
  }
}

/* ───────────────────────── Tab1：人员 → 分类 → 记录 ───────────────────────── */

const keyword = ref('')
const filterYear = ref(CURRENT_YEAR)
const filterMonth = ref(0)
const filterDomain = ref('')

const filterDomainOptions = computed(() => [
  { key: '', name: '全部分类' },
  ...domains.value.map((domain) => ({ key: domain.key, name: domain.name })),
])

const persons = ref<ReprintPerson[]>([])
const personsLoading = ref(false)
const personsError = ref('')
const selectedPersonKey = ref('')

const selectedPerson = computed(
  () => persons.value.find((person) => person.key === selectedPersonKey.value) ?? null,
)

const branches = ref<ReprintPersonDomain[]>([])
const branchesLoading = ref(false)
const branchesError = ref('')
const selectedBranchDomain = ref('')

const selectedBranch = computed(
  () => branches.value.find((branch) => branch.domain === selectedBranchDomain.value) ?? null,
)

const branchRecords = ref<ReprintRecord[]>([])
const recordsLoading = ref(false)
const recordsError = ref('')
const selectedRecordId = ref('')

const selectedRecord = computed(
  () => branchRecords.value.find((record) => record.id === selectedRecordId.value) ?? null,
)

const templates = ref<ReprintTemplate[]>([])
const templatesLoading = ref(false)
const templatesError = ref('')
const selectedTemplateIds = ref<number[]>([])

const selectedTemplates = computed(() =>
  templates.value.filter((template) => selectedTemplateIds.value.includes(template.id)),
)
const applicableTemplates = computed(() => templates.value.filter((template) => template.isApplicable))
const allTemplatesSelected = computed(
  () =>
    applicableTemplates.value.length > 0 &&
    applicableTemplates.value.every((template) => selectedTemplateIds.value.includes(template.id)),
)

function selectAllTemplates(): void {
  selectedTemplateIds.value = applicableTemplates.value.map((template) => template.id)
}

function clearTemplates(): void {
  selectedTemplateIds.value = []
}

const history = ref<ReprintHistoryItem[]>([])
const historyLoading = ref(false)
const historyError = ref('')

let personTicket = 0
let branchTicket = 0
let recordTicket = 0
let templateTicket = 0
let historyTicket = 0

function stopJobPolling(): void {
  if (jobPollTimer !== undefined) {
    window.clearInterval(jobPollTimer)
    jobPollTimer = undefined
  }
  jobPollTicks = 0
}

function clearTemplatesAndHistory(): void {
  templates.value = []
  selectedTemplateIds.value = []
  templatesError.value = ''
  history.value = []
  historyError.value = ''
  submission.value = null
  stopJobPolling()
}

function clearRecordDownstream(): void {
  branchRecords.value = []
  selectedRecordId.value = ''
  recordsError.value = ''
  clearTemplatesAndHistory()
}

function clearSelectionDownstream(): void {
  branches.value = []
  selectedBranchDomain.value = ''
  branchesError.value = ''
  clearRecordDownstream()
}

function resetDefaultSubmission(): void {
  submission.value = null
  stopJobPolling()
}

async function loadPersons(): Promise<void> {
  const ticket = ++personTicket
  personsLoading.value = true
  personsError.value = ''
  persons.value = []
  selectedPersonKey.value = ''
  clearSelectionDownstream()

  try {
    const list = await listReprintPersons({
      keyword: keyword.value.trim(),
      year: filterYear.value,
      month: filterMonth.value,
      domain: filterDomain.value,
    })
    if (ticket !== personTicket) return
    persons.value = list
  } catch (error) {
    if (ticket !== personTicket) return
    persons.value = []
    personsError.value = handleError(error, '人员名单加载失败')
  } finally {
    if (ticket === personTicket) personsLoading.value = false
  }
}

function selectPerson(person: ReprintPerson): void {
  if (selectedPersonKey.value === person.key) return
  selectedPersonKey.value = person.key
  clearSelectionDownstream()
  void loadBranches()
}

async function loadBranches(): Promise<void> {
  const ticket = ++branchTicket
  const person = selectedPerson.value
  branchesLoading.value = true
  branchesError.value = ''
  branches.value = []
  selectedBranchDomain.value = ''
  clearRecordDownstream()
  if (!person) {
    branchesLoading.value = false
    return
  }

  let list: ReprintPersonDomain[] = []
  try {
    list = await listReprintPersonDomains(person.key, {
      year: filterYear.value,
      month: filterMonth.value,
    })
  } catch (error) {
    if (ticket === branchTicket) {
      branchesError.value = handleError(error, '名下分类加载失败')
    }
  } finally {
    if (ticket === branchTicket) {
      branchesLoading.value = false
      if (!branchesError.value) branches.value = list
    }
  }

  if (ticket !== branchTicket) return
  if (list.length === 1) await selectBranch(list[0])
}

async function selectBranch(branch: ReprintPersonDomain): Promise<void> {
  if (selectedBranchDomain.value === branch.domain) return
  selectedBranchDomain.value = branch.domain
  clearRecordDownstream()
  await loadRecords()
}

async function loadRecords(): Promise<void> {
  const ticket = ++recordTicket
  const person = selectedPerson.value
  const domain = selectedBranchDomain.value
  recordsLoading.value = true
  recordsError.value = ''
  branchRecords.value = []
  selectedRecordId.value = ''
  clearTemplatesAndHistory()
  if (!person || !domain) {
    recordsLoading.value = false
    return
  }

  let list: ReprintRecord[] = []
  try {
    list = await listReprintRecords({
      domain,
      idCard: person.idCard || person.key,
      year: filterYear.value,
      month: filterMonth.value,
    })
  } catch (error) {
    if (ticket === recordTicket) {
      recordsError.value = handleError(error, '记录列表加载失败')
    }
  } finally {
    if (ticket === recordTicket) {
      recordsLoading.value = false
      if (!recordsError.value) branchRecords.value = list
    }
  }

  if (ticket !== recordTicket) return
  if (list.length > 0) selectRecord(list[0])
}

function selectRecord(record: ReprintRecord): void {
  if (selectedRecordId.value === record.id) return
  selectedRecordId.value = record.id
  clearTemplatesAndHistory()
  void loadRecordDetails(record)
}

async function loadRecordDetails(record: ReprintRecord): Promise<void> {
  const domain = selectedBranchDomain.value
  if (!domain) return
  await Promise.all([loadRecordTemplates(record, domain), loadRecordHistory(record, domain)])
}

async function loadRecordTemplates(record: ReprintRecord, domain: string): Promise<void> {
  const ticket = ++templateTicket
  templatesLoading.value = true
  templatesError.value = ''
  templates.value = []
  selectedTemplateIds.value = []
  try {
    const list = await listReprintTemplates(domain, record.id)
    if (ticket !== templateTicket) return
    templates.value = list
    selectedTemplateIds.value = list
      .filter((template) => template.isSelected && template.isApplicable)
      .map((template) => template.id)
  } catch (error) {
    if (ticket !== templateTicket) return
    templates.value = []
    templatesError.value = handleError(error, '输出模板加载失败')
  } finally {
    if (ticket === templateTicket) templatesLoading.value = false
  }
}

async function loadRecordHistory(record: ReprintRecord, domain: string): Promise<void> {
  const ticket = ++historyTicket
  historyLoading.value = true
  historyError.value = ''
  history.value = []
  try {
    const list = await listReprintHistory(domain, record.id)
    if (ticket !== historyTicket) return
    history.value = list
  } catch (error) {
    if (ticket !== historyTicket) return
    history.value = []
    historyError.value = handleError(error, '历史打印留痕加载失败')
  } finally {
    if (ticket === historyTicket) historyLoading.value = false
  }
}

function refreshPersons(): void {
  void loadPersons()
}

function onFilterChanged(): void {
  void loadPersons()
}

function refreshSelectedPerson(): void {
  if (selectedPerson.value) {
    clearSelectionDownstream()
    void loadBranches()
  }
}

/* ───────────────────────── 打印设置 / 任务提交 ───────────────────────── */

const copies = ref(1)
const isDuplex = ref(false)
const submittingMode = ref<ReprintJobMode | ''>('')
const originalLoading = ref(false)
const submission = ref<Submission | null>(null)

const submissionStatus = computed(() => {
  const current = submission.value
  if (!current) return 'Pending'
  if (current.mode === 'original') return 'Completed'
  if (current.jobs.length === 0) return 'Pending'
  if (current.jobs.every((job) => isCompletedStatus(job.status))) return 'Completed'
  if (current.jobs.every((job) => isTerminalStatus(job.status))) {
    return current.jobs.some((job) => isFailedStatus(job.status)) ? 'PartialFailed' : 'Completed'
  }
  if (current.jobs.some((job) => isRunningStatus(job.status))) return 'Processing'
  return 'Pending'
})

const submissionStatusLabel = computed(() => {
  const status = submissionStatus.value
  if (status === 'PartialFailed') return '部分失败'
  return jobStatusLabel(status)
})

const submissionStatusTone = computed<'online' | 'offline' | 'idle' | 'default'>(() => {
  const status = submissionStatus.value
  if (status === 'PartialFailed') return 'idle'
  return jobStatusTone(status)
})

let jobPollTimer: number | undefined
let jobPollTicks = 0
let jobPollBusy = false

function startJobPolling(): void {
  stopJobPolling()
  void refreshSubmission()
  jobPollTimer = window.setInterval(() => void refreshSubmission(), 4_000)
}

async function refreshSubmission(): Promise<void> {
  const current = submission.value
  if (!current || current.mode === 'original' || jobPollBusy) return
  jobPollBusy = true
  try {
    const page = await listPrintJobs({ pageIndex: 1, pageSize: 50 })
    if (submission.value !== current) return
    const statusMap = new Map(page.items.map((item) => [item.jobNo, item.status]))
    const allTerminal = current.jobs.every((job) => {
      const status = statusMap.get(job.jobNo)
      if (status) job.status = status
      return isTerminalStatus(job.status)
    })
    if (allTerminal) {
      stopJobPolling()
      if (current.mode !== 'preview') {
        const record = selectedRecord.value
        const domain = selectedBranchDomain.value
        if (record && domain) void loadRecordHistory(record, domain)
      }
      return
    }
    jobPollTicks += 1
    if (jobPollTicks >= 90) stopJobPolling()
  } catch {
    // 轮询失败不打断页面；用户可重新提交或刷新历史。
  } finally {
    jobPollBusy = false
  }
}

async function submitJob(mode: ReprintJobMode): Promise<void> {
  const record = selectedRecord.value
  const domain = selectedBranchDomain.value
  if (!record || !domain) {
    ElMessage.warning('请先选择人员与分类记录')
    return
  }
  if (selectedTemplateIds.value.length === 0) {
    ElMessage.warning('请至少勾选一个输出模板')
    return
  }
  const printer = selectedPrinter.value
  if (mode !== 'preview' && !printer) {
    ElMessage.warning('请先在「打印机绑定」中绑定并选择打印机')
    return
  }

  submittingMode.value = mode
  try {
    const created = await createReprintJob({
      domain,
      id: record.id,
      mode,
      source: 'template',
      templateIds: [...selectedTemplateIds.value],
      copies: Math.max(1, copies.value),
      isDuplex: isDuplex.value,
      printerBindingId: printer?.id,
      printerName: printer?.printerName,
    })
    const jobs: SubmissionJob[] =
      created.jobs.length > 0
        ? created.jobs.map((job: ReprintJobItem) => ({
            jobNo: job.jobNo,
            status: job.status || created.status,
            templateName: job.templateName,
          }))
        : [{ jobNo: created.jobNo, status: created.status, templateName: '' }]

    submission.value = {
      mode,
      jobs,
      templateNames: selectedTemplates.value.map((template) => template.name),
      submittedAt: new Date(),
      note: '',
    }
    startJobPolling()
    ElMessage.success(`已提交${modeLabel(mode)}任务：${jobs.map((job) => job.jobNo).join('、')}`)
  } catch (error) {
    ElMessage.error(handleError(error, `${modeLabel(mode)}任务提交失败`))
  } finally {
    submittingMode.value = ''
  }
}

/** 原样补打：带鉴权拉取 PDF 流，交给浏览器新窗口预览；失败回退下载。 */
async function openOriginal(): Promise<void> {
  const record = selectedRecord.value
  const domain = selectedBranchDomain.value
  if (!record || !domain) {
    ElMessage.warning('请先选择人员与分类记录')
    return
  }

  const popup = window.open('', '_blank')
  if (popup) {
    popup.document.title = '原始档案 PDF'
    if (popup.document.body) {
      popup.document.body.innerHTML =
        '<p style="font-family:sans-serif;padding:24px;color:#6d6354">正在加载原始 PDF…</p>'
    }
  }

  originalLoading.value = true
  try {
    const blob = await fetchReprintOriginal(domain, record.id, 0)
    const url = URL.createObjectURL(blob)
    const fileName = `原始档案_${record.name || record.id}.pdf`

    if (popup) {
      popup.location.replace(url)
    } else {
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = fileName
      anchor.style.display = 'none'
      document.body.appendChild(anchor)
      anchor.click()
      anchor.remove()
    }
    window.setTimeout(() => URL.revokeObjectURL(url), 120_000)
    submission.value = {
      mode: 'original',
      jobs: [],
      templateNames: [],
      submittedAt: new Date(),
      note: popup
        ? '已在新窗口打开原始 PDF（如被拦截，请允许本站弹窗后重试）。'
        : '浏览器阻止了新窗口，已下载原始 PDF。',
    }
    ElMessage.success('已打开原始 PDF')
  } catch (error) {
    popup?.close()
    ElMessage.error(handleError(error, '原样补打失败，可能该记录没有原始 PDF'))
  } finally {
    originalLoading.value = false
  }
}

/* ───────────────────────── Tab2：批量打印 ───────────────────────── */

const batchInitialized = ref(false)
const batchDomainKey = ref('')
const batchMode = ref<BatchMode>('archive')
const batchYear = ref(CURRENT_YEAR)
const batchMonth = ref(0)
const batchKeyword = ref('')

const batchDomain = computed(
  () => domains.value.find((domain) => domain.key === batchDomainKey.value) ?? null,
)
const batchIsDynamic = computed(
  () => hasMode(batchDomain.value, 'DYNAMIC') || hasMode(batchDomain.value, '动态'),
)
const batchIsAsset = computed(
  () => hasMode(batchDomain.value, 'ASSET') || hasMode(batchDomain.value, '资产'),
)
const batchNeedsMonth = computed(() => batchIsDynamic.value && batchMonth.value === 0)
const batchShowTemplatePicker = computed(
  () => !batchIsDynamic.value && batchMode.value === 'template',
)

const batchTemplates = ref<DocumentTemplate[]>([])
const batchTemplatesLoading = ref(false)
const batchTemplatesError = ref('')
const batchTemplateId = ref<number | null>(null)

const batchPersons = ref<ReprintPerson[]>([])
const batchLoading = ref(false)
const batchError = ref('')
const batchSummary = ref('')

const batchSubmitting = ref(false)
const batchResult = ref<ReprintBatchCreated | null>(null)
const batchStatus = ref<ReprintBatchStatus | null>(null)
const batchSubmission = ref<BatchSubmissionSnapshot | null>(null)
const batchRetryStatus = ref<Record<string, string>>({})
const retryingItem = ref('')

let batchTicket = 0

const batchModeHint = computed(() => {
  if (batchIsDynamic.value) {
    return '动态管理域：所选月份内的变更人群合并为一份 PDF，由本机 Agent 执行（具体以后端返回为准）。'
  }
  if (batchIsAsset.value) {
    return '资产核查：按筛选结果逐户打印，失败项会在下方进度明细中单独列出，可逐项重提。'
  }
  return '低收入人口 / 临时救助 / 普惠高龄：按筛选结果逐户打印；可选整份档案或指定模板。'
})

const batchCardCount = computed(() => batchStatus.value?.total ?? batchResult.value?.total ?? 0)

const batchProgress = computed(() => {
  const status = batchStatus.value
  if (!status || status.total <= 0) return 0
  const finished = status.completed + status.failed
  return Math.max(0, Math.min(100, Math.round((finished / status.total) * 100)))
})

const batchFailedItems = computed(() => {
  const items = batchStatus.value?.items ?? []
  return items.filter((item) => Boolean(item.error) || isFailedStatus(item.status))
})

const batchStatusText = computed(() => {
  const status = batchStatus.value
  if (!status) return batchResult.value ? '正在创建任务…' : ''
  if (status.done) {
    return status.failed > 0
      ? `批量完成：成功 ${status.completed}，失败 ${status.failed}`
      : `批量完成：成功 ${status.completed}`
  }
  return `执行中：已完成 ${status.completed}，失败 ${status.failed}，共 ${status.total}`
})

function batchItemFailed(item: { status?: unknown; error?: unknown }): boolean {
  const error = typeof item.error === 'string' ? item.error : ''
  const status = typeof item.status === 'string' ? item.status : ''
  return Boolean(error) || isFailedStatus(status)
}

async function loadBatchTemplates(): Promise<void> {
  const domain = batchDomainKey.value
  batchTemplates.value = []
  batchTemplateId.value = null
  batchTemplatesError.value = ''
  if (!domain || batchIsDynamic.value) return

  batchTemplatesLoading.value = true
  try {
    const list = await listDocumentTemplates(domain)
    if (domain !== batchDomainKey.value) return
    batchTemplates.value = list
    batchTemplateId.value = list[0]?.id ?? null
  } catch (error) {
    if (domain === batchDomainKey.value) {
      batchTemplatesError.value = handleError(error, '批量模板加载失败')
    }
  } finally {
    if (domain === batchDomainKey.value) batchTemplatesLoading.value = false
  }
}

async function loadBatchPersons(): Promise<void> {
  const domain = batchDomainKey.value
  const ticket = ++batchTicket
  batchError.value = ''
  batchSummary.value = ''

  if (!domain) {
    batchPersons.value = []
    return
  }
  if (batchNeedsMonth.value) {
    batchPersons.value = []
    batchSummary.value = '动态管理域请选择具体月份后再筛选。'
    return
  }

  batchLoading.value = true
  try {
    const list = await listReprintPersons({
      keyword: batchIsDynamic.value ? '' : batchKeyword.value.trim(),
      year: batchYear.value,
      month: batchMonth.value,
      domain,
    })
    if (ticket !== batchTicket) return
    batchPersons.value = list
    const scope = `${batchYear.value} 年${batchMonth.value ? ` ${batchMonth.value} 月` : '全部月份'}`
    batchSummary.value = `${scope} · ${batchDomain.value?.name ?? domain} · 命中 ${list.length} 人`
  } catch (error) {
    if (ticket !== batchTicket) return
    batchPersons.value = []
    batchError.value = handleError(error, '批量名单加载失败')
  } finally {
    if (ticket === batchTicket) batchLoading.value = false
  }
}

async function ensureBatchInitialized(): Promise<void> {
  if (!domains.value.length) return
  if (!batchDomainKey.value) batchDomainKey.value = domains.value[0]?.key ?? ''
  if (batchInitialized.value) return
  batchInitialized.value = true
  await loadBatchTemplates()
  await loadBatchPersons()
}

function switchTab(tab: TabKey): void {
  activeTab.value = tab
  if (tab === 'batch') void ensureBatchInitialized()
}

async function onBatchDomainChanged(): Promise<void> {
  batchRetryStatus.value = {}
  await loadBatchTemplates()
  await loadBatchPersons()
}

function onBatchFilterChanged(): void {
  void loadBatchPersons()
}

function refreshBatchList(): void {
  void loadBatchPersons()
}

let batchPollTimer: number | undefined
let batchPollTicks = 0
let batchPollBusy = false
let batchNotified = false

function stopBatchPolling(): void {
  if (batchPollTimer !== undefined) {
    window.clearInterval(batchPollTimer)
    batchPollTimer = undefined
  }
  batchPollTicks = 0
}

async function refreshBatchStatus(batchNo: string): Promise<void> {
  if (batchPollBusy) return
  batchPollBusy = true
  try {
    const status = await getReprintBatch(batchNo)
    if (batchResult.value?.batchNo !== batchNo) return
    batchStatus.value = status
    const finished =
      status.done ||
      (status.total > 0 && status.completed + status.failed >= status.total)
    if (finished) {
      stopBatchPolling()
      if (!batchNotified) {
        batchNotified = true
        if (status.failed > 0) {
          ElMessage.warning(`批量完成：成功 ${status.completed}，失败 ${status.failed}`)
        } else {
          ElMessage.success(`批量完成：成功 ${status.completed}`)
        }
      }
      return
    }
    batchPollTicks += 1
    if (batchPollTicks >= 150) stopBatchPolling()
  } catch {
    // 网络抖动不终止轮询；用户可稍后刷新。
  } finally {
    batchPollBusy = false
  }
}

function startBatchPolling(batchNo: string): void {
  stopBatchPolling()
  batchNotified = false
  void refreshBatchStatus(batchNo)
  batchPollTimer = window.setInterval(() => void refreshBatchStatus(batchNo), 3_000)
}

async function startBatch(): Promise<void> {
  const domain = batchDomainKey.value
  if (!domain) {
    ElMessage.warning('请选择业务域')
    return
  }
  if (batchNeedsMonth.value) {
    ElMessage.warning('动态管理域请选择具体月份后再批量')
    return
  }
  if (batchPersons.value.length === 0) {
    ElMessage.warning('当前筛选结果为空，请先刷新名单')
    return
  }
  const templateIds: number[] =
    batchShowTemplatePicker.value && batchTemplateId.value !== null
      ? [batchTemplateId.value]
      : []
  if (batchShowTemplatePicker.value && templateIds.length === 0) {
    ElMessage.warning('请选择批量模板')
    return
  }
  const printer = selectedPrinter.value
  if (!printer) {
    ElMessage.warning('请先绑定并选择打印机')
    return
  }

  try {
    await ElMessageBox.confirm(
      `即将对「${batchDomain.value?.name ?? domain}」${batchYear.value} 年${
        batchMonth.value ? ` ${batchMonth.value} 月` : '（全部月份）'
      }的 ${batchPersons.value.length} 人执行批量${batchIsDynamic.value ? '合并导出' : '打印'}，是否继续？`,
      '开始批量',
      {
        confirmButtonText: '开始',
        cancelButtonText: '取消',
        type: 'warning',
      },
    )
  } catch {
    return
  }

  batchSubmitting.value = true
  batchRetryStatus.value = {}
  try {
    const created = await createReprintBatch({
      domain,
      year: batchYear.value,
      month: batchMonth.value,
      templateIds,
      mode: 'print',
      copies: 1,
      isDuplex: isDuplex.value,
      keyword: batchIsDynamic.value ? undefined : batchKeyword.value.trim() || undefined,
    })
    batchResult.value = created
    batchStatus.value = {
      total: created.total,
      completed: 0,
      failed: 0,
      items: created.jobs.map((job) => ({
        jobNo: job.jobNo,
        businessId: job.businessId,
        name: job.name,
        status: 'Pending',
        error: '',
      })),
      done: false,
    }
    batchSubmission.value = {
      batchNo: created.batchNo,
      domain,
      year: batchYear.value,
      month: batchMonth.value,
      templateIds: [...templateIds],
      copies: 1,
      isDuplex: isDuplex.value,
      printerBindingId: printer.id,
      printerName: printer.printerName,
    }
    startBatchPolling(created.batchNo)
    ElMessage.success(`批量任务已提交：${created.batchNo || '—'}，共 ${created.total} 户`)
  } catch (error) {
    ElMessage.error(handleError(error, '批量任务提交失败'))
  } finally {
    batchSubmitting.value = false
  }
}

/** 单条失败重提（仅「指定模板」模式可用；整份档案需回原记录重打）。 */
async function retryBatchItem(item: ReprintBatchItem): Promise<void> {
  const snapshot = batchSubmission.value
  if (!snapshot) return
  if (snapshot.templateIds.length === 0) {
    ElMessage.warning('整份档案批量失败项无法单独重提，请回到该人员记录重新打印')
    return
  }
  if (!item.businessId) {
    ElMessage.warning('该失败项缺少业务编号，无法重提')
    return
  }
  if (retryingItem.value) return

  retryingItem.value = item.jobNo
  try {
    const created = await createReprintJob({
      domain: snapshot.domain,
      id: item.businessId,
      mode: 'print',
      source: 'template',
      templateIds: snapshot.templateIds,
      copies: snapshot.copies,
      isDuplex: snapshot.isDuplex,
      printerBindingId: snapshot.printerBindingId,
      printerName: snapshot.printerName,
    })
    const newJobNo = created.jobs[0]?.jobNo || created.jobNo
    batchRetryStatus.value = { ...batchRetryStatus.value, [item.jobNo]: newJobNo }
    ElMessage.success(`已重提：${item.name || item.jobNo}`)
  } catch (error) {
    ElMessage.error(handleError(error, '失败项重提失败'))
  } finally {
    retryingItem.value = ''
  }
}

function retryBatchRow(row: unknown): void {
  void retryBatchItem(row as ReprintBatchItem)
}

/* ───────────────────────── 生命周期 ───────────────────────── */

onMounted(async () => {
  await Promise.all([loadDomains(), loadPrinters()])
  await loadPersons()
  if (activeTab.value === 'batch') void ensureBatchInitialized()
})

onBeforeUnmount(() => {
  stopJobPolling()
  stopBatchPolling()
})
</script>

<template>
  <div class="nc-page reprint-page">
    <NcPageHeader
      title="历史补打中心"
      description="按人检索跨域文书 · 支持按月批量补打"
    >
      <template #actions>
        <div class="mode-switch" role="tablist" aria-label="补打模式">
          <button
            type="button"
            class="mode-switch__item"
            :class="{ 'is-active': activeTab === 'person' }"
            role="tab"
            :aria-selected="activeTab === 'person'"
            @click="switchTab('person')"
          >
            <el-icon><User /></el-icon>
            按人补打
          </button>
          <button
            type="button"
            class="mode-switch__item"
            :class="{ 'is-active': activeTab === 'batch' }"
            role="tab"
            :aria-selected="activeTab === 'batch'"
            @click="switchTab('batch')"
          >
            <el-icon><Files /></el-icon>
            批量打印
          </button>
        </div>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="permissionDenied"
      type="error"
      :closable="false"
      show-icon
      title="无权限使用历史补打中心"
      description="当前账号缺少补打/打印权限（如 REPRINT_VIEW 或对应业务域打印权限），请联系管理员授权后刷新页面。"
    />

    <el-alert
      v-else-if="domainsError"
      type="error"
      :closable="false"
      show-icon
      :title="domainsError"
    >
      <el-button link type="primary" :loading="domainsLoading" @click="loadDomains()">
        重新加载
      </el-button>
    </el-alert>

    <template v-if="activeTab === 'person'">
      <div class="person-layout">
        <!-- ══════════ 左栏：搜索 → 人 → 分类 → 记录 ══════════ -->
        <div class="person-column">
          <NcCard title="筛选条件" padding="sm">
            <div class="filter-stack">
              <div class="keyword-row">
                <el-input
                  v-model="keyword"
                  placeholder="按姓名 / 身份证筛选（可留空）"
                  clearable
                  :prefix-icon="Search"
                  @keyup.enter="refreshPersons"
                />
                <el-button type="primary" :icon="Search" :loading="personsLoading" @click="refreshPersons">
                  搜索
                </el-button>
              </div>

              <el-select v-model="filterYear" class="filter-select" @change="onFilterChanged">
                <el-option v-for="year in YEAR_OPTIONS" :key="year" :label="`${year} 年`" :value="year" />
              </el-select>

              <el-select v-model="filterMonth" class="filter-select" @change="onFilterChanged">
                <el-option
                  v-for="month in MONTH_OPTIONS"
                  :key="month.value"
                  :label="month.label"
                  :value="month.value"
                />
              </el-select>

              <el-select
                v-model="filterDomain"
                class="filter-select"
                :loading="domainsLoading"
                @change="onFilterChanged"
              >
                <el-option
                  v-for="option in filterDomainOptions"
                  :key="option.key || 'all'"
                  :label="option.name"
                  :value="option.key"
                />
              </el-select>

              <p class="tiny-note">
                月份按各业务域自身周期筛选（低收入 / 动态管理 B 线、资产核查 A 线、普惠高龄自然月、临时救助 C 线），以后端返回口径为准。
              </p>
            </div>
          </NcCard>

          <NcCard title="人员名单" padding="sm">
            <template #actions>
              <span class="count-chip">{{ persons.length }}</span>
              <el-button
                link
                type="primary"
                :icon="Refresh"
                :loading="personsLoading"
                @click="refreshPersons"
              >
                刷新
              </el-button>
            </template>

            <el-skeleton v-if="personsLoading && persons.length === 0" :rows="5" animated />

            <el-alert
              v-else-if="personsError"
              type="error"
              :closable="false"
              show-icon
              :title="personsError"
            />

            <el-empty
              v-else-if="persons.length === 0"
              :image-size="64"
              description="没有匹配的人员"
            />

            <div v-else class="list-scroll person-scroll">
              <button
                v-for="person in persons"
                :key="person.key"
                type="button"
                class="list-row person-row"
                :class="{ 'is-selected': person.key === selectedPersonKey }"
                @click="selectPerson(person)"
              >
                <span class="row-main">
                  <strong>{{ person.name || '—' }}</strong>
                  <span class="row-sub">{{ person.idCard || '—' }}</span>
                  <span class="row-domain">
                    {{ person.domainNames.length ? person.domainNames.join(' · ') : '—' }}
                  </span>
                </span>
                <span class="row-side">
                  <span class="row-time">{{ formatDay(person.latestTime) }}</span>
                  <span v-if="person.count > 0" class="count-chip count-chip--small">
                    {{ person.count }}
                  </span>
                </span>
              </button>
            </div>
          </NcCard>

          <NcCard v-if="selectedPerson" title="名下分类（点击查看文书）" padding="sm">
            <template #actions>
              <el-button
                link
                type="primary"
                :icon="Refresh"
                :loading="branchesLoading"
                @click="refreshSelectedPerson"
              >
                刷新
              </el-button>
            </template>

            <el-skeleton v-if="branchesLoading && branches.length === 0" :rows="3" animated />

            <el-alert
              v-else-if="branchesError"
              type="error"
              :closable="false"
              show-icon
              :title="branchesError"
            />

            <el-empty
              v-else-if="branches.length === 0"
              :image-size="56"
              description="该人员当前条件下没有分类记录"
            />

            <div v-else class="list-scroll branch-scroll">
              <button
                v-for="branch in branches"
                :key="branch.domain"
                type="button"
                class="list-row"
                :class="{ 'is-selected': branch.domain === selectedBranchDomain }"
                @click="selectBranch(branch)"
              >
                <span class="row-main">
                  <strong>{{ branch.name }}</strong>
                  <span class="row-sub">最近 {{ formatDay(branch.latestTime) }}</span>
                </span>
                <span class="count-chip count-chip--small">{{ branch.count }}</span>
              </button>
            </div>
          </NcCard>

          <NcCard v-if="selectedBranch" title="记录（点击切换）" padding="sm">
            <template #actions>
              <span class="count-chip">{{ branchRecords.length }}</span>
            </template>

            <el-skeleton v-if="recordsLoading && branchRecords.length === 0" :rows="3" animated />

            <el-alert
              v-else-if="recordsError"
              type="error"
              :closable="false"
              show-icon
              :title="recordsError"
            />

            <el-empty
              v-else-if="branchRecords.length === 0"
              :image-size="56"
              description="该分类下暂无记录"
            />

            <div v-else class="list-scroll record-scroll">
              <button
                v-for="record in branchRecords"
                :key="record.id"
                type="button"
                class="list-row record-row"
                :class="{ 'is-selected': record.id === selectedRecordId }"
                @click="selectRecord(record)"
              >
                <span class="record-main">
                  <strong>{{ formatDay(record.businessDate) }}</strong>
                  <span v-if="record.versionLabel" class="record-version">
                    {{ record.versionLabel }}
                  </span>
                  <span v-if="record.classification" class="record-chip">
                    {{ record.classification }}
                  </span>
                  <span v-if="record.status" class="record-chip record-chip--status">
                    {{ record.status }}
                  </span>
                </span>
                <span class="row-sub truncate">{{ record.extra || record.no || '—' }}</span>
              </button>
            </div>
          </NcCard>
        </div>

        <!-- ══════════ 中栏：摘要 / 模板 / 打印设置 / 留痕 ══════════ -->
        <div class="person-column">
          <NcCard v-if="!selectedRecord" title="文书与打印" padding="sm">
            <el-empty :image-size="72" description="请先选择左侧人员与分类记录">
              <p class="tiny-note">选中记录后可选择输出模板、打印机并提交预览 / 打印 / 保存。</p>
            </el-empty>
          </NcCard>

          <template v-else>
            <NcCard title="选中记录摘要" padding="sm">
              <div class="record-summary">
                <div class="summary-head">
                  <span class="summary-avatar">
                    <el-icon :size="20"><User /></el-icon>
                  </span>
                  <div>
                    <strong>{{ maskName(selectedRecord.name) }}</strong>
                    <p class="row-sub">{{ maskIdCard(selectedRecord.idCard) }}</p>
                  </div>
                </div>
                <div class="summary-grid">
                  <div>
                    <span>业务分类</span>
                    <strong>{{ selectedBranch?.name || domainName(selectedBranchDomain) }}</strong>
                  </div>
                  <div>
                    <span>业务日期</span>
                    <strong>{{ formatDay(selectedRecord.businessDate) }}</strong>
                  </div>
                  <div>
                    <span>状态</span>
                    <strong>{{ selectedRecord.status || '—' }}</strong>
                  </div>
                  <div>
                    <span>版本</span>
                    <strong>{{ selectedRecord.versionLabel || '—' }}</strong>
                  </div>
                  <div class="span-two">
                    <span>补充说明</span>
                    <strong>{{ selectedRecord.extra || '—' }}</strong>
                  </div>
                  <div v-if="selectedRecord.no" class="span-two">
                    <span>文号</span>
                    <strong>{{ selectedRecord.no }}</strong>
                  </div>
                </div>
              </div>
            </NcCard>

            <NcCard title="输出模板" padding="sm">
              <template #actions>
                <el-button link type="primary" :disabled="applicableTemplates.length === 0 || allTemplatesSelected" @click="selectAllTemplates">
                  全选
                </el-button>
                <el-button link type="primary" :disabled="selectedTemplateIds.length === 0" @click="clearTemplates">
                  清空
                </el-button>
              </template>

              <el-skeleton v-if="templatesLoading" :rows="4" animated />

              <el-alert
                v-else-if="templatesError"
                type="error"
                :closable="false"
                show-icon
                :title="templatesError"
              >
                <el-button link type="primary" @click="selectedRecord && loadRecordDetails(selectedRecord)">
                  重新加载
                </el-button>
              </el-alert>

              <el-empty v-else-if="templates.length === 0" :image-size="64" description="该记录暂无可用输出模板" />

              <el-checkbox-group v-else v-model="selectedTemplateIds" class="template-list">
                <el-checkbox
                  v-for="template in templates"
                  :key="template.id"
                  :value="template.id"
                  :disabled="!template.isApplicable"
                  class="template-row"
                >
                  <span class="template-content">
                    <span class="template-name">{{ template.name }}</span>
                    <span class="template-tags">
                      <span v-if="template.fileType" class="record-chip">{{ template.fileType }}</span>
                      <span v-if="template.docType" class="record-chip">{{ template.docType }}</span>
                      <span v-if="!template.isApplicable" class="record-chip record-chip--muted">不适用</span>
                    </span>
                  </span>
                </el-checkbox>
              </el-checkbox-group>

              <p class="tiny-note">
                已选 {{ selectedTemplateIds.length }} / {{ templates.length }} 个模板
              </p>
            </NcCard>

            <NcCard title="打印设置" padding="sm">
              <div class="settings-form">
                <label class="field">
                  <span>打印机</span>
                  <el-select
                    v-model="selectedPrinterId"
                    placeholder="选择本机绑定的打印机"
                    :loading="printersLoading"
                    filterable
                    class="field-control"
                  >
                    <el-option
                      v-for="printer in printers"
                      :key="printer.id"
                      :label="`${printer.printerName}${printer.isDefault ? '（默认）' : ''}${printer.online ? '' : '（离线）'}`"
                      :value="printer.id"
                    />
                  </el-select>
                </label>

                <label class="field field--inline">
                  <span>份数</span>
                  <el-input-number v-model="copies" :min="1" :max="99" controls-position="right" />
                </label>

                <label class="field field--inline">
                  <span>双面打印</span>
                  <el-switch v-model="isDuplex" />
                </label>
              </div>

              <p v-if="!printersLoading && printers.length === 0" class="tiny-note tiny-note--warn">
                当前账号未绑定打印机，请先前往「打印机绑定」生成绑定码并完成绑定；预览仍可提交。
              </p>

              <div class="action-row">
                <el-button
                  type="primary"
                  :icon="View"
                  :loading="submittingMode === 'preview'"
                  :disabled="!!submittingMode"
                  @click="submitJob('preview')"
                >
                  预览
                </el-button>
                <el-button
                  type="primary"
                  :icon="Printer"
                  :loading="submittingMode === 'print'"
                  :disabled="!!submittingMode"
                  @click="submitJob('print')"
                >
                  打印
                </el-button>
                <el-button
                  type="success"
                  :icon="Download"
                  :loading="submittingMode === 'save'"
                  :disabled="!!submittingMode"
                  @click="submitJob('save')"
                >
                  保存
                </el-button>
              </div>

              <el-divider class="soft-divider" />

              <el-button
                class="original-button"
                type="warning"
                plain
                :icon="Files"
                :loading="originalLoading"
                @click="openOriginal"
              >
                原样补打（浏览器打开原始 PDF）
              </el-button>
              <p class="tiny-note">
                原样补打直接读取历史 PDF 流，不经过本机 Agent；若浏览器拦截弹窗将自动下载。
              </p>
            </NcCard>

            <NcCard title="历史打印留痕" padding="sm">
              <template #actions>
                <span class="count-chip">{{ history.length }}</span>
              </template>

              <el-skeleton v-if="historyLoading" :rows="3" animated />

              <el-alert
                v-else-if="historyError"
                type="error"
                :closable="false"
                show-icon
                :title="historyError"
              />

              <el-empty
                v-else-if="history.length === 0"
                :image-size="56"
                description="该记录暂无历史打印留痕"
              />

              <div v-else class="history-list">
                <article v-for="(item, index) in history" :key="`${item.createdAt}-${index}`" class="history-row">
                  <div class="history-main">
                    <strong>{{ item.templateName }}</strong>
                    <span class="row-sub">打印机：{{ item.printerName || '—' }}</span>
                  </div>
                  <div class="history-side">
                    <span>{{ formatDateTime(item.createdAt) }}</span>
                    <span class="row-sub">{{ maskName(item.operatorName) }}</span>
                    <span v-if="item.status" class="record-chip">{{ item.status }}</span>
                  </div>
                </article>
              </div>
            </NcCard>
          </template>
        </div>

        <!-- ══════════ 右栏：状态面板（不内嵌 PDF） ══════════ -->
        <div class="person-column status-column">
          <NcCard title="任务状态" padding="sm">
            <div v-if="!submission" class="status-empty">
              <el-icon :size="46"><View /></el-icon>
              <p>选择左侧人员与分类后点击预览</p>
              <span>预览 / 打印 / 保存由本机 Agent 执行</span>
            </div>

            <div v-else class="status-body">
              <div class="status-headline">
                <NcStatusDot :status="submissionStatusTone" :label="submissionStatusLabel" />
                <span class="mode-badge">{{ modeLabel(submission.mode) }}</span>
              </div>

              <dl class="status-fields">
                <div>
                  <dt>方式</dt>
                  <dd>{{ modeLabel(submission.mode) }}</dd>
                </div>
                <div>
                  <dt>提交时间</dt>
                  <dd>{{ formatDateTime(submission.submittedAt.toISOString()) }}</dd>
                </div>
                <div>
                  <dt>模板</dt>
                  <dd>{{ submission.templateNames.length ? submission.templateNames.join('、') : '—' }}</dd>
                </div>
                <div v-if="submission.jobs.length">
                  <dt>任务号</dt>
                  <dd class="status-jobs">
                    <span v-for="job in submission.jobs" :key="job.jobNo">
                      {{ job.jobNo }}
                      <small>{{ jobStatusLabel(job.status) }}</small>
                    </span>
                  </dd>
                </div>
              </dl>

              <div v-if="submission.jobs.length" class="status-job-list">
                <div v-for="job in submission.jobs" :key="job.jobNo" class="status-job">
                  <div>
                    <strong>{{ job.jobNo }}</strong>
                    <span v-if="job.templateName" class="row-sub">{{ job.templateName }}</span>
                  </div>
                  <NcStatusDot
                    :status="jobStatusTone(job.status)"
                    :label="jobStatusLabel(job.status)"
                  />
                </div>
              </div>

              <el-alert
                v-if="submission.note"
                type="success"
                :closable="false"
                show-icon
                :title="submission.note"
              />

              <div class="agent-note">
                <el-icon><InfoFilled /></el-icon>
                <span>预览 / 打印 / 保存由本机 Agent 执行；请确认 Agent 在线并已绑定目标打印机。</span>
              </div>

              <el-button class="status-reset" text @click="resetDefaultSubmission">
                清除状态
              </el-button>
            </div>
          </NcCard>

          <NcCard title="当前选择" padding="sm">
            <dl class="status-fields">
              <div>
                <dt>人员</dt>
                <dd>{{ selectedPerson ? maskName(selectedPerson.name) : '—' }}</dd>
              </div>
              <div>
                <dt>分类</dt>
                <dd>{{ selectedBranch?.name || '—' }}</dd>
              </div>
              <div>
                <dt>记录</dt>
                <dd>{{ selectedRecord ? formatDay(selectedRecord.businessDate) : '—' }}</dd>
              </div>
              <div>
                <dt>打印机</dt>
                <dd>{{ selectedPrinter?.printerName || '未选择' }}</dd>
              </div>
            </dl>
          </NcCard>
        </div>
      </div>
    </template>

    <!-- ══════════ Tab2 批量打印 ══════════ -->
    <template v-else>
      <div class="batch-layout">
        <NcCard title="批量条件" padding="sm">
          <template #actions>
            <el-button link type="primary" :icon="Refresh" :loading="batchLoading" @click="refreshBatchList">
              刷新名单
            </el-button>
          </template>

          <div class="batch-form">
            <label class="field">
              <span>业务域</span>
              <el-select
                v-model="batchDomainKey"
                class="field-control"
                :loading="domainsLoading"
                @change="onBatchDomainChanged"
              >
                <el-option
                  v-for="domain in domains"
                  :key="domain.key"
                  :label="domain.name"
                  :value="domain.key"
                />
              </el-select>
            </label>

            <label v-if="!batchIsDynamic" class="field">
              <span>批量方式</span>
              <el-select v-model="batchMode" class="field-control">
                <el-option label="整份档案（逐户全部适用模板）" value="archive" />
                <el-option label="指定模板（逐户该模板）" value="template" />
              </el-select>
            </label>

            <label class="field">
              <span>年份</span>
              <el-select v-model="batchYear" class="field-control" @change="onBatchFilterChanged">
                <el-option v-for="year in YEAR_OPTIONS" :key="year" :label="`${year} 年`" :value="year" />
              </el-select>
            </label>

            <label class="field">
              <span>月份</span>
              <el-select v-model="batchMonth" class="field-control" @change="onBatchFilterChanged">
                <el-option
                  v-for="month in MONTH_OPTIONS"
                  :key="month.value"
                  :label="month.label"
                  :value="month.value"
                />
              </el-select>
            </label>

            <label v-if="!batchIsDynamic" class="field field--wide">
              <span>关键词</span>
              <el-input
                v-model="batchKeyword"
                placeholder="可留空 = 按年月加载全部"
                clearable
                :prefix-icon="Search"
                @keyup.enter="refreshBatchList"
              />
            </label>

            <label v-if="batchShowTemplatePicker" class="field field--wide">
              <span>模板</span>
              <el-select
                v-model="batchTemplateId"
                class="field-control"
                placeholder="选择批量模板"
                :loading="batchTemplatesLoading"
                filterable
              >
                <el-option
                  v-for="template in batchTemplates"
                  :key="template.id"
                  :label="template.name"
                  :value="template.id"
                />
              </el-select>
            </label>
          </div>

          <el-alert
            v-if="batchTemplatesError"
            class="inline-alert"
            type="error"
            :closable="false"
            show-icon
            :title="batchTemplatesError"
          />

          <el-alert
            v-if="batchNeedsMonth"
            class="inline-alert"
            type="warning"
            :closable="false"
            show-icon
            title="动态管理域请选择具体月份（不支持全部月份）"
          />

          <p class="batch-hint">
            <el-icon><InfoFilled /></el-icon>
            <span>{{ batchModeHint }}</span>
          </p>

          <div class="batch-settings">
            <label class="field">
              <span>打印机</span>
              <el-select
                v-model="selectedPrinterId"
                class="field-control"
                placeholder="选择打印机"
                :loading="printersLoading"
                filterable
              >
                <el-option
                  v-for="printer in printers"
                  :key="printer.id"
                  :label="`${printer.printerName}${printer.isDefault ? '（默认）' : ''}${printer.online ? '' : '（离线）'}`"
                  :value="printer.id"
                />
              </el-select>
            </label>

            <label class="field field--inline">
              <span>双面打印</span>
              <el-switch v-model="isDuplex" />
            </label>

            <el-button
              type="primary"
              :icon="Printer"
              :loading="batchSubmitting"
              :disabled="batchSubmitting || batchNeedsMonth"
              @click="startBatch"
            >
              开始批量
            </el-button>
          </div>

          <p class="tiny-note">
            批量任务由本机 Agent 领取执行；低收入 / 临时救助 / 高龄、资产核查逐户打印，动态管理合并 PDF（若后端返回不同，以实际进度为准）。
          </p>
        </NcCard>

        <NcCard title="筛选结果" padding="sm">
          <template #actions>
            <span class="count-chip">{{ batchPersons.length }}</span>
          </template>

          <p v-if="batchSummary" class="batch-summary">{{ batchSummary }}</p>

          <el-skeleton v-if="batchLoading && batchPersons.length === 0" :rows="5" animated />

          <el-alert
            v-else-if="batchError"
            type="error"
            :closable="false"
            show-icon
            :title="batchError"
          />

          <el-empty
            v-else-if="batchPersons.length === 0"
            :image-size="64"
            description="暂无筛选结果（先筛选记录再执行批量）"
          />

          <div v-else class="list-scroll batch-result-list">
            <article v-for="person in batchPersons" :key="person.key" class="batch-person">
              <div class="batch-person-main">
                <strong>{{ person.name || '—' }}</strong>
                <span>{{ person.idCard || '—' }}</span>
                <span class="row-domain">
                  {{ person.domainNames.length ? person.domainNames.join(' · ') : '—' }}
                </span>
              </div>
              <div class="batch-person-side">
                <span>{{ formatDay(person.latestTime) }}</span>
                <span v-if="person.count > 0" class="count-chip count-chip--small">
                  {{ person.count }}
                </span>
              </div>
            </article>
          </div>
        </NcCard>

        <NcCard v-if="batchResult" title="批量进度" padding="sm">
          <div class="batch-progress-head">
            <div>
              <strong>{{ batchResult.batchNo || '批量任务' }}</strong>
              <p class="row-sub">共 {{ batchCardCount }} 户 · 由本机 Agent 执行</p>
            </div>
            <span class="count-chip">{{ batchCardCount }}</span>
          </div>

          <div class="batch-stats">
            <NcStat label="总数" :value="batchStatus?.total ?? batchResult.total" tone="ink" />
            <NcStat label="已完成" :value="batchStatus?.completed ?? 0" tone="pine" />
            <NcStat label="失败" :value="batchStatus?.failed ?? 0" tone="seal" />
          </div>

          <el-progress
            class="batch-progress"
            :percentage="batchProgress"
            :stroke-width="10"
            :status="batchStatus?.failed ? 'exception' : undefined"
          />

          <p class="batch-progress-text">{{ batchStatusText }}</p>

          <el-alert
            v-if="batchFailedItems.length > 0"
            class="inline-alert"
            type="error"
            :closable="false"
            show-icon
            :title="`有 ${batchFailedItems.length} 户失败，请查看下方失败明细`"
          />

          <el-table
            v-if="batchStatus && batchStatus.items.length > 0"
            :data="batchStatus.items"
            class="batch-table"
            size="small"
            max-height="340"
            row-key="jobNo"
          >
            <el-table-column label="姓名" min-width="110">
              <template #default="{ row }">{{ row.name || '—' }}</template>
            </el-table-column>
            <el-table-column prop="jobNo" label="任务号" min-width="150" />
            <el-table-column label="状态" width="100">
              <template #default="{ row }">
                <NcStatusDot
                  :status="jobStatusTone(row.status)"
                  :label="jobStatusLabel(row.status)"
                />
              </template>
            </el-table-column>
            <el-table-column label="错误" min-width="200">
              <template #default="{ row }">
                <span v-if="row.error" class="batch-error">{{ row.error }}</span>
                <span v-else class="nc-muted">—</span>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="110" align="right">
              <template #default="{ row }">
                <span v-if="batchRetryStatus[row.jobNo]" class="retry-done">
                  已重提 {{ batchRetryStatus[row.jobNo] }}
                </span>
                <el-button
                  v-else-if="batchItemFailed(row)"
                  link
                  type="primary"
                  :loading="retryingItem === row.jobNo"
                  @click="retryBatchRow(row)"
                >
                  重提
                </el-button>
                <span v-else class="nc-muted">—</span>
              </template>
            </el-table-column>
          </el-table>

          <el-empty
            v-else-if="!batchStatus"
            :image-size="56"
            description="正在获取批量任务进度…"
          />

          <p class="tiny-note">
            失败可单独重提（仅「指定模板」模式可用）；整份档案失败项请回到该人员记录重新打印。
          </p>
        </NcCard>
      </div>
    </template>
  </div>
</template>

<style scoped>
.reprint-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

/* ── Tab 切换 ─────────────────────────────────────────────────── */
.mode-switch {
  display: inline-flex;
  padding: 2px;
  border: 1px solid var(--nc-line-strong);
  background-color: var(--nc-paper-3);
}

.mode-switch__item {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  height: 36px;
  padding: 0 18px;
  border: 0;
  background: transparent;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-md);
  font-weight: 600;
  cursor: pointer;
  transition:
    background-color var(--nc-transition),
    color var(--nc-transition);
}

.mode-switch__item:hover {
  color: var(--nc-seal);
}

.mode-switch__item.is-active {
  background-color: var(--nc-seal);
  color: #fff8f2;
}

/* ── 三栏 ─────────────────────────────────────────────────────── */
.person-layout {
  display: grid;
  grid-template-columns: 320px 400px minmax(0, 1fr);
  gap: 12px;
  align-items: start;
}

.person-column {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}

.status-column {
  position: sticky;
  top: 12px;
}

.filter-stack {
  display: flex;
  flex-direction: column;
  gap: 9px;
}

.keyword-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 8px;
}

.filter-select,
.field-control {
  width: 100%;
}

.tiny-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.7;
}

.tiny-note--warn {
  color: var(--nc-amber-dark);
}

.count-chip {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 24px;
  height: 22px;
  padding: 0 7px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  line-height: 1;
}

.count-chip--small {
  min-width: 20px;
  height: 20px;
  padding: 0 5px;
  border-color: transparent;
  background-color: var(--nc-paper-4);
  color: var(--nc-muted-2);
}

/* ── 列表行 ───────────────────────────────────────────────────── */
.list-scroll {
  display: flex;
  flex-direction: column;
  gap: 4px;
  overflow-y: auto;
  padding-right: 2px;
}

.person-scroll {
  max-height: 330px;
}

.branch-scroll {
  max-height: 210px;
}

.record-scroll {
  max-height: 240px;
}

.list-row {
  display: flex;
  flex: none;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  width: 100%;
  padding: 9px 10px;
  border: 1px solid transparent;
  background-color: var(--nc-paper-soft);
  color: var(--nc-ink);
  text-align: left;
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition);
}

.list-row:hover {
  border-color: var(--nc-line-strong);
  background-color: var(--nc-paper-2);
}

.list-row.is-selected {
  border-color: rgba(171, 50, 39, 0.42);
  background-color: var(--nc-seal-wash);
}

.row-main {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
  line-height: 1.45;
}

.row-main strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.row-sub {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.row-domain {
  overflow: hidden;
  color: var(--nc-seal);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.row-side {
  display: flex;
  flex: none;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
}

.row-time {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.record-row {
  flex-direction: column;
  align-items: stretch;
  gap: 4px;
}

.record-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 5px;
}

.record-main strong {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  font-weight: 700;
}

.record-version {
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-xs);
}

.record-chip {
  display: inline-flex;
  align-items: center;
  max-width: 100%;
  padding: 1px 6px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
  line-height: 1.55;
  white-space: nowrap;
}

.record-chip--status {
  border-color: rgba(46, 71, 56, 0.24);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.record-chip--muted {
  border-color: transparent;
  background-color: var(--nc-paper-4);
  color: var(--nc-muted);
}

.truncate {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── 选中记录摘要 ─────────────────────────────────────────────── */
.summary-head {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
}

.summary-head strong {
  font-size: var(--nc-text-lg);
  color: var(--nc-ink);
}

.summary-avatar {
  display: grid;
  flex: none;
  place-items: center;
  width: 38px;
  height: 38px;
  border: 1px solid rgba(171, 50, 39, 0.3);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal);
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 12px;
}

.summary-grid > div {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.summary-grid span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.summary-grid strong {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  line-height: 1.55;
  text-overflow: ellipsis;
}

.span-two {
  grid-column: 1 / -1;
}

/* ── 模板 ─────────────────────────────────────────────────────── */
.template-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
  max-height: 250px;
  overflow-y: auto;
}

.template-row {
  width: 100%;
  height: auto;
  min-height: 40px;
  padding: 6px 7px;
  border: 1px solid transparent;
  background-color: var(--nc-paper-soft);
}

.template-row:hover {
  border-color: var(--nc-line);
}

.template-row :deep(.el-checkbox__label) {
  min-width: 0;
  white-space: normal;
}

.template-content {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.template-name {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  line-height: 1.5;
}

.template-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

/* ── 打印设置 ─────────────────────────────────────────────────── */
.settings-form {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 10px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
}

.field > span {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.04em;
}

.field--inline {
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.action-row {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  margin-top: 13px;
}

.action-row :deep(.el-button) {
  width: 100%;
  margin-left: 0;
}

.soft-divider {
  margin: 14px 0 10px;
}

.original-button {
  width: 100%;
}

/* ── 留痕 ─────────────────────────────────────────────────────── */
.history-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-height: 240px;
  overflow-y: auto;
}

.history-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
  padding: 8px 9px;
  background-color: var(--nc-paper-soft);
}

.history-main {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.history-main strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.history-side {
  display: flex;
  flex: none;
  flex-direction: column;
  align-items: flex-end;
  gap: 2px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  text-align: right;
}

/* ── 右栏状态 ─────────────────────────────────────────────────── */
.status-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 9px;
  min-height: 280px;
  padding: 22px 10px;
  border: 1px dashed var(--nc-line);
  background-color: var(--nc-paper-soft);
  color: var(--nc-muted);
  text-align: center;
}

.status-empty p {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-md);
  font-weight: 600;
}

.status-empty span {
  font-size: var(--nc-text-xs);
}

.status-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.status-headline {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.mode-badge {
  padding: 2px 8px;
  border: 1px solid rgba(171, 50, 39, 0.32);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  white-space: nowrap;
}

.status-fields {
  display: flex;
  flex-direction: column;
  gap: 7px;
  margin: 0;
}

.status-fields > div {
  display: grid;
  grid-template-columns: 68px minmax(0, 1fr);
  gap: 8px;
  align-items: start;
}

.status-fields dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.status-fields dd {
  margin: 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  word-break: break-all;
}

.status-jobs {
  display: flex;
  flex-direction: column;
  gap: 3px;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs) !important;
}

.status-jobs span {
  display: flex;
  justify-content: space-between;
  gap: 8px;
}

.status-jobs small {
  color: var(--nc-muted);
  font-family: var(--nc-font-sans);
  font-size: var(--nc-text-xs);
}

.status-job-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.status-job {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 7px 8px;
  background-color: var(--nc-paper-soft);
}

.status-job > div {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.status-job strong {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.agent-note {
  display: flex;
  align-items: flex-start;
  gap: 7px;
  padding: 9px 10px;
  border-left: 2px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.agent-note .el-icon {
  flex: none;
  margin-top: 2px;
}

.status-reset {
  align-self: flex-start;
}

/* ── 批量 ─────────────────────────────────────────────────────── */
.batch-layout {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.batch-form {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px 12px;
}

.field--wide {
  grid-column: span 2;
}

.inline-alert {
  margin-top: 12px;
}

.batch-hint {
  display: flex;
  align-items: flex-start;
  gap: 7px;
  margin-top: 12px;
  padding: 9px 10px;
  border-left: 2px solid var(--nc-seal);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.batch-hint .el-icon {
  flex: none;
  margin-top: 2px;
}

.batch-settings {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) auto auto;
  gap: 10px 14px;
  align-items: end;
  margin-top: 13px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.batch-result-list {
  max-height: 300px;
}

.batch-person {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
  padding: 8px 10px;
  background-color: var(--nc-paper-soft);
}

.batch-person-main {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.batch-person-main strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
}

.batch-person-main span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.batch-person-side {
  display: flex;
  flex: none;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.batch-summary {
  margin-bottom: 8px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.batch-progress-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.batch-progress-head strong {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-md);
  color: var(--nc-ink);
}

.batch-stats {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  margin: 12px 0;
}

.batch-progress {
  margin: 4px 0 8px;
}

.batch-progress-text {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.batch-table {
  margin-top: 12px;
}

.batch-error {
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
  word-break: break-word;
}

.retry-done {
  color: var(--nc-pine);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

/* ── 响应式 ───────────────────────────────────────────────────── */
@media (max-width: 1280px) {
  .person-layout {
    grid-template-columns: 300px 370px minmax(0, 1fr);
  }
}

@media (max-width: 1100px) {
  .person-layout {
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  }

  .status-column {
    position: static;
    grid-column: 1 / -1;
  }

  .batch-form {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 860px) {
  .person-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .status-column {
    grid-column: auto;
  }

  .batch-form,
  .batch-settings {
    grid-template-columns: minmax(0, 1fr);
  }

  .field--wide {
    grid-column: auto;
  }

  .batch-settings {
    align-items: stretch;
  }

  .batch-settings .el-button {
    width: 100%;
  }

  .batch-stats {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

@media (max-width: 600px) {
  .mode-switch {
    width: 100%;
  }

  .mode-switch__item {
    flex: 1;
    justify-content: center;
    padding: 0 10px;
  }

  .keyword-row {
    grid-template-columns: minmax(0, 1fr);
  }

  .keyword-row .el-button {
    width: 100%;
  }

  .action-row {
    grid-template-columns: minmax(0, 1fr);
  }

  .batch-stats {
    grid-template-columns: minmax(0, 1fr);
  }

  .summary-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
