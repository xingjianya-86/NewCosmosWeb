<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { Component } from 'vue'
import {
  ArrowDown,
  House,
  Menu as MenuIcon,
  MoreFilled,
  Printer,
  SwitchButton,
  UserFilled,
} from '@element-plus/icons-vue'
import { APP_NAME, APP_SHORT_NAME, APP_VERSION } from '@/config/app'
import { useAuthStore } from '@/stores/auth'

interface NavItem {
  name: string
  path: string
  title: string
  shortTitle: string
  icon: Component
}

const navItems: NavItem[] = [
  { name: 'home', path: '/', title: '首页', shortTitle: '首页', icon: House },
  { name: 'printers', path: '/printers', title: '打印机绑定', shortTitle: '打印机', icon: Printer },
]

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const drawerVisible = ref(false)
const pageTitle = computed(() => route.meta.title ?? '')
const activePath = computed(() => (route.path === '/' ? '/' : route.path))
const avatarText = computed(() => auth.displayName.slice(0, 1).toUpperCase())
const organization = computed(() => auth.user?.organizationName || '未设置组织')
const expiresText = computed(() => formatDateTime(auth.expiresAt))
const isMoreActive = computed(() => !navItems.some((item) => item.path === route.path))

const HEARTBEAT_INTERVAL = 60_000
let heartbeatTimer: number | undefined
let heartbeatBusy = false

