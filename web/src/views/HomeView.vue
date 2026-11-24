<script setup lang="ts">
import { computed, ref } from 'vue'
import type { Component } from 'vue'
import {
  ArrowRight,
  Clock,
  FolderOpened,
  Monitor,
  OfficeBuilding,
  Printer,
  Refresh,
  Setting,
  Tickets,
  User,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { APP_NAME } from '@/config/app'
import { useAuthStore } from '@/stores/auth'

interface InfoItem {
  label: string
  value: string
  extra?: string
  icon: Component
}

interface QuickItem {
  title: string
  description: string
  icon: Component
  path?: string
}

const auth = useAuthStore()
const refreshing = ref(false)

const dateText = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric',
  month: 'long',
  day: 'numeric',
  weekday: 'long',
}).format(new Date())

const greeting = computed(() => {
  const hour = new Date().getHours()
  if (hour < 6) return '夜深了'
  if (hour < 9) return '早上好'
  if (hour < 12) return '上午好'
  if (hour < 14) return '中午好'
  if (hour < 18) return '下午好'
  return '晚上好'
})

const organization = computed(() => auth.user?.organizationName || '未设置组织')

const deviceKindText = computed(() => (auth.deviceKind === 'mobile-web' ? '手机浏览器' : '桌面浏览器'))

const expiresText = computed(() => formatDateTime(auth.expiresAt))

const remainingText = computed(() => {
  const expires = Date.parse(auth.expiresAt)
  if (!Number.isFinite(expires)) return '有效期未知'
  const diff = expires - Date.now()
  if (diff <= 0) return '会话已过期'
  const hours = Math.floor(diff / 3_600_000)
  const minutes = Math.floor((diff % 3_600_000) / 60_000)
  if (hours >= 24) return `剩余约 ${Math.floor(hours / 24)} 天 ${hours % 24} 小时`
  if (hours >= 1) return `剩余约 ${hours} 小时 ${minutes} 分钟`
  return `剩余约 ${minutes} 分钟`
})

const deviceIdText = computed(() => {
  const id = auth.deviceId
  return id.length > 20 ? `${id.slice(0, 12)}…${id.slice(-6)}` : id
})

const infoItems = computed<InfoItem[]>(() => [
  {
    label: '当前账号',
    value: auth.user?.fullName || auth.user?.username || '—',
    extra: auth.user?.username ? `登录名 ${auth.user.username}` : undefined,
    icon: User,
  },
  {
    label: '所属组织',
    value: organization.value,
    extra: auth.user?.organizationId ? `组织编号 ${auth.user.organizationId}` : undefined,
    icon: OfficeBuilding,
  },
  {
    label: '会话有效期至',
    value: expiresText.value,
    extra: remainingText.value,
    icon: Clock,
  },
  {
    label: '当前设备',
    value: deviceIdText.value,
    extra: deviceKindText.value,
    icon: Monitor,
  },
])

const quickItems: QuickItem[] = [
  {
    title: '打印中心',
    description: '发起打印任务并在绑定设备执行',
    icon: Tickets,
  },
  {
    title: '文档中心',
    description: '救助档案与业务附件统一管理',
    icon: FolderOpened,
  },
  {
    title: '系统设置',
    description: '通知、偏好与安全选项',
    icon: Setting,
  },
]

function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat('zh-CN', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

async function refreshSession(): Promise<void> {
  if (refreshing.value) return
  refreshing.value = true
  try {
    await auth.heartbeat()
    ElMessage.success('会话状态已刷新')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '刷新会话失败'))
  } finally {
    refreshing.value = false
  }
}
</script>

<template>
  <div class="nc-page home-page">
    <section class="welcome">
      <div class="welcome-main">
        <p class="welcome-date">{{ dateText }}</p>
        <h1 class="welcome-title">{{ greeting }}，{{ auth.displayName }}</h1>
        <p class="welcome-sub">{{ organization }} · 欢迎使用{{ APP_NAME }}</p>
      </div>
      <div class="welcome-side">
        <el-tag class="session-tag" effect="light" round>
          <span class="tag-dot" />会话正常
        </el-tag>
        <el-button :icon="Refresh" :loading="refreshing" plain size="small" @click="refreshSession">
          刷新会话
        </el-button>
      </div>
    </section>

    <section class="info-grid" aria-label="会话信息">
      <article v-for="item in infoItems" :key="item.label" class="info-card">
        <span class="info-icon">
          <el-icon :size="18"><component :is="item.icon" /></el-icon>
        </span>
        <div class="info-main">
          <span class="info-label">{{ item.label }}</span>
          <strong class="info-value" :title="item.value">{{ item.value }}</strong>
          <span v-if="item.extra" class="info-extra">{{ item.extra }}</span>
        </div>
      </article>
    </section>

    <section class="section">
      <div class="section-head">
        <h2 class="nc-section-title">快捷入口</h2>
        <span class="section-note">更多业务模块建设中</span>
      </div>

      <div class="quick-grid">
        <RouterLink to="/printers" class="quick-card">
          <span class="quick-icon"><el-icon :size="22"><Printer /></el-icon></span>
          <span class="quick-text">
            <strong>打印机绑定</strong>
            <small>管理本账号的设备与打印机</small>
          </span>
          <el-icon class="quick-arrow"><ArrowRight /></el-icon>
        </RouterLink>

        <div v-for="item in quickItems" :key="item.title" class="quick-card is-disabled" aria-disabled="true">
          <span class="quick-icon"><el-icon :size="22"><component :is="item.icon" /></el-icon></span>
          <span class="quick-text">
            <strong>{{ item.title }}</strong>
            <small>{{ item.description }}</small>
          </span>
          <span class="quick-badge">即将上线</span>
        </div>
      </div>
    </section>

    <section class="tips-card nc-card">
      <h2 class="nc-section-title">打印使用提示</h2>
      <ol class="tips-list">
        <li><span>1</span>在需要执行打印的 Windows 电脑上安装并启动 NewCosmos Agent。</li>
        <li><span>2</span>进入「打印机绑定」，填写 Agent 的设备密钥与已上报的打印机名称。</li>
        <li><span>3</span>手机端发起打印后，任务将在绑定的电脑上由默认打印机执行。</li>
      </ol>
    </section>
  </div>
