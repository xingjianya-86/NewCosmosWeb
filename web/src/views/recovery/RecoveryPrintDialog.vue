<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getErrorMessage } from '@/api/http'
import { listPrinters } from '@/api/printers'
import {
  createPrintJob,
  listDocumentTemplates,
  listPrintJobs,
  type CreatedPrintJob,
  type DocumentTemplate,
  type PrintJobMode,
} from '@/api/archivePrint'
import type { PrinterBinding } from '@/types/api'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    recordId: number | null
    personName?: string
    sourceDisplay?: string
    /** print = 保存并打印；reprint = 列表补打。 */
    action?: 'print' | 'reprint'
    /** 保存并打印后自动提交一次 print 任务。 */
    autoSubmit?: boolean
  }>(),
  {
    personName: '',
    sourceDisplay: '',
    action: 'print',
    autoSubmit: false,
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: boolean): void
  (event: 'printed', job: CreatedPrintJob): void
}>()

const RECOVERY_BUSINESS_TYPE = 'Recovery'

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const templates = ref<DocumentTemplate[]>([])
const templatesLoading = ref(false)
const templatesError = ref('')
const printers = ref<PrinterBinding[]>([])
const printersLoading = ref(false)
const printersError = ref('')
const templateId = ref<number | null>(null)
const printerId = ref<number | null>(null)
const copies = ref(1)
const isDuplex = ref(false)
const submittingMode = ref<PrintJobMode | ''>('')
const lastJob = ref<CreatedPrintJob | null>(null)
const lastError = ref('')
const autoSubmitted = ref(false)
const refreshingJob = ref(false)

const isReprint = computed(() => props.action === 'reprint')
const dialogTitle = computed(() => (isReprint.value ? '补打追缴资金办理单' : '打印追缴资金办理单'))

const selectedPrinter = computed(
  () => printers.value.find((printer) => printer.id === printerId.value) ?? null,
)
const selectedTemplate = computed(
  () => templates.value.find((template) => template.id === templateId.value) ?? null,
)
const isSubmitting = computed(() => submittingMode.value !== '')

const canSubmit = computed(
  () => props.recordId !== null && templateId.value !== null && !isSubmitting.value && !templatesLoading.value,
)

const printerHint = computed(() => {
  if (printersLoading.value) return '正在读取已绑定打印机…'
  if (printersError.value) return printersError.value
  if (printers.value.length === 0) return '当前账号未绑定打印机；可先使用「预览」或「保存」。'
  if (printerId.value === null) return '打印模式需要选择打印机。'
  return ''
})

function preferredTemplateId(): number | null {
  const list = templates.value
  const preferred =
    list.find((template) => `${template.name} ${template.docType}`.includes('追缴资金办理单'))
    ?? list.find((template) => `${template.name} ${template.docType}`.includes('追缴'))
    ?? list[0]
  return preferred?.id ?? null
}

function pickPreferredPrinter(): number | null {
  const list = printers.value
  const preferred =
    list.find((printer) => printer.isDefault && printer.online)
    ?? list.find((printer) => printer.isDefault)
    ?? list.find((printer) => printer.online)
    ?? list[0]
  return preferred?.id ?? null
}

function reset(): void {
  templateId.value = preferredTemplateId()
  printerId.value = pickPreferredPrinter()
  copies.value = 1
  isDuplex.value = false
  lastJob.value = null
  lastError.value = ''
  autoSubmitted.value = false
}

async function loadTemplates(force = false): Promise<void> {
  if (!force && templates.value.length > 0) return
  templatesLoading.value = true
  templatesError.value = ''
  try {
    templates.value = await listDocumentTemplates(RECOVERY_BUSINESS_TYPE)
    if (templateId.value === null || !templates.value.some((item) => item.id === templateId.value)) {
      templateId.value = preferredTemplateId()
    }
  } catch (error) {
    templates.value = []
    templateId.value = null
    templatesError.value = getErrorMessage(error, '打印模板加载失败')
  } finally {
    templatesLoading.value = false
  }
}

async function loadPrinters(force = false): Promise<void> {
  if (!force && printers.value.length > 0) return
  printersLoading.value = true
  printersError.value = ''
  try {
    printers.value = await listPrinters()
    if (printerId.value === null || !printers.value.some((item) => item.id === printerId.value)) {
      printerId.value = pickPreferredPrinter()
    }
  } catch (error) {
    printers.value = []
    printerId.value = null
    printersError.value = getErrorMessage(error, '打印机列表加载失败')
  } finally {
    printersLoading.value = false
  }
}

function normalizedCopies(): number {
  const value = Number.isFinite(copies.value) ? Math.trunc(copies.value) : 1
  return Math.min(99, Math.max(1, value))
}

