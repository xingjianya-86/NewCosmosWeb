<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  Download,
  Link,
  Position,
  Refresh,
  SwitchButton,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  LocalOpCancelledError,
  parseLocalOpResult,
  runLocalOp,
} from '@/api/localOps'
import { listDevices } from '@/api/printers'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import type { PrinterDevice } from '@/types/api'

/*
 * 网络接入（ZeroTier）：目标电脑上的 Agent 执行本机操作，本页只负责下发与展示。
 *
 * 配置（接入模式 / 网络ID / Moon / 授权地址）存浏览器 localStorage：
 * 桌面版写 config\network.ini 属"部署侧"配置，Web 端不读写服务端配置文件，
 * 本页只负责本机操作与展示（差异见 docs/20261127_ZeroTier与公文字体Web迁移.md）。
 * 「打开网页授权」是浏览器能力（window.open），不需要 Agent 参与。
 */

interface ZtNetwork {
  nwid?: string
  name?: string
  status?: string
  assignedAddresses?: string[]
}

interface ZtStatusResult {
  installed?: boolean
  nodeId?: string
  version?: string
  online?: boolean
  tokenAccessDenied?: boolean
  networks?: ZtNetwork[]
}

interface NetworkAccessConfig {
  mode: 'private' | 'public'
  privateNetworkId: string
  moonId: string
  publicNetworkId: string
  /** 网页授权地址（私有化=自建控制器 UI；公共=ZeroTier Central）。 */
  authorizationUrl: string
}

const STORAGE_KEY = 'nc.network-access.config'

const defaultConfig: NetworkAccessConfig = {
  mode: 'private',
  privateNetworkId: '',
  moonId: '',
  publicNetworkId: '',
  authorizationUrl: '',
}

const config = reactive<NetworkAccessConfig>({ ...defaultConfig })

function loadConfig(): void {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (!raw) return
    const parsed = JSON.parse(raw) as Partial<NetworkAccessConfig>
    if (parsed.mode === 'private' || parsed.mode === 'public') config.mode = parsed.mode
    config.privateNetworkId = parsed.privateNetworkId ?? ''
    config.moonId = parsed.moonId ?? ''
    config.publicNetworkId = parsed.publicNetworkId ?? ''
    config.authorizationUrl = parsed.authorizationUrl ?? ''
  } catch {
    // 配置损坏按默认值处理，不阻断页面
  }
}

function saveConfig(): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(config))
  } catch {
    // 隐私模式/存储满：静默降级，本页仍可用（只是不记住配置）
  }
}

watch(config, saveConfig, { deep: true })

const isPrivate = computed(() => config.mode === 'private')
const effectiveNetworkId = computed(() =>
  (isPrivate.value ? config.privateNetworkId : config.publicNetworkId).trim(),
)

/* ── 目标设备 ─────────────────────────────────────────────────── */
const devices = ref<PrinterDevice[]>([])
const devicesLoading = ref(false)
const deviceId = ref<number | null>(null)

function capabilityLabel(device: PrinterDevice): string {
  const caps = device.capabilities ?? []
  return caps.length > 0 ? ` · ${caps.join(' · ')}` : ''
}

