<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Document,
  Finished,
  InfoFilled,
  Printer,
  Refresh,
  View,
} from '@element-plus/icons-vue'
import {
  completeArchiveOutput,
  createArchiveOutputJob,
  getArchiveProduction,
  listArchiveOutputTemplates,
} from '@/api/archiveOutput'
import { getErrorMessage } from '@/api/http'
import { listPrinters } from '@/api/printers'
import { describePrintJobError, listPrintJobs } from '@/api/archivePrint'
import type { ArchiveOutputMode, ArchiveOutputTemplate, ArchiveProductionInfo } from '@/types/archiveOutput'
import type { PrinterBinding } from '@/types/api'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import {
  archiveJobStatusLabel,
  archiveJobStatusTone,
  isArchiveJobTerminal,
} from './archiveDisplay'

const route = useRoute()
const router = useRouter()

const POLL_INTERVAL = 4_000
const JOB_PAGE_SIZE = 50

/* ───────────────────────── 入口参数 ───────────────────────── */

function firstQuery(key: string): string {
  const raw = route.query[key]
  const value = Array.isArray(raw) ? raw[0] : raw
  return typeof value === 'string' ? value.trim() : ''
}

interface OutputContext {
  businessType: string
  businessId: number
  classification: string
  operationOverride: string
  docTypes: string[]
}

/**
 * 标准入口由档案制作中心携带 businessType + businessId；
 * applicationId 作为兼容回退（FamilyApplication）。
 * docTypes 参数用于按文档类型过滤模板（如异地协助只出入户调查表和邻里访问调查表）。
 */
const context = computed<OutputContext | null>(() => {
  const classification = firstQuery('classification')
  const operationOverride = firstQuery('operationOverride')
  const docTypesText = firstQuery('docTypes')
  const docTypes = docTypesText
    ? docTypesText.split(',').map((s) => s.trim()).filter(Boolean)
    : []

  const businessType = firstQuery('businessType')
  const businessIdText = firstQuery('businessId')
  const businessId = Number(businessIdText)
  if (businessType && businessIdText && Number.isFinite(businessId) && businessId > 0) {
    return { businessType, businessId, classification, operationOverride, docTypes }
  }

  const applicationId = Number(firstQuery('applicationId'))
  if (Number.isFinite(applicationId) && applicationId > 0) {
    return {
      businessType: 'FamilyApplication',
      businessId: applicationId,
      classification,
      operationOverride,
      docTypes,
    }
  }

  return null
})

/* ───────────────────────── 制作信息（副标题/分类回退） ───────── */

const info = ref<ArchiveProductionInfo | null>(null)

async function loadInfo(): Promise<void> {
  const current = context.value
  if (!current) {
    info.value = null
    return
  }

  try {
    info.value = await getArchiveProduction({
      businessType: current.businessType,
      businessId: current.businessId,
    })
  } catch {
    // 制作信息仅用于副标题与分类回退，失败不阻断模板加载。
    info.value = null
  }
}

const subtitle = computed(() => {
  if (!context.value) return '请从档案制作中心选择记录进入'
  return (
    info.value?.statusText ||
    info.value?.title ||
    `${context.value.businessType} · #${context.value.businessId}`
  )
})

const resolvedClassification = computed(
  () => context.value?.classification || info.value?.classification || '',
)
const resolvedOperationOverride = computed(
  () => context.value?.operationOverride || info.value?.operationOverride || '',
)

/** 是否为异地协助模式（docTypes 过滤时隐藏完成归档按钮） */
const isRemoteMode = computed(() => (context.value?.docTypes.length ?? 0) > 0)

/* ───────────────────────── 模板 ───────────────────────── */

const templates = ref<ArchiveOutputTemplate[]>([])
const selectedTemplateIds = ref<number[]>([])
const templatesLoading = ref(false)
const templatesError = ref('')
const previewTemplateId = ref<number | null>(null)
let templatesInitialized = false

const selectedTemplates = computed(() =>
  templates.value.filter((template) => selectedTemplateIds.value.includes(template.id)),
)
const allTemplatesSelected = computed(
  () =>
    templates.value.length > 0 && selectedTemplateIds.value.length === templates.value.length,
)
const previewTemplate = computed(
  () => templates.value.find((template) => template.id === previewTemplateId.value) ?? null,
)
const previewHint = computed(() => {
  if (previewTemplate.value) {
    const type = previewTemplate.value.fileType || previewTemplate.value.docType || '文档'
    return `预览提示：${previewTemplate.value.name}（${type}）；实际输出以左侧勾选模板为准。`
  }
  return '预览模板仅用于提示，实际预览/打印/保存以左侧勾选模板为准。'
})

