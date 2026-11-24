<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import { Delete, InfoFilled, Plus, Printer as PrinterIcon, Refresh, Star } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { bindPrinter, listDevices, listPrinters, setDefaultPrinter, unbindPrinter } from '@/api/printers'
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
</script>

<template>
  <div class="nc-page printers-page">
    <header class="nc-page-header">
      <div>
        <h1 class="nc-page-title">打印机绑定</h1>
        <p class="nc-page-desc">管理当前账号可用的打印设备。手机端仅入队，实际打印由绑定的 Windows 电脑执行。</p>
      </div>
      <div class="header-actions">
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
        <el-button type="primary" :icon="Plus" @click="openBindDialog">绑定打印机</el-button>
      </div>
    </header>

    <div class="summary">
      <div class="summary-item">
        <span>已绑定</span>
        <strong>{{ items.length }}</strong>
      </div>
      <div class="summary-item">
        <span>在线设备</span>
        <strong>{{ onlineCount }}</strong>
      </div>
      <div class="summary-item summary-item--wide">
        <span>默认打印机</span>
        <strong :title="defaultPrinter">{{ defaultPrinter }}</strong>
      </div>
    </div>

    <div class="hint-bar">
      <el-icon><InfoFilled /></el-icon>
      <span>选择已启动 Agent 的电脑和该电脑上报的打印机；绑定只属于当前账号，不会打到别人的打印机。</span>
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

    <div v-loading="loading && items.length > 0" class="list-area">
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
              <el-tag v-if="row.isDefault" type="primary" effect="light" size="small">默认</el-tag>
              <span v-else class="nc-muted">—</span>
            </template>
          </el-table-column>

          <el-table-column label="状态" width="100" align="center">
            <template #default="{ row }">
              <span class="status-text">
                <i class="status-dot" :class="row.online ? 'is-online' : 'is-offline'" />
                {{ row.online ? '在线' : '离线' }}
              </span>
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
              <span class="status-text">
                <i class="status-dot" :class="row.online ? 'is-online' : 'is-offline'" />
                {{ row.online ? '在线' : '离线' }}
              </span>
            </div>

            <div class="card-printer">
              <el-icon><PrinterIcon /></el-icon>
              <span>{{ row.printerName }}</span>
              <el-tag v-if="row.isDefault" type="primary" effect="light" size="small">默认</el-tag>
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

      <el-empty v-else-if="!loading && !loadError" description="尚未绑定打印机">
        <el-button type="primary" :icon="Plus" @click="openBindDialog">绑定第一台打印机</el-button>
        <p class="empty-tip">请先在 Windows 电脑启动 Agent，再选择该电脑上报的打印机完成绑定。</p>
      </el-empty>
    </div>

    <el-dialog
      v-model="dialogVisible"
      title="绑定打印机"
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
  gap: 14px;
}

.header-actions {
  display: flex;
  flex: none;
  gap: 8px;
}

.summary {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.summary-item {
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 120px;
  padding: 10px 16px;
  border: 1px solid var(--nc-border);
  border-radius: 9px;
  background: #fff;
}

.summary-item span {
  color: var(--nc-text-muted);
  font-size: 12px;
}

.summary-item strong {
  overflow: hidden;
  max-width: 240px;
  font-size: 16px;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.summary-item--wide {
  flex: 1;
  min-width: 220px;
}

.summary-item--wide strong {
  font-size: 14px;
}

.hint-bar {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 11px 14px;
  border: 1px solid #dce9f5;
  border-radius: 9px;
  background: #f5f9fe;
  color: #40607c;
  font-size: 12px;
  line-height: 1.6;
}

.hint-bar .el-icon {
  flex: none;
  margin-top: 2px;
  color: var(--nc-primary);
}

.load-error {
  margin-bottom: 2px;
}

.list-area {
  min-height: 220px;
}

.list-skeleton {
  padding: 22px;
  border: 1px solid var(--nc-border);
  border-radius: var(--nc-radius);
  background: #fff;
}

.printer-table {
  width: 100%;
  border: 1px solid var(--nc-border);
  border-radius: var(--nc-radius);
  overflow: hidden;
}

.device-cell {
  display: flex;
  flex-direction: column;
  line-height: 1.45;
}

.device-cell strong {
  font-size: 13px;
  font-weight: 600;
}

.device-cell span {
  color: var(--nc-text-muted);
  font-size: 11px;
}

.printer-cell {
  display: flex;
  align-items: center;
  gap: 7px;
  color: var(--nc-text);
}

.printer-cell .el-icon {
  color: var(--nc-primary);
}

.status-text {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--nc-text-secondary);
  font-size: 12px;
  white-space: nowrap;
}

.status-dot {
  display: inline-block;
  width: 7px;
  height: 7px;
  border-radius: 50%;
}

.status-dot.is-online {
  background: #42b883;
  box-shadow: 0 0 0 3px rgba(66, 184, 131, 0.15);
}

.status-dot.is-offline {
  background: #b6c0cc;
  box-shadow: 0 0 0 3px rgba(182, 192, 204, 0.16);
}

.printer-cards {
  display: grid;
  gap: 10px;
}

.printer-card {
  padding: 15px 16px;
  border: 1px solid var(--nc-border);
  border-radius: var(--nc-radius);
  background: #fff;
  box-shadow: 0 1px 2px rgba(16, 42, 67, 0.04);
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
  padding: 9px 11px;
  border-radius: 8px;
  background: #f6f9fd;
  font-size: 13px;
}

.card-printer .el-icon {
  color: var(--nc-primary);
}

.card-meta {
  margin-top: 9px;
  color: var(--nc-text-muted);
  font-size: 11px;
}

.card-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
  padding-top: 11px;
  border-top: 1px solid #f0f3f8;
}

.empty-tip {
  margin-top: 10px;
  color: var(--nc-text-muted);
  font-size: 12px;
}

.dialog-tip {
  margin-bottom: 18px;
}

.switch-row {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  color: var(--nc-text-muted);
  font-size: 12px;
  line-height: 1.6;
}

@media (max-width: 767px) {
  .header-actions {
    width: 100%;
  }

  .header-actions .el-button {
    flex: 1;
  }

  .summary {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    gap: 8px;
  }

  .summary-item {
    min-width: 0;
    padding: 9px 12px;
  }

  .summary-item--wide {
    grid-column: 1 / -1;
    min-width: 0;
  }

  .summary-item--wide strong {
    max-width: none;
  }

  .hint-bar {
    font-size: 11px;
  }
}
</style>
