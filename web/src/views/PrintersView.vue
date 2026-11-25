<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import {
  Delete,
  InfoFilled,
  Key,
  Plus,
  Printer as PrinterIcon,
  Refresh,
  Star,
  Timer,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  bindPrinter,
  generatePrinterBindCode,
  listDevices,
  listPrinters,
  setDefaultPrinter,
  unbindPrinter,
} from '@/api/printers'
import type { PrinterBindCode } from '@/api/printers'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { PrinterBinding, PrinterDevice } from '@/types/api'

const isNarrow = useMediaQuery('(max-width: 900px)')

const items = ref<PrinterBinding[]>([])
const devices = ref<PrinterDevice[]>([])
const loading = ref(false)
const devicesLoading = ref(false)
const loadError = ref('')

const dialogVisible = ref(false)
const submitting = ref(false)
const actionId = ref<number | null>(null)
const formRef = ref<FormInstance>()

const form = reactive<{
  deviceId: number | null
  printerName: string
  isDefault: boolean
}>({
  deviceId: null,
  printerName: '',
  isDefault: false,
})

const rules: FormRules<typeof form> = {
  deviceId: [{ required: true, message: '请选择 Agent 设备', trigger: 'change' }],
  printerName: [{ required: true, message: '请选择打印机', trigger: 'change' }],
}

const selectedDevice = computed(() => devices.value.find((device) => device.id === form.deviceId))
const printerOptions = computed(() => selectedDevice.value?.printers ?? [])

const onlineCount = computed(() => items.value.filter((item) => item.online).length)
const defaultPrinter = computed(() => items.value.find((item) => item.isDefault)?.printerName ?? '未设置')

/* ── 本机绑定码 ─────────────────────────────────────────────────── */
const bindCodeVisible = ref(false)
const bindCodeLoading = ref(false)
const bindCode = ref<PrinterBindCode | null>(null)
const bindCodeError = ref('')
const remainingSeconds = ref(0)
let countdownTimer: number | undefined
let bindingWatchTimer: number | undefined

const bindCodeText = computed(() => bindCode.value?.code || '------')
const bindCodeExpired = computed(() => remainingSeconds.value <= 0)
const countdownText = computed(() => {
  const total = Math.max(0, remainingSeconds.value)
  const minutes = Math.floor(total / 60)
  const seconds = total % 60
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
})

function formatTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(date)
}

async function load(options: { silent?: boolean } = {}): Promise<void> {
  if (!options.silent) loading.value = true
  loadError.value = ''
  try {
    items.value = await listPrinters()
  } catch (error) {
    loadError.value = getErrorMessage(error, '打印机列表加载失败')
  } finally {
    loading.value = false
  }
}

async function loadDevices(): Promise<void> {
  devicesLoading.value = true
  try {
    devices.value = await listDevices()
  } catch (error) {
    devices.value = []
    ElMessage.error(getErrorMessage(error, '设备列表加载失败'))
  } finally {
    devicesLoading.value = false
  }
}

function stopCountdown(): void {
  if (countdownTimer !== undefined) {
    window.clearInterval(countdownTimer)
    countdownTimer = undefined
  }
}

function stopBindingWatch(): void {
  if (bindingWatchTimer !== undefined) {
    window.clearInterval(bindingWatchTimer)
    bindingWatchTimer = undefined
  }
}

function tickCountdown(): void {
  const expiresAt = bindCode.value?.expiresAt
  if (!expiresAt) {
    remainingSeconds.value = 0
    stopCountdown()
    return
  }
  const expires = new Date(expiresAt).getTime()
  if (Number.isNaN(expires)) {
    remainingSeconds.value = 0
    stopCountdown()
    return
  }
  remainingSeconds.value = Math.max(0, Math.ceil((expires - Date.now()) / 1000))
  if (remainingSeconds.value <= 0) stopCountdown()
}

function startCountdown(): void {
  stopCountdown()
  tickCountdown()
  countdownTimer = window.setInterval(tickCountdown, 1_000)
}

