<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  ArrowLeft,
  Camera,
  Document,
  Printer,
  Refresh,
  Search,
  UserFilled,
  View,
} from '@element-plus/icons-vue'
import { ApiError, getErrorMessage } from '@/api/http'
import { listPrinters } from '@/api/printers'
import { listPrintJobs } from '@/api/archivePrint'
import {
  createArchiveProofJob,
  getArchiveCurrent,
  getArchiveImported,
  getArchiveProofHistory,
  getArchiveProofTemplates,
  getArchiveStats,
  searchArchives,
} from '@/api/archive'
import type { PrinterBinding } from '@/types/api'
import type {
  ArchiveCurrentDetail,
  ArchiveProofGenericTemplate,
  ArchiveProofHistoryItem,
  ArchiveProofJobInput,
  ArchiveProofJobMode,
  ArchiveProofJobResult,
  ArchiveProofUnitTemplate,
  ArchiveSearchItem,
  ArchiveStats,
  ImportedFamilyDetail,
} from '@/types/archive'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import {
  archiveJobStatusLabel,
  archiveJobStatusTone,
  classificationLabel,
  formatAmount,
  formatCount,
  formatDateTimeText,
  formatDecimal,
  isArchiveJobTerminal,
} from './archiveDisplay'

const router = useRouter()

/* ───────────────────────── 页头统计 ───────────────────────── */

const stats = ref<ArchiveStats | null>(null)
const statsLoading = ref(false)
const statsError = ref('')

const totalArchivesText = computed(() =>
  statsLoading.value ? '…' : formatCount(stats.value?.totalArchives),
)
const monthlyArchivesText = computed(() =>
  statsLoading.value ? '…' : formatCount(stats.value?.monthlyArchives),
)

async function loadStats(): Promise<void> {
  statsLoading.value = true
  statsError.value = ''
  try {
    stats.value = await getArchiveStats()
  } catch (error) {
    stats.value = null
    statsError.value = getErrorMessage(error, '归档统计加载失败')
  } finally {
    statsLoading.value = false
  }
}

/* ───────────────────────── 搜索 ───────────────────────── */

const keyword = ref('')
const searching = ref(false)
const searchError = ref('')
const keywordWarning = ref('')
const hasSearched = ref(false)
const results = ref<ArchiveSearchItem[]>([])
let searchTicket = 0

watch(keyword, () => {
  if (keywordWarning.value) keywordWarning.value = ''
})

function resultKey(item: ArchiveSearchItem): string {
  return item.isFromCurrentLibrary
    ? `current:${item.applicationId ?? ''}`
    : `imported:${item.sourceTable}:${item.sourceId ?? ''}`
}

async function runSearch(): Promise<void> {
  const value = keyword.value.trim()
  if (!value) {
    keywordWarning.value = '请输入姓名或身份证号后再搜索'
    ElMessage.warning('请输入姓名或身份证号后搜索')
    return
  }

  keywordWarning.value = ''
  const ticket = ++searchTicket
  searching.value = true
  searchError.value = ''
  try {
    const response = await searchArchives(value)
    if (ticket !== searchTicket) return
    results.value = response.items ?? []
    hasSearched.value = true
  } catch (error) {
    if (ticket !== searchTicket) return
    results.value = []
    hasSearched.value = true
    searchError.value = getErrorMessage(error, '档案搜索失败')
  } finally {
    if (ticket === searchTicket) searching.value = false
  }
}

/* ───────────────────────── 选中档案 ───────────────────────── */

const selected = ref<ArchiveSearchItem | null>(null)
const currentDetail = ref<ArchiveCurrentDetail | null>(null)
const importedDetail = ref<ImportedFamilyDetail | null>(null)
const detailLoading = ref(false)
const detailError = ref('')

const isCurrentLibrary = computed(() => selected.value?.isFromCurrentLibrary === true)

const currentSummary = computed(() => {
  const summary = currentDetail.value?.summary
  const form = currentDetail.value?.form
  const basic = form?.basic
  const classification = form?.classification
  return {
    name:
      summary?.applicantName ||
      basic?.applicantName ||
      selected.value?.applicantName ||
      '—',
    idCard:
      summary?.applicantIdCard ||
      basic?.applicantIdCard ||
      selected.value?.applicantIdCard ||
      '—',
    classification:
      classificationLabel(
        summary?.classificationName ||
          summary?.classification ||
          summary?.classificationResult ||
          classification?.classificationText ||
          classification?.classificationResult,
      ) ||
      selected.value?.sourceDisplay ||
      '—',
    address:
      summary?.address || selected.value?.address || basic?.address || '—',
    familySize:
      summary?.familySize ??
      (form?.members?.length || selected.value?.familySize || 0),
    totalAmount:
      summary?.totalAmount ||
      summary?.totalGuaranteeAmount ||
      summary?.guaranteeAmount ||
      classification?.totalGuaranteeAmount ||
      selected.value?.totalAmount ||
      0,
  }
})

const displayName = computed(() =>
  isCurrentLibrary.value
    ? currentSummary.value.name
    : importedDetail.value?.applicantName || selected.value?.applicantName || '—',
)

const displayIdCard = computed(() =>
  isCurrentLibrary.value
    ? currentSummary.value.idCard
    : importedDetail.value?.applicantIdCard ||
      selected.value?.applicantIdCard ||
      '—',
)