const deviceOptions = computed(() =>
  devices.value
    .map((device) => ({
      id: device.id,
      // 只有"在线 + 上报 zerotier 能力"的设备可执行本机操作；老 Agent 未上报能力时不可选。
      eligible: device.online && (device.capabilities ?? []).includes('zerotier'),
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

/* ── 连接状态（四行只读） ─────────────────────────────────────── */
const status = reactive({
  loaded: false,
  installed: false,
  nodeId: '—',
  version: '—',
  online: false,
  tokenAccessDenied: false,
  networks: [] as ZtNetwork[],
})

const joinedNetwork = computed(() => {
  const target = effectiveNetworkId.value.toLowerCase()
  if (!target) return null
  return (
    status.networks.find((network) => (network.nwid ?? '').toLowerCase() === target) ?? null
  )
})
const joined = computed(() => joinedNetwork.value !== null)
const authorized = computed(
  () => (joinedNetwork.value?.status ?? '').toUpperCase() === 'OK',
)
const assignedIp = computed(() => joinedNetwork.value?.assignedAddresses?.[0] ?? '—')

const statusText = computed(() => {
  if (!status.loaded) return '未检测'
  if (!status.installed) return '未检测到 ZeroTier（可点击“安装 ZeroTier”）'
  if (status.tokenAccessDenied) {
    return 'ZeroTier 权限未授权：点击“接入网络”将请求一次管理员授权（UAC）'
  }
  if (!status.nodeId || status.nodeId === '—') return 'ZeroTier 已安装，但未获取到节点信息'
  if (joined.value) {
    if (authorized.value) {
      return status.online
        ? `已接入网络 · 已授权（IP ${assignedIp.value}，节点 ${status.nodeId}）`
        : `已接入网络 · 已授权但离线（IP ${assignedIp.value}，节点 ${status.nodeId}）`
    }
    const authUrl = config.authorizationUrl.trim() || '授权地址'
    return `已接入网络 · 等待服务器授权（请在 ${authUrl} 勾选授权本机节点）`
  }
  return `未接入目标网络（节点 ${status.nodeId}，请在服务器网页端授权）`
})

const statusTone = computed<'online' | 'offline' | 'idle'>(() => {
  if (!status.loaded || !status.installed) return 'idle'
  if (status.tokenAccessDenied) return 'idle'
  if (joined.value && authorized.value && status.online) return 'online'
  if (joined.value && authorized.value) return 'idle'
  return 'offline'
})

function applyStatus(result: ZtStatusResult): void {
  status.loaded = true
  status.installed = Boolean(result.installed)
  status.nodeId = typeof result.nodeId === 'string' && result.nodeId ? result.nodeId : '—'
  status.version = typeof result.version === 'string' && result.version ? result.version : '—'
  status.online = Boolean(result.online)
  status.tokenAccessDenied = Boolean(result.tokenAccessDenied)
  status.networks = Array.isArray(result.networks) ? result.networks : []
}

/* ── 本机操作下发 ─────────────────────────────────────────────── */
const busy = ref('')
/** 页面卸载标记：停止仍在进行的轮询（runLocalOp 的 isCancelled）。 */
let destroyed = false

async function runOp<T = Record<string, unknown>>(
  op: string,
  args?: Record<string, unknown>,
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
    const outcome = await runLocalOp(deviceId.value, op, args, {
      isCancelled: () => destroyed,
    })
    if (outcome.status === 'Failed') {
      // 失败摘要（含 UAC 取消的 CANCELLED 文案）原样展示
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

async function refreshStatus(): Promise<void> {
  const result = await runOp<ZtStatusResult>('zerotier.status')
  if (!result) return
  applyStatus(result)
}

async function installZeroTier(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '将安装随包分发的 ZeroTier，并授予本机接入权限（需要管理员权限，会弹出 UAC）。是否继续？',
      '安装 ZeroTier',
      { confirmButtonText: '安装', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  const result = await runOp('zerotier.install')
  if (!result) return
  ElMessage.success('ZeroTier 安装完成')
  await refreshStatus()
}

const NETWORK_ID_PATTERN = /^[0-9a-fA-F]{16}$/

async function joinNetwork(): Promise<void> {
  const networkId = effectiveNetworkId.value
  if (!networkId) {
    ElMessage.warning(
      isPrivate.value ? '私有化网络ID未配置' : '请填写公共服务器（ZeroTier Central）网络ID',
    )
    return
  }
  if (!NETWORK_ID_PATTERN.test(networkId)) {
    ElMessage.warning('网络ID格式无效（应为 16 位十六进制）')
    return
  }

  // 管理令牌未授权：首次接入提权授予当前用户读权限（一次 UAC，之后免授权）
  if (status.tokenAccessDenied) {
    try {
      await ElMessageBox.confirm(
        '首次接入需要读取 ZeroTier 管理令牌（默认仅管理员可读），将请求一次管理员授权（UAC）。是否继续？',
        '需要一次管理员授权',
        { confirmButtonText: '继续', cancelButtonText: '取消', type: 'warning' },
      )
    } catch {
      return
    }
  }

  const args: Record<string, unknown> = { networkId }
  const moonId = config.moonId.trim()
  if (isPrivate.value && moonId) args.moonId = moonId

  const result = await runOp('zerotier.join', args)
  if (!result) return

  const authUrl = config.authorizationUrl.trim() || '（未填写授权地址）'
  await ElMessageBox.alert(
    `已加入网络 ${networkId}。\n\n请在服务器网页端授权本机节点：\n节点ID：${status.nodeId}\n授权地址：${authUrl}\n\n接入后请在服务器网页端（授权地址）勾选授权本机节点，授权前网络层不通。`,
    '已提交接入',
    { confirmButtonText: '知道了', type: 'success' },
  ).catch(() => undefined)

  await refreshStatus()
}

async function leaveNetwork(): Promise<void> {
  const networkId = effectiveNetworkId.value
  if (!networkId) {
    ElMessage.warning('网络ID未配置')
    return
  }

  try {
    await ElMessageBox.confirm(`确定退出网络 ${networkId} 吗？`, '退出网络', {
      confirmButtonText: '退出',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }

  const result = await runOp('zerotier.leave', { networkId })
  if (!result) return
  ElMessage.success('已退出网络')
  await refreshStatus()
}

function openAuthorizationPage(): void {
  const url = config.authorizationUrl.trim()
  if (!url) {
    ElMessage.warning('请先填写授权地址')
    return
  }
  window.open(url, '_blank', 'noopener')
}

onMounted(async () => {
  loadConfig()
  await loadDevices()
  if (!noTarget.value) await refreshStatus()
})

onBeforeUnmount(() => {
  destroyed = true
})
</script>

<template>
  <div class="nc-page network-access-page">
    <NcPageHeader
      title="网络接入"
      description="ZeroTier 异地组网 · 私有化/公共服务器可选"
      kicker="系统 · 网络"
    >
      <template #actions>
        <el-button
          :icon="Refresh"
          :loading="busy === 'zerotier.status'"
          :disabled="!canOperate"
          @click="refreshStatus"
        >
          检测状态
        </el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="noTarget"
      type="warning"
      title="无在线 Agent 设备"
      description="请在目标电脑启动 NewCosmos Agent 并等待心跳（Agent 需具备 zerotier 能力），再刷新本页。"
      :closable="false"
      show-icon
    />

    <div class="target-bar">
      <span class="target-label">目标设备</span>
      <el-select
        v-model="deviceId"
        placeholder="选择具备 zerotier 能力的在线设备"
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
      <span class="target-hint">
        本机操作由所选电脑上的 Agent 执行（下发需「系统运维」权限），配置仅保存在本浏览器。
      </span>
    </div>

    <div class="card-grid">
      <NcCard title="接入模式">
        <el-select v-model="config.mode" class="mode-select">
          <el-option label="私有化服务器（自建控制器）" value="private" />
          <el-option label="公共服务器（ZeroTier Central）" value="public" />
        </el-select>
        <p class="hint">
          私有化服务器：使用自建控制器（推荐，数据自主）；公共服务器：使用 ZeroTier Central。
        </p>
      </NcCard>

      <NcCard v-if="isPrivate" title="私有化服务器">
        <div class="field-grid">
          <label class="field">
            <span>网络ID</span>
            <el-input v-model="config.privateNetworkId" placeholder="16 位十六进制网络ID" />
          </label>
          <label class="field">
            <span>Moon 地址（可选加速）</span>
            <el-input v-model="config.moonId" placeholder="16 位十六进制 Moon ID" />
          </label>
        </div>
        <label class="field">
          <span>网页授权地址</span>
          <el-input
            v-model="config.authorizationUrl"
            placeholder="http://服务器地址/授权页面"
          />
        </label>
      </NcCard>

      <NcCard v-else title="公共服务器（ZeroTier Central）">
        <label class="field">
          <span>网络ID</span>
          <el-input
            v-model="config.publicNetworkId"
            placeholder="在 my.zerotier.com 创建的网络ID"
          />
        </label>
        <label class="field">
          <span>授权地址</span>
          <el-input
            v-model="config.authorizationUrl"
            placeholder="https://my.zerotier.com/network/…"
          />
        </label>
      </NcCard>

      <NcCard title="连接状态" class="status-card">
        <dl class="status-list">
          <div>
            <dt>ZeroTier：</dt>
            <dd>
              <NcStatusDot :status="statusTone" :label="statusText" />
            </dd>
          </div>
          <div>
            <dt>节点ID：</dt>
            <dd>{{ status.nodeId }}</dd>
          </div>
          <div>
            <dt>分配IP：</dt>
            <dd>{{ assignedIp }}</dd>
          </div>
          <div>
            <dt>版本：</dt>
            <dd>{{ status.version }}</dd>
          </div>
        </dl>

        <div class="action-bar">
          <el-button
            :icon="Refresh"
            :loading="busy === 'zerotier.status'"
            :disabled="!canOperate"
            @click="refreshStatus"
          >
            检测状态
          </el-button>
          <el-button
            :icon="Download"
            :loading="busy === 'zerotier.install'"
            :disabled="!canOperate"
            @click="installZeroTier"
          >
            安装 ZeroTier
          </el-button>
 <el-button
            type="primary"
            :icon="Position"
            :loading="busy === 'zerotier.join'"
            :disabled="!canOperate"
            @click="joinNetwork"
          >
            接入网络
          </el-button>
          <el-button
            :icon="SwitchButton"
            :loading="busy === 'zerotier.leave'"
            :disabled="!canOperate"
            @click="leaveNetwork"
          >
            退出网络
          </el-button>
          <el-button :icon="Link" @click="openAuthorizationPage">打开网页授权</el-button>
        </div>

        <p class="hint">
          提示：接入后请在服务器网页端（授权地址）勾选授权本机节点，授权前网络层不通；
          接入时如弹出管理员授权（UAC）请点“是”，取消会以 CANCELLED 回传到本页。
        </p>
      </NcCard>
    </div>
  </div>
</template>

<style scoped>
.network-access-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.target-bar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
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

.target-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.card-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 15px;
  align-items: start;
}

.status-card {
  grid-column: 1 / -1;
}

.mode-select {
  width: 100%;
}

.field-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  margin-bottom: 10px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.field span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.hint {
  margin: 10px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.status-list {
  display: grid;
  gap: 8px;
  margin: 0;
}

.status-list > div {
  display: grid;
  grid-template-columns: 76px minmax(0, 1fr);
  gap: 10px;
  align-items: baseline;
}

.status-list dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.status-list dd {
  margin: 0;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
  word-break: break-all;
}

.action-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 16px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.action-bar :deep(.el-button + .el-button) {
  margin-left: 0;
}

@media (max-width: 1000px) {
  .card-grid {
    grid-template-columns: 1fr;
  }

  .field-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 767px) {
  .target-bar {
    align-items: stretch;
    flex-direction: column;
  }

  .target-select {
    width: 100%;
  }

  .action-bar :deep(.el-button) {
    flex: 1 1 calc(50% - 8px);
    margin-left: 0;
  }
}
</style>