/** Agent 在另一侧完成绑定，这里轮询比对绑定集合，发现新绑定即提示并关闭弹窗。 */
function startBindingWatch(): void {
  stopBindingWatch()
  const knownIds = new Set(items.value.map((item) => item.id))

  bindingWatchTimer = window.setInterval(async () => {
    if (!bindCodeVisible.value) return
    try {
      const list = await listPrinters()
      if (list.some((item) => !knownIds.has(item.id))) {
        items.value = list
        loadError.value = ''
        ElMessage.success('已检测到本机打印机绑定成功')
        closeBindCodeDialog()
      }
    } catch {
      // 轮询失败静默处理，避免打断倒计时；用户可手动刷新。
    }
  }, 4_000)
}

async function requestBindCode(): Promise<void> {
  if (bindCodeLoading.value) return
  bindCodeLoading.value = true
  bindCodeError.value = ''
  try {
    bindCode.value = await generatePrinterBindCode()
    startCountdown()
    startBindingWatch()
  } catch (error) {
    bindCode.value = null
    remainingSeconds.value = 0
    stopCountdown()
    stopBindingWatch()
    bindCodeError.value = getErrorMessage(error, '绑定码生成失败，请稍后重试')
  } finally {
    bindCodeLoading.value = false
  }
}

async function openBindCodeDialog(): Promise<void> {
  bindCodeVisible.value = true
  await requestBindCode()
}

function closeBindCodeDialog(): void {
  bindCodeVisible.value = false
}

function onBindCodeClosed(): void {
  stopCountdown()
  stopBindingWatch()
  bindCode.value = null
  bindCodeError.value = ''
  remainingSeconds.value = 0
}

async function refreshBindings(): Promise<void> {
  await load()
  if (!loadError.value) ElMessage.success('绑定列表已刷新')
}

async function openBindDialog(): Promise<void> {
  form.deviceId = null
  form.printerName = ''
  form.isDefault = items.value.length === 0
  dialogVisible.value = true
  await nextTick()
  formRef.value?.clearValidate()
  await loadDevices()
}

function onDeviceChanged(): void {
  form.printerName = ''
  formRef.value?.clearValidate('printerName')
}

function onDialogClosed(): void {
  formRef.value?.clearValidate()
}

async function submitBind(): Promise<void> {
  if (submitting.value) return

  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  if (!form.deviceId || !form.printerName) {
    ElMessage.warning('请选择设备和打印机')
    return
  }

  submitting.value = true
  try {
    await bindPrinter({
      deviceId: form.deviceId,
      printerName: form.printerName,
      isDefault: form.isDefault,
    })
    ElMessage.success('打印机绑定成功')
    dialogVisible.value = false
    await load({ silent: true })
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '绑定失败，请核对设备与打印机名称'))
  } finally {
    submitting.value = false
  }
}

async function handleSetDefault(row: PrinterBinding): Promise<void> {
  if (row.isDefault || actionId.value !== null) return
  actionId.value = row.id
  try {
    await setDefaultPrinter(row.id)
    ElMessage.success(`已将“${row.printerName}”设为默认`)
    await load({ silent: true })
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '设置默认打印机失败'))
  } finally {
    actionId.value = null
  }
}

async function handleUnbind(row: PrinterBinding): Promise<void> {
  if (actionId.value !== null) return

  try {
    await ElMessageBox.confirm(
      `确定解除“${row.printerName}”与设备“${row.deviceName}”的绑定吗？`,
      '解除绑定',
      {
        confirmButtonText: '解除',
        cancelButtonText: '取消',
        type: 'warning',
      },
    )
  } catch {
    return
  }

  actionId.value = row.id
  try {
    await unbindPrinter(row.id)
    ElMessage.success('已解除绑定')
    await load({ silent: true })
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '解除绑定失败'))
  } finally {
    actionId.value = null
  }
}

onMounted(() => {
  void load()
})

onBeforeUnmount(() => {
  stopCountdown()
  stopBindingWatch()
})
</script>