const displayClassification = computed(() => {
  if (!selected.value) return ''
  if (isCurrentLibrary.value) return currentSummary.value.classification
  return (
    classificationLabel(importedDetail.value?.derivedClassificationCode) ||
    selected.value.sourceDisplay ||
    '—'
  )
})

const currentRows = computed(() => [
  { label: '姓名', value: currentSummary.value.name },
  { label: '身份证', value: currentSummary.value.idCard },
  { label: '分类', value: currentSummary.value.classification },
  { label: '地址', value: currentSummary.value.address },
  { label: '家庭人口', value: `${currentSummary.value.familySize} 人` },
  { label: '保障金额', value: formatAmount(currentSummary.value.totalAmount, '元/月') },
])

const importedRows = computed(() => {
  const detail = importedDetail.value
  if (!detail) return []
  const amount =
    detail.monthlyGuaranteeAmount > 0
      ? detail.monthlyGuaranteeAmount
      : detail.totalAmount
  return [
    {
      label: '姓名',
      value: detail.applicantName || selected.value?.applicantName || '—',
    },
    {
      label: '身份证',
      value: detail.applicantIdCard || selected.value?.applicantIdCard || '—',
    },
    {
      label: '分类',
      value:
        classificationLabel(detail.derivedClassificationCode) ||
        selected.value?.sourceDisplay ||
        '—',
    },
    {
      label: '地址',
      value:
        detail.address ||
        selected.value?.address ||
        [detail.district, detail.town, detail.community]
          .filter((part) => Boolean(part))
          .join('') ||
        '—',
    },
    { label: '家庭人口', value: `${detail.familySize || 0} 人` },
    { label: '保障金额', value: formatAmount(amount, '元/月') },
    {
      label: '人均收入',
      value:
        detail.perCapitaIncome !== null && detail.perCapitaIncome !== undefined
          ? `${formatDecimal(detail.perCapitaIncome)} 元/年`
          : '—',
    },
    { label: '户口类型', value: detail.hukouType || '—' },
    { label: '供养方式', value: detail.supportMode || '—' },
    { label: '首次享受', value: detail.firstReceiveMonth || '—' },
    { label: '申请原因', value: detail.applyReason || '—' },
  ]
})

const summaryRows = computed(() =>
  isCurrentLibrary.value ? currentRows.value : importedRows.value,
)

const memberRows = computed(() => importedDetail.value?.members ?? [])

const hasArchiveDetail = computed(() =>
  isCurrentLibrary.value
    ? currentDetail.value !== null
    : importedDetail.value !== null,
)

async function selectArchive(item: ArchiveSearchItem): Promise<void> {
  selected.value = item
  currentDetail.value = null
  importedDetail.value = null
  detailError.value = ''
  detailLoading.value = true
  lastJob.value = null
  stopPolling()
  results.value = []
  hasSearched.value = false
  searchTicket += 1
  searching.value = false

  // 切换档案时立即作废旧的历史请求，避免旧响应写回新选中项。
  historyTicket += 1
  history.value = []
  historyError.value = ''
  historyLoading.value = false

  try {
    if (item.isFromCurrentLibrary) {
      if (item.applicationId === null) throw new Error('当前库档案缺少申请编号')
      currentDetail.value = await getArchiveCurrent(item.applicationId)
    } else {
      if (!item.sourceTable || item.sourceId === null) {
        throw new Error('导入库档案缺少来源信息')
      }
      importedDetail.value = await getArchiveImported({
        source: item.source,
        sourceTable: item.sourceTable,
        sourceId: item.sourceId,
      })
    }
  } catch (error) {
    detailError.value = getErrorMessage(error, '档案信息加载失败')
  } finally {
    detailLoading.value = false
  }

  void loadHistory()
}

function viewCurrentArchive(): void {
  const id = selected.value?.applicationId ?? currentDetail.value?.applicationId
  if (id === null || id === undefined) {
    ElMessage.warning('当前档案缺少申请编号')
    return
  }
  void router.push(`/applications/${id}`)
}

/* ───────────────────────── 证明模板 ───────────────────────── */

interface ProofTemplateOption {
  key: string
  isGeneric: boolean
  templateId: number
  templateName: string
  unitName: string
  fileType: string
  displayName: string
}

const genericTemplates = ref<ArchiveProofGenericTemplate[]>([])
const unitTemplates = ref<ArchiveProofUnitTemplate[]>([])
const templatesLoading = ref(false)
const templatesError = ref('')
const selectedTemplateKey = ref('')
const targetUnitName = ref('')

const templateOptions = computed<ProofTemplateOption[]>(() => [
  ...genericTemplates.value.map((template) => ({
    key: `generic:${template.id}`,
    isGeneric: true,
    templateId: template.id,
    templateName: template.name,
    unitName: '',
    fileType: template.fileType,
    displayName: `通配 · ${template.name}`,
  })),
  ...unitTemplates.value.map((template) => ({
    key: `unit:${template.id}`,
    isGeneric: false,
    templateId: template.templateId,
    templateName: template.templateName,
    unitName: template.unitName,
    fileType: template.fileType,
    displayName: `${template.unitName} · ${template.templateName}`,
  })),
])

const genericOptions = computed(() =>
  templateOptions.value.filter((option) => option.isGeneric),
)
const unitOptions = computed(() =>
  templateOptions.value.filter((option) => !option.isGeneric),
)

const selectedTemplate = computed(
  () =>
    templateOptions.value.find(
      (option) => option.key === selectedTemplateKey.value,
    ) ?? null,
)

