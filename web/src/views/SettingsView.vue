<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Connection, Download, Lock, Refresh } from '@element-plus/icons-vue'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import FontInstallCard from '@/views/system/FontInstallCard.vue'
import { useAuthStore } from '@/stores/auth'
import { APP_NAME, APP_VERSION } from '@/config/app'
import {
  CLIENT_DOWNLOADS,
  changePassword,
  getClientReleases,
  getSessionPolicy,
} from '@/api/settings'
import { getErrorMessage } from '@/api/http'
import type { ClientRelease, SessionPolicy } from '@/api/settings'

const router = useRouter()
const auth = useAuthStore()
const user = computed(() => auth.user)

const policy = ref<SessionPolicy | null>(null)
const policyError = ref('')
const releases = ref<{ windows: ClientRelease | null; android: ClientRelease | null }>({
  windows: null,
  android: null,
})

const password = reactive({
  oldPassword: '',
  newPassword: '',
  confirmPassword: '',
  saving: false,
})

const policyText = computed(() => {
  if (!policy.value) return ''
  const modeText =
    policy.value.mode === 1 ? '允许多端' : policy.value.mode === 2 ? '严格单会话' : '按端限制'
  return `${modeText} · 桌面 ${policy.value.maxDesktop} 台 / 移动 ${policy.value.maxMobile} 台 · 会话最长 ${policy.value.absoluteDays} 天`
})

async function loadPolicy(): Promise<void> {
  policyError.value = ''
  try {
    policy.value = await getSessionPolicy()
  } catch (error) {
    policyError.value = getErrorMessage(error, '会话策略加载失败')
  }
}

async function loadReleases(): Promise<void> {
  releases.value = await getClientReleases()
}

async function submitPassword(): Promise<void> {
  if (!password.oldPassword || !password.newPassword) {
    ElMessage.warning('请填写原密码和新密码')
    return
  }
  if (password.newPassword.length < 6 || password.newPassword.length > 20) {
    ElMessage.warning('新密码长度必须为 6–20 位')
    return
  }
  if (password.newPassword !== password.confirmPassword) {
    ElMessage.warning('两次输入的新密码不一致')
    return
  }

  password.saving = true
  try {
    await changePassword(password.oldPassword, password.newPassword)
    ElMessage.success('密码已修改')
    password.oldPassword = ''
    password.newPassword = ''
    password.confirmPassword = ''
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '修改密码失败'))
  } finally {
    password.saving = false
  }
}

function openDownload(url: string): void {
  window.open(url, '_blank', 'noopener')
}

onMounted(() => {
  void loadPolicy()
  void loadReleases()
})
</script>

