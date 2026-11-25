<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { getErrorMessage } from '@/api/http'
import { listPrinters } from '@/api/printers'
import {
  createTempReliefPrintJob,
  listTempReliefPrintTemplates,
  type CreatedPrintJob,
  type DocumentTemplate,
  type PrintJobMode,
} from '@/api/tempRelief'
import type { PrinterBinding } from '@/types/api'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    applicationId: number | null
    applicationNo?: string
    applicantName?: string
    mode?: PrintJobMode | 'reprint'
    /** 业务分类（Large/Small），仅用于把服务端返回的对应模板排到前面。 */
    reliefType?: 'Large' | 'Small'
  }>(),
  {
    applicationNo: '',
    applicantName: '',
    mode: 'print',
    reliefType: undefined,
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: boolean): void
  (event: 'printed'): void
}>()

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
const outputMode = ref<PrintJobMode>('print')
const copies = ref(1)
const isDuplex = ref(false)
const submitting = ref(false)
const lastJob = ref<CreatedPrintJob | null>(null)
const lastError = ref('')

const isReprint = computed(() => props.mode === 'reprint')
const dialogTitle = computed(() => {
  const action = isReprint.value ? '补打' : '打印'
  const no = props.applicationNo ? ` · ${props.applicationNo}` : ''
  return `${action}临时救助文书${no}`
})

const sortedTemplates = computed(() => {
  const reliefType = props.reliefType
  if (!reliefType) return templates.value
  const keyword = reliefType === 'Small' ? '小额' : '大额'
  const legacy = reliefType === 'Small' ? 'Small' : 'Large'
  const score = (template: DocumentTemplate): number => {
    const text = `${template.docType} ${template.name}`
    return text.includes(keyword) || text.includes(legacy) ? 0 : 1
  }
  return [...templates.value].sort((a, b) => score(a) - score(b))
})

const selectedTemplate = computed(
  () => templates.value.find((template) => template.id === templateId.value) ?? null,
)
const selectedPrinter = computed(
  () => printers.value.find((printer) => printer.id === printerId.value) ?? null,
)

const canSubmit = computed(
  () =>
    props.applicationId !== null &&
    templateId.value !== null &&
    !submitting.value &&
    !templatesLoading.value &&
    (outputMode.value === 'preview' || printerId.value !== null),
)

const printerHint = computed(() => {
  if (printersLoading.value) return '正在读取已绑定打印机…'
  if (printersError.value) return printersError.value
  if (printers.value.length === 0) {
    return '当前账号未绑定打印机；可先选择「预览」生成任务。'
  }
  if (outputMode.value === 'print' && printerId.value === null) {
    return '打印模式需要选择打印机。'
  }
  return ''
})

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
  templateId.value = sortedTemplates.value[0]?.id ?? null
  printerId.value = pickPreferredPrinter()
  outputMode.value = 'print'
  copies.value = 1
  isDuplex.value = false
  lastJob.value = null
  lastError.value = ''
}