async function loadTemplates(): Promise<void> {
  const current = context.value
  if (!current) return

  templatesLoading.value = true
  templatesError.value = ''
  try {
    const list = await listArchiveOutputTemplates({
      businessType: current.businessType,
      businessId: current.businessId,
      classification: resolvedClassification.value || undefined,
      operationOverride: resolvedOperationOverride.value || undefined,
    })
    // 按文档类型过滤（如异地协助只出入户调查表和邻里访问调查表）
    const filtered = current.docTypes.length > 0
      ? list.filter((t) => current.docTypes.includes(t.docType))
      : list
    templates.value = filtered
    const available = new Set(filtered.map((template) => template.id))

    if (!templatesInitialized) {
      // 默认全选；若后端明确 defaultSelected=false 则尊重后端预选。
      selectedTemplateIds.value = filtered
        .filter((template) => template.defaultSelected !== false)
        .map((template) => template.id)
      templatesInitialized = true
    } else {
      selectedTemplateIds.value = selectedTemplateIds.value.filter((id) => available.has(id))
    }

    if (previewTemplateId.value !== null && !available.has(previewTemplateId.value)) {
      previewTemplateId.value = null
    }
  } catch (loadError) {
    templates.value = []
    selectedTemplateIds.value = []
    previewTemplateId.value = null
    templatesError.value = getErrorMessage(loadError, '输出模板加载失败')
  } finally {
    templatesLoading.value = false
  }
}

function selectAllTemplates(): void {
  selectedTemplateIds.value = templates.value.map((template) => template.id)
}

function clearTemplates(): void {
  selectedTemplateIds.value = []
}

/* ───────────────────────── 打印机 / 打印设置 ─────────────────── */

const printers = ref<PrinterBinding[]>([])
const selectedPrinterId = ref<number | null>(null)
const printersLoading = ref(false)
const printersError = ref('')

const selectedPrinter = computed(
  () => printers.value.find((printer) => printer.id === selectedPrinterId.value) ?? null,
)

async function loadPrinters(): Promise<void> {
  printersLoading.value = true
  printersError.value = ''
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
  } catch (loadError) {
    printers.value = []
    selectedPrinterId.value = null
    printersError.value = getErrorMessage(loadError, '打印机列表加载失败')
  } finally {
    printersLoading.value = false
  }
}

const copies = ref<number | undefined>(1)
const isDuplex = ref(true)

const copiesCount = computed(() => Math.min(99, Math.max(1, Math.round(copies.value || 1))))

/* ───────────────────────── 输出任务 ───────────────────────── */

interface OutputJobState {
  id: number
  jobNo: string
  status: string
  jobKind: string
  mode: ArchiveOutputMode
  templateNames: string[]
  printerName: string
  copies: number
  isDuplex: boolean
  errorMessage: string
  createdAt: string
}

const currentJob = ref<OutputJobState | null>(null)
const submitting = ref(false)
const submittingMode = ref<ArchiveOutputMode | null>(null)
const jobError = ref('')
const jobPolling = ref(false)
const completing = ref(false)

let pollTimer: number | undefined
let jobTicket = 0

const MODE_LABELS: Record<ArchiveOutputMode, string> = {
  preview: '预览',
  print: '打印',
  save: '保存',
}

const canSubmit = computed(
  () =>
    Boolean(context.value) &&
    !submitting.value &&
    templates.value.length > 0 &&
    selectedTemplateIds.value.length > 0,
)

const jobStatusLabel = computed(() => {
  const job = currentJob.value
  if (!job) return ''
  if (job.status === 'Pending') {
    if (job.mode === 'preview') return '待预览'
    if (job.mode === 'save') return '待保存'
    return '待打印'
  }
  return archiveJobStatusLabel(job.status)
})

const jobStatusTone = computed(() =>
  currentJob.value ? archiveJobStatusTone(currentJob.value.status) : 'default',
)

/**
 * 任务消息展示口径：LocalOp 的完成消息是 `RESULT:` 结构化结果（不是错误），
 * 按完成信息渲染；其余（含失败摘要）仍按错误告警。见 archivePrint.describePrintJobError。
 */
