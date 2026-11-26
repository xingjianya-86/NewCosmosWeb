<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { Component } from 'vue'
import { useRouter } from 'vue-router'
import {
  ArrowRight,
  Calendar,
  Camera,
  Check,
  Coin,
  DataBoard,
  FolderOpened,
  Link,
  Money,
  Printer,
  Refresh,
  Search,
  SwitchButton,
  Trophy,
  User,
  UserFilled,
  Warning,
} from '@element-plus/icons-vue'
import { getDashboardSummary } from '@/api/dashboard'
import { getErrorMessage } from '@/api/http'
import type { DashboardSummary } from '@/types/dashboard'

interface StatCard {
  label: string
  caption: string
  value: number | null
  icon: Component
  gradient: 'blue' | 'green' | 'orange' | 'purple'
}

interface ModuleCard {
  title: string
  subtitle: string
  description: string
  icon: Component
  iconBg: string
  iconColor: string
  buttonText: string
  to?: string
  href?: string
}

interface ModuleGroup {
  title: string
  cards: ModuleCard[]
}

interface WarningBanner {
  key: string
  text: string
  icon: Component
  to: string
}

const router = useRouter()
const mascotUrl = `${import.meta.env.BASE_URL}images/newcosmos_character.png`

/* ═══════════════════════════════════════════════════════════════════
   首页概览：GET /api/dashboard/summary（登录即可）
   各数据块失败返回 null，页面显示「—」+ 重试，不用假 0。
   ═══════════════════════════════════════════════════════════════════ */
const summary = ref<DashboardSummary | null>(null)
const loading = ref(false)
const loadError = ref('')
let loadTicket = 0