const isGenericSelected = computed(
  () => selectedTemplate.value?.isGeneric === true,
)
const isUnitSelected = computed(
  () => selectedTemplate.value !== null && !selectedTemplate.value.isGeneric,
)

const noGenericHint = computed(() =>
  !templatesLoading.value && genericTemplates.value.length === 0
    ? '未配置通配证明模板，请到「模板管理」将模板标记为「证明文件」分类。'
    : '',
)

const noUnitHint = computed(() =>
  !templatesLoading.value && unitTemplates.value.length === 0
    ? '未配置单位专属模板，可点击「单位模板维护」为接收单位上传专属模板；也可直接使用通配证明模板。'
    : '',
)

watch(templateOptions, (options) => {
  if (options.some((option) => option.key === selectedTemplateKey.value)) return
  selectedTemplateKey.value =
    options.find((option) => option.isGeneric)?.key ?? options[0]?.key ?? ''
})

async function loadProofTemplates(): Promise<void> {
  templatesLoading.value = true
  templatesError.value = ''
  try {
    const response = await getArchiveProofTemplates()
    genericTemplates.value = response.generic ?? []
    unitTemplates.value = response.units ?? []
  } catch (error) {
    genericTemplates.value = []
    unitTemplates.value = []
    templatesError.value = getErrorMessage(error, '证明模板加载失败')
  } finally {
    templatesLoading.value = false
  }
}

/* ───────────────────────── 打印机 / 打印参数 ───────────────────────── */

const printers = ref<PrinterBinding[]>([])
const selectedPrinterId = ref<number | null>(null)
const printerError = ref('')
const copies = ref(1)
const isDuplex = ref(false)

const selectedPrinter = computed(
  () =>
    printers.value.find((printer) => printer.id === selectedPrinterId.value) ??
    null,
)

async function loadPrinters(): Promise<void> {
  printerError.value = ''
  try {
    const response = await listPrinters()
    printers.value = Array.isArray(response) ? response : []
    const preferred =
      printers.value.find((printer) => printer.isDefault) ?? printers.value[0]
    selectedPrinterId.value = preferred?.id ?? null
  } catch (error) {
    printers.value = []
    selectedPrinterId.value = null
    printerError.value = getErrorMessage(error, '打印机列表加载失败')
  }
}

function normalizedCopies(): number {
  const value = Number.isFinite(copies.value) ? Math.trunc(copies.value) : 1
  return Math.min(99, Math.max(1, value))
}

function printerLabel(printer: PrinterBinding): string {
  const name = printer.printerName || `打印机 #${printer.id}`
  return printer.deviceName ? `${name}（${printer.deviceName}）` : name
}

/* ───────────────────────── 证明任务提交 ───────────────────────── */

interface LastJobRecord {
  job: ArchiveProofJobResult
  mode: ArchiveProofJobMode
  templateName: string
  unitName: string
  printerName: string
  copies: number
  isDuplex: boolean
  submittedAt: Date
}

const MODE_LABELS: Record<ArchiveProofJobMode, string> = {
  preview: '预览',
  print: '打印',
  save: '保存',
}

const submittingMode = ref<ArchiveProofJobMode | ''>('')
const permissionDenied = ref(false)
const lastJob = ref<LastJobRecord | null>(null)
const jobRefreshing = ref(false)

const hasSelectedTemplate = computed(() => selectedTemplate.value !== null)

const canSubmit = computed(
  () =>
    selected.value !== null &&
    hasArchiveDetail.value &&
    hasSelectedTemplate.value &&
    !detailLoading.value &&
    !templatesLoading.value &&
    !submittingMode.value &&
    !permissionDenied.value &&
    (!isGenericSelected.value || targetUnitName.value.trim().length > 0),
)

const submitHint = computed(() => {
  if (!selected.value) return '请先在左侧搜索并选择档案'
  if (detailLoading.value) return '正在加载档案数据…'
  if (!hasArchiveDetail.value) return '档案数据尚未加载完成，请重新选择档案'
  if (templatesLoading.value) return '正在加载证明模板…'
  if (!hasSelectedTemplate.value) {
    return templatesError.value
      ? '证明模板加载失败，请刷新页面重试'
      : '暂无可用的证明模板，请先配置证明模板'
  }
  if (isGenericSelected.value && !targetUnitName.value.trim()) {
    return '通配模板需填写目标证明单位'
  }
  if (permissionDenied.value) {
    return '当前账号无打印证明权限（PRINT_LOWINCOMEPROOF），请联系管理员'
  }
  return ''
})

function modeLabel(mode: ArchiveProofJobMode): string {
  return MODE_LABELS[mode]
}

const jobStatusLabel = computed(() =>
  lastJob.value ? archiveJobStatusLabel(lastJob.value.job.status) : '',
)
const jobStatusTone = computed(() =>
  lastJob.value ? archiveJobStatusTone(lastJob.value.job.status) : 'default',
)

