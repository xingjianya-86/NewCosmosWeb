<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowRight, Refresh, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { listApplications } from '@/api/applications'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { ApplicationListItem, ApplicationStatus } from '@/types/application'
import ApplicationStatusTag from './ApplicationStatusTag.vue'
import { formatDateTime, formatMoney, formatShortDateTime } from './format'
import { APPLICATION_STATUS_OPTIONS } from './status'

const isNarrow = useMediaQuery('(max-width: 960px)')
const router = useRouter()

const keyword = ref('')
const status = ref<ApplicationStatus | ''>('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<ApplicationListItem[]>([])
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
  if (loading.value && items.value.length === 0) return '正在读取申请档案…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `共 ${totalCount.value} 条记录${suffix}`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的申请记录' : '暂无社会救助申请记录',
)

function locationText(row: ApplicationListItem): string {
  const parts = [row.town, row.village].filter((part) => Boolean(part && part.trim()))
  if (parts.length > 0) return parts.join(' / ')
  return row.address?.trim() || '—'
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listApplications({
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
    loadError.value = getErrorMessage(error, '申请列表加载失败')
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

function openDetail(row: ApplicationListItem): void {
  void router.push({ name: 'application-detail', params: { id: row.id } })
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page applications-page">
    <NcPageHeader
      title="申请管理"
      description="查看社会救助申请档案、家庭成员与收入情况。支持按申请人、身份证或家庭成员检索。"
      kicker="社会救助 · 申请档案"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="申请总数" :value="totalCount" tone="ink" compact />
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
          placeholder="搜索申请人姓名 / 身份证 / 家庭成员"
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
            v-for="option in APPLICATION_STATUS_OPTIONS"
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

    <NcCard title="申请列表" :subtitle="listSubtitle" padding="none" class="list-card">
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
            <el-table-column label="申请编号" min-width="136">
              <template #default="{ row }">
                <span class="app-no">{{ row.applicationNo }}</span>
              </template>
            </el-table-column>

            <el-table-column label="申请人" min-width="104">
              <template #default="{ row }">
                <div class="applicant-cell">
                  <strong>{{ row.applicantName }}</strong>
                  <span v-if="row.applicantPhone">{{ row.applicantPhone }}</span>
                </div>
              </template>
            </el-table-column>

            <el-table-column label="身份证（脱敏）" min-width="150">
              <template #default="{ row }">
                <span class="id-card">{{ row.applicantIdCardMasked || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="状态" width="96">
              <template #default="{ row }">
                <ApplicationStatusTag :status="row.status" :status-text="row.statusText" />
              </template>
            </el-table-column>

            <el-table-column label="分类" min-width="76">
              <template #default="{ row }">{{ row.classificationResult || '—' }}</template>
            </el-table-column>

            <el-table-column label="家庭人数" width="76" align="center">
              <template #default="{ row }">{{ row.familySize }} 人</template>
            </el-table-column>

            <el-table-column label="年人均收入（元）" width="116" align="right">
              <template #default="{ row }">
                <span class="money">{{ formatMoney(row.perCapitaAnnualIncome) }}</span>
              </template>
            </el-table-column>

            <el-table-column label="乡镇 / 村" min-width="124">
              <template #default="{ row }">
                <span class="location" :title="locationText(row as ApplicationListItem)">
                  {{ locationText(row as ApplicationListItem) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="更新时间" width="104">
              <template #default="{ row }">{{ formatShortDateTime(row.updatedAt) }}</template>
            </el-table-column>

            <el-table-column label="操作" width="56" align="right">
              <template #default="{ row }">
                <el-button link type="primary" @click.stop="openDetail(row as ApplicationListItem)">
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
                <ApplicationStatusTag :status="row.status" :status-text="row.statusText" />
              </header>

              <div class="card-person">
                <strong>{{ row.applicantName }}</strong>
                <span class="id-card">{{ row.applicantIdCardMasked || '—' }}</span>
              </div>

              <div class="card-meta-grid">
                <div>
                  <span>救助分类</span>
                  <strong>{{ row.classificationResult || '—' }}</strong>
                </div>
                <div>
                  <span>家庭人数</span>
                  <strong>{{ row.familySize }} 人</strong>
                </div>
                <div>
                  <span>年人均收入</span>
                  <strong>{{ formatMoney(row.perCapitaAnnualIncome) }} 元</strong>
                </div>
                <div>
                  <span>更新时间</span>
                  <strong>{{ formatDateTime(row.updatedAt) }}</strong>
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
.applications-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

/* ── 概要统计 ─────────────────────────────────────────────────── */
.summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

/* ── 检索条 ───────────────────────────────────────────────────── */
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

.filter-status {
  width: 150px;
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

/* ── 列表 ─────────────────────────────────────────────────────── */
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

.applicant-cell {
  display: flex;
  flex-direction: column;
  line-height: 1.45;
}

.applicant-cell strong {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
}

.applicant-cell span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.id-card {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.money {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.location {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── 手机卡片 ─────────────────────────────────────────────────── */
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

/* ── 空状态 / 分页 ────────────────────────────────────────────── */
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

/* ── 手机 ─────────────────────────────────────────────────────── */
@media (max-width: 767px) {
  .filter-bar {
    padding: 12px;
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
