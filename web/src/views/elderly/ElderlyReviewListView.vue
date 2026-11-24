<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Refresh, Search } from '@element-plus/icons-vue'
import { listElderlyReviews } from '@/api/elderly'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { ElderlyReviewListItem } from '@/types/elderly'
import ElderlyChip from './ElderlyChip.vue'
import {
  formatDateTime,
  formatMonth,
  formatShortDateTime,
  reviewResultTone,
  reviewStatusTone,
} from './display'

const isNarrow = useMediaQuery('(max-width: 960px)')

const STATUS_TABS = [
  { label: '全部', value: '' },
  { label: '待复核', value: 'Pending' },
  { label: '已完成', value: 'Completed' },
]

const keyword = ref('')
const status = ref('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<ElderlyReviewListItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')

let requestTicket = 0

const hasFilters = computed(() => keyword.value.trim().length > 0 || status.value !== '')
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取复核记录…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `共 ${totalCount.value} 条复核记录${suffix}`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的复核记录' : '暂无高龄津贴复核记录',
)

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listElderlyReviews({
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      status: status.value || undefined,
    })
    if (ticket !== requestTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    pageSize.value = response.pageSize || pageSize.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0
  } catch (error) {
    if (ticket !== requestTicket) return
    items.value = []
    totalCount.value = 0
    totalPages.value = 0
    loadError.value = getErrorMessage(error, '复核列表加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function handleSearch(): void {
  page.value = 1
  void load()
}

function handleStatusChange(): void {
  page.value = 1
  void load()
}

function handleReset(): void {
  keyword.value = ''
  status.value = ''
  page.value = 1
  void load()
}

function handlePageChange(value: number): void {
  page.value = value
  void load()
}

function handleSizeChange(value: number): void {
  pageSize.value = value
  page.value = 1
  void load()
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page elderly-review-page">
    <NcPageHeader
      title="高龄津贴复核"
      description="身份 + 年龄重评 · 停旧增新 · 停止/新增/调整三表。当前仅提供复核记录只读查询。"
      kicker="普惠高龄 · 类别复核"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="复核记录" :value="totalCount" tone="ink" compact />
      <NcStat label="本页记录" :value="items.length" tone="pine" compact />
      <NcStat label="当前页" :value="`${page} / ${displayTotalPages}`" tone="seal" compact />
    </div>

    <NcCard padding="none" class="filter-card">
      <div class="status-tabs" role="tablist" aria-label="按复核状态筛选">
        <el-radio-group v-model="status" @change="handleStatusChange">
          <el-radio-button
            v-for="tab in STATUS_TABS"
            :key="tab.value || 'all'"
            :value="tab.value"
          >
            {{ tab.label }}
          </el-radio-button>
        </el-radio-group>
      </div>

      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索姓名 / 身份证号 / 复核编号"
          @clear="handleSearch"
        />
        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            查询
          </el-button>
          <el-button v-if="hasFilters" text native-type="button" @click="handleReset">
            重置
          </el-button>
        </div>
      </form>
    </NcCard>

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

    <NcCard title="复核记录" :subtitle="listSubtitle" padding="none" class="list-card">
      <div
        v-loading="loading && items.length > 0"
        class="list-area"
        :class="{ 'is-cards': isNarrow }"
      >
        <el-skeleton v-if="loading && items.length === 0" :rows="5" animated class="list-skeleton" />

        <template v-else-if="items.length > 0">
          <el-table v-if="!isNarrow" :data="items" row-key="id" class="review-table">
            <el-table-column label="复核编号" min-width="136">
              <template #default="{ row }">
                <span class="mono">{{ row.reviewNo || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="姓名" min-width="90">
              <template #default="{ row }">
                <strong class="person-name">{{ row.name }}</strong>
              </template>
            </el-table-column>

            <el-table-column label="身份证（脱敏）" min-width="150">
              <template #default="{ row }">
                <span class="mono">{{ row.idCardMasked || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="触发来源" min-width="140">
              <template #default="{ row }">
                <span :title="row.triggerReason">{{ row.triggerText || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="类别变化（旧 → 新）" min-width="220">
              <template #default="{ row }">
                <span class="category-change" :title="row.categoryChangeText">
                  {{ row.categoryChangeText || '—' }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="复核结果" width="104">
              <template #default="{ row }">
                <ElderlyChip
                  v-if="row.resultText"
                  :text="row.resultText"
                  :tone="reviewResultTone(row.reviewResult)"
                />
                <span v-else class="muted">—</span>
              </template>
            </el-table-column>

            <el-table-column label="状态" width="92">
              <template #default="{ row }">
                <ElderlyChip :text="row.statusText" :tone="reviewStatusTone(row.status)" />
              </template>
            </el-table-column>

            <el-table-column label="生效 / 计发" width="120">
              <template #default="{ row }">
                <div class="month-cell">
                  <span>{{ formatMonth(row.effectiveMonth || row.issueStartMonth) }}</span>
                </div>
              </template>
            </el-table-column>

            <el-table-column label="创建时间" width="104">
              <template #default="{ row }">{{ formatShortDateTime(row.createdAt) }}</template>
            </el-table-column>

            <el-table-column label="复核时间" width="104">
              <template #default="{ row }">{{ formatShortDateTime(row.reviewedAt) }}</template>
            </el-table-column>
          </el-table>

          <div v-else class="review-cards">
            <article v-for="row in items" :key="row.id" class="review-card">
              <header class="card-head">
                <span class="mono">{{ row.reviewNo || '—' }}</span>
                <ElderlyChip :text="row.statusText" :tone="reviewStatusTone(row.status)" />
              </header>

              <div class="card-person">
                <strong>{{ row.name }}</strong>
                <span class="mono">{{ row.idCardMasked || '—' }}</span>
              </div>

              <div class="card-meta-grid">
                <div>
                  <span>触发来源</span>
                  <strong>{{ row.triggerText || '—' }}</strong>
                </div>
                <div>
                  <span>复核结果</span>
                  <strong>{{ row.resultText || '—' }}</strong>
                </div>
                <div class="span-2">
                  <span>类别变化</span>
                  <strong>{{ row.categoryChangeText || '—' }}</strong>
                </div>
                <div>
                  <span>生效 / 计发月</span>
                  <strong>{{ formatMonth(row.effectiveMonth || row.issueStartMonth) }}</strong>
                </div>
                <div>
                  <span>复核时间</span>
                  <strong>{{ formatDateTime(row.reviewedAt) }}</strong>
                </div>
              </div>
            </article>
          </div>
        </template>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="emptyDescription">
            <el-button v-if="hasFilters" :icon="Refresh" @click="handleReset">清除筛选</el-button>
          </el-empty>
        </div>
      </div>

      <template #footer>
        <div v-if="items.length > 0" class="pagination">
          <span class="pagination-note">
            第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 条
          </span>
          <el-pagination
            :current-page="page"
            :page-size="pageSize"
            :page-sizes="[10, 20, 50]"
            :total="totalCount"
            :layout="paginationLayout"
            :pager-count="isNarrow ? 5 : 7"
            :small="isNarrow"
            background
            @current-change="handlePageChange"
            @size-change="handleSizeChange"
          />
        </div>
      </template>
    </NcCard>
  </div>
</template>

<style scoped>
.elderly-review-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.filter-card {
  overflow: hidden;
}

.status-tabs {
  padding: 12px 16px 0;
  border-bottom: 1px solid var(--nc-line-faint);
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
}

.filter-search {
  flex: 1 1 260px;
  max-width: 440px;
}

.filter-actions {
  display: flex;
  flex: none;
  gap: 8px;
  margin-left: auto;
}

.load-error {
  margin: 0;
}

.list-card {
  overflow: hidden;
}

.list-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
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

.review-table {
  width: 100%;
}

.person-name {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
}

.mono {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.category-change {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.month-cell {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.muted {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.review-cards {
  display: grid;
  gap: 10px;
}

.review-card {
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

.card-person {
  display: flex;
  align-items: baseline;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 8px;
}

.card-person strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.card-person .mono {
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

.card-meta-grid .span-2 {
  grid-column: span 2;
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

.empty-wrap {
  padding: 10px 8px;
}

.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.pagination-note {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

@media (max-width: 767px) {
  .summary {
    grid-template-columns: minmax(0, 1fr);
  }

  .filter-bar {
    padding: 12px;
  }

  .filter-search {
    flex: 1 1 100%;
    max-width: none;
  }

  .filter-actions {
    flex: 1 1 100%;
    margin-left: 0;
  }

  .filter-actions .el-button {
    flex: 1;
  }

  .pagination {
    justify-content: center;
    padding: 12px;
  }

  .pagination-note {
    width: 100%;
    text-align: center;
  }
}
</style>