async function submitJob(mode: ArchiveProofJobMode): Promise<void> {
  const item = selected.value
  const template = selectedTemplate.value
  if (!item || !template) return

  const unitName = template.isGeneric
    ? targetUnitName.value.trim()
    : template.unitName
  if (template.isGeneric && !unitName) {
    ElMessage.warning('请填写目标证明单位')
    return
  }

  const payload: ArchiveProofJobInput = {
    templateId: template.templateId,
    targetUnitName: unitName,
    mode,
    copies: normalizedCopies(),
    isDuplex: isDuplex.value,
    printerName: selectedPrinter.value?.printerName || undefined,
    printerBindingId: selectedPrinter.value?.id ?? null,
  }

  if (isCurrentLibrary.value) {
    if (item.applicationId === null) {
      ElMessage.warning('当前库档案缺少申请编号')
      return
    }
    payload.applicationId = item.applicationId
  } else {
    payload.source = item.source
    payload.sourceTable = item.sourceTable
    if (item.sourceId !== null) payload.sourceId = item.sourceId
  }

  submittingMode.value = mode
  try {
    const job = await createArchiveProofJob(payload)
    lastJob.value = {
      job,
      mode,
      templateName: template.templateName,
      unitName,
      printerName: selectedPrinter.value?.printerName ?? '',
      copies: normalizedCopies(),
      isDuplex: isDuplex.value,
      submittedAt: new Date(),
    }
    ElMessage.success('已推送本机 Agent')
    startPolling()
    if (mode !== 'preview') void loadHistory()
  } catch (error) {
    if (error instanceof ApiError && error.status === 403) {
      permissionDenied.value = true
      ElMessage.error('当前账号无打印证明权限（PRINT_LOWINCOMEPROOF）')
    } else {
      ElMessage.error(getErrorMessage(error, `证明${MODE_LABELS[mode]}任务提交失败`))
    }
  } finally {
    submittingMode.value = ''
  }
}

/* ───────────────────────── 任务状态轮询 ───────────────────────── */

let pollTimer: number | undefined
let pollTicks = 0
let pollBusy = false

function stopPolling(): void {
  if (pollTimer !== undefined) {
    window.clearInterval(pollTimer)
    pollTimer = undefined
  }
  pollTicks = 0
}

function startPolling(): void {
  stopPolling()
  pollTimer = window.setInterval(() => {
    void refreshJobStatus(true)
  }, 4000)
}

async function refreshJobStatus(silent: boolean): Promise<void> {
  const record = lastJob.value
  if (!record || pollBusy) return
  pollBusy = true
  if (!silent) jobRefreshing.value = true

  try {
    const page = await listPrintJobs({ pageIndex: 1, pageSize: 50 })
    const found = page.items.find(
      (item) => item.jobNo === record.job.jobNo || item.id === record.job.id,
    )
    if (found) {
      lastJob.value = {
        ...record,
        job: { ...record.job, status: found.status },
      }
      if (isArchiveJobTerminal(found.status)) {
        stopPolling()
        if (record.mode !== 'preview') void loadHistory()
      }
    }
    pollTicks += 1
    if (pollTicks >= 20) stopPolling()
  } catch (error) {
    if (!silent) {
      ElMessage.error(getErrorMessage(error, '任务状态刷新失败'))
    }
  } finally {
    pollBusy = false
    if (!silent) jobRefreshing.value = false
  }
}

/* ───────────────────────── 证明开具历史 ───────────────────────── */

const history = ref<ArchiveProofHistoryItem[]>([])
const historyLoading = ref(false)
const historyError = ref('')
let historyTicket = 0

async function loadHistory(): Promise<void> {
  const ticket = ++historyTicket
  const item = selected.value
  history.value = []
  historyError.value = ''
  if (!item) return

  historyLoading.value = true
  try {
    const response = await getArchiveProofHistory({
      applicationId: isCurrentLibrary.value ? item.applicationId : item.sourceId,
      idCard: item.applicantIdCard || undefined,
    })
    if (ticket !== historyTicket) return
    history.value = response
  } catch (error) {
    if (ticket !== historyTicket) return
    historyError.value = getErrorMessage(error, '证明开具历史加载失败')
  } finally {
    if (ticket === historyTicket) historyLoading.value = false
  }
}

/* ───────────────────────── 页面入口 ───────────────────────── */

function goWorkbench(): void {
  void router.push('/')
}

function goPhotoQuery(): void {
  void router.push('/archives/photos')
}

function goPublicityOutput(): void {
  void router.push('/archives/publicity')
}

function goProofTemplates(): void {
  void router.push('/archives/proof-templates')
}

function goPrinters(): void {
  void router.push('/printers')
}

onMounted(() => {
  void loadStats()
  void loadProofTemplates()
  void loadPrinters()
})

onBeforeUnmount(stopPolling)
</script>

