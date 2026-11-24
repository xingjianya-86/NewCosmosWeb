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
import NcCard from '@/components/NcCard.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { APP_NAME } from '@/config/app'
import { useAuthStore } from '@/stores/auth'

type Tone = 'ink' | 'seal' | 'pine' | 'amber'

interface InfoItem {
  label: string
  value: string
  extra?: string
  icon: Component
  tone?: Tone
}

interface QuickItem {
  title: string
  description: string
  icon: Component
  path?: string
  tone?: Tone
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
    tone: 'seal',
  },
  {
    label: '所属组织',
    value: organization.value,
    extra: auth.user?.organizationId ? `组织编号 ${auth.user.organizationId}` : undefined,
    icon: OfficeBuilding,
    tone: 'pine',
  },
  {
    label: '会话有效期至',
    value: expiresText.value,
    extra: remainingText.value,
    icon: Clock,
    tone: 'amber',
  },
  {
    label: '当前设备',
    value: deviceIdText.value,
    extra: deviceKindText.value,
    icon: Monitor,
    tone: 'ink',
  },
])

const quickItems: QuickItem[] = [
  {
    title: '打印中心',
    description: '发起打印任务并在绑定设备执行',
    icon: Tickets,
    tone: 'seal',
  },
  {
    title: '文档中心',
    description: '救助档案与业务附件统一管理',
    icon: FolderOpened,
    tone: 'pine',
  },
  {
    title: '系统设置',
    description: '通知、偏好与安全选项',
    icon: Setting,
    tone: 'amber',
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
        <NcStatusDot status="online" label="会话正常" />
        <el-button :icon="Refresh" :loading="refreshing" plain size="small" @click="refreshSession">
          刷新会话
        </el-button>
      </div>
    </section>

    <section class="info-grid" aria-label="会话信息">
      <article
        v-for="item in infoItems"
        :key="item.label"
        class="info-card"
        :class="`tone-${item.tone ?? 'ink'}`"
      >
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
        <RouterLink to="/printers" class="quick-card tone-seal">
          <span class="quick-icon"><el-icon :size="22"><Printer /></el-icon></span>
          <span class="quick-text">
            <strong>打印机绑定</strong>
            <small>管理本账号的设备与打印机</small>
          </span>
          <el-icon class="quick-arrow"><ArrowRight /></el-icon>
        </RouterLink>

        <div
          v-for="item in quickItems"
          :key="item.title"
          class="quick-card is-disabled"
          :class="`tone-${item.tone ?? 'ink'}`"
          aria-disabled="true"
        >
          <span class="quick-icon"><el-icon :size="22"><component :is="item.icon" /></el-icon></span>
          <span class="quick-text">
            <strong>{{ item.title }}</strong>
            <small>{{ item.description }}</small>
          </span>
          <span class="quick-badge">即将上线</span>
        </div>
      </div>
    </section>

    <NcCard title="打印使用提示" class="tips-card">
      <ol class="tips-list">
        <li><span>1</span>在需要执行打印的 Windows 电脑上安装并启动 NewCosmos Agent。</li>
        <li><span>2</span>进入「打印机绑定」，选择已注册的 Agent 设备与该设备上报的打印机。</li>
        <li><span>3</span>手机端发起打印后，任务将在绑定的电脑上由默认打印机执行。</li>
      </ol>
    </NcCard>
  </div>
</template>

<style scoped>
.home-page {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

/* ── 欢迎区（档案首页题头） ───────────────────────────────────── */
.welcome {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  overflow: hidden;
  padding: 24px 26px 24px 24px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background:
    linear-gradient(90deg, rgba(171, 50, 39, 0.045), transparent 34%),
    var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
}

.welcome::after {
  content: '档';
  position: absolute;
  right: 24px;
  bottom: -34px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 128px;
  font-weight: 700;
  line-height: 1;
  opacity: 0.04;
  pointer-events: none;
}

.welcome-main,
.welcome-side {
  position: relative;
  z-index: 1;
}

.welcome-main {
  min-width: 0;
}

.welcome-date {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.1em;
}

.welcome-date::before {
  content: '';
  flex: none;
  width: 6px;
  height: 6px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal);
}

.welcome-title {
  margin-top: 9px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 26px;
  font-weight: 700;
  line-height: 1.35;
  letter-spacing: 0.04em;
}

.welcome-sub {
  margin-top: 7px;
  color: var(--nc-muted-2);
  font-size: 13px;
}

.welcome-side {
  display: flex;
  flex: none;
  align-items: center;
  gap: 12px;
}

/* ── 会话信息卡 ───────────────────────────────────────────────── */
.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(236px, 1fr));
  gap: 12px;
}