async function loadDashboard(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await getDashboardSummary()
    if (ticket !== loadTicket) return
    summary.value = response
    const allBlocksFailed =
      !response.dashboard &&
      !response.socialAssistance &&
      !response.elderly &&
      !response.assetVerification &&
      !response.reminders
    if (allBlocksFailed) {
      loadError.value = '数据加载失败'
    }
  } catch (error) {
    if (ticket !== loadTicket) return
    summary.value = null
    loadError.value = getErrorMessage(error, '数据加载失败')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

onMounted(() => {
  void loadDashboard()
})

/* ═══════════════════════════════════════════════════════════════════
   警告横幅区（旧版 MainPage.row1）
   渐退期 / 普惠高龄 / 大学生毕业接真实计数；死亡申报联动域未迁入 Core，保持隐藏。
   ═══════════════════════════════════════════════════════════════════ */
const warnings = computed<WarningBanner[]>(() => {
  const items: WarningBanner[] = []
  const reminders = summary.value?.reminders

  const graceExpiring = reminders?.gracePeriodExpiring ?? 0
  if (graceExpiring > 0) {
    items.push({
      key: 'grace-period',
      text: `有 ${graceExpiring} 户渐退期将在 30 天内到期或已到期，请及时前往「低收入人口救助帮扶」处理`,
      icon: Calendar,
      to: '/social-assistance/grace-period',
    })
  }

  const elderlyPendingNew = reminders?.elderlyPendingNew ?? 0
  const elderlyPendingTransfer = reminders?.elderlyPendingTransfer ?? 0
  const elderlyTotal = elderlyPendingNew + elderlyPendingTransfer
  if (elderlyTotal > 0) {
    items.push({
      key: 'elderly',
      text:
        `有 ${elderlyTotal} 位老人的高龄津贴需于下月办理变更` +
        `（${elderlyPendingNew} 人待新增 / ${elderlyPendingTransfer} 人需先停发后新增），` +
        '请及时前往「高龄津贴发放」处理',
      icon: Calendar,
      to: '/elderly',
    })
  }

  const collegeGraduating = reminders?.collegeStudentGraduating ?? 0
  if (collegeGraduating > 0) {
    items.push({
      key: 'college-student',
      text: `有 ${collegeGraduating} 位大学生今年6月毕业，毕业后最长择业期半年，请及时前往「低收入人口救助帮扶」更新毕业状态并核查是否需退出最低生活保障`,
      icon: UserFilled,
      to: '/social-assistance',
    })
  }

  return items
})

/* ═══════════════════════════════════════════════════════════════════
   数据概览（旧版四张 StatCard，绑定真实统计；失败/加载中显示「—」）
   ═══════════════════════════════════════════════════════════════════ */
const stats = computed<StatCard[]>(() => {
  const dashboard = summary.value?.dashboard
  return [
    {
      label: '本月总新增',
      caption: '统计周期内 新入保（户）',
      value: dashboard?.monthlyNewAdditions ?? null,
      icon: Check,
      gradient: 'blue',
    },
    {
      label: '本月总退出',
      caption: '统计周期内 停保退出（户）',
      value: dashboard?.monthlyExits ?? null,
      icon: SwitchButton,
      gradient: 'green',
    },
    {
      label: '新申请经济核对',
      caption: '统计周期内 已申未出报告（件）',
      value: dashboard?.newAssetChecks ?? null,
      icon: Search,
      gradient: 'orange',
    },
    {
      label: '本月新增高龄老人',
      caption: '自然月受理登记（人）',
      value: dashboard?.monthlyNewElderly ?? null,
      icon: UserFilled,
      gradient: 'purple',
    },
  ]
})

const statsLoading = computed(() => loading.value && summary.value === null)

function statText(value: number | null): string {
  if (value === null) return '—'
  return value.toLocaleString('zh-CN')
}

/* ═══════════════════════════════════════════════════════════════════
   模块直达区：目录、文案、图标底色取 legacy MainViewModel.RebuildDashboard
   ═══════════════════════════════════════════════════════════════════ */
const moduleGroups: ModuleGroup[] = [
  {
    title: '救助业务',
    cards: [
      {
        title: '低收入人口救助帮扶',
        subtitle: '认定审核确认 · 动态管理',
        description: '覆盖救助申请、保障对象动态管理、临时救助、月报表与渐退期管理',
        icon: Coin,
        iconBg: '#EBF5FF',
        iconColor: '#ab3227',
        buttonText: '进入模块',
        to: '/social-assistance',
      },
      {
        title: '高龄津贴发放',
        subtitle: '申请登记 · 停发管理 · 类别复核',
        description: '覆盖高龄津贴申请登记、停发办理、类别复核与月度报表',
        icon: UserFilled,
        iconBg: '#F5F3FF',
        iconColor: '#5a4a72',
        buttonText: '进入模块',
        to: '/elderly',
      },
      {
        title: '家庭经济状况核对',
        subtitle: '经济核对 · 报告审核',
        description: '覆盖快速核对、报告上传月度审核与核对月报表',
        icon: Search,
        iconBg: '#FFFBEB',
        iconColor: '#96681c',
        buttonText: '进入模块',
        to: '/asset-verification',
      },
      {
        title: '救助档案管理',
        subtitle: '历史档案检索查阅',
        description: '现存低收入人口历史档案查询',
        icon: FolderOpened,
        iconBg: '#ECFDF5',
        iconColor: '#2e4738',
        buttonText: '进入模块',
        to: '/archives/query',
      },
      {
        title: '死亡情况说明',
        subtitle: '死亡申报 · 身份聚合 · 文书出具',
        description: '覆盖村内死亡申报、在库身份聚合、数据提醒与情况说明出具',
        icon: Warning,
        iconBg: '#F1F0FB',
        iconColor: '#6D28D9',
        buttonText: '进入模块',
        to: '/archives/death-statements',
      },
      {
        title: '后补追缴',
        subtitle: '停止人员追缴 · 手工录入',
        description: '覆盖低保、刚性支出、临时救助与高龄停止人员的追缴办理与台账',
        icon: Money,
        iconBg: '#FFF3E0',
        iconColor: '#7a5315',
        buttonText: '进入模块',
        to: '/recovery',
      },
      {
        title: '照片补传',
        subtitle: '按人检索 · 连续采集 · 推送打印',
        description:
          '跨低收入/高龄/临时救助检索人员已传照片数，勾选后连续采集证件照并可推送打印',
        icon: Camera,
        iconBg: '#ECFDF5',
        iconColor: '#2e4738',
        buttonText: '进入模块',
        to: '/photo-backfill',
      },
    ],
  },
  {
    title: '管理工具',
    cards: [
      {
        title: '数据中心',
        subtitle: '备份恢复 · 数据导入导出',
        description: '数据库管理、数据字典、标准配置、数据导入与区域数据维护',
        icon: DataBoard,
        iconBg: '#F8FAFC',
        iconColor: '#6d6354',
        buttonText: '进入模块',
        to: '/system/data-center',
      },
      {
        title: '用户与权限管理',
        subtitle: '账户 · 角色 · 权限配置',
        description: '系统用户、角色权限与经办机构信息维护（经办机构亦可从左侧菜单进入）',
        icon: User,
        iconBg: '#FDF2F8',
        iconColor: '#7f231b',
        buttonText: '进入模块',
        to: '/system/users',
      },
      {
        title: '值班管理',
        subtitle: '月度值班表 · 班务调整',
        description: '值班表轮转生成、请假/串班/代班调整、节假日维护与政务值班表导出',
        icon: Calendar,
        iconBg: '#ECFDF5',
        iconColor: '#2e4738',
        buttonText: '进入模块',
        to: '/duty',
      },
      {
        title: '历史补打中心',
        subtitle: '按人检索 · 批量补打',
        description:
          '跨域按人检索低保/临时救助/普惠高龄/资产核查/动态管理档案，支持留痕原样补打与按月批量打印',
        icon: Printer,
        iconBg: '#F0F9FF',
        iconColor: '#6d6354',
        buttonText: '进入模块',
        to: '/reprint',
      },
    ],
  },
  {
    title: '娱乐天地',
    cards: [
      {
        title: '彩票助手',
        subtitle: '机选 · 智能预测',
        description: '双色球/大乐透历史数据、号码统计分析与智能预测',
        icon: Trophy,
        iconBg: '#FFF7ED',
        iconColor: '#EA580C',
        buttonText: '进入模块',
        to: '/coming-soon/lottery',
      },
    ],
  },
  {
    title: '快捷链接',
    cards: [
      {
        title: '作者官网',
        subtitle: '软件更新 · 通告',
        description: '用于软件更新以及通告',
        icon: Link,
        iconBg: '#EBF5FF',
        iconColor: '#ab3227',
        buttonText: '访问网站',
        href: 'https://sua17.cn',
      },
      {
        title: '黑龙江民政官网',
        subtitle: '黑龙江省民政厅',
        description: '访问黑龙江省民政厅网上办事大厅',
        icon: Link,
        iconBg: '#ECFDF5',
        iconColor: '#2e4738',
        buttonText: '访问网站',
        href: 'https://zwfw.mzt.hlj.gov.cn/casserver/login',
      },
    ],
  },
]

function openCard(card: ModuleCard): void {
  if (card.href) {
    window.open(card.href, '_blank', 'noopener')
    return
  }
  if (card.to) void router.push(card.to)
}

function openWarning(banner: WarningBanner): void {
  void router.push(banner.to)
}
</script>

<template>
  <div class="home">
    <div class="home-main">
      <!-- 警告横幅区（渐退期 / 高龄待办 / 大学生毕业，计数为 0 时不显示） -->
      <div v-if="warnings.length" class="warnings">
        <button
          v-for="banner in warnings"
          :key="banner.key"
          class="warning-banner"
          type="button"
          @click="openWarning(banner)"
        >
          <el-icon :size="18" class="warning-icon"><component :is="banner.icon" /></el-icon>
          <span class="warning-text">{{ banner.text }}</span>
          <el-icon :size="16" class="warning-arrow"><ArrowRight /></el-icon>
        </button>
      </div>

      <div class="home-inner">
        <!-- 数据概览 -->
        <section class="stats-section" aria-label="数据概览">
          <div class="section-heading">
            <h2 class="section-title"><span class="seal-dash" aria-hidden="true" />数据概览</h2>
            <el-button
              class="stats-refresh"
              size="small"
              :icon="Refresh"
              :loading="loading"
              @click="loadDashboard"
            >
              刷新数据
            </el-button>
          </div>

          <div v-if="loadError" class="stats-error" role="alert">
            <span>数据加载失败：{{ loadError }}</span>
            <el-button link type="primary" @click="loadDashboard">重试</el-button>
          </div>

          <div class="stats-grid">
            <article
              v-for="stat in stats"
              :key="stat.label"
              class="stat-card"
              :class="`stat-card--${stat.gradient}`"
            >
              <div class="stat-head">
                <el-icon :size="18"><component :is="stat.icon" /></el-icon>
                <span>{{ stat.label }}</span>
              </div>
              <strong
                class="stat-value"
                :class="{ 'is-placeholder': stat.value === null, 'is-loading': statsLoading }"
              >{{ statText(stat.value) }}</strong>
              <p class="stat-caption">{{ stat.caption }}</p>
            </article>
          </div>
        </section>

        <!-- 模块直达区（一域一卡） -->
        <div class="module-groups">
          <section
            v-for="group in moduleGroups"
            :key="group.title"
            class="module-group"
            :aria-label="group.title"
          >
            <h2 class="section-title section-title--group">
              <span class="seal-dash" aria-hidden="true" />{{ group.title }}
            </h2>
            <div class="card-grid">
              <article v-for="card in group.cards" :key="card.title" class="module-card">
                <header class="module-head">
                  <span
                    class="module-icon"
                    :style="{ backgroundColor: card.iconBg, color: card.iconColor }"
                  >
                    <el-icon :size="24"><component :is="card.icon" /></el-icon>
                  </span>
                  <span class="module-heading">
                    <strong class="module-title">{{ card.title }}</strong>
                    <small class="module-subtitle">{{ card.subtitle }}</small>
                  </span>
                </header>
                <p class="module-desc">{{ card.description }}</p>
                <button class="module-button" type="button" @click="openCard(card)">
                  {{ card.buttonText }}
                </button>
              </article>
            </div>
          </section>
        </div>
      </div>
    </div>

    <!-- 看板娘立绘栏（桌面显示，底部对齐白卡） -->
    <aside class="mascot-col" aria-hidden="true">
      <div class="mascot-card">
        <img :src="mascotUrl" alt="" />
      </div>
    </aside>
  </div>
</template>

<style scoped>
.home {
  display: flex;
  align-items: flex-start;
  min-height: 100%;
}

.home-main {
  flex: 1;
  min-width: 0;
}

/* ── 警告横幅 ─────────────────────────────────────────────────── */
.warnings {
  display: flex;
  flex-direction: column;
}

.warning-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  width: calc(100% - 20px);
  margin: 5px 10px;
  padding: 10px 16px;
  border: 0;
  background-color: #f3e6cf;
  color: #7a5315;
  font-size: 18px;
  line-height: 1.5;
  text-align: left;
  cursor: pointer;
}

