<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Printer, Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { getErrorMessage } from '@/api/http'
import { getAssetTask } from '@/api/assetVerification'
import { useAssetPrintDialog } from '@/api/assetPrint'
import type { AssetTaskDetail } from '@/types/assetVerification'
import { useMediaQuery } from '@/composables/useMediaQuery'
import AssetStatusTag from './AssetStatusTag.vue'
import {
  applicationReasonText,
  formatDate,
  formatDateTime,
  idTypeText,
  relationshipText,
  textOrDash,
} from './format'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    taskId: number | null
  }>(),
  {
    taskId: null,
  },
)

const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()

const narrow = useMediaQuery('(max-width: 720px)')
const detail = ref<AssetTaskDetail | null>(null)
const loading = ref(false)
const loadError = ref('')

/* ── 打印授权书 / 核查报告（与列表页共用同一套对话框状态） ─────── */
const printVisible = ref(false)
const {
  templates: printTemplates,
  templatesLoading: printTemplatesLoading,
  templatesError: printTemplatesError,
  printers: printPrinters,
  printersLoading: printPrintersLoading,
  printersError: printPrintersError,
  submitting: printSubmitting,
  templateId: printTemplateId,
  printerId: printPrinterId,
  mode: printMode,
  copies: printCopies,
  isDuplex: printIsDuplex,
  canSubmit: printCanSubmit,
  loadTemplates: loadPrintTemplates,
  loadPrinters: loadPrintPrinters,
  open: openPrintDialog,
  submit: submitPrintJob,
} = useAssetPrintDialog()

let requestTicket = 0

const basicFields = computed<Array<{ label: string; value: string }>>(() => {
  const task = detail.value
  if (!task) return []

  return [
    { label: '批次号', value: textOrDash(task.batchId) },
    { label: '申请人', value: textOrDash(task.applicantName) },
    { label: '身份证号', value: textOrDash(task.applicantIdCard) },
    { label: '证件类型', value: idTypeText(task.applicantIdType) },
    { label: '与户主关系', value: relationshipText(task.relationship) },
    { label: '是否户主', value: task.isHead ? '是' : '否' },
    { label: '户主身份证号', value: textOrDash(task.headIdCard) },
    { label: '联系电话', value: textOrDash(task.contactPhone) },
    { label: '所属社区', value: textOrDash(task.community) },
    { label: '家庭住址', value: textOrDash(task.familyAddress) },
    { label: '申请日期', value: formatDate(task.applicationDate) },
    { label: '当前状态', value: textOrDash(task.statusText) },
    { label: '创建时间', value: formatDateTime(task.createdAt) },
    { label: '更新时间', value: formatDateTime(task.updatedAt) },
  ]
})

async function load(taskId: number): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await getAssetTask(taskId)
    if (ticket !== requestTicket) return
    detail.value = response
  } catch (error) {
    if (ticket !== requestTicket) return
    detail.value = null
    loadError.value = getErrorMessage(error, '核对详情加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function handleVisibleChange(value: boolean): void {
  emit('update:modelValue', value)
}

async function openPrint(): Promise<void> {
  if (!detail.value) return
  printVisible.value = true
  await openPrintDialog()
}

async function handlePrintSubmit(): Promise<void> {
  const task = detail.value
  if (!task) return

  try {
    await submitPrintJob(task.id)
    ElMessage.success('已推送到 PC 端')
    printVisible.value = false
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '推送打印失败'))
  }
}

watch(
  () => [props.modelValue, props.taskId] as const,
  ([visible, taskId]) => {
    if (!visible || taskId === null) return
    detail.value = null
    void load(taskId)
  },
)
</script>

