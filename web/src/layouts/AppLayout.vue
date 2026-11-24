<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { Component } from 'vue'
import {
  Calendar,
  Coin,
  DataBoard,
  Document,
  FolderOpened,
  House,
  Link,
  Menu as MenuIcon,
  Money,
  MoreFilled,
  OfficeBuilding,
  Printer,
  Search,
  Setting,
  SwitchButton,
  Trophy,
  User,
  UserFilled,
} from '@element-plus/icons-vue'
import { APP_NAME, APP_VERSION } from '@/config/app'
import { useAuthStore } from '@/stores/auth'

interface NavItem {
  label: string
  icon: Component
  /** 站内路由；与 href 二选一 */
  path?: string
  /** 外链；新窗口打开 */
  href?: string
}

interface NavGroup {
  title: string
  items: NavItem[]
}

/*
 * 侧栏目录 = legacy Pages/Main/MainPage.xaml（分组与文案逐项对齐）。
 * 已实现页面：首页 /、低收入人口救助帮扶 /applications、打印机绑定 /printers；
 * 其余模块统一走 /coming-soon/:module 占位页，保持旧文案不变。
 */
const navGroups: NavGroup[] = [
  {
    title: '工 作 台',
    items: [{ label: '首页', icon: House, path: '/' }],
  },
  {
    title: '救助业务',
    items: [
      { label: '低收入人口救助帮扶', icon: Coin, path: '/applications' },
      { label: '高龄津贴发放', icon: UserFilled, path: '/coming-soon/elderly' },
      { label: '家庭经济状况核对', icon: Search, path: '/coming-soon/asset-verification' },
      { label: '救助档案管理', icon: FolderOpened, path: '/coming-soon/archive' },
      { label: '后补追缴', icon: Money, path: '/coming-soon/recovery' },
    ],
  },
  {
    title: '管理工具',
    items: [
      { label: '数据中心', icon: DataBoard, path: '/coming-soon/database' },
      { label: '用户与权限管理', icon: User, path: '/coming-soon/users' },
      { label: '经办机构管理', icon: OfficeBuilding, path: '/coming-soon/organizations' },
      { label: '文书模板管理', icon: Document, path: '/coming-soon/templates' },
      { label: '值班管理', icon: Calendar, path: '/coming-soon/duty' },
      // Web 端既有能力：打印机绑定（旧版由桌面端承担，Web 保留入口）
      { label: '打印机绑定', icon: Printer, path: '/printers' },
    ],
  },
  {
    title: '娱乐天地',
    items: [{ label: '彩票助手', icon: Trophy, path: '/coming-soon/lottery' }],
  },
  {
    title: '链接',
    items: [
      { label: '政策官网', icon: Link, href: 'https://sua17.cn' },
      {
        label: '民政官网',
        icon: Link,
        href: 'https://zwfw.mzt.hlj.gov.cn/casserver/login',
      },
    ],
  },
]

const mobileTabs: NavItem[] = [
  { label: '首页', icon: House, path: '/' },
  { label: '申请', icon: Coin, path: '/applications' },
  { label: '打印机', icon: Printer, path: '/printers' },
]

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const drawerVisible = ref(false)
const contentRef = ref<HTMLElement | null>(null)
const mascotIcon = `${import.meta.env.BASE_URL}images/mascot_icon.png`

const activePath = computed(() => {
  if (route.name === 'coming-soon') return `/coming-soon/${String(route.params.module ?? '')}`
  if (route.path === '/applications' || route.path.startsWith('/applications/')) {
    return '/applications'
  }
  if (route.path === '/printers' || route.path.startsWith('/printers/')) return '/printers'
  return route.path
})

const isMoreActive = computed(
  () => !mobileTabs.some((item) => item.path === activePath.value),
)

const expiresText = computed(() => formatDateTime(auth.expiresAt))

/* ── 顶栏时钟：旧版格式 yyyy-MM-dd HH:mm，每分钟刷新 ───────────── */
const now = ref(new Date())
let clockTimer: number | undefined