const jobErrorDisplay = computed(() => {
  const job = currentJob.value
  return job ? describePrintJobError(job) : { text: '', isError: true }
})

function modeLabel(mode: ArchiveOutputMode): string {
  return MODE_LABELS[mode]
}

function successMessage(mode: ArchiveOutputMode): string {
  if (mode === 'preview') return '已提交预览任务，请在本机 Agent 查看预览'
  if (mode === 'save') return '已提交保存任务，文件将由本机 Agent 保存'
  return '已提交打印任务，请在本机 Agent 查看打印机'
}

async function submit(mode: ArchiveOutputMode): Promise<void> {
  if (submitting.value) return

  const current = context.value
  if (!current) {
    ElMessage.warning('缺少业务参数，请从档案制作中心进入')
    return
  }
  if (templates.value.length === 0) {
    ElMessage.warning('暂无可用模板')
    return
  }
  if (selectedTemplates.value.length === 0) {
    ElMessage.warning('请至少选择一份模板')
    return
  }

  const printer = selectedPrinter.value
  if (!printer) {
    ElMessage.warning('请先绑定并选择打印机')
    return
  }

  submitting.value = true
  submittingMode.value = mode
  jobError.value = ''

  try {
    const result = await createArchiveOutputJob({
      businessType: current.businessType,
      businessId: current.businessId,
      classification: resolvedClassification.value || undefined,
      operationOverride: resolvedOperationOverride.value || undefined,
      templateIds: selectedTemplates.value.map((template) => template.id),
      mode,
      copies: mode === 'print' ? copiesCount.value : undefined,
      isDuplex: mode === 'print' ? isDuplex.value : undefined,
      printerBindingId: printer.id,
      printerName: printer.printerName,
    })

    currentJob.value = {
      id: result.id,
      jobNo: result.jobNo,
      status: result.status || 'Pending',
      jobKind: result.jobKind,
      mode,
      templateNames: result.templates.length
        ? result.templates.map((template) => template.name)
        : selectedTemplates.value.map((template) => template.name),
      printerName: `${printer.deviceName} · ${printer.printerName}`,
      copies: mode === 'print' ? copiesCount.value : 1,
      isDuplex: mode === 'print' ? isDuplex.value : false,
      errorMessage: '',
      createdAt: new Date().toISOString(),
    }

    ElMessage.success(successMessage(mode))
    startPolling()
  } catch (submitError) {
    jobError.value = getErrorMessage(submitError, '输出任务提交失败')
    ElMessage.error(jobError.value)
  } finally {
    submitting.value = false
    submittingMode.value = null
  }
}

/* ───────────────────────── 任务状态轮询 ───────────────────────── */

async function pollJob(): Promise<void> {
  const job = currentJob.value
  if (!job || job.id <= 0) return
  if (isArchiveJobTerminal(job.status)) {
    stopPolling()
    return
  }

  const ticket = ++jobTicket
  jobPolling.value = true
  try {
    const page = await listPrintJobs({ pageIndex: 1, pageSize: JOB_PAGE_SIZE })
    if (ticket !== jobTicket) return

    const found = page.items.find(
      (item) => item.id === job.id || (job.jobNo && item.jobNo === job.jobNo),
    )
    if (!found) return

    currentJob.value = {
      ...job,
      status: found.status || job.status,
      errorMessage: found.errorMessage || job.errorMessage,
      printerName: found.printerName || job.printerName,
      copies: found.copies || job.copies,
      isDuplex: found.isDuplex,
    }

    if (isArchiveJobTerminal(found.status)) stopPolling()
  } catch {
    // 轮询失败保留上次状态，用户可手动刷新。
  } finally {
    if (ticket === jobTicket) jobPolling.value = false
  }
}

function startPolling(): void {
  stopPolling()
  void pollJob()
  if (currentJob.value && isArchiveJobTerminal(currentJob.value.status)) return

  pollTimer = window.setInterval(() => {
    if (document.visibilityState !== 'visible') return
    if (submitting.value || completing.value) return
    void pollJob()
  }, POLL_INTERVAL)
}

function stopPolling(): void {
  if (pollTimer !== undefined) {
    window.clearInterval(pollTimer)
    pollTimer = undefined
  }
}

function refreshAll(): void {
  void loadInfo()
  void loadTemplates()
  void loadPrinters()
  if (currentJob.value) void pollJob()
}

/* ───────────────────────── 完成归档 ───────────────────────── */

