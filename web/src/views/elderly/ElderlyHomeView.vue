<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { Component } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowRight, Calendar, Document, Plus, Refresh, SwitchButton, Warning } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { getElderlySummary } from '@/api/elderly'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type { ElderlySummary } from '@/types/elderly'
import { formatCount } from './display'

interface StatCapsule {
  key: string
  label: string
  value: string
  to: string
}

interface FeatureCard {
  key: string
  title: string
  description: string
  icon: Component
  to: string
  query?: Record<string, string>
}

const router = useRouter()

const summary = ref<ElderlySummary | null>(null)
const loading = ref(false)
const loadError = ref('')

let requestTicket = 0

const stats = computed<StatCapsule[]>(() => [
  {
    key: 'active',
    label: '在享领取（人）',
    value: formatCount(summary.value?.activeCount),
    to: '',
  },
  {
    key: 'monthly-new',
    label: '本月新增登记（人）',
    value: formatCount(summary.value?.monthlyNew),
    to: '',
  },
  {
    key: 'pending-new',
    label: '待新增（人）',
    value: formatCount(summary.value?.pendingNew),
    to: '/elderly/pending',
  },
  {
    key: 'pending-transfer',
    label: '需停旧增新（人）',
    value: formatCount(summary.value?.pendingTransfer),
    to: '/elderly/pending',
  },
])

const featureCards: FeatureCard[] = [
  {
    key: 'application',
    title: '津贴申请登记',
    description: '受理登记、审核确认高龄津贴申请',
    icon: Plus,
    to: '/elderly/applications',
  },
  {
    key: 'stop',
    title: '停发办理',
    description: '死亡、户籍迁出等情形停发处理',
    icon: SwitchButton,
    to: '/elderly/stops',
  },
  {
    key: 'monthly',
    title: '高龄津贴月报表',
    description: '按月生成、导出发放统计报表',
    icon: Document,
    to: '/coming-soon/elderly-report',
  },
  {
    key: 'review',
    title: '高龄津贴复核',
    description: '身份/年龄重评，停旧增新并打印三表',
    icon: Warning,
    to: '/elderly/reviews',
  },
  {
    key: 'pending',
    title: '下月待办',
    description: '已完结档案中满80周岁待办理人员',
    icon: Calendar,
    to: '/elderly/pending',
  },
]

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await getElderlySummary()
    if (ticket !== requestTicket) return
    summary.value = response
  } catch (error) {
    if (ticket !== requestTicket) return
    summary.value = null
    loadError.value = getErrorMessage(error, '高龄津贴概览加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function openStat(stat: StatCapsule): void {
  if (!stat.to) return
  void router.push(stat.to)
}

function openCard(card: FeatureCard): void {
  void router.push({ path: card.to, query: card.query })
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page elderly-home">
    <NcPageHeader
      title="高龄津贴发放"
      description="80 周岁以上老年人津贴申请登记 · 停发管理 · 类别复核 · 月度报表"
      kicker="普惠高龄 · 发放管理"
    >
      <div class="stat-grid" aria-label="高龄津贴概览">
        <component
          v-for="stat in stats"
          :key="stat.key"
          :is="stat.to ? 'button' : 'div'"
          class="stat-capsule"
          :class="{ 'is-link': Boolean(stat.to) }"
          @click="openStat(stat)"
        >
          <strong class="stat-value">{{ stat.value }}</strong>
          <span class="stat-label">{{ stat.label }}</span>
          <el-icon v-if="stat.to" class="stat-arrow"><ArrowRight /></el-icon>
        </component>
      </div>

      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="load()">重新加载</el-button>
    </el-alert>

    <section class="business-section" aria-label="业务办理">
      <h2 class="section-title"><span class="seal-dash" aria-hidden="true" />业务办理</h2>

      <div class="feature-grid">
        <button
          v-for="card in featureCards"
          :key="card.key"
          type="button"
          class="feature-card"
          :class="`feature-card--${card.key}`"
          @click="openCard(card)"
        >
          <span class="feature-icon">
            <el-icon :size="22"><component :is="card.icon" /></el-icon>
          </span>
          <span class="feature-copy">
            <strong>{{ card.title }}</strong>
            <span>{{ card.description }}</span>
          </span>
          <el-icon class="feature-arrow"><ArrowRight /></el-icon>
        </button>
      </div>
    </section>
  </div>
</template>

<style scoped>
.elderly-home {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

/* ── 统计胶囊 ─────────────────────────────────────────────────── */
.stat-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
  margin-top: 16px;
}

.stat-capsule {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  padding: 11px 14px 12px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  color: inherit;
  font-family: inherit;
  text-align: left;
}

.stat-capsule.is-link {
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.stat-capsule.is-link:hover {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-sm);
}

.stat-capsule.is-link:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.stat-value {
  overflow: hidden;
  color: var(--nc-seal);
  font-family: var(--nc-font-serif);
  font-size: 24px;
  font-weight: 700;
  line-height: 1.2;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.stat-label {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.04em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.stat-arrow {
  position: absolute;
  top: 11px;
  right: 10px;
  color: var(--nc-muted);
  font-size: 12px;
}

/* ── 业务办理 ─────────────────────────────────────────────────── */
.business-section {
  margin-top: 4px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 0 0 12px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 18px;
  font-weight: 700;
  letter-spacing: 0.03em;
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

/* 与旧版 2 列布局一致：月报表占第 2 行左格，复核/下月待办占第 3 行。 */
.feature-card--monthly {
  grid-column: 1;
  grid-row: 2;
}

.feature-card--review {
  grid-column: 1;
  grid-row: 3;
}

.feature-card--pending {
  grid-column: 2;
  grid-row: 3;
}

.feature-card {
  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 0;
  padding: 18px 20px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
  color: inherit;
  font-family: inherit;
  text-align: left;
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

.feature-icon {
  display: grid;
  flex: none;
  place-items: center;
  width: 42px;
  height: 42px;
  border-radius: 10px;
  background-color: var(--nc-paper-3);
  color: var(--nc-seal);
}

.feature-copy {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.feature-copy strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 16px;
  font-weight: 700;
  letter-spacing: 0.03em;
}

.feature-copy span {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.5;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.feature-arrow {
  flex: none;
  color: var(--nc-muted);
}

.load-error {
  margin: 0;
}

/* ── 响应式 ───────────────────────────────────────────────────── */
@media (max-width: 1023px) {
  .stat-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .stat-grid {
    gap: 8px;
  }

  .feature-grid {
    grid-template-columns: minmax(0, 1fr);
    gap: 10px;
  }

  .feature-card--monthly,
  .feature-card--review,
  .feature-card--pending {
    grid-column: auto;
    grid-row: auto;
  }

  .feature-card {
    padding: 15px 16px;
  }
}

@media (max-width: 420px) {
  .stat-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
