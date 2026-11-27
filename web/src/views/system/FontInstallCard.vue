<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Check, Download, Refresh } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  LocalOpCancelledError,
  parseLocalOpResult,
  runLocalOp,
} from '@/api/localOps'
import { listDevices } from '@/api/printers'
import NcCard from '@/components/NcCard.vue'
import type { PrinterDevice } from '@/types/api'

/*
 * 公文字体检查/一键安装（LocalOp: font.check / font.install）。
 *
 * 交互骨架照桌面 MainViewModel 的启动检查：非阻塞检查 → 缺失区块 → 一键安装（后台执行）
 * → 成功/失败两套文案 → 手动安装兜底。
 *
 * 只对公文文书打印/导出版式有意义：缺字体会被替换导致版式不正确，但不阻塞其他功能。
 * 字体安装在目标电脑的 Agent 上执行（复制到用户字体目录 + 写 HKCU 注册表，免管理员）。
 */

interface FontCheckResult {
  dirExists?: boolean
  total?: number
  missing?: string[]
  omitted?: number
}

interface FontInstallResult {
  installed?: number
  total?: number
}

/** 手动兜底文案：安装失败时提示可直接到安装目录手工装。 */
const MANUAL_HINT = '可手动安装 公文字体 目录内的字体'

const devices = ref<PrinterDevice[]>([])
const devicesLoading = ref(false)
const deviceId = ref<number | null>(null)
const busy = ref('')
let destroyed = false

const checked = ref(false)
const dirExists = ref(true)
const total = ref(0)
const missing = ref<string[]>([])
const omitted = ref(0)

function capabilityLabel(device: PrinterDevice): string {
  const caps = device.capabilities ?? []
  return caps.length > 0 ? ` · ${caps.join(' · ')}` : ''
}

const deviceOptions = computed(() =>
  devices.value
    .map((device) => ({
      id: device.id,
      // 仅"在线 + 上报 font 能力"的设备可用；老 Agent 未上报能力时不可选。
      eligible: device.online && (device.capabilities ?? []).includes('font'),
      label: `${device.machineName}（${device.online ? '在线' : '离线'}）${capabilityLabel(device)}`,
    }))
    .sort((left, right) => Number(right.eligible) - Number(left.eligible)),
)

const noTarget = computed(() => !deviceOptions.value.some((item) => item.eligible))
const canOperate = computed(() => !noTarget.value && deviceId.value !== null && busy.value === '')

async function loadDevices(): Promise<void> {
  devicesLoading.value = true
  try {
    devices.value = await listDevices()
    const current = deviceOptions.value.find((item) => item.id === deviceId.value)
    if (!current || !current.eligible) {
      deviceId.value = deviceOptions.value.find((item) => item.eligible)?.id ?? null
    }
  } catch (error) {
    devices.value = []
    deviceId.value = null
    ElMessage.error(getErrorMessage(error, '设备列表加载失败'))
  } finally {
    devicesLoading.value = false
  }
}

async function runOp<T = Record<string, unknown>>(
  op: string,
  errorHint?: string,
): Promise<T | null> {
  if (noTarget.value || deviceId.value === null) {
    ElMessage.warning('无在线 Agent 设备')
    return null
  }

  const failText = (message: string): string =>
    errorHint ? `${message}；${errorHint}` : message

  busy.value = op
  try {
    const outcome = await runLocalOp(deviceId.value, op, undefined, {
      isCancelled: () => destroyed,
    })
    if (outcome.status === 'Failed') {
      ElMessage.error(failText(outcome.message || '本机操作执行失败'))
      return null
    }

    const result = parseLocalOpResult<Record<string, unknown>>(outcome.message)
    if (!result) {
      ElMessage.success('本机操作已完成')
      return null
    }
    return result as T
  } catch (error) {
    if (!(error instanceof LocalOpCancelledError)) {
      ElMessage.error(failText(getErrorMessage(error, '本机操作下发失败')))
    }
    return null
  } finally {
    busy.value = ''
  }
}

async function checkFonts(): Promise<void> {
  const result = await runOp<FontCheckResult>('font.check')
  if (!result) return

  checked.value = true
  dirExists.value = result.dirExists !== false
  total.value = Number(result.total ?? 0)
  omitted.value = Number(result.omitted ?? 0)
  missing.value = Array.isArray(result.missing)
    ? result.missing.filter((name): name is string => typeof name === 'string')
    : []

  if (missing.value.length === 0) {
    ElMessage.success(
      dirExists.value
        ? `公文字体检查通过（共 ${total.value} 个字体文件，无缺失）`
        : '未找到随包「公文字体」目录，按无缺失处理',
    )
  }
}