async function completeArchive(): Promise<void> {
  const current = context.value
  if (!current || completing.value) return

  const subject = info.value?.title || `${current.businessType} #${current.businessId}`
  try {
    await ElMessageBox.confirm(
      `完成归档后本次办理结束，档案将标记为已归档。确定完成「${subject}」的归档吗？`,
      '完成归档',
      {
        type: 'warning',
        confirmButtonText: '完成归档',
        cancelButtonText: '取消',
      },
    )
  } catch {
    return
  }

  completing.value = true
  try {
    await completeArchiveOutput({
      businessType: current.businessType,
      businessId: current.businessId,
    })
    ElMessage.success('已完成归档，本次办理结束')
    stopPolling()
    await router.push(
      current.businessType === 'FamilyApplication' ? '/applications' : '/archives/query',
    )
  } catch (completeError) {
    ElMessage.error(getErrorMessage(completeError, '完成归档失败'))
  } finally {
    completing.value = false
  }
}

/* ───────────────────────── 导航 ───────────────────────── */

function goBack(): void {
  stopPolling()
  if (window.history.length > 1) {
    router.back()
    return
  }
  goProduction()
}

function goProduction(): void {
  const current = context.value
  if (!current) {
    void router.push('/archives/production')
    return
  }
  void router.push({
    name: 'archive-production',
    query: {
      businessType: current.businessType,
      businessId: String(current.businessId),
    },
  })
}

function goArchiveQuery(): void {
  void router.push('/archives/query')
}

function goPrinters(): void {
  void router.push('/printers')
}

onMounted(() => {
  void loadInfo()
  void loadTemplates()
  void loadPrinters()
})

onBeforeUnmount(() => {
  stopPolling()
})
</script>