.warning-banner:hover {
  background-color: #efdfc1;
}

.warning-icon {
  flex: none;
}

.warning-text {
  flex: 1;
  min-width: 0;
}

.warning-arrow {
  flex: none;
  opacity: 0.6;
}

/* ── 内容骨架 ─────────────────────────────────────────────────── */
.home-inner {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 20px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 0 0 8px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 19px;
  font-weight: 700;
  line-height: 1.4;
  letter-spacing: 0.02em;
}

.section-title--group {
  margin: 12px 0 8px;
}

.seal-dash {
  flex: none;
  width: 14px;
  height: 2px;
  background-color: var(--nc-seal);
}

/* ── 数据概览卡 ───────────────────────────────────────────────── */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
}

.stats-section > .section-title {
  /* 旧版外层 VerticalStackLayout Spacing=20 + 标题 Margin 0,0,0,8 */
  margin-bottom: 28px;
}

.section-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 28px;
}

.section-heading .section-title {
  margin-bottom: 0;
}

.stats-refresh {
  flex: none;
}

.stats-error {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  margin: -16px 0 12px;
  padding: 8px 12px;
  border: 1px solid rgba(171, 50, 39, 0.35);
  border-left: 3px solid var(--nc-seal);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-size: 13px;
  line-height: 1.5;
}