async function loadTemplates(force = false): Promise<void> {
  if (!force && templates.value.length > 0) return
  templatesLoading.value = true
  templatesError.value = ''
  try {
    const list = await listTempReliefPrintTemplates()
    templates.value = list
    if (templateId.value === null || !list.some((item) => item.id === templateId.value)) {
      templateId.value = sortedTemplates.value[0]?.id ?? null
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
    const list = await listPrinters()
    printers.value = list
    if (printerId.value !== null && list.some((item) => item.id === printerId.value)) {
      return
    }
    printerId.value = pickPreferredPrinter()
  } catch (error) {
    printers.value = []
    printerId.value = null
    printersError.value = getErrorMessage(error, '打印机列表加载失败')
  } finally {
    printersLoading.value = false
  }
}

async function open(): Promise<void> {
  reset()
  await Promise.all([loadTemplates(), loadPrinters(true)])
  reset()
}

async function handleSubmit(): Promise<void> {
  if (props.applicationId === null) {
    ElMessage.warning('缺少临时救助申请记录，无法打印')
    return
  }
  if (templateId.value === null) {
    ElMessage.warning('请选择打印模板')
    return
  }
  if (outputMode.value === 'print' && printerId.value === null) {
    ElMessage.warning(
      printers.value.length === 0
        ? '当前账号未绑定打印机，请先在本机 Agent 绑定，或改用预览模式'
        : '请选择打印机',
    )
    return
  }

  submitting.value = true
  lastError.value = ''
  try {
    const job = await createTempReliefPrintJob({
      applicationId: props.applicationId,
      templateId: templateId.value,
      templateName: selectedTemplate.value?.name,
      bindingId: printerId.value,
      mode: outputMode.value,
      copies: copies.value,
      isDuplex: isDuplex.value,
    })
    lastJob.value = job
    ElMessage.success(
      job.jobNo
        ? `${isReprint.value ? '补打' : '打印'}任务 ${job.jobNo} 已推送`
        : `${isReprint.value ? '补打' : '打印'}任务已推送`,
    )
    emit('printed')
  } catch (error) {
    lastJob.value = null
    lastError.value = getErrorMessage(error, isReprint.value ? '补打失败' : '打印失败')
    ElMessage.error(lastError.value)
  } finally {
    submitting.value = false
  }
}

watch(
  () => props.modelValue,
  (next) => {
    if (next) void open()
  },
)
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="dialogTitle"
    width="min(560px, 92vw)"
    append-to-body
    destroy-on-close
    class="temp-relief-print-dialog"
  >
    <div class="print-target">
      <div>
        <span class="print-target__label">申请编号</span>
        <strong class="print-target__value mono">{{ applicationNo || '—' }}</strong>
      </div>
      <div>
        <span class="print-target__label">申请人</span>
        <strong class="print-target__value">{{ applicantName || '—' }}</strong>
      </div>
    </div>

    <el-form label-position="top" class="print-form" @submit.prevent="handleSubmit">
      <el-form-item label="打印模板">
        <el-select
          v-model="templateId"
          filterable
          :loading="templatesLoading"
          :disabled="templatesLoading"
          placeholder="选择临时救助打印模板"
        >
          <el-option
            v-for="template in sortedTemplates"
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
          <el-button
            v-if="printersError"
            link
            type="primary"
            @click="loadPrinters(true)"
          >
            重试
          </el-button>
        </p>
      </el-form-item>

      <el-form-item label="输出方式">
        <el-radio-group v-model="outputMode">
          <el-radio-button value="print">打印</el-radio-button>
          <el-radio-button value="preview">预览</el-radio-button>
        </el-radio-group>
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

      <p v-if="selectedPrinter && outputMode === 'print'" class="print-summary">
        将推送到 <strong>{{ selectedPrinter.deviceName }}</strong> 的
        <strong>{{ selectedPrinter.printerName }}</strong>。
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
          <el-tag type="success" effect="dark">任务已推送</el-tag>
          <span class="mono">{{ lastJob.jobNo || `#${lastJob.id}` }}</span>
        </div>
        <p>
          状态：{{ lastJob.status || 'Pending' }} · 方式：{{ lastJob.mode === 'preview' ? '预览' : '打印' }}
          · 类型：{{ lastJob.jobKind || 'Template' }}
        </p>
        <p class="print-result__hint">
          任务由本机 Agent 领取后执行；如打印机离线或失败，请在打印任务列表查看失败原因。
        </p>
      </div>
    </el-form>

    <template #footer>
      <el-button :disabled="submitting" @click="visible = false">
        {{ lastJob ? '关闭' : '取消' }}
      </el-button>
      <el-button type="primary" :loading="submitting" :disabled="!canSubmit" @click="handleSubmit">
        {{ outputMode === 'preview' ? '生成预览' : (isReprint ? '补打' : '打印') }}
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.temp-relief-print-dialog :deep(.el-dialog__body) {
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
}
</style>
