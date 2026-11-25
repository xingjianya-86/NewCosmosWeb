<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  confirmElderlyApplication,
  deleteElderlyApplication,
  getElderlyApplication,
  listElderlyApplications,
} from '@/api/elderly'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { ElderlyApplicationListItem } from '@/types/elderly'
import ElderlyChip from './ElderlyChip.vue'
import type { ChipTone } from './display'
import { formatDate, formatMonth } from './display'

type TabStatus = 'Draft' | 'Confirmed' | 'Stopped'

const TABS: Array<{ value: TabStatus; label: string }> = [
  { value: 'Draft', label: '草稿' },
  { value: 'Confirmed', label: '已确认' },
  { value: 'Stopped', label: '已停发' },
]

const TAB_META: Record<TabStatus, { text: string; tone: ChipTone }> = {
  Draft: { text: '草稿', tone: 'amber' },
  Confirmed: { text: '在享', tone: 'pine' },
  Stopped: { text: '已停发', tone: 'seal' },
}

const EMPTY_TEXT: Record<TabStatus, string> = {
  Draft: '暂无草稿记录',
  Confirmed: '暂无已确认（在享）记录',
  Stopped: '暂无已停发记录',
}

interface StopInfo {
  date: string | null
  reason: string
}

const route = useRoute()
const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

// 旧版默认进入「已确认」Tab。
const activeTab = ref<TabStatus>('Confirmed')
const keyword = ref('')
const page = ref(1)
// 已确认每页 10 条，草稿 / 已停发每页 20 条（旧版固定分页）。
const pageSize = computed(() => (activeTab.value === 'Confirmed' ? 10 : 20))

const items = ref<ElderlyApplicationListItem[]>([])
const counts = ref<Record<TabStatus, number>>({ Draft: 0, Confirmed: 0, Stopped: 0 })
const stopInfo = ref<Record<number, StopInfo>>({})
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')

let requestTicket = 0

const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const emptyText = computed(() => EMPTY_TEXT[activeTab.value])
const tabMeta = computed(() => TAB_META[activeTab.value])
const hasKeyword = computed(() => keyword.value.trim().length > 0)
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'prev, pager, next, jumper',
)

function readStatusFromQuery(): TabStatus | null {
  const raw = route.query.status
  const value = Array.isArray(raw) ? raw[0] : raw
  return value === 'Draft' || value === 'Confirmed' || value === 'Stopped' ? value : null
}

