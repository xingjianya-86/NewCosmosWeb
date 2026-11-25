<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Refresh, Search } from '@element-plus/icons-vue'
import {
  getElderlyStopStats,
  listElderlyApplications,
  searchElderlyImported,
} from '@/api/elderly'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { ElderlyApplicationListItem, ElderlyImportedItem, ElderlyStopStats } from '@/types/elderly'
import { formatAge, formatMonth } from './display'

interface StopRow {
  key: string
  kind: 'current' | 'imported'
  id: number
  applicationNo: string
  name: string
  idCard: string
  categoryText: string
  sourceText: string
  issueStartMonth: string
  age: number | null
}

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const keyword = ref('')
const page = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')
const stats = ref<ElderlyStopStats>({ activeCount: null, monthlyStopped: null })

const currentRows = ref<ElderlyApplicationListItem[]>([])
const importedRows = ref<ElderlyImportedItem[]>([])
const searchMode = ref(false)

let requestTicket = 0

const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const rows = computed<StopRow[]>(() => {
  const current = currentRows.value.map((row) => ({
    key: `current-${row.id}`,
    kind: 'current' as const,
    id: row.id,
    applicationNo: row.applicationNo,
    name: row.name,
    idCard: row.idCardMasked,
    categoryText: row.categoryText,
    sourceText: '当前库',
    issueStartMonth: row.issueStartMonth,
    age: row.age,
  }))
  const imported = importedRows.value.map((row) => ({
    key: `imported-${row.id}`,
    kind: 'imported' as const,
    id: row.id,
    applicationNo: '--',
    name: row.name,
    idCard: row.idCard,
    categoryText: row.originalType || row.personType || '',
    sourceText: `历史导入 (${row.dataYear ?? 0}年)`,
    issueStartMonth: '',
    age: row.age,
  }))
  return [...current, ...imported]
})

const emptyText = computed(() =>
  searchMode.value ? '未找到匹配的记录，请更换关键词重试' : '暂无在享人员，无需停发',
)

const activeText = computed(() =>
  stats.value.activeCount === null ? '—' : stats.value.activeCount.toLocaleString('zh-CN'),
)
const stoppedText = computed(() =>
  stats.value.monthlyStopped === null ? '—' : stats.value.monthlyStopped.toLocaleString('zh-CN'),
)