<template>
  <div class="nc-page archive-query-page">
    <NcPageHeader
      title="救助档案管理"
      description="低收入人口历史档案检索查阅"
      kicker="社会救助 · 档案查阅"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goWorkbench">返回工作台</el-button>
        <el-button :icon="Camera" @click="goPhotoQuery">照片查询</el-button>
        <el-button type="primary" :icon="Document" @click="goPublicityOutput">
          每月公示文档输出
        </el-button>
      </template>

      <div class="header-stats">
        <NcStat
          label="归档总数（件）"
          :value="totalArchivesText"
          tone="ink"
          compact
          :hint="statsError ? '统计加载失败' : ''"
        />
        <NcStat
          label="当月归档（件）"
          :value="monthlyArchivesText"
          tone="seal"
          compact
          :hint="statsError ? '统计加载失败' : ''"
        />
      </div>
    </NcPageHeader>

    <el-alert
      class="agent-alert"
      type="info"
      :closable="false"
      show-icon
      title="预览 / 打印 / 保存由本机 Agent 执行，请确保 Agent 已启动并绑定打印机"
    />

    <div class="archive-layout">
      <div class="archive-left">
        <!-- 搜索 -->
        <NcCard title="档案搜索">
          <div class="search-row">
            <el-input
              v-model="keyword"
              placeholder="输入姓名或身份证号搜索（当前库+历史库）"
              clearable
              @keyup.enter="runSearch"
            />
            <el-button
              type="primary"
              :icon="Search"
              :loading="searching"
              @click="runSearch"
            >
              搜索
            </el-button>
          </div>
          <p class="field-hint">
            支持按姓名或身份证号检索当前库与历史导入库档案。
          </p>
          <p v-if="keywordWarning" class="inline-hint is-warning">
            {{ keywordWarning }}
          </p>

          <el-alert
            v-if="searchError"
            class="inline-alert"
            type="error"
            :title="searchError"
            :closable="false"
            show-icon
          />

          <div v-if="searching" class="result-skeleton">
            <el-skeleton :rows="4" animated />
          </div>
          <div v-else-if="results.length > 0" class="result-list">
            <button
              v-for="item in results"
              :key="resultKey(item)"
              type="button"
              class="result-item"
              @click="selectArchive(item)"
            >
              <span class="result-head">
                <span
                  class="source-chip"
                  :class="
                    item.isFromCurrentLibrary ? 'is-current' : 'is-imported'
                  "
                >
                  {{ item.sourceDisplay || (item.isFromCurrentLibrary ? '当前库' : '导入库') }}
                </span>
                <strong class="result-name">{{ item.applicantName || '—' }}</strong>
              </span>
              <span class="result-id">{{ item.applicantIdCard || '—' }}</span>
              <span class="result-address">{{ item.address || '—' }}</span>
              <span v-if="item.matchedPersonInfo" class="result-matched">
                {{ item.matchedPersonInfo }}
              </span>
              <span class="result-select">选择</span>
            </button>
          </div>
          <el-empty
            v-else-if="hasSearched && !searching && !searchError"
            description="未找到匹配的档案，请更换关键词重试"
            :image-size="60"
          />
        </NcCard>

        <!-- 选中档案 -->
        <NcCard v-if="selected" title="选中档案">
          <template #actions>
            <span class="source-chip" :class="isCurrentLibrary ? 'is-current' : 'is-imported'">
              {{ isCurrentLibrary ? '当前库' : selected.sourceDisplay || '导入库' }}
            </span>
          </template>

          <el-skeleton v-if="detailLoading" :rows="6" animated />

          <template v-else>
            <div class="selected-head">
              <span class="selected-avatar">
                <el-icon><UserFilled /></el-icon>
              </span>
              <div class="selected-main">
                <strong>{{ displayName }}</strong>
                <p>{{ displayIdCard }}</p>
                <p class="selected-source">{{ selected.sourceDisplay || '—' }}</p>
              </div>
              <span class="classification-chip">{{ displayClassification }}</span>
            </div>

            <el-alert
              v-if="detailError"
              class="inline-alert"
              type="error"
              :title="detailError"
              :closable="false"
              show-icon
            />

            <dl v-if="summaryRows.length > 0" class="summary-list">
              <div
                v-for="row in summaryRows"
                :key="row.label"
                class="summary-row"
              >
                <dt>{{ row.label }}</dt>
                <dd>{{ row.value }}</dd>
              </div>
            </dl>

            <template v-if="isCurrentLibrary">
              <el-button
                class="block-button"
                :icon="View"
                :disabled="currentDetail === null"
                @click="viewCurrentArchive"
              >
                查看档案信息（只读）
              </el-button>
            </template>

            <template v-else-if="importedDetail">
              <div class="subsection-title">家庭成员</div>
              <div class="member-table">
                <div class="member-head">
                  <span>姓名</span>
                  <span>关系</span>
                  <span>年龄</span>
                  <span>年收入</span>
                  <span>分类施保</span>
                </div>
                <div
                  v-for="(member, index) in memberRows"
                  :key="`${member.idCard || member.name}-${index}`"
                  class="member-row"
                >
                  <span class="member-name">{{ member.name || '—' }}</span>
                  <span>{{ member.relationship || '—' }}</span>
                  <span>{{ member.age ?? '—' }}</span>
                  <span>
                    {{
                      member.annualIncome === null ||
                      member.annualIncome === undefined
                        ? '—'
                        : formatDecimal(member.annualIncome)
                    }}
                  </span>
                  <span>
                    {{
                      member.classifiedSubsidyAmount > 0
                        ? formatDecimal(member.classifiedSubsidyAmount)
                        : '—'
                    }}
                  </span>
                </div>
                <p v-if="memberRows.length === 0" class="empty-line">
                  该家庭无成员明细
                </p>
              </div>
            </template>
          </template>
        </NcCard>

        <!-- 证明打印 -->
        <NcCard v-if="selected" title="打印证明文件">
          <template #actions>
            <el-button size="small" @click="goProofTemplates">单位模板维护</el-button>
          </template>

          <p class="field-hint">
            证明文件模板（通配模板手填目标单位 / 单位专属模板）。
          </p>

          <el-skeleton v-if="templatesLoading" :rows="2" animated />

          <template v-else>
            <el-select
              v-model="selectedTemplateKey"
              class="full-width"
              placeholder="选择证明模板"
              :disabled="templateOptions.length === 0"
            >
              <el-option-group
                v-if="genericOptions.length > 0"
                label="通配证明模板"
              >
                <el-option
                  v-for="option in genericOptions"
                  :key="option.key"
                  :label="option.displayName"
                  :value="option.key"
                />
              </el-option-group>
              <el-option-group
                v-if="unitOptions.length > 0"
                label="单位专属模板"
              >
                <el-option
                  v-for="option in unitOptions"
                  :key="option.key"
                  :label="option.displayName"
                  :value="option.key"
                />
              </el-option-group>
            </el-select>

            <div v-if="isGenericSelected" class="unit-field">
              <span class="field-label">目标证明单位（必填）</span>
              <el-input
                v-model="targetUnitName"
                placeholder="如：XX市民政局 / XX街道办事处"
              />
            </div>

            <div v-else-if="isUnitSelected" class="unit-field">
              <span class="field-label">接收单位</span>
              <div class="unit-readonly">
                {{ selectedTemplate?.unitName || '—' }}
              </div>
            </div>

            <p v-if="selectedTemplate" class="template-name">
              模板：{{ selectedTemplate.templateName }}
              <span v-if="selectedTemplate.fileType" class="template-type">
                · {{ selectedTemplate.fileType }}
              </span>
            </p>

            <el-alert
              v-if="templatesError"
              class="inline-alert"
              type="error"
              :title="templatesError"
              :closable="false"
              show-icon
            />
            <el-button
              v-if="templatesError"
              class="block-button"
              :loading="templatesLoading"
              @click="loadProofTemplates()"
            >
              重新加载证明模板
            </el-button>
            <p v-if="noGenericHint" class="inline-hint is-warning">
              {{ noGenericHint }}
            </p>
            <p v-if="noUnitHint" class="inline-hint is-warning">
              {{ noUnitHint }}
            </p>
            <el-button
              v-if="templateOptions.length === 0 && !templatesLoading"
              class="block-button"
              @click="goProofTemplates"
            >
              前往单位模板维护
            </el-button>

            <div class="print-grid">
              <div class="print-field print-field--wide">
                <span class="field-label">打印机</span>
                <el-select
                  v-model="selectedPrinterId"
                  class="full-width"
                  placeholder="选择打印机"
                  :disabled="printers.length === 0"
                >
                  <el-option
                    v-for="printer in printers"
                    :key="printer.id"
                    :label="printerLabel(printer)"
                    :value="printer.id"
                  />
                </el-select>
              </div>
              <div class="print-field">
                <span class="field-label">份数</span>
                <el-input-number
                  v-model="copies"
                  class="full-width"
                  :min="1"
                  :max="99"
                  :step="1"
                  step-strictly
                  controls-position="right"
                  aria-label="打印份数"
                />
              </div>
              <div class="print-field print-field--switch">
                <span class="field-label">双面打印</span>
                <el-switch v-model="isDuplex" />
              </div>
            </div>

            <p v-if="printerError" class="inline-hint is-warning">
              {{ printerError }}
              <el-button link type="primary" @click="loadPrinters()">重试</el-button>
            </p>
            <p v-else-if="printers.length === 0" class="inline-hint is-warning">
              未绑定打印机，打印任务将由 Agent 使用本机默认打印机；
              <el-button link type="primary" @click="goPrinters">前往打印机绑定</el-button>
            </p>

            <div class="proof-actions">
              <el-button
                type="primary"
                :icon="View"
                :loading="submittingMode === 'preview'"
                :disabled="!canSubmit"
                @click="submitJob('preview')"
              >
                预览
              </el-button>
              <el-button
                :icon="Printer"
                :loading="submittingMode === 'print'"
                :disabled="!canSubmit"
                @click="submitJob('print')"
              >
                打印
              </el-button>
              <el-button
                type="success"
                :loading="submittingMode === 'save'"
                :disabled="!canSubmit"
                @click="submitJob('save')"
              >
                保存
              </el-button>
            </div>

            <p v-if="submitHint" class="inline-hint is-warning">{{ submitHint }}</p>
            <p class="inline-hint">
              提交后由本机 Agent 领取任务；Web 端不内嵌 PDF 预览。
            </p>
          </template>
        </NcCard>

        <!-- 证明开具历史 -->
        <NcCard v-if="selected && !detailLoading" title="证明开具历史">
          <template #actions>
            <span v-if="history.length > 0" class="count-chip">
              {{ history.length }}
            </span>
          </template>

          <el-skeleton v-if="historyLoading" :rows="3" animated />
          <el-alert
            v-else-if="historyError"
            class="inline-alert"
            type="error"
            :title="historyError"
            :closable="false"
            show-icon
          />
          <div v-else-if="history.length > 0" class="history-list">
            <div
              v-for="record in history"
              :key="record.id"
              class="history-item"
            >
              <div class="history-main">
                <strong>{{ record.templateName || '证明文件' }}</strong>
                <p>打印机：{{ record.printerName || '—' }}</p>
              </div>
              <div class="history-meta">
                <span>{{ formatDateTimeText(record.createdAt) }}</span>
                <span>经办人：{{ record.operatorName || '—' }}</span>
              </div>
            </div>
          </div>
          <el-empty
            v-else
            description="该档案暂无证明开具记录"
            :image-size="56"
          />
        </NcCard>
      </div>

      <!-- 右栏：任务状态（不内嵌 PDF） -->
      <div class="archive-right">
        <NcCard
          title="证明任务状态"
          subtitle="预览 / 打印 / 保存由本机 Agent 执行，Web 端不内嵌 PDF"
          padding="lg"
        >
          <div v-if="lastJob" class="job-panel">
            <div class="job-head">
              <span class="job-mode">{{ modeLabel(lastJob.mode) }}</span>
              <NcStatusDot :status="jobStatusTone" :label="jobStatusLabel" />
            </div>

            <dl class="job-meta">
              <div class="job-meta-row">
                <dt>任务号</dt>
                <dd class="mono">{{ lastJob.job.jobNo || `#${lastJob.job.id}` }}</dd>
              </div>
              <div class="job-meta-row">
                <dt>模式</dt>
                <dd>{{ modeLabel(lastJob.mode) }}</dd>
              </div>
              <div class="job-meta-row">
                <dt>状态</dt>
                <dd>
                  {{ jobStatusLabel }}
                  <span v-if="lastJob.job.status" class="muted">
                    （{{ lastJob.job.status }}）
                  </span>
                </dd>
              </div>
              <div class="job-meta-row">
                <dt>模板 / 明细表</dt>
                <dd>{{ lastJob.templateName || '—' }}</dd>
              </div>
              <div class="job-meta-row">
                <dt>接收单位</dt>
                <dd>{{ lastJob.unitName || '—' }}</dd>
              </div>
              <div class="job-meta-row">
                <dt>打印机</dt>
                <dd>{{ lastJob.printerName || 'Agent 默认打印机' }}</dd>
              </div>
              <div class="job-meta-row">
                <dt>份数 / 双面</dt>
                <dd>
                  {{ lastJob.copies }} 份 /
                  {{ lastJob.isDuplex ? '双面' : '单面' }}
                </dd>
              </div>
              <div class="job-meta-row">
                <dt>提交时间</dt>
                <dd>{{ formatDateTimeText(lastJob.submittedAt.toISOString()) }}</dd>
              </div>
            </dl>

            <p class="job-desc">
              已推送本机 Agent，预览 / 打印 / 保存由本机 Agent 执行。
            </p>

            <el-button
              size="small"
              :icon="Refresh"
              :loading="jobRefreshing"
              @click="refreshJobStatus(false)"
            >
              刷新任务状态
            </el-button>
          </div>

          <el-empty
            v-else
            class="preview-empty"
            description="选择左侧档案并点击预览"
            :image-size="120"
          >
            <template #description>
              <p>选择左侧档案并点击预览</p>
              <p class="preview-empty-hint">
                预览、打印与保存均由本机 Agent 执行。
              </p>
            </template>
          </el-empty>

          <div v-if="selected" class="archive-meta">
            <p class="archive-meta-title">{{ displayName }}</p>
            <p class="archive-meta-line">
              {{ displayIdCard }} · {{ selected.sourceDisplay || '—' }}
            </p>
            <p class="archive-meta-line">{{ displayClassification }}</p>
          </div>
        </NcCard>
      </div>
    </div>
  </div>
