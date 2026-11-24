<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Refresh, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { listElderlyApplications } from '@/api/elderly'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { ElderlyApplicationListItem } from '@/types/elderly'
import ElderlyChip from './ElderlyChip.vue'
import { applicationStatusTone, formatAge, formatDate, formatMoney, formatMonth } from './display'

const route = useRoute()
const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const STATUS_TABS = [
  { label: '全部', value: '' },
  { label: '草稿', value: 'Draft' },
  { label: '在享', value: 'Confirmed' },
  { label: '已停发', value: 'Stopped' },
]
const VALID_STATUSES = ['Draft', 'Confirmed', 'Stopped']

const keyword = ref('')
const status = ref('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<ElderlyApplicationListItem[]>([])
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
  if (loading.value && items.value.length === 0) return '正在读取高龄津贴档案…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `共 ${totalCount.value} 条记录${suffix}`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的高龄津贴档案' : '暂无高龄津贴登记记录',
)

function readStatusFromQuery(): string {
  const raw = route.query.status
  const value = Array.isArray(raw) ? raw[0] : raw
  return typeof value === 'string' && VALID_STATUSES.includes(value) ? value : ''
}

function syncStatusQuery(): void {
  void router.replace({ query: { ...route.query, status: status.value || undefined } })
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listElderlyApplications({
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
    loadError.value = getErrorMessage(error, '高龄津贴列表加载失败')
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
  syncStatusQuery()
  void load()
}

function handleReset(): void {
  keyword.value = ''
  status.value = ''
  page.value = 1
  syncStatusQuery()
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

function openDetail(row: ElderlyApplicationListItem): void {
  void router.push({ name: 'elderly-application-detail', params: { id: row.id } })
}

watch(
  () => route.query.status,
  () => {
    const next = readStatusFromQuery()
    if (next === status.value) return
    status.value = next
    page.value = 1
    void load()
  },
)

onMounted(() => {
  status.value = readStatusFromQuery()
  void load()
})
</script>

<template>
  <div class="nc-page elderly-list-page">
    <NcPageHeader
      title="高龄津贴申请登记"
      description="高龄津贴受理登记、审核确认与停发管理。支持按姓名、身份证号或申请编号检索。"
      kicker="普惠高龄 · 登记档案"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="筛选结果" :value="totalCount" tone="ink" compact />
      <NcStat label="本页记录" :value="items.length" tone="pine" compact />
      <NcStat label="当前页" :value="`${page} / ${displayTotalPages}`" tone="seal" compact />
    </div>

    <NcCard padding="none" class="filter-card">
      <div class="status-tabs" role="tablist" aria-label="按状态筛选">
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
          placeholder="搜索姓名 / 身份证号 / 申请编号"
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

    <NcCard title="登记列表" :subtitle="listSubtitle" padding="none" class="list-card">
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
            row-key="id"
            class="app-table"
            @row-click="openDetail"
          >
            <el-table-column label="申请编号" min-width="140">
              <template #default="{ row }">
                <span class="app-no">{{ row.applicationNo }}</span>
              </template>
            </el-table-column>

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

            <el-table-column label="享受类别" min-width="190">
              <template #default="{ row }">
                <span class="category" :title="row.categoryText">{{ row.categoryText || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="状态" width="96">
              <template #default="{ row }">
                <ElderlyChip
                  :text="row.statusText || row.status"
                  :tone="applicationStatusTone(row.status)"
                />
              </template>
            </el-table-column>

            <el-table-column label="受理日期" width="108">
              <template #default="{ row }">{{ formatDate(row.applyDate) }}</template>
            </el-table-column>

            <el-table-column label="计发年月" width="108">
              <template #default="{ row }">{{ formatMonth(row.issueStartMonth) }}</template>
            </el-table-column>

            <el-table-column label="月标准（元）" width="112" align="right">
              <template #default="{ row }">
                <span class="mono">{{ formatMoney(row.issueAmount) }}</span>
              </template>
            </el-table-column>

            <el-table-column label="操作" width="64" align="right">
              <template #default="{ row }">
                <el-button link type="primary" @click.stop="openDetail(row as ElderlyApplicationListItem)">
                  查看
                </el-button>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="app-cards">
            <article
              v-for="row in items"
              :key="row.id"
              class="app-card"
              role="button"
              tabindex="0"
              @click="openDetail(row)"
              @keydown.enter="openDetail(row)"
            >
              <header class="card-head">
                <span class="app-no">{{ row.applicationNo }}</span>
                <ElderlyChip
                  :text="row.statusText || row.status"
                  :tone="applicationStatusTone(row.status)"
                />
              </header>

              <div class="card-person">
                <strong>{{ row.name }}</strong>
                <span class="mono">{{ row.idCardMasked || '—' }}</span>
              </div>

              <div class="card-meta-grid">
                <div>
                  <span>性别 / 年龄</span>
                  <strong>{{ row.gender || '—' }} · {{ formatAge(row.age) }}</strong>
                </div>
                <div>
                  <span>享受类别</span>
                  <strong>{{ row.categoryText || '—' }}</strong>
                </div>
                <div>
                  <span>受理日期</span>
                  <strong>{{ formatDate(row.applyDate) }}</strong>
                </div>
                <div>
                  <span>计发年月</span>
                  <strong>{{ formatMonth(row.issueStartMonth) }}</strong>
                </div>
                <div>
                  <span>月标准</span>
                  <strong>{{ formatMoney(row.issueAmount) }} 元</strong>
                </div>
              </div>

              <button class="card-foot" type="button" @click.stop="openDetail(row)">
                查看档案
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
.elderly-list-page {
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
  min-height: 220px;
}

.list-area.is-cards {
  padding: 10px;
}

.list-skeleton {
  padding: 22px;
}

.app-table {
  width: 100%;
}

.app-table :deep(.el-table__row) {
  cursor: pointer;
}

.app-no {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.02em;
  white-space: nowrap;
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

.category {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-cards {
  display: grid;
  gap: 10px;
}

.app-card {
  padding: 14px 15px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.app-card:hover {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-sm);
}

.app-card:focus-visible {
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

.card-person .mono {
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

.card-foot {
  width: 100%;
  margin-top: 10px;
  padding: 0;
  border: none;
  background: none;
  color: var(--nc-seal);
  font-family: inherit;
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-align: right;
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
