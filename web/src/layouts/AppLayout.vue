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
        <div class="brand-mark">新</div>
        <div class="brand-text">
          <strong>{{ APP_SHORT_NAME }}</strong>
          <span>政务服务平台</span>
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

        <h1 class="topbar-title">{{ pageTitle }}</h1>
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
}

.sidebar {
  position: fixed;
  top: 0;
  bottom: 0;
  left: 0;
  z-index: 20;
  display: flex;
  flex-direction: column;
  width: var(--nc-sidebar-width);
  background: #fff;
  border-right: 1px solid var(--nc-border);
}

.brand {
  display: flex;
  align-items: center;
  gap: 11px;
  padding: 20px 18px 18px;
}

.brand-mark {
  display: grid;
  place-items: center;
  width: 38px;
  height: 38px;
  border-radius: 9px;
  background: linear-gradient(150deg, #2a76ba, #164c7e);
  color: #fff;
  font-size: 18px;
  font-weight: 700;
}

.brand-text {
  display: flex;
  flex-direction: column;
  line-height: 1.35;
}

.brand-text strong {
  font-size: 15px;
  letter-spacing: 0.4px;
}

.brand-text span {
  font-size: 11px;
  color: var(--nc-text-muted);
  letter-spacing: 1.6px;
}

.nav-section {
  padding: 12px 24px 6px;
  font-size: 11px;
  letter-spacing: 1.4px;
  color: var(--nc-text-muted);
}

.nav-menu {
  flex: 1;
  padding: 0 12px;
  border-right: none;
  overflow-y: auto;
}

.nav-menu :deep(.el-menu-item) {
  height: 44px;
  margin: 3px 0;
  border-radius: 8px;
  color: var(--nc-text-secondary);
}

.nav-menu :deep(.el-menu-item.is-active) {
  background: var(--nc-primary-soft);
  color: var(--nc-primary);
  font-weight: 600;
}

.nav-menu :deep(.el-menu-item.is-disabled) {
  opacity: 0.65;
}

.nav-icon {
  margin-right: 4px;
}

.nav-soon {
  margin-left: auto;
  padding: 0 6px;
  border-radius: 4px;
  background: #f2f4f8;
  color: var(--nc-text-muted);
  font-size: 10px;
  line-height: 18px;
}

.side-foot {
  display: flex;
  align-items: center;
  gap: 7px;
  padding: 14px 22px 18px;
  font-size: 11px;
  color: var(--nc-text-muted);
  border-top: 1px solid var(--nc-border);
}

.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #42b883;
  box-shadow: 0 0 0 3px rgba(66, 184, 131, 0.14);
}

.layout-body {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  margin-left: var(--nc-sidebar-width);
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
  background: rgba(255, 255, 255, 0.94);
  border-bottom: 1px solid var(--nc-border);
  backdrop-filter: blur(8px);
}

.topbar-title {
  font-size: 16px;
  font-weight: 600;
  white-space: nowrap;
}

.topbar-org {
  max-width: 220px;
  overflow: hidden;
  text-overflow: ellipsis;
}

.topbar-spacer {
  flex: 1;
}

.menu-toggle {
  display: none;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  padding: 0;
  border: 1px solid var(--nc-border);
  border-radius: 8px;
  background: #fff;
  color: var(--nc-text);
  cursor: pointer;
}

.user-trigger {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 4px 8px 4px 4px;
  border: none;
  border-radius: 10px;
  background: transparent;
  cursor: pointer;
  transition: background 0.15s ease;
}

.user-trigger:hover {
  background: #f4f7fb;
}

.avatar {
  display: grid;
  place-items: center;
  width: 32px;
  height: 32px;
  border-radius: 50%;
  background: var(--nc-primary-soft);
  color: var(--nc-primary);
  font-size: 14px;
  font-weight: 600;
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
  font-size: 13px;
  font-weight: 600;
  color: var(--nc-text);
}

.user-org {
  font-size: 11px;
  color: var(--nc-text-muted);
}

.user-arrow {
  color: var(--nc-text-muted);
}

.content {
  flex: 1;
  min-width: 0;
}

.tabbar {
  display: none;
}

/* 手机抽屉 */
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
  border-bottom: 1px solid var(--nc-border);
}

.drawer-user {
  display: flex;
  flex-direction: column;
  line-height: 1.4;
}

.drawer-user strong {
  font-size: 16px;
}

.drawer-user span {
  font-size: 12px;
  color: var(--nc-text-muted);
}

.drawer-session {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 4px 4px;
  font-size: 12px;
  color: var(--nc-text-secondary);
}

.drawer-menu {
  border-right: none;
  padding: 6px 0;
}

.drawer-menu :deep(.el-menu-item) {
  height: 46px;
  border-radius: 8px;
  margin: 2px 0;
}

.drawer-menu :deep(.el-menu-item.is-active) {
  background: var(--nc-primary-soft);
  color: var(--nc-primary);
  font-weight: 600;
}

.drawer-foot {
  margin-top: auto;
  padding-top: 20px;
}

.drawer-foot p {
  font-size: 11px;
}

.drawer-logout {
  width: 100%;
  margin: 10px 0 14px;
}

.drawer-version {
  text-align: center;
  color: var(--nc-text-muted);
}

.page-fade-enter-active,
.page-fade-leave-active {
  transition: opacity 0.16s ease, transform 0.16s ease;
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

  .topbar-title {
    font-size: 15px;
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
    background: rgba(255, 255, 255, 0.97);
    border-top: 1px solid var(--nc-border);
    backdrop-filter: blur(8px);
  }

  .tab-item {
    display: flex;
    flex: 1;
    flex-direction: column;
    align-items: center;
    gap: 2px;
    padding: 8px 0 7px;
    border: none;
    background: none;
    color: var(--nc-text-muted);
    font-size: 11px;
    cursor: pointer;
  }

  .tab-item.is-active {
    color: var(--nc-primary);
    font-weight: 600;
  }
}
</style>