async function loadStats(): Promise<void> {
  try {
    stats.value = await getElderlyStopStats()
  } catch {
    stats.value = { activeCount: null, monthlyStopped: null }
  }
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  const search = keyword.value.trim()
  loading.value = true
  loadError.value = ''
  searchMode.value = Boolean(search)

  try {
    if (search) {
      const [currentResult, importedResult] = await Promise.allSettled([
        listElderlyApplications({
          page: 1,
          pageSize: 20,
          status: 'Confirmed',
          keyword: search,
        }),
        searchElderlyImported(search),
      ])
      if (ticket !== requestTicket) return

      currentRows.value =
        currentResult.status === 'fulfilled' ? currentResult.value.items ?? [] : []
      importedRows.value =
        importedResult.status === 'fulfilled' ? importedResult.value ?? [] : []
      totalCount.value = currentRows.value.length + importedRows.value.length
      totalPages.value = 1
      page.value = 1

      if (
        currentResult.status === 'rejected'
        || importedResult.status === 'rejected'
      ) {
        loadError.value = '部分搜索来源加载失败，请稍后重试'
      }
      return
    }

    const response = await listElderlyApplications({
      page: page.value,
      pageSize: pageSize.value,
      status: 'Confirmed',
    })
    if (ticket !== requestTicket) return

    currentRows.value = response.items ?? []
    importedRows.value = []
    page.value = response.pageIndex || page.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0
  } catch (error) {
    if (ticket !== requestTicket) return
    currentRows.value = []
    importedRows.value = []
    totalCount.value = 0
    totalPages.value = 0
    loadError.value = getErrorMessage(error, '停发人员列表加载失败')
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

function openStop(row: StopRow): void {
  if (row.kind === 'imported') {
    void router.push({
      path: `/elderly/stops/history/${row.id}`,
      query: {
        idCard: row.idCard,
        name: row.name,
        age: row.age === null ? undefined : String(row.age),
        category: row.categoryText,
      },
    })
    return
  }

  void router.push(`/elderly/stops/${row.id}`)
}

function openPhotos(): void {
  // TODO(照片模块): 统一档案照片对话框接入后替换。
  ElMessage.info('照片查询随档案照片模块接入')
}

onMounted(() => {
  void load()
  void loadStats()
})
</script>

<template>
  <div class="nc-page elderly-stop-list-page">
    <NcPageHeader
      title="停发办理"
      description="在享人员停发、历史名册建档并停发"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="在享领取（人）" :value="activeText" tone="ink" compact />
      <NcStat label="本月停发（人次）" :value="stoppedText" tone="amber" compact />
    </div>

    <NcCard padding="none" class="filter-card">
      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索申请人姓名、身份证号（支持当前库+导入库）"
          @clear="handleSearch"
        />
        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            搜索
          </el-button>
          <el-button v-if="keyword.trim()" text native-type="button" @click="handleReset">
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

    <NcCard padding="none" class="list-card">
      <div v-loading="loading" class="list-area" :class="{ 'is-cards': isNarrow }">
        <el-skeleton v-if="loading && rows.length === 0" :rows="5" animated />

        <template v-else-if="rows.length > 0">
          <el-table v-if="!isNarrow" :data="rows" row-key="key" class="stop-table">
            <el-table-column label="序号" width="64">
              <template #default="{ $index }">{{ $index + 1 }}</template>
            </el-table-column>
            <el-table-column label="申请编号" min-width="120">
              <template #default="{ row }">
                <span class="mono">{{ row.applicationNo }}</span>
              </template>
            </el-table-column>
            <el-table-column label="申请人" min-width="96">
              <template #default="{ row }">{{ row.name }}</template>
            </el-table-column>
            <el-table-column label="身份证号" min-width="150">
              <template #default="{ row }">
                <span class="mono">{{ row.idCard || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="享受类别/来源" min-width="210">
              <template #default="{ row }">
                <div class="source-cell">
                  <span>{{ row.categoryText || '—' }}</span>
                  <small>{{ row.sourceText }}</small>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="计发年月/年龄" min-width="130">
              <template #default="{ row }">
                {{ row.kind === 'current' ? formatMonth(row.issueStartMonth) : formatAge(row.age) }}
              </template>
            </el-table-column>
            <el-table-column label="操作" width="150" align="right">
              <template #default="{ row }">
                <el-button size="small" @click="openPhotos()">照片</el-button>
                <el-button size="small" type="warning" @click="openStop(row as StopRow)">
                  {{ row.kind === 'current' ? '停发' : '建档并停发' }}
                </el-button>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="stop-cards">
            <article v-for="(row, index) in rows" :key="row.key" class="stop-card">
              <header class="card-head">
                <div>
                  <strong>{{ row.name }}</strong>
                  <span class="card-no mono">{{ row.applicationNo }}</span>
                </div>
                <span class="card-index">#{{ index + 1 }}</span>
              </header>
              <p class="mono">{{ row.idCard || '—' }}</p>
              <dl class="card-meta">
                <div>
                  <dt>享受类别/来源</dt>
                  <dd>{{ row.categoryText || '—' }} · {{ row.sourceText }}</dd>
                </div>
                <div>
                  <dt>计发年月/年龄</dt>
                  <dd>
                    {{ row.kind === 'current' ? formatMonth(row.issueStartMonth) : formatAge(row.age) }}
                  </dd>
                </div>
              </dl>
              <div class="card-actions">
                <el-button size="small" @click="openPhotos()">照片</el-button>
                <el-button size="small" type="warning" @click="openStop(row)">
                  {{ row.kind === 'current' ? '停发' : '建档并停发' }}
                </el-button>
              </div>
            </article>
          </div>
        </template>

        <div v-else-if="!loading" class="empty-wrap">
          <el-empty :description="emptyText" />
        </div>
      </div>

      <template v-if="!searchMode && totalCount > 0" #footer>
        <div class="pagination">
          <span class="pagination-note">
            第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 条
          </span>
          <el-pagination
            :current-page="page"
            :page-size="pageSize"
            :total="totalCount"
            :layout="isNarrow ? 'prev, pager, next' : 'prev, pager, next, jumper'"
            :pager-count="isNarrow ? 5 : 7"
            :small="isNarrow"
            background
            @current-change="handlePageChange"
          />
        </div>
      </template>
    </NcCard>
  </div>
</template>

<style scoped>
.elderly-stop-list-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.summary {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.filter-card,
.list-card {
  overflow: hidden;
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  padding: 14px 16px;
}

.filter-search {
  flex: 1 1 320px;
}

.filter-actions {
  display: flex;
  gap: 8px;
}

.load-error {
  margin: 0;
}

.list-area {
  padding: 0 16px 16px;
}

.source-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.source-cell small {
  color: var(--nc-muted);
}

.stop-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 12px;
}

.stop-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.card-head > div {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.card-head strong {
  font-size: var(--nc-text-md);
}

.card-no,
.card-index {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  margin: 0;
}

.card-meta dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-meta dd {
  margin: 2px 0 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.card-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.empty-wrap {
  padding: 30px 0;
}

.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 12px 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.pagination-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.mono {
  font-family: var(--nc-font-mono);
}

@media (max-width: 700px) {
  .summary {
    grid-template-columns: minmax(0, 1fr);
  }

  .filter-bar {
    flex-direction: column;
  }

  .filter-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
