<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { Component } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowLeft, ArrowRight, Document, Plus, Refresh, Search, Tickets } from '@element-plus/icons-vue'
import { getAssetVerificationSummary } from '@/api/assetVerification'
import { getErrorMessage } from '@/api/http'
import type { AssetVerificationSummary } from '@/types/assetVerification'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'

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

const summary = ref<AssetVerificationSummary | null>(null)
const loading = ref(false)
const loadError = ref('')
let loadTicket = 0

const stats = computed<StatCapsule[]>(() => [
  { label: '待上传（户）', value: summary.value?.pendingReportCount ?? null, tone: 'amber' },
  { label: '本月已完成（户）', value: summary.value?.monthlyCompleted ?? null, tone: 'pine' },
  { label: '本月新增（户）', value: summary.value?.monthlyNew ?? null, tone: 'seal' },
  { label: '全年累计（户）', value: summary.value?.yearTotal ?? null, tone: 'ink' },
])

/* 文案与旧版 Pages/AssetVerification/AssetVerificationHomePage.xaml 一致。 */
const featureCards: FeatureCard[] = [
  {
    title: '发起核查',
    description: '录入申请人与授权人，提交快速经济状况核查',
    icon: Plus,
    to: '/asset-verification/new',
  },
  {
    title: '快速经济核对',
    description: '发起单户家庭经济状况核对申请',
    icon: Search,
    to: '/asset-verification/tasks',
  },
  {
    title: '月度审核',
    description: '核对报告上传、复核与批量处理',
    icon: Document,
    to: '/asset-verification/monthly',
  },
  {
    title: '核对月报表',
    description: '按月生成、导出核对统计报表',
    icon: Tickets,
    to: '/coming-soon/asset-report',
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
    const response = await getAssetVerificationSummary()
    if (ticket !== loadTicket) return
    summary.value = response
    if (
      response.pendingReportCount === null &&
      response.monthlyCompleted === null &&
      response.monthlyNew === null &&
      response.yearTotal === null
    ) {
      loadError.value = '数据加载失败'
    }
  } catch (error) {
    if (ticket !== loadTicket) return
    summary.value = null
    loadError.value = getErrorMessage(error, '核对统计加载失败')
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
  <div class="nc-page asset-home">
    <NcPageHeader
      title="家庭经济状况核对"
      description="居民家庭经济状况核对 · 授权报告上传与月度审核"
      kicker="救助业务 / 经济状况核对"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/')">返回工作台</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新统计</el-button>
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
      <p class="section-note">
        快速核对与月度审核当前开放只读查询，报告上传与审核写入将在下一批开放。
      </p>

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
            进入功能
            <el-icon :size="14"><ArrowRight /></el-icon>
          </span>
        </article>
      </div>
    </section>
  </div>
</template>

<style scoped>
.asset-home {
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
  margin: 4px 0 6px;
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

.section-note {
  margin-bottom: 12px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
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
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition),
    transform var(--nc-transition);
}

.feature-card:hover {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-sm);
  transform: translateY(-1px);
}

.feature-card:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.feature-head {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  min-width: 0;
}

.feature-icon {
  display: grid;
  flex: none;
  place-items: center;
  width: 42px;
  height: 42px;
  border: 1px solid rgba(150, 104, 28, 0.24);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.feature-heading {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.feature-title {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.feature-desc {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.feature-foot {
  display: flex;
  align-items: center;
  gap: 5px;
  margin-top: auto;
  padding-top: 4px;
  color: var(--nc-seal);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

@media (max-width: 960px) {
  .feature-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 767px) {
  .asset-home {
    gap: 16px;
  }

  .module-stats {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .feature-card {
    padding: 16px 15px;
  }
}
</style>