function syncStatusQuery(): void {
  void router.replace({ query: { ...route.query, status: activeTab.value } })
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  const tab = activeTab.value
  loading.value = true
  loadError.value = ''

  try {
    const response = await listElderlyApplications({
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      status: tab,
    })
    if (ticket !== requestTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0

    if (tab === 'Stopped') {
      void loadStopInfo(ticket, items.value)
    }
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

/**
 * 列表接口不含停发时间/原因，已停发卡片按需补详情。
 * 单页最多 20 条，并发读取后回填；失败项保持「—」。
 */
async function loadStopInfo(
  ticket: number,
  rows: ElderlyApplicationListItem[],
): Promise<void> {
  stopInfo.value = {}
  if (rows.length === 0) return

  const results = await Promise.allSettled(
    rows.map(async (row) => {
      const detail = await getElderlyApplication(row.id)
      return {
        id: row.id,
        info: {
          date: detail.actualStopDate,
          reason: detail.stopReasonText || detail.stopReason || '',
        } as StopInfo,
      }
    }),
  )
  if (ticket !== requestTicket) return

  const next: Record<number, StopInfo> = {}
  for (const result of results) {
    if (result.status === 'fulfilled') next[result.value.id] = result.value.info
  }
  stopInfo.value = next
}

async function loadCounts(): Promise<void> {
  const statuses: TabStatus[] = ['Draft', 'Confirmed', 'Stopped']
  await Promise.allSettled(
    statuses.map(async (status) => {
      const response = await listElderlyApplications({ page: 1, pageSize: 1, status })
      counts.value[status] = response.totalCount ?? 0
    }),
  )
}

function switchTab(next: TabStatus): void {
  if (activeTab.value === next) return
  activeTab.value = next
  page.value = 1
  keyword.value = ''
  syncStatusQuery()
  void load()
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

function handleCreate(): void {
  void router.push('/elderly/applications/new')
}

function handleView(row: ElderlyApplicationListItem): void {
  void router.push(`/elderly/applications/${row.id}`)
}

function handleEdit(row: ElderlyApplicationListItem): void {
  void router.push(`/elderly/applications/${row.id}/edit`)
}

function handleStop(row: ElderlyApplicationListItem): void {
  // 停发页下一阶段实现，先保留入口。
  void router.push(`/elderly/stops/${row.id}`)
}

/**
 * TODO(照片模块): 下一阶段替换为统一档案照片对话框，按身份证检索
 * （对应旧版 ArchivePhotoQueryPage 的预填身份证行为）。
 */
function handlePhotos(_row: ElderlyApplicationListItem): void {
  ElMessage.info('照片查询随档案照片模块接入')
}

async function handleConfirm(row: ElderlyApplicationListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确认要确认 [${row.applicationNo}] ${row.name} 的高龄补贴登记生效？`,
      '确认生效',
      { confirmButtonText: '确认', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await confirmElderlyApplication(row.id)
    ElMessage.success('登记已确认生效')
    await Promise.all([load(), loadCounts()])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '确认登记生效失败'))
  }
}

async function handleDelete(row: ElderlyApplicationListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确认要删除登记 [${row.applicationNo}] ${row.name} 吗？\n此操作不可恢复。`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await deleteElderlyApplication(row.id)
    await Promise.all([load(), loadCounts()])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除登记失败'))
  }
}

function stopDate(row: ElderlyApplicationListItem): string {
  return formatDate(stopInfo.value[row.id]?.date ?? null)
}

function stopReason(row: ElderlyApplicationListItem): string {
  return stopInfo.value[row.id]?.reason || '—'
}

watch(
  () => route.query.status,
  () => {
    const next = readStatusFromQuery() ?? 'Confirmed'
    if (next === activeTab.value) return
    activeTab.value = next
    page.value = 1
    keyword.value = ''
    void load()
  },
)

onMounted(() => {
  activeTab.value = readStatusFromQuery() ?? 'Confirmed'
  void load()
  void loadCounts()
})
</script>

<template>
  <div class="nc-page elderly-list-page">
    <NcPageHeader
      title="高龄津贴申请登记"
      description="高龄津贴受理登记、审核确认与停发管理"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
        <el-button type="primary" :icon="Plus" @click="handleCreate">新增登记</el-button>
      </template>
    </NcPageHeader>

    <NcCard padding="none" class="filter-card">
      <div class="status-tabs" role="tablist" aria-label="按状态筛选">
        <button
          v-for="tab in TABS"
          :key="tab.value"
          type="button"
          role="tab"
          class="status-tab"
          :class="{ 'is-active': activeTab === tab.value }"
          :aria-selected="activeTab === tab.value"
          @click="switchTab(tab.value)"
        >
          <span>{{ tab.label }}</span>
          <span
            v-if="counts[tab.value] > 0"
            class="status-tab__count"
            :class="`status-tab__count--${tab.value.toLowerCase()}`"
          >
            {{ counts[tab.value] }}
          </span>
        </button>
      </div>

      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索姓名、身份证号..."
          @clear="handleSearch"
        />
        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            查询
          </el-button>
          <el-button v-if="hasKeyword" text native-type="button" @click="handleReset">
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
      <div v-loading="loading && items.length > 0" class="list-area">
        <el-skeleton v-if="loading && items.length === 0" :rows="5" animated class="list-skeleton" />

        <div v-else-if="items.length > 0" class="app-cards">
          <article v-for="row in items" :key="row.id" class="app-card">
            <header class="card-head">
              <div class="card-person">
                <strong>{{ row.name }}</strong>
                <ElderlyChip :text="tabMeta.text" :tone="tabMeta.tone" />
              </div>

              <div v-if="activeTab === 'Draft'" class="card-actions">
                <el-button size="small" type="info" plain @click="handlePhotos(row)">
                  照片
                </el-button>
                <el-button size="small" type="success" @click="handleConfirm(row)">
                  确认
                </el-button>
                <el-button size="small" type="warning" @click="handleEdit(row)">
                  编辑
                </el-button>
                <el-button size="small" type="danger" @click="handleDelete(row)">
                  删除
                </el-button>
              </div>

              <div v-else-if="activeTab === 'Confirmed'" class="card-actions">
                <el-button size="small" type="primary" @click="handleView(row)">
                  查看档案
                </el-button>
                <el-button size="small" type="info" plain @click="handlePhotos(row)">
                  照片
                </el-button>
                <el-button size="small" type="warning" @click="handleStop(row)">
                  停发
                </el-button>
              </div>
            </header>

            <p class="card-id mono">{{ row.idCardMasked || '—' }}</p>
            <p class="card-no">
              编号: <span class="mono">{{ row.applicationNo || '—' }}</span>
            </p>

            <dl class="card-meta">
              <template v-if="activeTab === 'Draft'">
                <div>
                  <dt>受理日期</dt>
                  <dd>{{ formatDate(row.applyDate) }}</dd>
                </div>
                <div>
                  <dt>享受类别</dt>
                  <dd>{{ row.categoryText || '—' }}</dd>
                </div>
              </template>

              <template v-else-if="activeTab === 'Confirmed'">
                <div>
                  <dt>计发年月</dt>
                  <dd>{{ formatMonth(row.issueStartMonth) }}</dd>
                </div>
                <div>
                  <dt>享受类别</dt>
                  <dd>{{ row.categoryText || '—' }}</dd>
                </div>
              </template>

              <template v-else>
                <div>
                  <dt>停发时间</dt>
                  <dd>{{ stopDate(row) }}</dd>
                </div>
                <div>
                  <dt>停发原因</dt>
                  <dd :title="stopReason(row)">{{ stopReason(row) }}</dd>
                </div>
              </template>
            </dl>
          </article>
        </div>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="emptyText" />
        </div>
      </div>

      <template #footer>
        <div v-if="totalCount > 0" class="pagination">
          <span class="pagination-note">
            第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 条
          </span>
          <el-pagination
            :current-page="page"
            :page-size="pageSize"
            :total="totalCount"
            :layout="paginationLayout"
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
.elderly-list-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.filter-card,
.list-card {
  overflow: hidden;
}

.status-tabs {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.status-tab {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 9px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
  font-family: inherit;
  font-size: var(--nc-text-sm);
  cursor: pointer;
}

.status-tab.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
  color: var(--nc-ink);
  font-weight: 700;
}

.status-tab__count {
  min-width: 20px;
  padding: 0 6px;
  border-radius: 999px;
  color: #fff;
  font-family: var(--nc-font-mono);
  font-size: 11px;
  font-weight: 700;
  line-height: 18px;
  text-align: center;
}

.status-tab__count--draft {
  background-color: var(--nc-seal);
}

.status-tab__count--confirmed {
  background-color: var(--nc-pine);
}

.status-tab__count--stopped {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal);
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

.list-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.list-area {
  min-height: 220px;
  padding: 10px;
}

.list-skeleton {
  padding: 12px;
}

.app-cards {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 10px;
}

.app-card {
  padding: 14px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.card-person {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
}

.card-person strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.card-actions {
  display: flex;
  flex: none;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}

.card-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.card-id {
  margin: 8px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.card-no {
  margin: 4px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.mono {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.card-no .mono {
  color: var(--nc-muted);
}

.card-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 16px;
  margin: 10px 0 0;
  padding-top: 9px;
  border-top: 1px solid var(--nc-line-faint);
}

.card-meta div {
  min-width: 0;
}

.card-meta dt {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.card-meta dd {
  margin: 2px 0 0;
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

  .card-head {
    flex-direction: column;
    align-items: stretch;
  }

  .card-actions {
    justify-content: flex-start;
  }

  .card-meta {
    grid-template-columns: minmax(0, 1fr);
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