<template>
  <div class="nc-page archive-output">
    <NcPageHeader
      title="档案输出"
      :description="subtitle"
      kicker="救助档案管理 / 档案输出"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="templatesLoading" @click="refreshAll">刷新</el-button>
        <el-button :icon="ArrowLeft" @click="goBack">返回</el-button>
      </template>
    </NcPageHeader>

    <!-- 缺少入口参数 -->
    <NcCard v-if="!context" padding="lg">
      <div class="empty-state">
        <el-icon class="empty-state__icon" :size="46"><Document /></el-icon>
        <p class="empty-state__title">请从档案制作中心选择记录进入</p>
        <p class="empty-state__hint">
          档案输出需要 businessType + businessId 参数，或兼容的 applicationId 参数。
        </p>
        <div class="empty-state__actions">
          <el-button type="primary" @click="goProduction">返回档案制作中心</el-button>
          <el-button @click="goArchiveQuery">档案查询</el-button>
        </div>
      </div>
    </NcCard>

    <template v-else>
      <!-- 工具栏 -->
      <NcCard class="toolbar-card" padding="sm">
        <div class="toolbar">
          <div class="toolbar-item toolbar-item--preview">
            <span class="toolbar-label">预览模板</span>
            <el-select
              v-model="previewTemplateId"
              class="toolbar-select"
              clearable
              filterable
              placeholder="可选，用于提示"
              :disabled="templates.length === 0"
            >
              <el-option
                v-for="template in templates"
                :key="template.id"
                :value="template.id"
                :label="template.name"
              >
                <div class="template-option">
                  <span class="template-option__name">{{ template.name }}</span>
                  <span v-if="template.fileType" class="template-option__type">
                    {{ template.fileType }}
                  </span>
                </div>
              </el-option>
            </el-select>
          </div>

          <div class="toolbar-item">
            <span class="toolbar-label">打印机</span>
            <el-select
              v-model="selectedPrinterId"
              class="toolbar-select"
              filterable
              :loading="printersLoading"
              :placeholder="printers.length > 0 ? '选择打印机' : '请先绑定打印机'"
            >
              <el-option
                v-for="printer in printers"
                :key="printer.id"
                :value="printer.id"
                :label="`${printer.deviceName} · ${printer.printerName} · ${printer.online ? '在线' : '离线'}`"
              >
                <div class="printer-option">
                  <span class="printer-option__name">{{ printer.printerName }}</span>
                  <span class="printer-option__device">{{ printer.deviceName }}</span>
                  <span class="printer-option__state" :class="{ 'is-online': printer.online }">
                    {{ printer.online ? '在线' : '离线' }}
                  </span>
                </div>
              </el-option>
            </el-select>
          </div>

          <div class="toolbar-item">
            <span class="toolbar-label">份数</span>
            <el-input-number
              v-model="copies"
              :min="1"
              :max="99"
              :step="1"
              step-strictly
              size="small"
              controls-position="right"
            />
          </div>

          <div class="toolbar-item toolbar-item--switch">
            <span class="toolbar-label">双面打印</span>
            <el-switch v-model="isDuplex" />
          </div>
        </div>

        <p class="toolbar-hint">
          <el-icon><InfoFilled /></el-icon>
          <span>{{ previewHint }}</span>
        </p>

        <p v-if="printersError" class="toolbar-warning">
          {{ printersError }}；预览/打印/保存均需已绑定打印机。
          <el-button link type="primary" size="small" @click="loadPrinters">重新加载</el-button>
        </p>
        <p v-else-if="!printersLoading && printers.length === 0" class="toolbar-warning">
          当前账号未绑定打印机，预览/打印/保存均需先绑定打印机。
          <el-button link type="primary" size="small" @click="goPrinters">去绑定打印机</el-button>
        </p>
        <p v-else-if="selectedPrinter && !selectedPrinter.online" class="toolbar-warning">
          所选打印机离线：任务会进入队列，等待该电脑的 Agent 上线后执行。
        </p>
      </NcCard>

      <!-- 模板 + 任务状态 -->
      <div class="output-grid">
        <NcCard
          title="输出模板"
          subtitle="默认全选，可取消本次不需要的模板；模板由后端按分类预选"
        >
          <template #actions>
            <span class="template-total">
              {{ selectedTemplateIds.length }} / {{ templates.length }}
            </span>
            <el-button
              link
              type="primary"
              size="small"
              :disabled="templates.length === 0 || allTemplatesSelected"
              @click="selectAllTemplates"
            >
              全选
            </el-button>
            <el-button
              link
              size="small"
              :disabled="selectedTemplateIds.length === 0"
              @click="clearTemplates"
            >
              清空
            </el-button>
          </template>

          <el-alert
            v-if="templatesError"
            class="panel-error"
            type="error"
            :title="templatesError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadTemplates">重新加载</el-button>
          </el-alert>

          <el-skeleton
            v-else-if="templatesLoading && templates.length === 0"
            :rows="5"
            animated
          />

          <el-checkbox-group
            v-else-if="templates.length > 0"
            v-model="selectedTemplateIds"
            class="template-list"
          >
            <el-checkbox
              v-for="template in templates"
              :key="template.id"
              :value="template.id"
              class="template-item"
            >
              <span class="template-content">
                <span class="template-name">{{ template.name }}</span>
                <span class="template-tags">
                  <span v-if="template.fileType" class="template-tag template-tag--file">
                    {{ template.fileType }}
                  </span>
                  <span v-if="template.docType" class="template-tag">
                    {{ template.docType }}
                  </span>
                </span>
              </span>
            </el-checkbox>
          </el-checkbox-group>

          <el-empty
            v-else-if="!templatesLoading"
            :image-size="72"
            description="暂无可用模板"
          >
            <p class="empty-tip">
              请确认当前业务分类已配置输出模板，或返回档案制作中心检查来源数据。
            </p>
          </el-empty>
        </NcCard>

        <NcCard title="任务状态" subtitle="预览/打印/保存任务由本机 Agent 领取执行">
          <template #actions>
            <el-button
              v-if="currentJob"
              size="small"
              :icon="Refresh"
              :loading="jobPolling"
              @click="pollJob"
            >
              刷新状态
            </el-button>
          </template>

          <el-alert
            v-if="jobError"
            class="panel-error"
            type="error"
            :title="jobError"
            :closable="false"
            show-icon
          />

          <div v-if="!currentJob" class="job-empty">
            <el-icon class="job-empty__icon" :size="38"><Printer /></el-icon>
            <p class="job-empty__title">尚未提交输出任务</p>
            <p class="job-empty__hint">
              勾选模板后点击「预览选中」「打印选中」或「保存选中」，任务状态会在此处自动刷新。
            </p>
          </div>

          <div v-else class="job-status">
            <div class="job-status__head">
              <div class="job-status__identity">
                <span class="job-status__caption">任务号</span>
                <span class="job-status__no">{{ currentJob.jobNo || `#${currentJob.id}` }}</span>
                <span class="job-status__mode">{{ modeLabel(currentJob.mode) }}</span>
              </div>
              <NcStatusDot :status="jobStatusTone" :label="jobStatusLabel" />
            </div>

            <dl class="job-meta">
              <div class="job-meta__row">
                <dt>模式</dt>
                <dd>{{ modeLabel(currentJob.mode) }}</dd>
              </div>
              <div class="job-meta__row">
                <dt>模板</dt>
                <dd class="job-meta__templates">
                  <span
                    v-for="(name, index) in currentJob.templateNames"
                    :key="`${name}-${index}`"
                    class="job-template-tag"
                  >
                    {{ name }}
                  </span>
                  <span v-if="currentJob.templateNames.length === 0">—</span>
                </dd>
              </div>
              <div class="job-meta__row">
                <dt>打印机</dt>
                <dd>{{ currentJob.printerName || '—' }}</dd>
              </div>
              <div class="job-meta__row">
                <dt>份数</dt>
                <dd>
                  {{
                    currentJob.mode === 'print'
                      ? `${currentJob.copies} 份 · ${currentJob.isDuplex ? '双面' : '单面'}`
                      : '—'
                  }}
                </dd>
              </div>
              <div class="job-meta__row">
                <dt>状态</dt>
                <dd>{{ jobStatusLabel }}</dd>
              </div>
            </dl>

            <el-alert
              v-if="jobErrorDisplay.text"
              class="panel-error"
              :type="jobErrorDisplay.isError ? 'error' : 'info'"
              :title="jobErrorDisplay.text"
              :closable="false"
              show-icon
            />

            <p class="agent-hint">
              <el-icon><InfoFilled /></el-icon>
              <span>预览/打印/保存由本机 Agent 执行；Web 端不内嵌 PDF，状态每 4 秒自动刷新。</span>
            </p>
          </div>
        </NcCard>
      </div>

      <!-- 底部操作栏 -->
      <div class="action-bar">
        <el-button :icon="ArrowLeft" @click="goBack">返回</el-button>
        <div class="action-bar__spacer" />
        <el-button
          type="success"
          :icon="Document"
          :loading="submitting && submittingMode === 'save'"
          :disabled="!canSubmit"
          :title="selectedPrinter ? '' : '请先绑定并选择打印机'"
          @click="submit('save')"
        >
          保存选中
        </el-button>
        <el-button
          type="primary"
          :icon="Printer"
          :loading="submitting && submittingMode === 'print'"
          :disabled="!canSubmit"
          :title="selectedPrinter ? '' : '请先绑定并选择打印机'"
          @click="submit('print')"
        >
          打印选中
        </el-button>
        <el-button
          :icon="View"
          :loading="submitting && submittingMode === 'preview'"
          :disabled="!canSubmit"
          :title="selectedPrinter ? '' : '请先绑定并选择打印机'"
          @click="submit('preview')"
        >
          预览选中
        </el-button>
        <el-button
          v-if="!isRemoteMode"
          type="warning"
          :icon="Finished"
          :loading="completing"
          @click="completeArchive"
        >
          完成归档
        </el-button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.archive-output {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.panel-error {
  margin: 0;
}

/* ── 空态 ─────────────────────────────────────────────────────── */
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 36px 20px;
  text-align: center;
}