function pad(value: number): string {
  return value < 10 ? `0${value}` : String(value)
}

function formatClock(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

const dateText = computed(() => formatClock(now.value))

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

function isActive(item: NavItem): boolean {
  return Boolean(item.path) && item.path === activePath.value
}

function navigate(path: string): void {
  drawerVisible.value = false
  if (route.path !== path) void router.push(path)
}

function goSettings(): void {
  void router.push('/coming-soon/settings')
}

async function handleLogout(): Promise<void> {
  try {
    await ElMessageBox.confirm('确定要退出登录吗', '确认', {
      confirmButtonText: '确定',
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

watch(
  () => route.fullPath,
  () => {
    drawerVisible.value = false
    contentRef.value?.scrollTo({ top: 0 })
  },
)

onMounted(() => {
  clockTimer = window.setInterval(() => {
    now.value = new Date()
  }, 60_000)
  void runHeartbeat()
  heartbeatTimer = window.setInterval(() => void runHeartbeat(), HEARTBEAT_INTERVAL)
  document.addEventListener('visibilitychange', handleVisibilityChange)
})

onBeforeUnmount(() => {
  if (clockTimer !== undefined) window.clearInterval(clockTimer)
  if (heartbeatTimer !== undefined) window.clearInterval(heartbeatTimer)
  document.removeEventListener('visibilitychange', handleVisibilityChange)
})
</script>

<template>
  <div class="layout">
    <!-- ── 顶部报头：朱砂竖条 + 宋体标题 + DM Mono kicker + 底部双线 ── -->
    <header class="topbar">
      <div class="topbar-seal" aria-hidden="true" />
      <div class="topbar-main">
        <div class="topbar-bar">
          <button
            class="menu-toggle"
            type="button"
            aria-label="打开菜单"
            @click="drawerVisible = true"
          >
            <el-icon :size="20"><MenuIcon /></el-icon>
          </button>

          <div class="brand">
            <img class="brand-logo" :src="mascotIcon" alt="" width="34" height="34" />
            <div class="brand-text">
              <span class="brand-kicker">NEWCOSMOS</span>
              <h1 class="brand-title" :title="APP_NAME">{{ APP_NAME }}</h1>
            </div>
          </div>

          <div class="topbar-actions">
            <div class="topbar-date">
              <el-icon :size="16"><Calendar /></el-icon>
              <span>{{ dateText }}</span>
            </div>

            <div class="user-badge" :title="auth.displayName">
              <el-icon :size="20"><UserFilled /></el-icon>
              <span class="user-badge-name">{{ auth.displayName }}</span>
            </div>

            <button class="icon-button" type="button" aria-label="系统设置" @click="goSettings">
              <el-icon :size="18"><Setting /></el-icon>
            </button>
          </div>
        </div>
        <div class="topbar-ink" aria-hidden="true" />
        <div class="topbar-gap" aria-hidden="true" />
        <div class="topbar-hairline" aria-hidden="true" />
      </div>
    </header>

    <div class="body">
      <!-- ── 200px 墨黑侧栏 ── -->
      <aside class="sidebar" aria-label="主导航">
        <nav class="sidebar-inner">
          <template v-for="group in navGroups" :key="group.title">
            <p class="nav-group-title">{{ group.title }}</p>
            <template v-for="item in group.items" :key="item.label">
              <a
                v-if="item.href"
                class="nav-item"
                :href="item.href"
                target="_blank"
                rel="noopener noreferrer"
                @click="drawerVisible = false"
              >
                <el-icon :size="16"><component :is="item.icon" /></el-icon>
                <span>{{ item.label }}</span>
              </a>
              <button
                v-else
                class="nav-item"
                :class="{
                  'is-active': isActive(item),
                  'nav-item--home': item.path === '/',
                }"
                type="button"
                @click="navigate(item.path ?? '/')"
              >
                <el-icon :size="16"><component :is="item.icon" /></el-icon>
                <span>{{ item.label }}</span>
              </button>
            </template>
          </template>

          <div class="nav-divider" aria-hidden="true" />

          <button class="nav-item nav-item--logout" type="button" @click="handleLogout">
            <el-icon :size="16"><SwitchButton /></el-icon>
            <span>退出登录</span>
          </button>
        </nav>
      </aside>

      <div class="workspace">
        <main ref="contentRef" class="content">
          <RouterView v-slot="{ Component: viewComponent }">
            <Transition name="page-fade" mode="out-in">
              <component :is="viewComponent" />
            </Transition>
          </RouterView>
        </main>
      </div>
    </div>

    <!-- ── 底部状态栏（通栏，旧版 Grid.Row=3） ── -->
    <footer class="statusbar">
      <span class="statusbar-item">
        <span class="statusbar-label">数据库:</span>
        <strong class="statusbar-value">未检测</strong>
      </span>
      <span class="statusbar-divider" aria-hidden="true" />
      <span class="statusbar-item">
        <span class="statusbar-label">会话:</span>
        <strong class="statusbar-value">在线</strong>
      </span>
      <span class="statusbar-divider" aria-hidden="true" />
      <span class="statusbar-item statusbar-item--version">
        <span class="statusbar-label">版本:</span>
        <span class="statusbar-value">v{{ APP_VERSION }}</span>
      </span>
      <span class="statusbar-copy">© 2026 帝皇权杖δ-me13 社会救助管理系统</span>
    </footer>

    <!-- ── 手机 / 平板底部导航 ── -->
    <nav class="tabbar" aria-label="底部导航">
      <button
        v-for="item in mobileTabs"
        :key="item.label"
        class="tab-item"
        :class="{ 'is-active': activePath === item.path }"
        type="button"
        @click="navigate(item.path ?? '/')"
      >
        <el-icon :size="20"><component :is="item.icon" /></el-icon>
        <span>{{ item.label }}</span>
      </button>
      <button
        class="tab-item"
        :class="{ 'is-active': drawerVisible || isMoreActive }"
        type="button"
        @click="drawerVisible = true"
      >
        <el-icon :size="20"><MoreFilled /></el-icon>
        <span>更多</span>
      </button>
    </nav>

    <!-- ── 手机 / 平板抽屉（复用旧版墨黑侧栏语言） ── -->
    <el-drawer
      v-model="drawerVisible"
      class="drawer-shell"
      direction="ltr"
      size="78%"
      :with-header="false"
    >
      <div class="drawer">
        <div class="drawer-head">
          <img class="drawer-logo" :src="mascotIcon" alt="" width="40" height="40" />
          <div class="drawer-brand">
            <span class="drawer-kicker">NEWCOSMOS</span>
            <strong>{{ APP_NAME }}</strong>
          </div>
        </div>

        <div class="drawer-user">
          <el-icon :size="18"><UserFilled /></el-icon>
          <span>{{ auth.displayName }}</span>
          <small>有效期至 {{ expiresText }}</small>
        </div>

        <nav class="drawer-nav">
          <template v-for="group in navGroups" :key="group.title">
            <p class="nav-group-title">{{ group.title }}</p>
            <template v-for="item in group.items" :key="item.label">
              <a
                v-if="item.href"
                class="nav-item"
                :href="item.href"
                target="_blank"
                rel="noopener noreferrer"
                @click="drawerVisible = false"
              >
                <el-icon :size="16"><component :is="item.icon" /></el-icon>
                <span>{{ item.label }}</span>
              </a>
              <button
                v-else
                class="nav-item"
                :class="{
                  'is-active': isActive(item),
                  'nav-item--home': item.path === '/',
                }"
                type="button"
                @click="navigate(item.path ?? '/')"
              >
                <el-icon :size="16"><component :is="item.icon" /></el-icon>
                <span>{{ item.label }}</span>
              </button>
            </template>
          </template>

          <div class="nav-divider" aria-hidden="true" />

          <button class="nav-item nav-item--logout" type="button" @click="handleLogout">
            <el-icon :size="16"><SwitchButton /></el-icon>
            <span>退出登录</span>
          </button>
        </nav>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.layout {
  display: flex;
  flex-direction: column;
  height: 100vh;
  height: 100dvh;
  overflow: hidden;
  background-color: var(--nc-paper);
}

/* ── 顶栏 ─────────────────────────────────────────────────────── */
.topbar {
  display: grid;
  flex: none;
  grid-template-columns: 6px minmax(0, 1fr);
  height: 76px;
}

.topbar-seal {
  background-color: var(--nc-seal);
}

.topbar-main {
  display: grid;
  grid-template-rows: minmax(0, 1fr) 3px 4px 1px;
  min-width: 0;
  background-color: var(--nc-paper);
}

.topbar-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  min-width: 0;
  padding: 12px 20px;
  background-color: #f8f3e9;
}

.brand {
  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 0;
}

.brand-logo {
  display: block;
  flex: none;
  width: 34px;
  height: 34px;
  object-fit: cover;
}

.brand-text {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.brand-kicker {
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-size: 13px;
  line-height: 1.2;
  letter-spacing: 3.6px;
}

.brand-title {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 21px;
  font-weight: 700;
  line-height: 1.3;
  letter-spacing: 0.01em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.topbar-actions {
  display: flex;
  flex: none;
  align-items: center;
  gap: 20px;
}

.topbar-date {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--nc-ink-2);
  font-size: 18px;
  white-space: nowrap;
}

.user-badge {
  display: flex;
  align-items: center;
  gap: 8px;
  max-width: 260px;
  padding: 5px 10px;
  border: 1px solid var(--nc-line-strong);
  background-color: #f2ece1;
  color: var(--nc-ink);
}

.user-badge .el-icon {
  flex: none;
  color: var(--nc-seal);
}

.user-badge-name {
  overflow: hidden;
  font-size: 18px;
  font-weight: 700;
  line-height: 1.4;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.icon-button {
  display: grid;
  flex: none;
  place-items: center;
  width: 40px;
  height: 40px;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--nc-ink-2);
  cursor: pointer;
  transition: background-color var(--nc-transition);
}

.icon-button:hover {
  background-color: #f2ece1;
}

.topbar-ink {
  background-color: var(--nc-ink);
}

.topbar-hairline {
  background-color: var(--nc-line-strong);
}

.menu-toggle {
  display: none;
  flex: none;
  place-items: center;
  width: 40px;
  height: 40px;
  padding: 0;
  border: 1px solid var(--nc-line);
  background-color: transparent;
  color: var(--nc-ink-2);
  cursor: pointer;
}

/* ── 主体：侧栏 + 工作区 ─────────────────────────────────────── */
.body {
  display: flex;
  flex: 1;
  min-height: 0;
}

.sidebar {
  flex: none;
  width: 200px;
  overflow-y: auto;
  background-color: var(--nc-ink);
  scrollbar-width: thin;
}

.sidebar-inner {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 12px 8px;
}

.nav-group-title {
  margin: 14px 0 4px 16px;
  color: #8b7f6c;
  font-family: var(--nc-font-mono);
  font-size: 13px;
  font-weight: 700;
  line-height: 1.5;
  letter-spacing: 2px;
}

.nav-item {
  display: flex;
  flex: none;
  align-items: center;
  gap: 12px;
  width: 100%;
  height: 44px;
  padding: 0 16px;
  border: 0;
  background: transparent;
  color: #a89a85;
  font-size: 15px;
  line-height: 1.4;
  text-align: left;
  cursor: pointer;
  transition:
    background-color var(--nc-transition),
    color var(--nc-transition);
}

.nav-item .el-icon {
  flex: none;
  color: inherit;
}

.nav-item:hover {
  background-color: #2b241e;
  color: var(--nc-paper-2);
}

.nav-item.is-active {
  background-color: var(--nc-seal);
  color: #ffffff;
}

.nav-item--home {
  color: #ffffff;
  font-size: 16px;
}

.nav-item--logout {
  color: var(--nc-seal-dark);
}

.nav-item--logout:hover {
  background-color: #2b241e;
  color: var(--nc-seal-light);
}

.nav-divider {
  flex: none;
  height: 1px;
  margin: 12px 8px;
  background-color: #2b241e;
}

.workspace {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
  background-color: #faf7f1;
}

.content {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

/* ── 状态栏 ───────────────────────────────────────────────────── */
.statusbar {
  display: flex;
  flex: none;
  align-items: center;
  gap: 20px;
  height: 44px;
  padding: 8px 20px;
  overflow: hidden;
  background-color: #f2ece1;
  color: var(--nc-ink-2);
  font-size: 15px;
  white-space: nowrap;
}

.statusbar-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.statusbar-label {
  color: var(--nc-ink-2);
}

.statusbar-value {
  color: var(--nc-ink);
  font-weight: 700;
}

.statusbar-item--version .statusbar-label,
.statusbar-item--version .statusbar-value {
  color: var(--nc-muted);
  font-weight: 400;
}

.statusbar-divider {
  flex: none;
  width: 1px;
  height: 12px;
  background-color: var(--nc-paper-4);
}

.statusbar-copy {
  margin-left: auto;
  overflow: hidden;
  color: var(--nc-muted);
  text-overflow: ellipsis;
}

/* ── 手机抽屉 ─────────────────────────────────────────────────── */
.drawer-shell :deep(.el-drawer__body) {
  display: flex;
  flex-direction: column;
  padding: 0;
  background-color: var(--nc-ink);
}

.drawer {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.drawer-head {
  display: flex;
  flex: none;
  align-items: center;
  gap: 12px;
  padding: 18px 16px 14px;
  border-bottom: 1px solid #2b241e;
}

.drawer-logo {
  flex: none;
  width: 40px;
  height: 40px;
  object-fit: cover;
}

.drawer-brand {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.drawer-kicker {
  color: var(--nc-seal-light);
  font-family: var(--nc-font-mono);
  font-size: 11px;
  letter-spacing: 3px;
}

.drawer-brand strong {
  overflow: hidden;
  color: #f8f3e9;
  font-family: var(--nc-font-serif);
  font-size: 15px;
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.drawer-user {
  display: flex;
  flex: none;
  align-items: center;
  gap: 8px;
  padding: 12px 16px;
  border-bottom: 1px solid #2b241e;
  color: #f8f3e9;
  font-size: 14px;
}

.drawer-user .el-icon {
  color: var(--nc-seal-light);
}

.drawer-user span {
  overflow: hidden;
  min-width: 0;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.drawer-user small {
  margin-left: auto;
  color: #8b7f6c;
  font-size: 11px;
  white-space: nowrap;
}

.drawer-nav {
  flex: 1;
  min-height: 0;
  padding: 8px;
  overflow-y: auto;
}

/* ── 平板 / 手机 ───────────────────────────────────────────────── */
.tabbar {
  display: none;
}

@media (max-width: 1023px) {
  .topbar {
    height: 64px;
  }

  .topbar-bar {
    padding: 8px 12px;
    gap: 10px;
  }

  .menu-toggle {
    display: grid;
  }

  .brand {
    gap: 10px;
  }

  .brand-logo {
    width: 30px;
    height: 30px;
  }

  .brand-kicker {
    font-size: 10px;
    letter-spacing: 2.4px;
  }

  .brand-title {
    font-size: 16px;
  }

  .topbar-date {
    display: none;
  }

  .topbar-actions {
    gap: 8px;
  }

  .user-badge {
    padding: 4px 8px;
  }

  .user-badge-name {
    display: none;
  }

  .sidebar {
    display: none;
  }

  .statusbar {
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

@media (min-width: 900px) and (max-width: 1180px) {
  .topbar-date {
    display: none;
  }
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
</style>