async function submit(mode: PrintJobMode): Promise<void> {
  if (props.recordId === null) {
    ElMessage.warning('缺少追缴记录编号，无法打印')
    return
  }
  if (templateId.value === null) {
    ElMessage.warning(
      templates.value.length === 0 ? '当前业务域暂无可用的打印模板' : '请选择打印模板',
    )
    return
  }
  if (mode === 'print' && printerId.value === null) {
    ElMessage.warning(
      printers.value.length === 0
        ? '当前账号未绑定打印机，请先在本机 Agent 绑定，或改用预览/保存'
        : '请选择打印机',
    )
    return
  }

  submittingMode.value = mode
  lastError.value = ''
  try {
    const job = await createPrintJob({
      businessType: RECOVERY_BUSINESS_TYPE,
      businessId: props.recordId,
      templateId: templateId.value,
      templateName: selectedTemplate.value?.name,
      bindingId: mode === 'print' ? printerId.value : null,
      printerName: mode === 'print' ? selectedPrinter.value?.printerName : undefined,
      copies: normalizedCopies(),
      isDuplex: isDuplex.value,
      mode,
    })
    lastJob.value = job
    emit('printed', job)
    ElMessage.success('已推送本机 Agent')
  } catch (error) {
    lastJob.value = null
    lastError.value = getErrorMessage(
      error,
      mode === 'preview' ? '推送预览失败' : mode === 'save' ? '保存文书失败' : '推送打印失败',
    )
    ElMessage.error(lastError.value)
  } finally {
    submittingMode.value = ''
  }
}

async function open(): Promise<void> {
  reset()
  await Promise.all([loadTemplates(), loadPrinters(true)])
  if (templateId.value === null) templateId.value = preferredTemplateId()
  if (printerId.value === null) printerId.value = pickPreferredPrinter()

  if (props.autoSubmit && !autoSubmitted.value) {
    autoSubmitted.value = true
    if (templateId.value === null) {
      lastError.value = '当前业务域暂无可用的打印模板，无法自动打印。'
      return
    }
    if (printerId.value === null) {
      lastError.value = '当前账号未绑定打印机，请选择预览/保存，或先在打印机绑定页绑定设备。'
      return
    }
    await submit('print')
  }
}

/** 从打印任务列表刷新最近一次任务的执行状态（Web 不内嵌 PDF）。 */
async function refreshJobStatus(): Promise<void> {
  const job = lastJob.value
  if (!job || refreshingJob.value) return
  refreshingJob.value = true
  try {
    const page = await listPrintJobs({ pageIndex: 1, pageSize: 50 })
    const found = page.items.find((item) => item.id === job.id)
    if (found) {
      lastJob.value = {
        ...job,
        status: found.status,
        mode: found.mode,
        jobKind: found.jobKind,
      }
    }
  } catch (error) {
    ElMessage.warning(getErrorMessage(error, '任务状态刷新失败'))
  } finally {
    refreshingJob.value = false
  }
}

watch(
  () => props.modelValue,
  (next) => {
    if (next) void open()
  },
)

const JOB_STATUS_LABELS: Record<string, string> = {
  Pending: '排队中',
  Running: '执行中',
  Processing: '执行中',
  Succeeded: '已完成',
  Completed: '已完成',
  Failed: '失败',
  Cancelled: '已取消',
}

