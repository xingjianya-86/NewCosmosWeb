<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Refresh, Search } from '@element-plus/icons-vue'
import { listMonthlyAssetTasks } from '@/api/assetVerification'
import { getErrorMessage } from '@/api/http'
import type { AssetCheckStatus, AssetTaskListItem } from '@/types/assetVerification'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import AssetTaskDetailDrawer from './AssetTaskDetailDrawer.vue'
import AssetTaskTable from './AssetTaskTable.vue'
import { ASSET_STATUS_OPTIONS } from './status'

const isNarrow = useMediaQuery('(max-width: 960px)')

const monthValue = ref(currentMonthValue())
const keyword = ref('')
const status = ref<AssetCheckStatus | ''>('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<AssetTaskListItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')

const drawerVisible = ref(false)
const activeTaskId = ref<number | null>(null)

let requestTicket = 0

const hasFilters = computed(() => keyword.value.trim().length > 0 || status.value !== '')
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

const monthLabel = computed(() => {
  const parsed = parseMonth(monthValue.value)
  return `${parsed.year} 年 ${parsed.month} 月`
})

const batchCount = computed(() => {
  const ids = new Set<string>()
  for (const item of items.value) {
    if (item.batchId) ids.add(item.batchId)
  }
  return ids.size
})

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取月度核对任务…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `${monthLabel.value} · 共 ${totalCount.value} 条记录${suffix}`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的月度核对任务' : `${monthLabel.value}暂无核对任务`,
)

function currentMonthValue(): string {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
}

function parseMonth(value: string): { year: number; month: number } {
  const now = new Date()
  const [rawYear, rawMonth] = (value || '').split('-')
  const year = Number(rawYear)
  const month = Number(rawMonth)

  if (!Number.isInteger(year) || !Number.isInteger(month) || month < 1 || month > 12) {
    return { year: now.getFullYear(), month: now.getMonth() + 1 }
  }

  return { year, month }
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  const { year, month } = parseMonth(monthValue.value)
  loading.value = true
  loadError.value = ''

  try {
    const response = await listMonthlyAssetTasks({
      year,
      month,
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
    loadError.value = getErrorMessage(error, '月度核对任务加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function handleSearch(): void {
  page.value = 1
  void load()
}

function handleMonthChange(): void {
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

function openDetail(item: AssetTaskListItem): void {
  activeTaskId.value = item.id
  drawerVisible.value = true
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page asset-monthly-page">
    <NcPageHeader
      title="月度审核"
      description="按月查看家庭经济状况核对任务与授权报告状态。当前为只读查询，报告上传与复核写入将在下一批开放。"
      kicker="家庭经济状况核对 · 月度审核"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="本月记录" :value="totalCount" tone="ink" compact />
      <NcStat label="本页记录" :value="items.length" tone="pine" compact />
      <NcStat label="本页批次" :value="batchCount" tone="amber" compact />
    </div>

    <NcCard padding="none" class="filter-card">
      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-date-picker
          v-model="monthValue"
          class="filter-month"
          type="month"
          value-format="YYYY-MM"
          format="YYYY 年 MM 月"
          :clearable="false"
          placeholder="选择年月"
          @change="handleMonthChange"
        />

        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索姓名 / 身份证 / 户主身份证"
          @clear="handleSearch"
        />

        <el-select
          v-model="status"
          class="filter-status"
          clearable
          placeholder="全部状态"
          @change="handleStatusChange"
        >
          <el-option
            v-for="option in ASSET_STATUS_OPTIONS"
            :key="option.value"
            :label="option.label"
            :value="option.value"
          />
        </el-select>

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

    <NcCard title="月度核对任务" :subtitle="listSubtitle" padding="none" class="list-card">
      <div
        v-loading="loading && items.length > 0"
        class="list-area"
        :class="{ 'is-cards': isNarrow }"
      >
        <el-skeleton v-if="loading && items.length === 0" :rows="5" animated class="list-skeleton" />

        <AssetTaskTable
          v-else-if="items.length > 0"
          :items="items"
          :narrow="isNarrow"
          @select="openDetail"
        />

        <div v-else-if="!loadError" class="empty-wrap">
          <el-empty :description="emptyDescription">
            <el-button v-if="hasFilters" :icon="Refresh" @click="handleReset">清除筛选</el-button>
          </el-empty>
        </div>
      </div>

      <template #footer>
        <div v-if="items.length > 0" class="pagination">
          <span class="pagination-note">
            {{ monthLabel }} · 第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 条
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

    <AssetTaskDetailDrawer v-model="drawerVisible" :task-id="activeTaskId" />
  </div>
</template>

<style scoped>
.asset-monthly-page {
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

.filter-month {
  flex: none;
  width: 176px;
}

.filter-search {
  flex: 1 1 240px;
  max-width: 380px;
}

.filter-status {
  width: 168px;
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

  .filter-month {
    flex: 1 1 calc(50% - 5px);
    width: auto;
  }

  .filter-search {
    flex: 1 1 100%;
    max-width: none;
  }

  .filter-status {
    flex: 1 1 calc(50% - 5px);
    width: auto;
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