</template>

<style scoped>
.home-page {
  display: flex;
  flex-direction: column;
  gap: 22px;
}

.welcome {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  overflow: hidden;
  padding: 26px 28px;
  border: 1px solid #dbe7f3;
  border-radius: var(--nc-radius);
  background:
    radial-gradient(420px 220px at 92% 0%, rgba(42, 118, 186, 0.12), transparent 70%),
    linear-gradient(115deg, #eff6fd 0%, #f8fbff 62%, #ffffff 100%);
}

.welcome::after {
  content: '';
  position: absolute;
  right: -70px;
  bottom: -110px;
  width: 240px;
  height: 240px;
  border: 1px solid rgba(28, 95, 158, 0.12);
  border-radius: 50%;
  box-shadow: 0 0 0 40px rgba(28, 95, 158, 0.04);
  pointer-events: none;
}

.welcome-main {
  position: relative;
  z-index: 1;
}

.welcome-date {
  color: var(--nc-text-muted);
  font-size: 12px;
  letter-spacing: 0.4px;
}

.welcome-title {
  margin-top: 8px;
  font-size: 25px;
  font-weight: 600;
  letter-spacing: 0.4px;
  color: #173a5b;
}

.welcome-sub {
  margin-top: 8px;
  color: var(--nc-text-secondary);
  font-size: 13px;
}

.welcome-side {
  position: relative;
  z-index: 1;
  display: flex;
  align-items: center;
  gap: 10px;
}

.session-tag :deep(.el-tag__content) {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.tag-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #42b883;
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(230px, 1fr));
  gap: 14px;
}

.info-card {
  display: flex;
  gap: 13px;
  padding: 16px 17px;
  border: 1px solid var(--nc-border);
  border-radius: var(--nc-radius);
  background: #fff;
  box-shadow: 0 1px 2px rgba(16, 42, 67, 0.04);
}

.info-icon {
  display: grid;
  place-items: center;
  flex: none;
  width: 36px;
  height: 36px;
  border-radius: 9px;
  background: var(--nc-primary-soft);
  color: var(--nc-primary);
}

.info-main {
  display: flex;
  flex-direction: column;
  min-width: 0;
  line-height: 1.45;
}

.info-label {
  color: var(--nc-text-muted);
  font-size: 11px;
  letter-spacing: 0.5px;
}

.info-value {
  margin-top: 3px;
  overflow: hidden;
  color: var(--nc-text);
  font-size: 14px;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.info-extra {
  margin-top: 2px;
  overflow: hidden;
  color: var(--nc-text-muted);
  font-size: 11px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.section-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.section-note {
  color: var(--nc-text-muted);
  font-size: 12px;
}

.quick-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 12px;
}

.quick-card {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 17px 18px;
  border: 1px solid var(--nc-border);
  border-radius: var(--nc-radius);
  background: #fff;
  transition: border-color 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;
}

.quick-card:not(.is-disabled):hover {
  border-color: #b7cfe6;
  box-shadow: 0 8px 20px rgba(28, 95, 158, 0.08);
  transform: translateY(-1px);
}

.quick-icon {
  display: grid;
  place-items: center;
  flex: none;
  width: 42px;
  height: 42px;
  border-radius: 10px;
  background: var(--nc-primary-soft);
  color: var(--nc-primary);
}

.quick-text {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.quick-text strong {
  font-size: 14px;
  font-weight: 600;
}

.quick-text small {
  overflow: hidden;
  color: var(--nc-text-muted);
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.quick-arrow {
  margin-left: auto;
  color: var(--nc-primary);
}

.quick-badge {
  margin-left: auto;
  padding: 1px 7px;
  border-radius: 4px;
  background: #f2f4f8;
  color: var(--nc-text-muted);
  font-size: 10px;
  white-space: nowrap;
}

.quick-card.is-disabled {
  background: #fbfcfe;
}

.quick-card.is-disabled .quick-icon {
  background: #f2f4f8;
  color: #97a4b3;
}

.tips-card {
  padding: 20px 22px 22px;
}

.tips-list {
  margin: 14px 0 0;
  padding: 0;
  list-style: none;
  display: grid;
  gap: 10px;
}

.tips-list li {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  color: var(--nc-text-secondary);
  font-size: 13px;
}

.tips-list span {
  display: grid;
  place-items: center;
  flex: none;
  width: 18px;
  height: 18px;
  margin-top: 2px;
  border-radius: 50%;
  background: var(--nc-primary-soft);
  color: var(--nc-primary);
  font-size: 11px;
  font-weight: 600;
}

@media (max-width: 767px) {
  .home-page {
    gap: 16px;
  }

  .welcome {
    flex-direction: column;
    align-items: flex-start;
    padding: 20px 18px;
  }

  .welcome-title {
    font-size: 21px;
  }

  .welcome-side {
    width: 100%;
    justify-content: space-between;
  }

  .info-grid {
    grid-template-columns: 1fr;
    gap: 10px;
  }

  .quick-grid {
    grid-template-columns: 1fr;
  }

  .quick-card {
    padding: 15px 16px;
  }

  .tips-card {
    padding: 18px 16px;
  }
}
</style>