<template>
  <el-drawer
    :model-value="modelValue"
    :size="narrow ? '94%' : '580px'"
    append-to-body
    @update:model-value="handleVisibleChange"
  >
    <template #header>
      <div class="drawer-head">
        <div>
          <p class="drawer-kicker">ASSET VERIFICATION / 核对详情</p>
          <h2 class="drawer-title">{{ detail?.applicantName || '核对详情' }}</h2>
        </div>
        <AssetStatusTag
          v-if="detail"
          :status="detail.status"
          :status-text="detail.statusText"
        />
      </div>
    </template>

    <div v-if="loading" class="drawer-loading">
      <el-skeleton :rows="8" animated />
    </div>

    <el-alert
      v-else-if="loadError"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button
        v-if="taskId !== null"
        link
        type="primary"
        :icon="Refresh"
        @click="load(taskId)"
      >
        重新加载
      </el-button>
    </el-alert>

    <div v-else-if="detail" class="detail-body">
      <section class="detail-section">
        <h3 class="section-title">申请信息</h3>
        <dl class="detail-grid">
          <div v-for="field in basicFields" :key="field.label" class="detail-item">
            <dt>{{ field.label }}</dt>
            <dd>{{ field.value }}</dd>
          </div>
          <div class="detail-item detail-item--wide">
            <dt>申请事由</dt>
            <dd class="reason">{{ applicationReasonText(detail.applicationReason) }}</dd>
          </div>
        </dl>
      </section>

      <section class="detail-section">
        <h3 class="section-title">同批次家庭成员</h3>

        <el-table
          v-if="detail.members.length > 0"
          :data="detail.members"
          size="small"
          class="member-table"
        >
          <el-table-column prop="applicantName" label="姓名" min-width="90">
            <template #default="{ row }">{{ textOrDash(row.applicantName) }}</template>
          </el-table-column>
          <el-table-column prop="idCard" label="身份证号" min-width="164">
            <template #default="{ row }">
              <span class="mono">{{ textOrDash(row.idCard) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="与户主关系" min-width="96">
            <template #default="{ row }">{{ textOrDash(row.relationship) }}</template>
          </el-table-column>
          <el-table-column label="户主" width="64" align="center">
            <template #default="{ row }">{{ row.isHead ? '是' : '—' }}</template>
          </el-table-column>
        </el-table>

        <p v-else class="member-empty">暂无同批次家庭成员。</p>
      </section>

      <section class="detail-section print-section">
        <el-button type="primary" :icon="Printer" @click="openPrint">
          打印授权书 / 核查报告
        </el-button>
        <p class="print-note">按整户数据推送到已绑定的 PC 端打印机，可选预览或直接打印。</p>
      </section>
    </div>

    <div v-else class="drawer-empty">
      <el-empty description="未找到核对详情" />
    </div>
  </el-drawer>

  <el-dialog
    v-model="printVisible"
    :width="narrow ? '94%' : '520px'"
    title="打印授权书 / 核查报告"
    append-to-body
  >
    <el-form label-position="top" class="print-form">
      <el-form-item label="核对任务">
        <el-input
          :model-value="`${detail?.applicantName || '未命名'}（${detail?.batchId || '无批次号'}）`"
          disabled
        />
      </el-form-item>

      <el-form-item label="打印机">
        <el-select
          v-model="printPrinterId"
          :loading="printPrintersLoading"
          :disabled="printPrintersLoading"
          filterable
          clearable
          placeholder="选择已绑定的打印机"
        >
          <el-option
            v-for="printer in printPrinters"
            :key="printer.id"
            :label="`${printer.deviceName} · ${printer.printerName} · ${printer.online ? '在线' : '离线'}`"
            :value="printer.id"
          />
        </el-select>
        <p v-if="printPrintersError" class="form-hint is-error">
          {{ printPrintersError }}
          <el-button link type="primary" @click="loadPrintPrinters(true)">重试</el-button>
        </p>
        <p v-else-if="printPrintersLoading" class="form-hint">正在读取已绑定打印机…</p>
        <p v-else-if="printPrinters.length === 0" class="form-hint is-error">
          当前账号未绑定打印机，请先在本机 Agent 绑定；预览模式可继续使用。
        </p>
        <p v-else-if="printMode === 'print' && !printPrinterId" class="form-hint is-error">
          打印模式需要选择打印机
        </p>
        <p v-else-if="printMode === 'preview' && !printPrinterId" class="form-hint">
          预览模式可不选打印机
        </p>
      </el-form-item>

      <el-form-item label="打印模板">
        <el-select
          v-model="printTemplateId"
          :loading="printTemplatesLoading"
          :disabled="printTemplatesLoading"
          placeholder="选择模板"
        >
          <el-option
            v-for="template in printTemplates"
            :key="template.id"
            :label="template.name"
            :value="template.id"
          />
        </el-select>
        <p v-if="printTemplatesError" class="form-hint is-error">
          {{ printTemplatesError }}
          <el-button link type="primary" @click="loadPrintTemplates(true)">重试</el-button>
        </p>
        <p v-else-if="!printTemplatesLoading && printTemplates.length === 0" class="form-hint">
          当前业务域暂无可用的打印模板
        </p>
      </el-form-item>

      <el-form-item label="输出方式">
        <el-radio-group v-model="printMode">
          <el-radio-button value="preview">预览</el-radio-button>
          <el-radio-button value="print">打印</el-radio-button>
        </el-radio-group>
      </el-form-item>

      <div class="print-options">
        <el-form-item label="份数">
          <el-input-number v-model="printCopies" :min="1" :max="99" />
        </el-form-item>
        <el-form-item label="单双面">
          <el-radio-group v-model="printIsDuplex">
            <el-radio-button :value="false">单面</el-radio-button>
            <el-radio-button :value="true">双面</el-radio-button>
          </el-radio-group>
        </el-form-item>
      </div>
    </el-form>

    <template #footer>
      <el-button @click="printVisible = false">取消</el-button>
      <el-button
        type="primary"
        :loading="printSubmitting"
        :disabled="!printCanSubmit"
        @click="handlePrintSubmit"
      >
        确定推送
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.drawer-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  min-width: 0;
}

.drawer-kicker {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.16em;
}

.drawer-title {
  margin-top: 4px;
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  font-weight: 700;
  letter-spacing: 0.03em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.drawer-loading {
  padding: 8px 2px;
}

.detail-body {
  display: flex;
  flex-direction: column;
  gap: 22px;
  padding-bottom: 12px;
}

.detail-section {
  min-width: 0;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.section-title::before {
  content: '';
  flex: none;
  width: 3px;
  height: 13px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal);
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1px;
  overflow: hidden;
  margin: 0;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-line-faint);
}

.detail-item {
  min-width: 0;
  padding: 10px 12px;
  background-color: var(--nc-paper-2);
}

.detail-item--wide {
  grid-column: 1 / -1;
}

.detail-item dt {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.detail-item dd {
  margin: 3px 0 0;
  overflow-wrap: anywhere;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.55;
}

.detail-item dd.reason {
  white-space: pre-wrap;
}

.member-table {
  width: 100%;
}

.member-empty {
  padding: 18px 4px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.drawer-empty {
  padding: 24px 0;
}

.print-section {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px 14px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.print-note {
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.print-form :deep(.el-select) {
  width: 100%;
}

.print-options {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.form-hint {
  margin: 6px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.form-hint.is-error {
  color: var(--el-color-danger);
}

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

@media (max-width: 720px) {
  .detail-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .print-options {
    grid-template-columns: minmax(0, 1fr);
  }

  .drawer-title {
    font-size: var(--nc-text-lg);
  }
}
</style>
