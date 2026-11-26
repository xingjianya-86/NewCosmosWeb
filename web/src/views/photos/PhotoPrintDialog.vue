<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { Printer as PrinterIcon } from '@element-plus/icons-vue'
import { listPrinters } from '@/api/printers'
import { printPhotos } from '@/api/photos'
import { getErrorMessage } from '@/api/http'
import type { PrinterBinding } from '@/types/api'
import type { PhotoPrintLayout, PhotoPrintResult } from '@/types/photos'
import { PHOTO_LAYOUT_OPTIONS, photoActionError } from './photoMeta'

const props = defineProps<{
  modelValue: boolean
  photoIds: number[]
}>()

const emit = defineEmits<{
  (event: 'update:modelValue', value: boolean): void
  (event: 'printed', result: PhotoPrintResult): void
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const printers = ref<PrinterBinding[]>([])
const printersLoading = ref(false)
const printersError = ref('')
const printerId = ref<number | null>(null)
const layout = ref<PhotoPrintLayout>('')
const submitting = ref(false)
const result = ref<PhotoPrintResult | null>(null)
const lastError = ref('')

const photoCount = computed(() => props.photoIds.length)
const dialogTitle = computed(() => `打印照片（${photoCount.value} 张）`)
const selectedPrinter = computed(
  () => printers.value.find((printer) => printer.id === printerId.value) ?? null,
)
const canSubmit = computed(() => photoCount.value > 0 && !submitting.value)

const printerHint = computed(() => {
  if (printersLoading.value) return '正在读取已绑定打印机…'
  if (printersError.value) return printersError.value
  if (printers.value.length === 0) {
    return '当前账号未绑定打印机；任务将由手机/电脑端 Agent 使用本机默认打印机执行。'
  }
  if (printerId.value === null) return '未选择打印机时由 Agent 使用默认打印机。'
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

async function loadPrinters(): Promise<void> {
  printersLoading.value = true
  printersError.value = ''
  try {
    printers.value = await listPrinters()
    if (
      printerId.value !== null &&
      printers.value.some((printer) => printer.id === printerId.value)
    ) {
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

function reset(): void {
  layout.value = ''
  result.value = null
  lastError.value = ''
  printerId.value = null
}

async function open(): Promise<void> {
  reset()
  await loadPrinters()
}

async function submit(): Promise<void> {
  if (photoCount.value === 0) {
    ElMessage.warning('请先勾选要打印的照片')
    return
  }

  submitting.value = true
  lastError.value = ''
  try {
    const printed = await printPhotos({
      photoIds: [...props.photoIds],
      layout: layout.value,
      bindingId: printerId.value,
      printerName: selectedPrinter.value?.printerName,
    })
    result.value = printed
    emit('printed', printed)

    if (printed.failures.length === 0) {
      ElMessage.success(
        printed.jobNo ? `打印任务 ${printed.jobNo} 已推送` : '打印任务已推送',
      )
    } else if (printed.successCount > 0) {
      ElMessage.warning(`成功 ${printed.successCount} 张，失败 ${printed.failures.length} 项`)
    } else {
      ElMessage.error('照片打印失败')
    }
  } catch (error) {
    result.value = null
    lastError.value = photoActionError(
      error,
      '照片打印失败',
      '打印照片',
      'PRINT_FAMILYAPPLICATION',
    )
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
    width="min(600px, 94vw)"
    append-to-body
    destroy-on-close
    class="photo-print-dialog"
  >
    <div class="print-summary">
      <div>
        <span class="print-summary__label">待打印</span>
        <strong>{{ photoCount }} 张照片</strong>
      </div>
      <div>
        <span class="print-summary__label">执行端</span>
        <strong>{{ selectedPrinter?.deviceName || '本机 Agent' }}</strong>
      </div>
    </div>

    <el-form label-position="top" class="print-form" @submit.prevent="submit">
      <el-form-item label="打印版式">
        <el-select v-model="layout" placeholder="选择版式">
          <el-option
            v-for="option in PHOTO_LAYOUT_OPTIONS"
            :key="option.value || 'default'"
            :label="option.label"
            :value="option.value"
          />
        </el-select>
        <p class="form-hint">「按默认」逐张判断：身份证原寸，其余整页。</p>
      </el-form-item>

      <el-form-item label="已绑定打印机">
        <el-select
          v-model="printerId"
          filterable
          clearable
          :loading="printersLoading"
          :disabled="printersLoading"
          placeholder="使用默认打印机"
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
          <el-button v-if="printersError" link type="primary" @click="loadPrinters">
            重试
          </el-button>
        </p>
      </el-form-item>

      <p v-if="selectedPrinter && selectedPrinter.online === false" class="form-hint is-warning">
        该打印机当前显示离线；任务会先入队，Agent 上线后自动领取。
      </p>

      <el-alert
        v-if="lastError"
        class="print-result print-result--error"
        type="error"
        :title="lastError"
        :closable="false"
        show-icon
      />

      <div v-if="result" class="print-result" :class="{ 'print-result--error': result.failures.length > 0 }">
        <div class="print-result__head">
          <el-tag
            :type="result.successCount > 0 ? (result.failures.length === 0 ? 'success' : 'warning') : 'danger'"
            effect="dark"
          >
            {{
              result.successCount > 0
                ? result.failures.length === 0
                  ? '任务已推送'
                  : '部分失败'
                : '打印失败'
            }}
          </el-tag>
          <span class="mono">{{ result.jobNo || (result.id ? `#${result.id}` : '—') }}</span>
        </div>
        <p>
          成功 {{ result.successCount }} / {{ photoCount }} 张 · 状态 {{ result.status || 'Pending' }}
        </p>
        <p v-if="result.message" class="print-result__hint">{{ result.message }}</p>
        <ul v-if="result.failures.length > 0" class="print-failures">
          <li v-for="(failure, index) in result.failures" :key="`${failure.photoId ?? 'x'}-${index}`">
            {{ failure.message }}
          </li>
        </ul>
        <p class="print-result__hint">
          任务由已登录的打印 Agent 领取执行；失败项可在打印任务列表查看原因。
        </p>
      </div>
    </el-form>

    <template #footer>
      <el-button :disabled="submitting" @click="visible = false">
        {{ result ? '关闭' : '取消' }}
      </el-button>
      <el-button type="primary" :icon="PrinterIcon" :loading="submitting" :disabled="!canSubmit" @click="submit">
        打印
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.photo-print-dialog :deep(.el-dialog__body) {
  max-height: 72vh;
  overflow-y: auto;
}

.print-summary {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px 16px;
  margin-bottom: 16px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.print-summary > div {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.print-summary__label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.1em;
}

.print-summary strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
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

.form-hint.is-warning {
  color: var(--nc-amber-dark);
}

.print-result {
  margin-top: 8px;
  padding: 10px 12px;
  border: 1px solid rgba(46, 71, 56, 0.3);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-pine-soft);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.print-result--error {
  border-color: rgba(171, 50, 39, 0.35);
  background: var(--nc-paper-3);
}

.print-result p {
  margin: 0;
  color: var(--nc-ink-2);
}

.print-result__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
  color: var(--nc-ink);
}

.print-result__hint {
  margin-top: 4px !important;
  color: var(--nc-muted) !important;
}

.print-failures {
  margin: 6px 0 0;
  padding-left: 18px;
  color: var(--nc-seal-dark);
}

.mono {
  font-family: var(--nc-font-mono);
}

@media (max-width: 560px) {
  .print-summary {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