.stat-value.is-placeholder {
  opacity: 0.55;
}

.stat-value.is-loading {
  animation: stat-pulse 1.1s ease-in-out infinite;
}

@keyframes stat-pulse {
  0%,
  100% {
    opacity: 0.35;
  }

  50% {
    opacity: 0.75;
  }
}

.stat-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  padding: 20px;
  border-radius: 2px;
  color: #ffffff;
}

.stat-card--blue {
  background: linear-gradient(135deg, #ab3227 0%, #7f231b 100%);
}

.stat-card--green {
  background: linear-gradient(135deg, #2e4738 0%, #22352a 100%);
}

.stat-card--orange {
  background: linear-gradient(135deg, #96681c 0%, #7a5315 100%);
}

.stat-card--purple {
  background: linear-gradient(135deg, #4c4237 0%, #322b24 100%);
}

.stat-head {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  font-size: 18px;
  line-height: 1.4;
  opacity: 0.9;
}

.stat-head span {
  min-width: 0;
  line-height: 1.35;
}

.stat-value {
  font-size: 38px;
  font-weight: 700;
  line-height: 1.15;
  letter-spacing: 0.01em;
}

.stat-caption {
  font-size: 16px;
  line-height: 1.5;
  opacity: 0.8;
}

/* ── 模块卡片 ─────────────────────────────────────────────────── */
.module-groups {
  display: flex;
  flex-direction: column;
}

.card-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.module-card {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-width: 0;
  padding: 20px;
  border: 1px solid var(--nc-line);
  border-radius: 2px;
  background-color: #f8f3e9;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition),
    transform var(--nc-transition);
}

.module-card:hover {
  border-color: var(--nc-line-strong);
  box-shadow: 0 6px 16px rgba(23, 19, 16, 0.12);
  transform: translateY(-1px);
}

.module-head {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.module-icon {
  display: grid;
  flex: none;
  place-items: center;
  width: 44px;
  height: 44px;
  border-radius: 12px;
}

.module-heading {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.module-title {
  color: var(--nc-ink);
  font-size: 16px;
  font-weight: 700;
  line-height: 1.4;
}

.module-subtitle {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: 14px;
  line-height: 1.4;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.module-desc {
  display: -webkit-box;
  height: 42px;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: 14px;
  line-height: 21px;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.module-button {
  height: 44px;
  margin-top: auto;
  border: 0;
  border-radius: 2px;
  background-color: var(--nc-seal);
  color: #ffffff;
  font-size: 14px;
  font-weight: 700;
  cursor: pointer;
  transition: background-color var(--nc-transition);
}

.module-button:hover {
  background-color: var(--nc-seal-hover);
}

/* ── 看板娘立绘栏 ─────────────────────────────────────────────── */
.mascot-col {
  position: sticky;
  top: 0;
  display: flex;
  flex: none;
  align-items: flex-end;
  align-self: flex-start;
  width: 200px;
  height: calc(100vh - 120px);
  padding: 0 16px 20px 0;
}

.mascot-card {
  width: 100%;
  padding: 12px 10px 10px;
  border-radius: 16px;
  background: linear-gradient(180deg, #ffffff 0%, #e7f0fe 100%);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

.mascot-card img {
  display: block;
  width: 100%;
  height: auto;
}

/* ── 窄桌面 / 平板 ───────────────────────────────────────────── */
@media (max-width: 1199px) {
  .stats-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 1023px) {
  .card-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .stats-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .mascot-col {
    display: none;
  }

  .warning-banner {
    font-size: 16px;
  }
}

@media (max-width: 767px) {
  .home-inner {
    gap: 16px;
    padding: 16px 14px;
  }

  .card-grid {
    grid-template-columns: 1fr;
  }

  .stat-card {
    padding: 16px;
  }

  .stat-head {
    font-size: 16px;
  }

  .stat-value {
    font-size: 32px;
  }

  .stat-caption {
    font-size: 14px;
  }

  .module-card {
    padding: 16px;
  }
}

@media (max-width: 420px) {
  .stats-grid {
    grid-template-columns: 1fr;
  }
}
</style>
