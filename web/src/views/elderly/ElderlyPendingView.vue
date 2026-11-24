<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowRight, Refresh } from '@element-plus/icons-vue'
import { getElderlyPending } from '@/api/elderly'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { ElderlyPendingItem } from '@/types/elderly'
import ElderlyChip from './ElderlyChip.vue'
import { formatAge, formatCount, formatDate, pendingTypeTone } from './display'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const items = ref<ElderlyPendingItem[]>([])
const pendingNew = ref<number | null>(null)
const pendingTransfer = ref<number | null>(null)
const activeTab = ref<'New' | 'Transfer'>('New')
const loading = ref(false)
const loadError = ref('')

let requestTicket = 0

const newItems = computed(() => items.value.filter((item) => item.type !== 'Transfer'))
const transferItems = computed(() => items.value.filter((item) => item.type === 'Transfer'))
const visibleItems = computed(() =>
  activeTab.value === 'Transfer' ? transferItems.value : newItems.value,
)

const emptyDescription = computed(() =>
  activeTab.value === 'Transfer' ? '暂无需停旧增新的老人' : '暂无待新增老人',
)

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取下月待办…'
  return activeTab.value === 'Transfer'
    ? `需停旧增新 ${transferItems.value.length} 人`
    : `待新增 ${newItems.value.length} 人`
})

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await getElderlyPending()
    if (ticket !== requestTicket) return
    items.value = response.items ?? []
    pendingNew.value = response.pendingNew ?? 0
    pendingTransfer.value = response.pendingTransfer ?? 0
  } catch (error) {
    if (ticket !== requestTicket) return
    items.value = []
    pendingNew.value = null
    pendingTransfer.value = null
    loadError.value = getErrorMessage(error, '下月待办加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function selectTab(tab: 'New' | 'Transfer'): void {
  activeTab.value = tab
}

function openApplication(item: ElderlyPendingItem): void {
  if (item.elderlyApplicationId <= 0) return
  void router.push({
    name: 'elderly-application-detail',
    params: { id: item.elderlyApplicationId },
  })
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page elderly-pending-page">
    <NcPageHeader
      title="高龄津贴下月待办"
      description="已完结档案中年满 80 周岁的待办老人名单，请及时办理新增登记或停旧增新。"
      kicker="普惠高龄 · 下月待办"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <button
        type="button"
        class="stat-capsule"
        :class="{ 'is-active': activeTab === 'New' }"
        @click="selectTab('New')"
      >
        <strong class="stat-value">{{ formatCount(pendingNew) }}</strong>
        <span class="stat-label">待新增（人）</span>
      </button>
      <button
        type="button"
        class="stat-capsule stat-capsule--transfer"
        :class="{ 'is-active': activeTab === 'Transfer' }"
        @click="selectTab('Transfer')"
      >
        <strong class="stat-value">{{ formatCount(pendingTransfer) }}</strong>
        <span class="stat-label">需停旧增新（人）</span>
      </button>
    </div>

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

    <NcCard title="待办名单" :subtitle="listSubtitle" padding="none" class="list-card">
      <div class="tab-bar" role="tablist" aria-label="待办类型">
        <button
          type="button"
          class="tab-button"
          :class="{ 'is-active': activeTab === 'New' }"
          role="tab"
          :aria-selected="activeTab === 'New'"
          @click="selectTab('New')"
        >
          待新增
          <span class="tab-count">{{ newItems.length }}</span>
        </button>
        <button
          type="button"
          class="tab-button"
          :class="{ 'is-active': activeTab === 'Transfer' }"
          role="tab"
          :aria-selected="activeTab === 'Transfer'"
          @click="selectTab('Transfer')"
        >
          需停旧增新
          <span class="tab-count">{{ transferItems.length }}</span>
        </button>
      </div>

      <div v-loading="loading" class="list-area" :class="{ 'is-cards': isNarrow }">
        <el-skeleton v-if="loading && items.length === 0" :rows="5" animated class="list-skeleton" />

        <template v-else-if="visibleItems.length > 0">
          <el-table v-if="!isNarrow" :data="visibleItems" class="pending-table">
            <el-table-column label="姓名" min-width="96">
              <template #default="{ row }">
                <div class="person-cell">
                  <strong>{{ row.name }}</strong>
                  <span>{{ row.gender || '—' }} · {{ formatAge(row.age) }}</span>
                </div>
              </template>
            </el-table-column>

            <el-table-column label="身份证（脱敏）" min-width="150">
              <template #default="{ row }">
                <span class="mono">{{ row.idCardMasked || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="成员身份" width="110">
              <template #default="{ row }">{{ row.memberRole || '—' }}</template>
            </el-table-column>

            <el-table-column label="待办类型" width="110">
              <template #default="{ row }">
                <ElderlyChip :text="row.typeText" :tone="pendingTypeTone(row.type)" />
              </template>
            </el-table-column>

            <el-table-column label="当前类别" min-width="180">
              <template #default="{ row }">{{ row.currentCategoryText || '—' }}</template>
            </el-table-column>

            <el-table-column label="状态标注" min-width="150">
              <template #default="{ row }">{{ row.statusLabel || '—' }}</template>
            </el-table-column>

            <el-table-column label="建议办理" min-width="160">
              <template #default="{ row }">
                <span class="action-text">{{ row.suggestedAction || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="操作" width="112" align="right">
              <template #default="{ row }">
                <el-button
                  v-if="row.elderlyApplicationId > 0"
                  link
                  type="primary"
                  @click="openApplication(row as ElderlyPendingItem)"
                >
                  在享档案
                </el-button>
                <span v-else class="muted">下一批办理</span>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="pending-cards">
            <article v-for="item in visibleItems" :key="`${item.idCardMasked}-${item.type}`" class="pending-card">
              <header class="card-head">
                <strong>{{ item.name }}</strong>
                <ElderlyChip :text="item.typeText" :tone="pendingTypeTone(item.type)" />
              </header>

              <div class="card-sub">
                <span class="mono">{{ item.idCardMasked || '—' }}</span>
                <span>{{ item.gender || '—' }} · {{ formatAge(item.age) }}</span>
              </div>

              <div class="card-meta-grid">
                <div>
                  <span>成员身份</span>
                  <strong>{{ item.memberRole || '—' }}</strong>
                </div>
                <div>
                  <span>当前类别</span>
                  <strong>{{ item.currentCategoryText || '—' }}</strong>
                </div>
                <div>
                  <span>状态标注</span>
                  <strong>{{ item.statusLabel || '—' }}</strong>
                </div>
                <div>
                  <span>出生日期</span>
                  <strong>{{ formatDate(item.birthDate) }}</strong>
                </div>
              </div>

              <p class="card-action">建议办理：{{ item.suggestedAction || '—' }}</p>

              <button
                v-if="item.elderlyApplicationId > 0"
                type="button"
                class="card-foot"
                @click="openApplication(item)"
              >
                查看在享档案
                <el-icon><ArrowRight /></el-icon>
              </button>
              <p v-else class="card-foot card-foot--muted">写流程下一批开通</p>
            </article>
          </div>
        </template>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="emptyDescription" />
        </div>
      </div>
    </NcCard>
  </div>
</template>

<style scoped>
.elderly-pending-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.summary {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.stat-capsule {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  padding: 12px 15px 13px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-pine);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  color: inherit;
  font-family: inherit;
  text-align: left;
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.stat-capsule--transfer {
  border-left-color: var(--nc-seal);
}

.stat-capsule:hover,
.stat-capsule.is-active {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-sm);
}

.stat-value {
  color: var(--nc-pine);
  font-family: var(--nc-font-serif);
  font-size: 26px;
  font-weight: 700;
  line-height: 1.2;
}

.stat-capsule--transfer .stat-value {
  color: var(--nc-seal);
}

.stat-label {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.05em;
}

.load-error {
  margin: 0;
}

.list-card {
  overflow: hidden;
}

.tab-bar {
  display: flex;
  gap: 6px;
  padding: 10px 14px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.tab-button {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  padding: 7px 14px;
  border: 1px solid transparent;
  border-radius: var(--nc-radius-xs);
  background: none;
  color: var(--nc-muted-2);
  font-family: inherit;
  font-size: var(--nc-text-sm);
  font-weight: 600;
  cursor: pointer;
}

.tab-button:hover {
  background-color: var(--nc-paper-3);
}

.tab-button.is-active {
  border-color: var(--nc-line);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink);
}

.tab-count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 20px;
  height: 18px;
  padding: 0 6px;
  border-radius: 999px;
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-size: 11px;
}

.list-area {
  min-height: 200px;
}

.list-area.is-cards {
  padding: 10px;
}

.list-skeleton {
  padding: 22px;
}

.pending-table {
  width: 100%;
}

.person-cell {
  display: flex;
  flex-direction: column;
  line-height: 1.45;
}

.person-cell strong {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
}

.person-cell span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.mono {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.action-text {
  color: var(--nc-pine);
  font-size: var(--nc-text-sm);
}

.muted {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.pending-cards {
  display: grid;
  gap: 10px;
}

.pending-card {
  padding: 14px 15px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.card-head strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.card-sub {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 10px;
  margin-top: 6px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-meta-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 14px;
  margin-top: 11px;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
}

.card-meta-grid div {
  display: flex;
  flex-direction: column;
  min-width: 0;
  line-height: 1.45;
}

.card-meta-grid span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.card-meta-grid strong {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-action {
  margin-top: 10px;
  color: var(--nc-pine);
  font-size: var(--nc-text-sm);
}

.card-foot {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 5px;
  width: 100%;
  margin-top: 8px;
  padding: 0;
  border: none;
  background: none;
  color: var(--nc-seal);
  font-family: inherit;
  font-size: var(--nc-text-sm);
  font-weight: 600;
  cursor: pointer;
}

.card-foot--muted {
  justify-content: flex-start;
  color: var(--nc-muted);
  font-weight: 400;
  cursor: default;
}

.empty-wrap {
  padding: 10px 8px;
}

@media (max-width: 767px) {
  .summary {
    gap: 8px;
  }

  .stat-capsule {
    padding: 10px 12px 11px;
  }

  .tab-bar {
    padding: 8px 10px;
  }
}
</style>