.empty-state__icon {
  color: var(--nc-muted);
}

.empty-state__title {
  margin: 6px 0 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
}

.empty-state__hint {
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.empty-state__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 10px;
  margin-top: 10px;
}

/* ── 工具栏 ───────────────────────────────────────────────────── */
.toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 12px 18px;
}

.toolbar-item {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
}

.toolbar-item--preview {
  flex: 1 1 260px;
  max-width: 360px;
}

.toolbar-item--switch {
  flex-direction: row;
  align-items: center;
  gap: 8px;
  padding-bottom: 3px;
}

.toolbar-label {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.06em;
  white-space: nowrap;
}

.toolbar-select {
  width: 100%;
  min-width: 190px;
}

.toolbar-hint,
.toolbar-warning {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  margin: 10px 0 0;
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.toolbar-hint {
  color: var(--nc-muted);
}

.toolbar-hint .el-icon {
  flex: none;
  margin-top: 2px;
  color: var(--nc-amber);
}

.toolbar-warning {
  padding: 6px 9px;
  border-left: 2px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.template-option {
  display: flex;
  align-items: center;
  gap: 8px;
}

.template-option__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.template-option__type {
  margin-left: auto;
  color: var(--nc-muted);
  font-size: 11px;
}

.printer-option {
  display: flex;
  align-items: center;
  gap: 8px;
}

.printer-option__name {
  overflow: hidden;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.printer-option__device {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: 11px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.printer-option__state {
  margin-left: auto;
  color: var(--nc-muted);
  font-size: 11px;
}

.printer-option__state.is-online {
  color: var(--nc-pine);
}

/* ── 模板 + 状态 ──────────────────────────────────────────────── */
.output-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.12fr) minmax(300px, 0.88fr);
  gap: 14px;
  align-items: start;
}

.template-total {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.template-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 430px;
  padding-right: 2px;
  overflow-y: auto;
}

.template-item {
  display: flex;
  align-items: flex-start;
  width: 100%;
  height: auto;
  margin: 0;
  padding: 9px 11px;
  border: 1px solid var(--nc-line-faint);
  border-left: 2px solid transparent;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-soft);
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition);
}

