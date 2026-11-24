<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { Component } from 'vue'
import { useRouter } from 'vue-router'
import {
  ArrowLeft,
  ArrowRight,
  Calendar,
  DataBoard,
  DocumentAdd,
  Notebook,
  Reading,
  Refresh,
  Search,
  Star,
  UserFilled,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { getSocialAssistanceSummary } from '@/api/socialAssistance'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import type { SocialAssistanceModuleSummary } from '@/types/socialAssistance'

interface StatCapsule {
  label: string
  value: number | null
  tone: 'ink' | 'seal' | 'pine' | 'amber'
}

interface FeatureCard {
  title: string
  description: string
  icon: Component
  to: string
}

const router = useRouter()

const summary = ref<SocialAssistanceModuleSummary | null>(null)
const loading = ref(false)
const loadError = ref('')
let loadTicket = 0

const stats = computed<StatCapsule[]>(() => [
  { label: '在享保障对象（户）', value: summary.value?.activeCount ?? null, tone: 'ink' },
  { label: '本月新增（户）', value: summary.value?.monthlyNewAdditions ?? null, tone: 'seal' },
  { label: '本月退出（户）', value: summary.value?.monthlyExits ?? null, tone: 'pine' },
  { label: '渐退期临期（户）', value: summary.value?.gracePeriodExpiring ?? null, tone: 'amber' },
])

/* 文案与旧版 Pages/SocialAssistance/SocialAssistanceHomePage.xaml 一致。 */
const featureCards: FeatureCard[] = [
  {
    title: '业务申请',
    description: '五步工作流录入救助申请',
    icon: DocumentAdd,
    to: '/applications',
  },
  {
    title: '保障对象动态管理',
    description: '户主变更、成员增减、死亡减员',
    icon: UserFilled,
    to: '/coming-soon/change-management',
  },
  {
    title: '临时救助',
    description: '急难型与支出型临时救助办理',
    icon: Star,
    to: '/coming-soon/temp-relief',
  },
  {
    title: '救助月报表',
    description: '按月生成、导出救助统计报表',
    icon: DataBoard,
    to: '/coming-soon/social-report',
  },
  {
    title: '渐退期管理',
    description: '进行中与已到期统一管理',
    icon: Calendar,
    to: '/social-assistance/grace-period',
  },
  {
    title: '大学生管理',
    description: '18-23周岁家庭成员大学生档案关联与毕业管理',
    icon: Reading,
    to: '/coming-soon/college-student',
  },
  {
    title: '近亲属备案',
    description: '工作人员近亲属享受救助备案名册维护',
    icon: Notebook,
    to: '/coming-soon/near-relative',
  },
  {
    title: '快速核验·模拟测算',
    description: '身份证一键核验土地/补贴/保障身份，收入与标准试算',
    icon: Search,
    to: '/coming-soon/quick-verify',
  },
]

function statText(value: number | null): string {
  if (value === null) return '—'
  return value.toLocaleString('zh-CN')
}

async function load(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await getSocialAssistanceSummary()
    if (ticket !== loadTicket) return
    summary.value = response
    if (
      response.activeCount === null &&
      response.monthlyNewAdditions === null &&
      response.monthlyExits === null &&
      response.gracePeriodExpiring === null
    ) {
      loadError.value = '数据加载失败'
    }
  } catch (error) {
    if (ticket !== loadTicket) return
    summary.value = null
    loadError.value = getErrorMessage(error, '模块统计加载失败')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

function openCard(card: FeatureCard): void {
  void router.push(card.to)
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page social-home">
    <NcPageHeader
      title="低收入人口救助帮扶"
      description="低收入人口认定审核确认 · 保障对象动态管理 · 临时救助"
      kicker="救助业务"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/')">返回工作台</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新数据</el-button>
      </template>
    </NcPageHeader>

    <div class="module-stats">
      <NcStat
        v-for="item in stats"
        :key="item.label"
        :label="item.label"
        :value="statText(item.value)"
        :tone="item.tone"
        compact
      />
    </div>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="`数据加载失败：${loadError}`"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="load">重试</el-button>
    </el-alert>

    <section class="feature-section" aria-label="业务办理">
      <h2 class="section-title"><span class="seal-dash" aria-hidden="true" />业务办理</h2>

      <div class="feature-grid">
        <article
          v-for="card in featureCards"
          :key="card.title"
          class="feature-card"
          role="button"
          tabindex="0"
          @click="openCard(card)"
          @keydown.enter="openCard(card)"
        >
          <header class="feature-head">
            <span class="feature-icon">
              <el-icon :size="22"><component :is="card.icon" /></el-icon>
            </span>
            <span class="feature-heading">
              <strong class="feature-title">{{ card.title }}</strong>
              <small class="feature-desc">{{ card.description }}</small>
            </span>
          </header>
          <span class="feature-foot">
            进入办理
            <el-icon :size="14"><ArrowRight /></el-icon>
          </span>
        </article>
      </div>
    </section>
  </div>
</template>

<style scoped>
.social-home {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.module-stats {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.load-error {
  margin: 0;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 4px 0 12px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 19px;
  font-weight: 700;
  line-height: 1.4;
  letter-spacing: 0.02em;
}

.seal-dash {
  flex: none;
  width: 14px;
  height: 2px;
  background-color: var(--nc-seal);
}

.feature-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
}

.feature-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  padding: 18px 20px;
  border: 1px solid var(--nc-line);
  border-radius: 2px;
  background-color: #f8f3e9;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition),
    transform var(--nc-transition);
}

.feature-card:hover {
  border-color: var(--nc-line-strong);
  box-shadow: 0 6px 16px rgba(23, 19, 16, 0.12);
  transform: translateY(-1px);
}

.feature-card:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.feature-head {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.feature-icon {
  display: grid;
  flex: none;
  place-items: center;
  width: 42px;
  height: 42px;
  border-radius: 12px;
  background-color: #ebf5ff;
  color: var(--nc-seal);
}

.feature-heading {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.feature-title {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 16px;
  font-weight: 700;
  line-height: 1.4;
  letter-spacing: 0.02em;
}

.feature-desc {
  color: var(--nc-muted-2);
  font-size: 13px;
  line-height: 1.55;
}

.feature-foot {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 5px;
  color: var(--nc-seal);
  font-size: 13px;
  font-weight: 600;
}

@media (max-width: 1023px) {
  .module-stats {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .social-home {
    gap: 14px;
  }

  .feature-grid {
    grid-template-columns: 1fr;
  }

  .feature-card {
    padding: 15px 16px;
  }

  .module-stats {
    gap: 8px;
  }
}

@media (max-width: 420px) {
  .module-stats {
    grid-template-columns: 1fr;
  }
}
</style>
