<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowRight, Refresh, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { listGracePeriods } from '@/api/socialAssistance'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { GracePeriodListItem } from '@/types/socialAssistance'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const keyword = ref('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<GracePeriodListItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')

let requestTicket = 0

const hasFilters = computed(() => keyword.value.trim().length > 0)
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取渐退期记录…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `共 ${totalCount.value} 条记录${suffix}`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的渐退期记录' : '暂无进行中或已到期的渐退期记录',
)

function locationText(row: GracePeriodListItem): string {
  const parts = [row.town, row.village].filter((part) => Boolean(part && part.trim()))
  return parts.length > 0 ? parts.join(' / ') : '—'
}

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function formatDate(value: string | null): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function daysText(row: GracePeriodListItem): string {
  if (row.daysLeft >= 0) return `剩 ${row.daysLeft} 天`
  return `逾期 ${Math.abs(row.daysLeft)} 天`
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listGracePeriods({
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
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
    loadError.value = getErrorMessage(error, '渐退期列表加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function handleSearch(): void {
  page.value = 1
  void load()
}

function handleReset(): void {
  keyword.value = ''
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

function openDetail(row: GracePeriodListItem): void {
  void router.push(`/applications/${row.applicationId}`)
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page grace-page">
    <NcPageHeader
      title="渐退期管理"
      description="查看进行中与已到期的渐退期记录，按到期日排序。仅只读查询，办理请前往对应申请档案。"
      kicker="低收入人口救助帮扶 · 渐退期"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="渐退期户数" :value="totalCount" tone="ink" compact />
      <NcStat label="本页记录" :value="items.length" tone="pine" compact />
      <NcStat label="当前页" :value="`${page} / ${displayTotalPages}`" tone="seal" compact />
    </div>

    <NcCard padding="none" class="filter-card">
      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索申请人姓名 / 身份证"
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

    <NcCard title="渐退期列表" :subtitle="listSubtitle" padding="none" class="list-card">
      <div
        v-loading="loading && items.length > 0"
        class="list-area"
        :class="{ 'is-cards': isNarrow }"
      >
        <el-skeleton v-if="loading && items.length === 0" :rows="5" animated class="list-skeleton" />

        <template v-else-if="items.length > 0">
          <el-table
            v-if="!isNarrow"
            :data="items"
            row-key="applicationId"
            class="grace-table"
            @row-click="openDetail"
          >
            <el-table-column label="申请编号" min-width="132">
              <template #default="{ row }">
                <span class="app-no">{{ row.applicationNo || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="申请人" min-width="100">
              <template #default="{ row }">
                <strong class="applicant-name">{{ row.applicantName || '—' }}</strong>
              </template>
            </el-table-column>

            <el-table-column label="身份证（脱敏）" min-width="150">
              <template #default="{ row }">
                <span class="id-card">{{ row.applicantIdCardMasked || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="乡镇 / 村" min-width="130">
              <template #default="{ row }">
                <span class="location" :title="locationText(row as GracePeriodListItem)">
                  {{ locationText(row as GracePeriodListItem) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="到期日" width="112">
              <template #default="{ row }">{{ formatDate(row.endDate) }}</template>
            </el-table-column>

            <el-table-column label="剩余天数" width="100" align="right">
              <template #default="{ row }">
                <span class="days" :class="{ 'is-expired': row.daysLeft < 0 }">
                  {{ daysText(row as GracePeriodListItem) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <span class="status" :class="row.status === 'Active' ? 'is-active' : 'is-expired'">
                  {{ row.statusText }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="操作" width="84" align="right">
              <template #default="{ row }">
                <el-button link type="primary" @click.stop="openDetail(row as GracePeriodListItem)">
                  查看档案
                </el-button>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="grace-cards">
            <article
              v-for="row in items"
              :key="row.applicationId"
              class="grace-card"
              role="button"
              tabindex="0"
              @click="openDetail(row)"
              @keydown.enter="openDetail(row)"
            >
              <header class="card-head">
                <span class="app-no">{{ row.applicationNo || '—' }}</span>
                <span class="status" :class="row.status === 'Active' ? 'is-active' : 'is-expired'">
                  {{ row.statusText }}
                </span>
              </header>

              <div class="card-person">
                <strong>{{ row.applicantName || '—' }}</strong>
                <span class="id-card">{{ row.applicantIdCardMasked || '—' }}</span>
              </div>

              <div class="card-meta-grid">
                <div>
                  <span>到期日</span>
                  <strong>{{ formatDate(row.endDate) }}</strong>
                </div>
                <div>
                  <span>剩余天数</span>
                  <strong class="days" :class="{ 'is-expired': row.daysLeft < 0 }">
                    {{ daysText(row) }}
                  </strong>
                </div>
              </div>

              <p class="card-location">{{ locationText(row) }}</p>

              <button class="card-foot" type="button" @click.stop="openDetail(row)">
                查看申请档案
                <el-icon><ArrowRight /></el-icon>
              </button>
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
.grace-page {
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

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
}

.filter-search {
  flex: 1 1 260px;
  max-width: 420px;
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
  min-height: 220px;
}

.list-area.is-cards {
  padding: 10px;
}

.list-skeleton {
  padding: 22px;
}

.grace-table {
  width: 100%;
}

.grace-table :deep(.el-table__row) {
  cursor: pointer;
}

.app-no {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.02em;
  white-space: nowrap;
}

.applicant-name {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
}

.id-card {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.location {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.days {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.days.is-expired {
  color: var(--nc-seal);
}

.status {
  display: inline-flex;
  align-items: center;
  padding: 1px 7px;
  border: 1px solid transparent;
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.04em;
  white-space: nowrap;
}

.status.is-active {
  border-color: rgba(46, 71, 56, 0.35);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.status.is-expired {
  border-color: rgba(171, 50, 39, 0.35);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.grace-cards {
  display: grid;
  gap: 10px;
}

.grace-card {
  padding: 14px 15px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.grace-card:hover {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-sm);
}

.grace-card:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
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
  margin-top: 10px;
}

.card-person strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.card-person .id-card {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-meta-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 14px;
  margin-top: 12px;
  padding-top: 11px;
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

.card-location {
  margin-top: 10px;
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-foot {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 5px;
  width: 100%;
  margin-top: 10px;
  padding: 0;
  border: none;
  background: none;
  color: var(--nc-seal);
  font-family: inherit;
  font-size: var(--nc-text-sm);
  font-weight: 600;
  cursor: pointer;
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

  .list-area.is-cards {
    padding: 10px;
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