.template-item:hover {
  border-color: var(--nc-line);
  background-color: var(--nc-paper-3);
}

.template-item.is-checked {
  border-color: var(--nc-seal);
  border-left-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
}

.template-item :deep(.el-checkbox__input) {
  margin-top: 2px;
}

.template-item :deep(.el-checkbox__label) {
  width: 100%;
  min-width: 0;
  padding-left: 8px;
  overflow: hidden;
}

.template-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.template-name {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  line-height: 1.45;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.template-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.template-tag {
  display: inline-flex;
  align-items: center;
  padding: 0 6px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: 10px;
  line-height: 1.7;
  white-space: nowrap;
}

.template-tag--file {
  border-color: rgba(49, 87, 110, 0.2);
  background-color: #e8f0f7;
  color: #31576e;
}

.empty-tip {
  margin: 6px 0 0;
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.6;
}

/* ── 任务状态 ─────────────────────────────────────────────────── */
.job-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 26px 14px;
  text-align: center;
}

.job-empty__icon {
  color: var(--nc-muted);
}

.job-empty__title {
  margin: 4px 0 0;
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
  font-weight: 600;
}

.job-empty__hint {
  max-width: 340px;
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.job-status {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.job-status__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.job-status__identity {
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
}

.job-status__caption {
  flex: none;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.job-status__no {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-md);
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.job-status__mode {
  padding: 0 6px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: 10px;
  line-height: 1.7;
  white-space: nowrap;
}

.job-meta {
  display: flex;
  flex-direction: column;
  margin: 0;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  overflow: hidden;
}

.job-meta__row {
  display: grid;
  grid-template-columns: 74px minmax(0, 1fr);
  gap: 10px;
  padding: 7px 10px;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
}

.job-meta__row:nth-child(even) {
  background-color: var(--nc-paper-3);
}

.job-meta__row:last-child {
  border-bottom: 0;
}

.job-meta dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.job-meta dd {
  margin: 0;
  overflow-wrap: anywhere;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  line-height: 1.65;
}

.job-meta__templates {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.job-template-tag {
  display: inline-flex;
  align-items: center;
  padding: 0 6px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
  color: var(--nc-ink-2);
  font-size: 10px;
  font-weight: 500;
  line-height: 1.7;
}

.agent-hint {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  margin: 0;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.65;
}

.agent-hint .el-icon {
  flex: none;
  margin-top: 2px;
  color: var(--nc-amber);
}

/* ── 底部操作栏 ───────────────────────────────────────────────── */
.action-bar {
  position: sticky;
  bottom: 0;
  z-index: 2;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  padding: 10px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-sm);
}

.action-bar__spacer {
  flex: 1;
}

/* ── 响应式 ───────────────────────────────────────────────────── */
@media (max-width: 1100px) {
  .output-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .template-list {
    max-height: none;
  }
}

@media (max-width: 767px) {
  .archive-output {
    gap: 10px;
  }

  .toolbar {
    flex-direction: column;
    align-items: stretch;
  }

  .toolbar-item--preview {
    max-width: none;
  }

  .toolbar-item--switch {
    flex-direction: row;
    justify-content: space-between;
    padding-bottom: 0;
  }

  .action-bar {
    flex-direction: column;
    align-items: stretch;
  }

  .action-bar :deep(.el-button) {
    width: 100%;
    margin-left: 0;
  }

  .action-bar__spacer {
    display: none;
  }
}
</style>