</template>

<style scoped>
.archive-query-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.header-stats {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  max-width: 420px;
  margin-top: 14px;
}

.agent-alert,
.inline-alert {
  margin: 0;
}

.inline-alert {
  margin-top: 10px;
}

/* ── 两栏工作台 ───────────────────────────────────────────────── */

.archive-layout {
  display: grid;
  grid-template-columns: 440px minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

.archive-left {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}

.archive-right {
  position: sticky;
  top: 16px;
  min-width: 0;
}

/* ── 搜索 ─────────────────────────────────────────────────────── */

.search-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 8px;
}

.search-row :deep(.el-button) {
  margin-left: 0;
}

.field-hint {
  margin-top: 8px;
  font-size: var(--nc-text-sm);
  line-height: 1.65;
  color: var(--nc-muted);
}

.inline-hint {
  margin-top: 8px;
  font-size: var(--nc-text-sm);
  line-height: 1.65;
  color: var(--nc-muted-2);
}

.inline-hint.is-warning {
  color: var(--nc-amber-dark);
}

.result-skeleton {
  margin-top: 12px;
}

.result-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 360px;
  margin-top: 12px;
  overflow-y: auto;
}

.result-item {
  position: relative;
  display: block;
  width: 100%;
  padding: 10px 68px 10px 10px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  font: inherit;
  color: inherit;
  text-align: left;
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition);
}