<template>
  <div class="nc-page settings-page">
    <NcPageHeader
      title="系统设置"
      description="账号安全、会话策略、客户端下载与版本信息。"
      kicker="系统 · 设置"
    >
      <template #actions>
        <el-button :icon="Refresh" @click="loadPolicy">刷新策略</el-button>
      </template>
    </NcPageHeader>

    <NcCard title="账号信息">
      <div class="info-grid">
        <div><span>用户名</span><strong>{{ user?.username || '—' }}</strong></div>
        <div><span>姓名</span><strong>{{ user?.fullName || '—' }}</strong></div>
        <div><span>所属组织</span><strong>{{ user?.organizationName || '—' }}</strong></div>
        <div><span>系统版本</span><strong>{{ APP_NAME }} {{ APP_VERSION }}</strong></div>
      </div>
    </NcCard>

    <NcCard title="修改密码">
      <div class="password-grid">
        <el-input
          v-model="password.oldPassword"
          type="password"
          show-password
          placeholder="原密码"
        />
        <el-input
          v-model="password.newPassword"
          type="password"
          show-password
          placeholder="新密码（6–20 位）"
        />
        <el-input
          v-model="password.confirmPassword"
          type="password"
          show-password
          placeholder="再次输入新密码"
        />
        <el-button type="primary" :icon="Lock" :loading="password.saving" @click="submitPassword">
          修改密码
        </el-button>
      </div>
      <p class="hint">修改密码后，其它设备上的该账号会话会被强制下线；当前会话保持登录。</p>
    </NcCard>

    <NcCard title="会话策略">
      <p v-if="policyError" class="error-note">{{ policyError }}</p>
      <p v-else-if="policy" class="policy-text">{{ policyText }}</p>
      <el-skeleton v-else :rows="1" animated />
      <p class="hint">
        登录端每 {{ policy?.heartbeatDesktopSeconds ?? 60 }} 秒心跳一次；被顶号或强制下线后约 60 秒内退出。
      </p>
    </NcCard>

    <NcCard
      title="本机维护"
      subtitle="由目标电脑上的 NewCosmos Agent 执行；需该设备在线并具备对应能力（心跳上报）。"
    >
      <div class="maint-grid">
        <article class="maint-card">
          <div>
            <strong>网络接入</strong>
            <span>ZeroTier 异地组网 · 私有化/公共服务器可选</span>
          </div>
          <el-button type="primary" :icon="Connection" @click="router.push('/system/network-access')">
            打开
          </el-button>
        </article>
      </div>
    </NcCard>

    <FontInstallCard />

    <NcCard title="客户端下载">
      <div class="client-grid">
        <article class="client-card">
          <div>
            <strong>Windows 客户端</strong>
            <span>NewCosmos Shell（含打印代理 Agent）</span>
            <small v-if="releases.windows">
              版本 {{ releases.windows.version || '—' }} · {{ releases.windows.notes || '—' }}
            </small>
          </div>
          <el-button type="primary" :icon="Download" @click="openDownload(CLIENT_DOWNLOADS.windows)">
            下载安装包
          </el-button>
        </article>

        <article class="client-card">
          <div>
            <strong>Android 客户端</strong>
            <span>移动端 WebView 壳</span>
            <small v-if="releases.android">
              版本 {{ releases.android.versionName || '—' }} · {{ releases.android.notes || '—' }}
            </small>
          </div>
          <el-button type="primary" :icon="Download" @click="openDownload(CLIENT_DOWNLOADS.android)">
            下载 APK
          </el-button>
        </article>
      </div>

      <p class="hint">
        Windows 安装包使用内部自签名证书；如提示未知发布者，可先下载
        <el-link type="primary" @click="openDownload(CLIENT_DOWNLOADS.codeSignCert)">
          代码签名证书
        </el-link>
        并导入“受信任的发布者”。
      </p>
    </NcCard>

    <NcCard title="更新日志">
      <div class="release-log">
        <div>
          <strong>Windows · {{ releases.windows?.version || '暂无清单' }}</strong>
          <p>{{ releases.windows?.notes || '暂无更新说明' }}</p>
        </div>
        <div>
          <strong>Android · {{ releases.android?.versionName || '暂无清单' }}</strong>
          <p>{{ releases.android?.notes || '暂无更新说明' }}</p>
        </div>
      </div>
    </NcCard>
  </div>
</template>

<style scoped>
.settings-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
}

.info-grid div {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.info-grid span { color: var(--nc-muted); font-size: var(--nc-text-xs); }
.info-grid strong { color: var(--nc-ink); word-break: break-all; }

.password-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr)) auto;
  gap: 10px;
  align-items: center;
}

.policy-text { margin: 0; color: var(--nc-ink); line-height: 1.8; }

.hint { margin: 10px 0 0; color: var(--nc-muted); line-height: 1.7; font-size: var(--nc-text-sm); }

.error-note { margin: 0; color: var(--nc-danger, #b42318); }

.client-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.client-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 14px 16px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
}

.client-card div { display: flex; flex-direction: column; gap: 4px; }
.client-card strong { color: var(--nc-ink); }
.client-card span, .client-card small { color: var(--nc-muted); font-size: var(--nc-text-xs); }

/* ── 本机维护（网络接入 / 公文字体入口） ─────────────────────── */
.maint-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 12px;
}

.maint-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 14px 16px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.maint-card div {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.maint-card strong { color: var(--nc-ink); }
.maint-card span { color: var(--nc-muted); font-size: var(--nc-text-xs); line-height: 1.6; }

.release-log {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.release-log p { margin: 6px 0 0; color: var(--nc-muted); line-height: 1.7; }

@media (max-width: 1000px) {
  .info-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .password-grid { grid-template-columns: 1fr; }
  .client-grid, .release-log { grid-template-columns: 1fr; }
}

@media (max-width: 767px) {
  .info-grid { grid-template-columns: 1fr; }
}
</style>