<template>
  <div class="nc-page printers-page">
    <NcPageHeader
      title="打印机绑定"
      description="网页生成绑定码，Agent 读取本机打印机后自动写入绑定。手机端仅入队，实际打印由绑定的 Windows 电脑执行。"
    >
      <template #actions>
        <div class="header-actions">
          <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
          <el-button :icon="Key" @click="openBindCodeDialog">生成本机绑定码</el-button>
          <el-button type="primary" :icon="Plus" @click="openBindDialog">手动绑定</el-button>
        </div>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="已绑定" :value="items.length" tone="ink" />
      <NcStat label="在线设备" :value="onlineCount" tone="pine" />
      <NcStat
        class="summary-default"
        label="默认打印机"
        :value="defaultPrinter"
        tone="seal"
        compact
      />
    </div>

    <div class="hint-bar">
      <el-icon><InfoFilled /></el-icon>
      <span>推荐在目标电脑上点「生成本机绑定码」，由 Agent 自动读取本机打印机完成绑定；也可手动选择设备与打印机作为兜底。绑定只属于当前账号，不会打到别人的打印机。</span>
    </div>

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

    <NcCard padding="none" class="list-card">
      <div
        v-loading="loading && items.length > 0"
        class="list-area"
        :class="{ 'list-area--cards': isNarrow }"
      >
        <el-skeleton v-if="loading && items.length === 0" :rows="4" animated class="list-skeleton" />

        <template v-else-if="items.length > 0">
          <el-table v-if="!isNarrow" :data="items" class="printer-table" row-key="id">
            <el-table-column label="绑定设备" min-width="180">
              <template #default="{ row }">
                <div class="device-cell">
                  <strong>{{ row.deviceName }}</strong>
                  <span>设备 #{{ row.deviceId }}</span>
                </div>
              </template>
            </el-table-column>

            <el-table-column label="打印机名称" min-width="200">
              <template #default="{ row }">
                <div class="printer-cell">
                  <el-icon><PrinterIcon /></el-icon>
                  <span>{{ row.printerName }}</span>
                </div>
              </template>
            </el-table-column>

            <el-table-column label="默认" width="90" align="center">
              <template #default="{ row }">
                <NcStatusDot v-if="row.isDefault" status="default" label="默认" />
                <span v-else class="nc-muted">—</span>
              </template>
            </el-table-column>

            <el-table-column label="状态" width="100" align="center">
              <template #default="{ row }">
                <NcStatusDot
                  :status="row.online ? 'online' : 'offline'"
                  :label="row.online ? '在线' : '离线'"
                />
              </template>
            </el-table-column>

            <el-table-column label="最近在线" width="150">
              <template #default="{ row }">{{ formatTime(row.lastSeenAt) }}</template>
            </el-table-column>

            <el-table-column label="操作" width="190" align="right">
              <template #default="{ row }">
                <el-button
                  link
                  type="primary"
                  :icon="Star"
                  :disabled="row.isDefault"
                  :loading="actionId === row.id"
                  @click="handleSetDefault(row as PrinterBinding)"
                >
                  设为默认
                </el-button>
                <el-button
                  link
                  type="danger"
                  :icon="Delete"
                  :loading="actionId === row.id"
                  @click="handleUnbind(row as PrinterBinding)"
                >
                  解绑
                </el-button>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="printer-cards">
            <article v-for="row in items" :key="row.id" class="printer-card">
              <div class="card-head">
                <div class="device-cell">
                  <strong>{{ row.deviceName }}</strong>
                  <span>设备 #{{ row.deviceId }}</span>
                </div>
                <NcStatusDot
                  :status="row.online ? 'online' : 'offline'"
                  :label="row.online ? '在线' : '离线'"
                />
              </div>

              <div class="card-printer">
                <el-icon><PrinterIcon /></el-icon>
                <span>{{ row.printerName }}</span>
                <NcStatusDot v-if="row.isDefault" status="default" label="默认" />
              </div>

              <p class="card-meta">最近在线：{{ formatTime(row.lastSeenAt) }}</p>

              <div class="card-actions">
                <el-button
                  size="small"
                  :icon="Star"
                  :disabled="row.isDefault"
                  :loading="actionId === row.id"
                  @click="handleSetDefault(row)"
                >
                  设为默认
                </el-button>
                <el-button
                  size="small"
                  type="danger"
                  plain
                  :icon="Delete"
                  :loading="actionId === row.id"
                  @click="handleUnbind(row)"
                >
                  解绑
                </el-button>
              </div>
            </article>
          </div>
        </template>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty description="尚未绑定打印机">
            <el-button type="primary" :icon="Key" @click="openBindCodeDialog">生成本机绑定码</el-button>
            <el-button :icon="Plus" @click="openBindDialog">手动绑定</el-button>
            <p class="empty-tip">
              请在 Windows 电脑启动 Agent 后生成本机绑定码，或在下方手动选择该电脑上报的打印机。
            </p>
          </el-empty>
        </div>
      </div>
    </NcCard>

    <el-dialog
      v-model="bindCodeVisible"
      title="本机绑定码"
      :width="isNarrow ? '92%' : '460px'"
      :close-on-click-modal="false"
      @closed="onBindCodeClosed"
    >
      <div v-if="bindCodeLoading && !bindCode" class="bind-code-loading">
        <el-skeleton :rows="3" animated />
      </div>

      <el-alert
        v-else-if="bindCodeError"
        type="error"
        :title="bindCodeError"
        :closable="false"
        show-icon
      />

      <div v-else class="bind-code-body">
        <p class="bind-code-label">绑定码</p>
        <div class="bind-code-value" :class="{ 'is-expired': bindCodeExpired }">
          {{ bindCodeText }}
        </div>

        <div class="bind-code-countdown" :class="{ 'is-expired': bindCodeExpired }">
          <el-icon><Timer /></el-icon>
          <span v-if="bindCodeExpired">绑定码已过期，请重新生成</span>
          <span v-else>有效期剩余 {{ countdownText }}</span>
        </div>

        <div class="bind-code-steps">
          <p>请在当前电脑的 NewCosmos Agent 托盘选择「绑定本机打印机」并输入此码。</p>
          <p class="nc-muted">
            Agent 读取本机打印机后会自动写入绑定，成功后本页列表会自动刷新。
          </p>
        </div>

        <p v-if="bindCode?.expiresAt" class="bind-code-expire nc-muted">
          过期时间：{{ formatTime(bindCode.expiresAt) }}
        </p>
      </div>

      <template #footer>
        <el-button @click="closeBindCodeDialog">关闭</el-button>
        <el-button :loading="loading" @click="refreshBindings">刷新绑定列表</el-button>
        <el-button type="primary" :loading="bindCodeLoading" @click="requestBindCode">
          重新生成
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="dialogVisible"
      title="手动绑定打印机"
      :width="isNarrow ? '92%' : '480px'"
      :close-on-click-modal="false"
      @closed="onDialogClosed"
    >
      <el-alert
        class="dialog-tip"
        type="info"
        :closable="false"
        show-icon
        title="只显示已注册 Agent 的设备和该设备上报的打印机；设备离线时仍可绑定，但打印任务会等待。"
      />

      <el-form ref="formRef" :model="form" :rules="rules" label-position="top" @submit.prevent="submitBind">
        <el-form-item label="Agent 设备" prop="deviceId">
          <el-select
            v-model="form.deviceId"
            placeholder="选择已注册的电脑"
            filterable
            :loading="devicesLoading"
            style="width: 100%"
            @change="onDeviceChanged"
          >
            <el-option
              v-for="device in devices"
              :key="device.id"
              :label="`${device.machineName}${device.online ? '（在线）' : '（离线）'}`"
              :value="device.id"
            />
          </el-select>
        </el-form-item>

        <el-form-item label="打印机" prop="printerName">
          <el-select
            v-model="form.printerName"
            placeholder="选择该设备上报的打印机"
            filterable
            :disabled="!selectedDevice"
            style="width: 100%"
          >
            <el-option
              v-for="name in printerOptions"
              :key="name"
              :label="name"
              :value="name"
            />
          </el-select>
          <p v-if="selectedDevice && printerOptions.length === 0" class="nc-muted form-tip">
            该设备尚未上报打印机，请等待 Agent 心跳后刷新。
          </p>
        </el-form-item>

        <el-form-item label="设为默认打印机">
          <div class="switch-row">
            <el-switch v-model="form.isDefault" />
            <span>同一账号只会有一台默认打印机，首个绑定会自动设为默认。</span>
          </div>
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="submitBind">确认绑定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.printers-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.header-actions {
  display: flex;
  flex: none;
  gap: 8px;
}