.result-item:hover {
  border-color: var(--nc-line);
  background-color: var(--nc-paper-2);
}

.result-head {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.source-chip {
  flex: none;
  padding: 1px 7px;
  border-radius: var(--nc-radius-xs);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  line-height: 1.6;
  white-space: nowrap;
}

.source-chip.is-current {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.source-chip.is-imported {
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.result-name {
  overflow: hidden;
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  color: var(--nc-ink);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.result-id,
.result-address {
  display: block;
  margin-top: 2px;
  overflow: hidden;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted-2);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.result-matched {
  display: block;
  margin-top: 2px;
  overflow: hidden;
  font-size: var(--nc-text-sm);
  color: var(--nc-amber-dark);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.result-select {
  position: absolute;
  top: 50%;
  right: 10px;
  padding: 3px 9px;
  transform: translateY(-50%);
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  color: var(--nc-seal);
}

/* ── 选中档案 ─────────────────────────────────────────────────── */

.selected-head {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

.selected-avatar {
  display: grid;
  flex: none;
  place-items: center;
  width: 42px;
  height: 42px;
  border-radius: 50%;
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-size: 20px;
}

.selected-main {
  flex: 1;
  min-width: 0;
}

.selected-main strong {
  display: block;
  overflow: hidden;
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  color: var(--nc-ink);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.selected-main p {
  margin-top: 2px;
  overflow: hidden;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted-2);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.selected-main .selected-source {
  color: var(--nc-muted);
}

.classification-chip {
  flex: none;
  max-width: 130px;
  padding: 2px 8px;
  overflow: hidden;
  border: 1px solid rgba(171, 50, 39, 0.4);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal-wash);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  line-height: 1.6;
  color: var(--nc-seal-dark);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.summary-list {
  display: flex;
  flex-direction: column;
  margin: 14px 0 0;
  border-top: 1px solid var(--nc-line-faint);
}

.summary-row {
  display: grid;
  grid-template-columns: 86px minmax(0, 1fr);
  gap: 10px;
  padding: 8px 0;
  border-bottom: 1px solid var(--nc-line-faint);
}

.summary-row dt {
  font-size: var(--nc-text-sm);
  color: var(--nc-muted);
}

.summary-row dd {
  margin: 0;
  font-size: var(--nc-text-base);
  font-weight: 600;
  line-height: 1.6;
  color: var(--nc-ink-2);
  word-break: break-word;
}

.block-button {
  width: 100%;
  margin-top: 14px;
  margin-left: 0;
}

.subsection-title {
  margin-top: 16px;
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  color: var(--nc-ink);
}

.member-table {
  margin-top: 8px;
  overflow-x: auto;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
}

.member-head,
.member-row {
  display: grid;
  grid-template-columns: minmax(74px, 1fr) 62px 42px 84px 84px;
  gap: 6px;
  min-width: 360px;
  padding: 7px 9px;
  font-size: var(--nc-text-sm);
}

.member-head {
  background-color: var(--nc-paper-3);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.06em;
  color: var(--nc-muted);
}

.member-row {
  border-top: 1px solid var(--nc-line-faint);
  color: var(--nc-ink-2);
}

.member-name {
  overflow: hidden;
  color: var(--nc-ink);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.empty-line {
  padding: 14px 0;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted);
  text-align: center;
}

/* ── 证明打印 ─────────────────────────────────────────────────── */

.full-width {
  width: 100%;
}

.unit-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 12px;
}

.field-label {
  font-size: var(--nc-text-sm);
  font-weight: 600;
  color: var(--nc-muted-2);
}

.unit-readonly {
  padding: 8px 12px;
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  font-size: var(--nc-text-base);
  color: var(--nc-ink);
}

.template-name {
  margin-top: 10px;
  font-size: var(--nc-text-sm);
  font-weight: 600;
  color: var(--nc-seal-dark);
}

.template-type {
  font-weight: 400;
  color: var(--nc-muted);
}

.print-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 140px;
  gap: 10px;
  align-items: end;
  margin-top: 14px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.print-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.print-field--wide {
  grid-column: 1 / -1;
}

.print-field--switch {
  align-items: flex-start;
}

.proof-actions {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  margin-top: 14px;
}

.proof-actions :deep(.el-button) {
  width: 100%;
  margin-left: 0;
}

/* ── 证明开具历史 ─────────────────────────────────────────────── */

.count-chip {
  padding: 0 7px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal-soft);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  line-height: 20px;
  color: var(--nc-seal-dark);
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.history-item {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 8px 10px;
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.history-main {
  min-width: 0;
}

.history-main strong {
  display: block;
  overflow: hidden;
  font-size: var(--nc-text-base);
  font-weight: 700;
  color: var(--nc-ink);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.history-main p {
  margin-top: 1px;
  overflow: hidden;
  font-size: var(--nc-text-xs);
  color: var(--nc-muted);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.history-meta {
  display: flex;
  flex: none;
  flex-direction: column;
  gap: 1px;
  align-items: flex-end;
  font-size: var(--nc-text-xs);
  color: var(--nc-muted-2);
  text-align: right;
}

/* ── 右栏任务状态 ─────────────────────────────────────────────── */

.preview-empty {
  min-height: 320px;
  padding: 24px 0;
}

.preview-empty :deep(.el-empty__description p) {
  max-width: 340px;
  color: var(--nc-muted-2);
  line-height: 1.7;
}

.preview-empty-hint {
  margin-top: 6px;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted);
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

.job-meta {
  display: flex;
  flex-direction: column;
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
  width: 84px;
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

.job-meta-row dd.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  color: var(--nc-ink);
}

.job-meta-row .muted {
  color: var(--nc-muted);
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

.archive-meta {
  margin-top: 20px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.archive-meta-title {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  color: var(--nc-ink);
}

.archive-meta-line {
  margin-top: 4px;
  font-size: var(--nc-text-sm);
  color: var(--nc-muted-2);
  word-break: break-word;
}

/* ── 响应式 ───────────────────────────────────────────────────── */

@media (max-width: 960px) {
  .archive-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .archive-right {
    position: static;
    order: 2;
  }

  .preview-empty {
    min-height: 200px;
  }
}

@media (max-width: 767px) {
  .header-stats {
    max-width: none;
  }
}

@media (max-width: 480px) {
  .proof-actions,
  .print-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .print-field--wide {
    grid-column: auto;
  }

  .history-item {
    flex-direction: column;
    gap: 4px;
  }

  .history-meta {
    align-items: flex-start;
    text-align: left;
  }
}
</style>