function statusLabel(status: string): string {
  return JOB_STATUS_LABELS[status] ?? (status || '排队中')
}
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="dialogTitle"
    width="min(580px, 94vw)"
    append-to-body
    destroy-on-close
    class="recovery-print-dialog"
  >
    <div class="print-target">
      <div>
        <span class="print-target__label">追缴人员</span>
        <strong class="print-target__value">{{ personName || '—' }}</strong>
      </div>
      <div>
        <span class="print-target__label">追缴分类</span>
        <strong class="print-target__value">{{ sourceDisplay || '—' }}</strong>
      </div>
    </div>

    <el-form label-position="top" class="print-form" @submit.prevent="submit('print')">
      <el-form-item label="打印模板">
        <el-select
          v-model="templateId"
          filterable
          :loading="templatesLoading"
          :disabled="templatesLoading"
          placeholder="选择追缴资金办理单模板"
        >
          <el-option
            v-for="template in templates"
            :key="template.id"
            :label="template.docType ? `${template.name} · ${template.docType}` : template.name"
            :value="template.id"
          />
        </el-select>
        <p v-if="templatesError" class="form-hint is-error">
          {{ templatesError }}
          <el-button link type="primary" @click="loadTemplates(true)">重试</el-button>
        </p>
        <p v-else-if="!templatesLoading && templates.length === 0" class="form-hint">
          当前业务域暂无可用的打印模板，请联系管理员配置。
        </p>
      </el-form-item>

      <el-form-item label="打印机">
        <el-select
          v-model="printerId"
          filterable
          clearable
          :loading="printersLoading"
          :disabled="printersLoading"
          placeholder="选择已绑定的打印机"
        >
          <el-option
            v-for="printer in printers"
            :key="printer.id"
            :label="`${printer.deviceName} · ${printer.printerName} · ${printer.online ? '在线' : '离线'}`"
            :value="printer.id"
          />
        </el-select>
        <p v-if="printerHint" class="form-hint" :class="{ 'is-error': Boolean(printersError) }">
          {{ printerHint }}
          <el-button v-if="printersError" link type="primary" @click="loadPrinters(true)">
            重试
          </el-button>
        </p>
      </el-form-item>

      <div class="print-options">
        <label class="print-option">
          <span>份数</span>
          <el-input-number v-model="copies" :min="1" :max="99" controls-position="right" />
        </label>
        <label class="print-option">
          <span>双面打印</span>
          <el-switch v-model="isDuplex" />
        </label>
      </div>

      <p v-if="selectedPrinter" class="print-summary">
        打印将推送到 <strong>{{ selectedPrinter.deviceName }}</strong> 的
        <strong>{{ selectedPrinter.printerName }}</strong>；预览与保存由本机 Agent 打开/落盘。
      </p>

      <el-alert
        v-if="lastError"
        class="print-result print-result--error"
        type="error"
        :title="lastError"
        :closable="false"
        show-icon
      />

      <div v-if="lastJob" class="print-result print-result--success">
        <div class="print-result__head">
          <el-tag type="success" effect="dark">已推送本机 Agent</el-tag>
          <span class="mono">{{ lastJob.jobNo || `#${lastJob.id}` }}</span>
          <el-button
            class="print-result__refresh"
            link
            type="primary"
            :loading="refreshingJob"
            @click="refreshJobStatus"
          >
            刷新状态
          </el-button>
        </div>
        <p>
          状态：{{ statusLabel(lastJob.status) }} · 方式：{{
            lastJob.mode === 'preview' ? '预览' : lastJob.mode === 'save' ? '保存' : '打印'
          }}
        </p>
        <p class="print-result__hint">
          任务由本机 Agent 领取后执行；如打印机离线或失败，请在打印任务列表查看失败原因。
        </p>
      </div>
    </el-form>

    <template #footer>
      <el-button :disabled="isSubmitting" @click="visible = false">
        {{ lastJob ? '关闭' : '取消' }}
      </el-button>
      <el-button
        :loading="submittingMode === 'preview'"
        :disabled="!canSubmit || isSubmitting"
        @click="submit('preview')"
      >
        预览
      </el-button>
      <el-button
        :loading="submittingMode === 'save'"
        :disabled="!canSubmit || isSubmitting"
        @click="submit('save')"
      >
        保存
      </el-button>
      <el-button
        type="primary"
        :loading="submittingMode === 'print'"
        :disabled="!canSubmit || isSubmitting"
        @click="submit('print')"
      >
        {{ isReprint ? '补打' : '打印' }}
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.recovery-print-dialog :deep(.el-dialog__body) {
  max-height: 70vh;
  overflow-y: auto;
}

.print-target {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px 16px;
  margin-bottom: 16px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.print-target > div {
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: 2px;
}

.print-target__label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.1em;
}

.print-target__value {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mono {
  font-family: var(--nc-font-mono);
}

.print-form :deep(.el-select) {
  width: 100%;
}

.form-hint {
  margin: 6px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.form-hint.is-error {
  color: var(--nc-seal);
}

.print-options {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px 24px;
  margin-bottom: 8px;
}

.print-option {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.print-summary {
  margin: 4px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.print-summary strong {
  color: var(--nc-ink-2);
}

.print-result {
  margin-top: 12px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-3);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.print-result--error {
  border-color: rgba(171, 50, 39, 0.35);
}

.print-result--success {
  border-color: rgba(46, 71, 56, 0.3);
  background: var(--nc-pine-soft);
}

.print-result__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
  color: var(--nc-ink);
}

.print-result__refresh {
  margin-left: auto;
}

.print-result p {
  margin: 0;
  color: var(--nc-ink-2);
}

.print-result__hint {
  margin-top: 4px !important;
  color: var(--nc-muted) !important;
}

@media (max-width: 560px) {
  .print-target {
    grid-template-columns: 1fr;
  }

  .recovery-print-dialog :deep(.el-dialog__footer) {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }

  .recovery-print-dialog :deep(.el-dialog__footer .el-button) {
    flex: 1 1 calc(50% - 8px);
    margin-left: 0;
  }
}
</style>