/* ── 概要统计 ─────────────────────────────────────────────────── */
.summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.summary-default {
  min-width: 0;
}

/* ── 提示条 ───────────────────────────────────────────────────── */
.hint-bar {
  display: flex;
  align-items: flex-start;
  gap: 9px;
  padding: 11px 14px;
  border: 1px solid rgba(150, 104, 28, 0.28);
  border-left: 3px solid var(--nc-amber);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

.hint-bar .el-icon {
  flex: none;
  margin-top: 2px;
  color: var(--nc-amber);
}

.load-error {
  margin-bottom: 0;
}

/* ── 列表 ─────────────────────────────────────────────────────── */
.list-card {
  overflow: hidden;
}

.list-area {
  min-height: 220px;
}

.list-area--cards {
  padding: 12px;
}

.list-skeleton {
  padding: 22px;
}

.printer-table {
  width: 100%;
}

.device-cell {
  display: flex;
  flex-direction: column;
  line-height: 1.45;
}

.device-cell strong {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
}

.device-cell span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.printer-cell {
  display: flex;
  align-items: center;
  gap: 7px;
  color: var(--nc-ink);
}

.printer-cell .el-icon {
  color: var(--nc-seal);
}

/* ── 手机卡片 ─────────────────────────────────────────────────── */
.printer-cards {
  display: grid;
  gap: 10px;
}

.printer-card {
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

.card-printer {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 7px;
  margin-top: 12px;
  padding: 8px 10px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink);
  font-size: 13px;
}

.card-printer .el-icon {
  color: var(--nc-seal);
}

.card-meta {
  margin-top: 9px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
  padding-top: 11px;
  border-top: 1px solid var(--nc-line-faint);
}

.card-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.empty-wrap {
  padding: 10px 8px;
}

.empty-tip {
  margin-top: 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

/* ── 绑定码弹窗 ───────────────────────────────────────────────── */
.bind-code-loading {
  padding: 6px 2px;
}

.bind-code-body {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  text-align: center;
}

.bind-code-label {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.12em;
}

.bind-code-value {
  padding: 12px 10px;
  border: 1px dashed var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: clamp(34px, 9vw, 46px);
  font-weight: 700;
  letter-spacing: 0.28em;
  line-height: 1.1;
  text-indent: 0.28em;
  width: 100%;
}

.bind-code-value.is-expired {
  border-color: var(--nc-line);
  color: var(--nc-muted);
}

.bind-code-countdown {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--nc-seal);
  font-size: var(--nc-text-sm);
  font-variant-numeric: tabular-nums;
}

.bind-code-countdown.is-expired {
  color: var(--nc-muted);
}

.bind-code-steps {
  width: 100%;
  margin-top: 4px;
  padding: 10px 12px;
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
  text-align: left;
}

.bind-code-steps p + p {
  margin-top: 4px;
}

.bind-code-expire {
  font-size: var(--nc-text-xs);
}

/* ── 弹窗 ─────────────────────────────────────────────────────── */
.dialog-tip {
  margin-bottom: 18px;
}

.form-tip {
  margin-top: 6px;
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.switch-row {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

/* ── 手机 ─────────────────────────────────────────────────────── */
@media (max-width: 767px) {
  .header-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    width: 100%;
  }

  .header-actions .el-button {
    width: 100%;
    min-width: 0;
    margin-left: 0;
    flex: none;
  }

  .header-actions .el-button:nth-child(2) {
    grid-column: 1 / -1;
  }

  .summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
  }

  .summary-default {
    grid-column: 1 / -1;
  }

  .hint-bar {
    padding: 10px 12px;
    font-size: var(--nc-text-xs);
  }

  .list-area--cards {
    padding: 10px;
  }

  .printer-card {
    padding: 13px 14px;
  }
}
</style>