async function installFonts(): Promise<void> {
  const result = await runOp<FontInstallResult>('font.install', MANUAL_HINT)
  if (!result) return

  const installed = Number(result.installed ?? 0)
  if (installed === 0) {
    ElMessage.info('公文字体已全部就位，无需安装')
  } else {
    ElMessage.success(`已安装 ${installed} 个公文字体，请重启应用与 Word 后生效`)
  }

  // 装完复检：清空缺失清单（下次点「检测」会重新扫描）
  checked.value = true
  missing.value = []
  omitted.value = 0
}

onMounted(async () => {
  await loadDevices()
})

onBeforeUnmount(() => {
  destroyed = true
})
</script>

<template>
  <NcCard
    title="公文字体"
    subtitle="公文文书打印/导出依赖系统已装字体；检测随包「公文字体」目录并一键安装到当前用户（免管理员）。"
  >
    <template #actions>
      <el-button
        :icon="Refresh"
        :loading="busy === 'font.check'"
        :disabled="!canOperate"
        @click="checkFonts"
      >
        检测
      </el-button>
      <el-button
        type="primary"
        :icon="Download"
        :loading="busy === 'font.install'"
        :disabled="!canOperate || missing.length === 0"
        @click="installFonts"
      >
        一键安装
      </el-button>
    </template>

    <el-alert
      v-if="noTarget"
      type="warning"
      title="无在线 Agent 设备"
      description="请在目标电脑启动 NewCosmos Agent 并等待心跳（Agent 需具备 font 能力），再刷新本页。"
      :closable="false"
      show-icon
      class="font-alert"
    />

    <div class="target-row">
      <span class="target-label">目标设备</span>
      <el-select
        v-model="deviceId"
        placeholder="选择具备 font 能力的在线设备"
        filterable
        :loading="devicesLoading"
        class="target-select"
      >
        <el-option
          v-for="device in deviceOptions"
          :key="device.id"
          :label="device.label"
          :value="device.id"
          :disabled="!device.eligible"
        />
      </el-select>
    </div>

    <p v-if="!checked" class="pending">
      尚未检测：点击右上角「检测」扫描随包「公文字体」目录，与系统已装字体比对。
    </p>

    <div v-if="checked" class="check-result">
      <p class="result-summary">
        <el-icon><Check /></el-icon>
        <span v-if="missing.length === 0">
          已检查 {{ total }} 个字体文件，无缺失{{ dirExists ? '' : '（随包目录不存在）' }}
        </span>
        <span v-else class="is-missing">
          已检查 {{ total }} 个字体文件，缺失 {{ missing.length }} 个
          <template v-if="omitted > 0">（另 {{ omitted }} 个未列出）</template>
        </span>
      </p>

      <ul v-if="missing.length > 0" class="missing-list">
        <li v-for="name in missing" :key="name">{{ name }}</li>
      </ul>
    </div>

    <p class="hint">
      公文字体缺失只影响公文文书打印/导出的版式（字体被替换），不阻塞其他功能；
      安装完成后请重启应用与 Word 生效。也可
      <strong>手动安装 公文字体 目录内的字体</strong>（复制到
      %LOCALAPPDATA%\Microsoft\Windows\Fonts 并在注册表登记）。
    </p>
  </NcCard>
</template>

<style scoped>
.font-alert {
  margin-bottom: 12px;
}

.target-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.target-label {
  flex: none;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.target-select {
  width: min(420px, 100%);
}

.check-result {
  margin-top: 14px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.result-summary {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 0;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.result-summary .el-icon {
  color: var(--nc-pine, #2f7d5b);
}

.result-summary .is-missing {
  color: var(--nc-amber-dark, #8a6116);
  font-weight: 600;
}

.missing-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 10px 0 0;
  padding: 0;
  list-style: none;
}

.missing-list li {
  padding: 3px 8px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.pending {
  margin: 14px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.hint {
  margin: 14px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.hint strong {
  color: var(--nc-ink);
  font-weight: 600;
}

@media (max-width: 767px) {
  .target-row {
    align-items: stretch;
    flex-direction: column;
  }

  .target-select {
    width: 100%;
  }
}
</style>