function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat('zh-CN', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

async function runHeartbeat(): Promise<void> {
  if (heartbeatBusy || !auth.isSessionValid()) return
  heartbeatBusy = true
  try {
    await auth.heartbeat()
  } catch {
    // 网络抖动不打断使用；401 已由 http 层统一处理
  } finally {
    heartbeatBusy = false
  }

  if (!auth.isSessionValid()) {
    auth.clear()
    await router.replace({ name: 'login' })
  }
}

function handleVisibilityChange(): void {
  if (document.visibilityState === 'visible') {
    void runHeartbeat()
  }
}

function navigate(path: string): void {
  drawerVisible.value = false
  if (route.path !== path) void router.push(path)
}

async function handleLogout(): Promise<void> {
  try {
    await ElMessageBox.confirm('退出后需要重新登录，确定继续吗？', '退出登录', {
      confirmButtonText: '退出',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }

  drawerVisible.value = false
  await auth.logout()
  ElMessage.success('已退出登录')
  await router.replace({ name: 'login' })
}

function handleUserCommand(command: string | number | object): void {
  if (command === 'logout') void handleLogout()
}

watch(
  () => route.fullPath,
  () => {
    drawerVisible.value = false
  },
)

onMounted(() => {
  void runHeartbeat()
  heartbeatTimer = window.setInterval(() => void runHeartbeat(), HEARTBEAT_INTERVAL)
  document.addEventListener('visibilitychange', handleVisibilityChange)
})

onBeforeUnmount(() => {
  if (heartbeatTimer !== undefined) window.clearInterval(heartbeatTimer)
  document.removeEventListener('visibilitychange', handleVisibilityChange)
})
</script>

<template>
  <div class="layout">
    <aside class="sidebar">
      <div class="brand">
        <div class="brand-mark">帝</div>
        <div class="brand-text">
          <strong>{{ APP_SHORT_NAME }}</strong>
          <span>社会救助管理系统</span>
        </div>
      </div>

      <div class="nav-section">业务导航</div>
      <el-menu :default-active="activePath" class="nav-menu" router>
        <el-menu-item v-for="item in navItems" :key="item.name" :index="item.path">
          <el-icon class="nav-icon"><component :is="item.icon" /></el-icon>
          <span>{{ item.title }}</span>
        </el-menu-item>
        <el-menu-item index="/print-center" disabled>
          <el-icon class="nav-icon"><Printer /></el-icon>
          <span>打印中心</span>
          <span class="nav-soon">待上线</span>
        </el-menu-item>
      </el-menu>

      <div class="side-foot">
        <span class="status-dot" />
        <span>会话在线 · v{{ APP_VERSION }}</span>
      </div>
    </aside>

    <div class="layout-body">
      <header class="topbar">
        <button class="menu-toggle" type="button" aria-label="打开菜单" @click="drawerVisible = true">
          <el-icon :size="20"><MenuIcon /></el-icon>
        </button>

        <div class="topbar-heading">
          <span class="topbar-kicker">档案 · 事务</span>
          <h1 class="topbar-title">{{ pageTitle }}</h1>
        </div>
        <el-tag class="topbar-org" effect="plain" round size="small">{{ organization }}</el-tag>

        <div class="topbar-spacer" />

        <el-dropdown trigger="click" @command="handleUserCommand">
          <button class="user-trigger" type="button">
            <span class="avatar">{{ avatarText }}</span>
            <span class="user-meta">
              <span class="user-name">{{ auth.displayName }}</span>
              <span class="user-org">{{ organization }}</span>
            </span>
            <el-icon class="user-arrow"><ArrowDown /></el-icon>
          </button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item disabled>会话有效期至 {{ expiresText }}</el-dropdown-item>
              <el-dropdown-item command="logout" divided>
                <el-icon><SwitchButton /></el-icon>
                退出登录
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </header>

      <main class="content">
        <RouterView v-slot="{ Component: viewComponent }">
          <Transition name="page-fade" mode="out-in">
            <component :is="viewComponent" />
          </Transition>
        </RouterView>
      </main>

      <nav class="tabbar" aria-label="底部导航">
        <button
          v-for="item in navItems"
          :key="item.name"
          class="tab-item"
          :class="{ 'is-active': activePath === item.path }"
          type="button"
          @click="navigate(item.path)"
        >
          <el-icon :size="20"><component :is="item.icon" /></el-icon>
          <span>{{ item.shortTitle }}</span>
        </button>
        <button class="tab-item" :class="{ 'is-active': drawerVisible || isMoreActive }" type="button" @click="drawerVisible = true">
          <el-icon :size="20"><MoreFilled /></el-icon>
          <span>更多</span>
        </button>
      </nav>
    </div>

    <el-drawer v-model="drawerVisible" direction="ltr" size="78%" :with-header="false">
      <div class="drawer">
        <div class="drawer-head">
          <div class="avatar avatar--lg">{{ avatarText }}</div>
          <div class="drawer-user">
            <strong>{{ auth.displayName }}</strong>
            <span>{{ organization }}</span>
          </div>
        </div>

        <div class="drawer-session">
          <el-icon><UserFilled /></el-icon>
          <span>账号 {{ auth.user?.username || '—' }}</span>
        </div>

        <el-menu :default-active="activePath" class="drawer-menu" router @select="drawerVisible = false">
          <el-menu-item v-for="item in navItems" :key="item.name" :index="item.path">
            <el-icon class="nav-icon"><component :is="item.icon" /></el-icon>
            <span>{{ item.title }}</span>
          </el-menu-item>
        </el-menu>

        <div class="drawer-foot">
          <p class="nc-muted">会话有效期至 {{ expiresText }}</p>
          <el-button type="danger" plain :icon="SwitchButton" class="drawer-logout" @click="handleLogout">
            退出登录
          </el-button>
          <p class="drawer-version">{{ APP_NAME }} · v{{ APP_VERSION }}</p>
        </div>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.layout {
  min-height: 100vh;
  background-color: var(--nc-paper);
}

/* ── 墨黑侧边栏 ───────────────────────────────────────────────── */
.sidebar {
  position: fixed;
  top: 0;
  bottom: 0;
  left: 0;
  z-index: 20;
  display: flex;
  flex-direction: column;
  width: var(--nc-sidebar-width);
  overflow: hidden;
  background:
    linear-gradient(180deg, #1c1814 0%, var(--nc-ink) 58%, #13100d 100%),
    var(--nc-ink);
  border-right: 1px solid rgba(242, 236, 225, 0.1);
}

.sidebar::after {
  content: '';
  position: absolute;
  inset: 0;
  background-image: repeating-linear-gradient(
    0deg,
    rgba(242, 236, 225, 0.022) 0 1px,
    transparent 1px 4px
  );
  pointer-events: none;
}

.sidebar > * {
  position: relative;
  z-index: 1;
}

.brand {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 22px 18px 18px;
  border-bottom: 1px solid var(--nc-line-paper);
}

.brand-mark {
  display: grid;
  flex: none;
  place-items: center;
  width: 40px;
  height: 40px;
  border: 1px solid rgba(242, 236, 225, 0.34);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-seal);
  box-shadow: inset 0 0 0 2px rgba(23, 19, 16, 0.16);
  color: var(--nc-paper-2);
  font-family: var(--nc-font-serif);
  font-size: 21px;
  font-weight: 700;
  line-height: 1;
}

.brand-text {
  display: flex;
  flex-direction: column;
  line-height: 1.4;
}

.brand-text strong {
  color: var(--nc-paper-2);
  font-family: var(--nc-font-serif);
  font-size: 16px;
  font-weight: 700;
  letter-spacing: 0.1em;
}

.brand-text span {
  color: var(--nc-muted);
  font-size: 10px;
  letter-spacing: 0.22em;
}

.nav-section {
  padding: 18px 22px 6px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.24em;
}

.nav-menu {
  --el-menu-bg-color: transparent;
  --el-menu-text-color: #a89a85;
  --el-menu-active-color: var(--nc-paper-2);
  --el-menu-hover-bg-color: #2b241e;
  --el-menu-hover-text-color: var(--nc-paper-2);
  --el-menu-border-color: transparent;

  flex: 1;
  padding: 0 12px;
  border-right: none;
  overflow-y: auto;
}

.nav-menu :deep(.el-menu-item) {
  height: 44px;
  margin: 2px 0;
  border-radius: var(--nc-radius-sm);
  color: #a89a85;
  letter-spacing: 0.04em;
}

.nav-menu :deep(.el-menu-item:hover) {
  color: var(--nc-paper-2);
  background-color: #2b241e;
}

.nav-menu :deep(.el-menu-item.is-active) {
  color: #fbf6ec;
  background-color: var(--nc-seal);
  box-shadow: inset 3px 0 0 var(--nc-seal-dark);
  font-weight: 600;
}

.nav-menu :deep(.el-menu-item.is-disabled) {
  color: var(--nc-muted);
  opacity: 0.55;
}

.nav-icon {
  margin-right: 4px;
}

.nav-soon {
  margin-left: auto;
  padding: 0 5px;
  border: 1px solid var(--nc-line-paper);
  border-radius: var(--nc-radius-xs);
  color: #a89a85;
  font-family: var(--nc-font-mono);
  font-size: 10px;
  line-height: 16px;
  letter-spacing: 0.06em;
}

.side-foot {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 14px 22px 18px;
  border-top: 1px solid var(--nc-line-paper);
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.04em;
}

.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background-color: var(--nc-pine-light);
  box-shadow: 0 0 0 3px rgba(74, 107, 86, 0.22);
}

/* ── 纸色内容区与顶栏 ─────────────────────────────────────────── */
.layout-body {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  margin-left: var(--nc-sidebar-width);
  background-color: var(--nc-paper);
}

.topbar {
  position: sticky;
  top: 0;
  z-index: 15;
  display: flex;
  align-items: center;
  gap: 12px;
  height: var(--nc-topbar-height);
  padding: 0 24px;
  background-color: rgba(248, 243, 233, 0.94);
  border-bottom: 1px solid var(--nc-line);
  backdrop-filter: blur(6px);
}

.topbar-heading {
  display: flex;
  flex-direction: column;
  justify-content: center;
  min-width: 0;
  line-height: 1.25;
}

.topbar-kicker {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 9px;
  font-weight: 600;
  letter-spacing: 0.24em;
}

.topbar-title {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 17px;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.topbar-org {
  max-width: 220px;
  overflow: hidden;
  border-radius: var(--nc-radius-xs);
  text-overflow: ellipsis;
}

.topbar-spacer {
  flex: 1;
}

.menu-toggle {
  display: none;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  padding: 0;
  border: 1px solid var(--nc-line-strong);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  color: var(--nc-ink-2);
  cursor: pointer;
}

.menu-toggle:hover {
  border-color: var(--nc-seal);
  color: var(--nc-seal);
}

.user-trigger {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 4px 8px 4px 4px;
  border: 1px solid transparent;
  border-radius: var(--nc-radius-sm);
  background: transparent;
  cursor: pointer;
  transition:
    background-color var(--nc-transition),
    border-color var(--nc-transition);
}

.user-trigger:hover {
  border-color: var(--nc-line-faint);
  background-color: var(--nc-paper-3);
}

.avatar {
  display: grid;
  place-items: center;
  width: 32px;
  height: 32px;
  border: 1px solid rgba(171, 50, 39, 0.28);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-serif);
  font-size: 14px;
  font-weight: 700;
}

.avatar--lg {
  width: 44px;
  height: 44px;
  font-size: 18px;
}

.user-meta {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  line-height: 1.3;
}

.user-name {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
}

.user-org {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.user-arrow {
  color: var(--nc-muted);
}

.content {
  flex: 1;
  min-width: 0;
}

.tabbar {
  display: none;
}

/* ── 手机抽屉 ─────────────────────────────────────────────────── */
.drawer {
  display: flex;
  flex-direction: column;
  min-height: 100%;
}

.drawer-head {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 4px 18px;
  border-bottom: 1px solid var(--nc-line);
}

.drawer-user {
  display: flex;
  flex-direction: column;
  line-height: 1.4;
}

.drawer-user strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 16px;
}

.drawer-user span {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.drawer-session {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 4px 4px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.drawer-menu {
  --el-menu-bg-color: transparent;
  --el-menu-text-color: var(--nc-ink-2);
  --el-menu-active-color: var(--nc-seal-dark);
  --el-menu-hover-bg-color: var(--nc-paper-3);

  border-right: none;
  padding: 6px 0;
}

.drawer-menu :deep(.el-menu-item) {
  height: 46px;
  margin: 2px 0;
  border-radius: var(--nc-radius-sm);
}

.drawer-menu :deep(.el-menu-item.is-active) {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-weight: 600;
}

.drawer-foot {
  margin-top: auto;
  padding-top: 20px;
}

.drawer-foot p {
  font-size: var(--nc-text-xs);
}

.drawer-logout {
  width: 100%;
  height: 42px;
  margin: 10px 0 14px;
}

.drawer-version {
  color: var(--nc-muted);
  text-align: center;
}

.page-fade-enter-active,
.page-fade-leave-active {
  transition:
    opacity 0.16s ease,
    transform 0.16s ease;
}

.page-fade-enter-from {
  opacity: 0;
  transform: translateY(6px);
}

.page-fade-leave-to {
  opacity: 0;
}

@media (max-width: 1023px) {
  .sidebar {
    display: none;
  }

  .layout-body {
    margin-left: 0;
  }

  .menu-toggle {
    display: inline-flex;
  }

  .topbar {
    padding: 0 12px;
    gap: 10px;
  }

  .topbar-kicker {
    display: none;
  }

  .topbar-title {
    font-size: 16px;
  }

  .topbar-org {
    display: none;
  }

  .user-meta {
    display: none;
  }

  .user-arrow {
    display: none;
  }

  .content {
    padding-bottom: calc(58px + env(safe-area-inset-bottom));
  }

  .tabbar {
    position: fixed;
    right: 0;
    bottom: 0;
    left: 0;
    z-index: 30;
    display: flex;
    padding-bottom: env(safe-area-inset-bottom);
    background-color: rgba(248, 243, 233, 0.97);
    border-top: 1px solid var(--nc-line);
    backdrop-filter: blur(6px);
  }

  .tab-item {
    display: flex;
    flex: 1;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 2px;
    min-height: 52px;
    padding: 8px 0 7px;
    border: none;
    background: none;
    color: var(--nc-muted);
    font-size: var(--nc-text-xs);
    cursor: pointer;
  }

  .tab-item.is-active {
    color: var(--nc-seal);
    box-shadow: inset 0 2px 0 var(--nc-seal);
    font-weight: 600;
  }
}
</style>