.info-card {
  --tone: var(--nc-ink-2);
  --tone-soft: var(--nc-paper-3);

  display: flex;
  gap: 13px;
  min-width: 0;
  padding: 15px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.info-card:hover {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-sm);
}

.tone-seal {
  --tone: var(--nc-seal);
  --tone-soft: var(--nc-seal-wash);
}

.tone-pine {
  --tone: var(--nc-pine);
  --tone-soft: var(--nc-pine-soft);
}

.tone-amber {
  --tone: var(--nc-amber);
  --tone-soft: var(--nc-amber-soft);
}

.tone-ink {
  --tone: var(--nc-ink-2);
  --tone-soft: var(--nc-paper-3);
}

.info-icon {
  display: grid;
  flex: none;
  place-items: center;
  width: 36px;
  height: 36px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--tone-soft);
  color: var(--tone);
}

.info-main {
  display: flex;
  flex-direction: column;
  min-width: 0;
  line-height: 1.45;
}

.info-label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.12em;
}

.info-value {
  margin-top: 4px;
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.info-extra {
  margin-top: 2px;
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── 快捷入口 ─────────────────────────────────────────────────── */
.section-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.section-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.quick-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 12px;
}

.quick-card {
  --tone: var(--nc-ink-2);
  --tone-soft: var(--nc-paper-3);

  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 0;
  padding: 16px 17px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition),
    transform var(--nc-transition);
}

.quick-card:not(.is-disabled):hover {
  border-color: var(--nc-seal);
  box-shadow: var(--nc-shadow-sm);
  transform: translateY(-1px);
}

.quick-icon {
  display: grid;
  flex: none;
  place-items: center;
  width: 42px;
  height: 42px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--tone-soft);
  color: var(--tone);
}

.quick-text {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.quick-text strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.quick-text small {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.quick-arrow {
  margin-left: auto;
  color: var(--nc-seal);
}

.quick-badge {
  flex: none;
  margin-left: auto;
  padding: 1px 6px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.06em;
  white-space: nowrap;
}

.quick-card.is-disabled {
  background-color: var(--nc-paper);
  box-shadow: none;
  opacity: 0.66;
}

.quick-card.is-disabled .quick-icon {
  color: var(--nc-muted);
}

/* ── 打印提示 ─────────────────────────────────────────────────── */
.tips-list {
  display: grid;
  gap: 11px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.tips-list li {
  display: flex;
  align-items: flex-start;
  gap: 11px;
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.75;
}

.tips-list span {
  display: grid;
  flex: none;
  place-items: center;
  width: 20px;
  height: 20px;
  margin-top: 1px;
  border: 1px solid rgba(171, 50, 39, 0.45);
  border-radius: var(--nc-radius-xs);
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: 11px;
  font-weight: 600;
}

/* ── 手机 ─────────────────────────────────────────────────────── */
@media (min-width: 768px) and (max-width: 1023px) {
  .info-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .home-page {
    gap: 16px;
  }

  .welcome {
    flex-direction: column;
    align-items: flex-start;
    gap: 16px;
    padding: 18px 16px;
  }

  .welcome::after {
    right: 8px;
    bottom: -28px;
    font-size: 104px;
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

  .info-card {
    padding: 13px 14px;
  }

  .quick-grid {
    grid-template-columns: 1fr;
  }

  .quick-card {
    padding: 14px 15px;
  }

  .section-note {
    font-size: var(--nc-text-xs);
  }
}
</style>
