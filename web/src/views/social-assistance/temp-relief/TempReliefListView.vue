<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  completeTempReliefArchive,
  confirmTempReliefApplication,
  deleteTempReliefApplication,
  getTempReliefSummary,
  listTempReliefApplications,
  stopTempReliefApplication,
} from '@/api/tempRelief'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { TempReliefListItem, TempReliefStatus, TempReliefSummary } from '@/types/tempRelief'

type StatusTab = TempReliefStatus | ''

const TABS: Array<{ value: StatusTab; label: string; hint: string }> = [
  { value: 'Draft', label: '草稿', hint: '办理中' },
  { value: 'Confirmed', label: '已确认', hint: '已生效' },
  { value: 'Stopped', label: '已终止', hint: '已终结' },
  { value: '', label: '全部', hint: '全部状态' },
]

const STATUS_TONE: Record<string, string> = {
  Draft: 'is-draft',
  Confirmed: 'is-confirmed',
  Stopped: 'is-stopped',
}

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

// 旧版默认进入「已确认」Tab。
const activeTab = ref<StatusTab>('Confirmed')
const keyword = ref('')
const page = ref(1)
// 旧版：已确认每页 10 条，草稿 / 已终止 / 全部每页 20 条。
const pageSize = ref(10)

const items = ref<TempReliefListItem[]>([])
const summary = ref<TempReliefSummary | null>(null)
const summaryLoading = ref(false)
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')

let requestTicket = 0

const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const hasKeyword = computed(() => keyword.value.trim().length > 0)
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

const activeTabLabel = computed(
  () => TABS.find((tab) => tab.value === activeTab.value)?.label ?? '全部',
)

const tabCount = computed<Record<string, number | null>>(() => {
  const value = summary.value
  if (!value) {
    return { Draft: null, Confirmed: null, Stopped: null, '': null }
  }
  const draft = value.draftCount ?? null
  const confirmed = value.confirmedCount ?? null
  const stopped = value.stoppedCount ?? null
  const all =
    draft === null || confirmed === null || stopped === null
      ? null
      : draft + confirmed + stopped
  return { Draft: draft, Confirmed: confirmed, Stopped: stopped, '': all }
})

const emptyText = computed(() => {
  if (hasKeyword.value) return '没有找到匹配的临时救助申请'
  switch (activeTab.value) {
    case 'Draft':
      return '暂无办理中的临时救助草稿'
    case 'Confirmed':
      return '暂无已确认的临时救助记录'
    case 'Stopped':
      return '暂无已终止的临时救助记录'
    default:
      return '暂无临时救助记录'
  }
})

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取临时救助记录…'
  const suffix = hasKeyword.value ? ' · 已应用关键字筛选' : ''
  return `${activeTabLabel.value} · 共 ${totalCount.value} 条记录${suffix}`
})

function formatDate(value: string | null): string {
  if (!value) return '—'
  const text = value.slice(0, 10)
  return /^\d{4}-\d{2}-\d{2}$/.test(text) ? text.split('-').join('/') : '—'
}

function formatMoney(value: number | null): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return `${value.toFixed(2)} 元`
}

function locationText(row: TempReliefListItem): string {
  const parts = [row.town, row.village].filter((part) => Boolean(part && part.trim()))
  return parts.length > 0 ? parts.join(' / ') : '—'
}

function statusClass(row: TempReliefListItem): string {
  return STATUS_TONE[row.status] ?? 'is-draft'
}

function statusText(row: TempReliefListItem): string {
  return row.statusText || activeTabLabel.value
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listTempReliefApplications({
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      status: activeTab.value || undefined,
    })
    if (ticket !== requestTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0
  } catch (error) {
    if (ticket !== requestTicket) return
    items.value = []
    totalCount.value = 0
    totalPages.value = 0
    loadError.value = getErrorMessage(error, '临时救助列表加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

async function loadSummary(): Promise<void> {
  summaryLoading.value = true
  try {
    summary.value = await getTempReliefSummary()
  } catch {
    summary.value = null
  } finally {
    summaryLoading.value = false
  }
}

function switchTab(next: StatusTab): void {
  if (activeTab.value === next) return
  activeTab.value = next
  pageSize.value = next === 'Confirmed' ? 10 : 20
  page.value = 1
  keyword.value = ''
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

function handleSizeChange(value: number): void {
  pageSize.value = value
  page.value = 1
  void load()
}

watch(activeTab, (next) => {
  pageSize.value = next === 'Confirmed' ? 10 : 20
})

function handleCreate(): void {
  void router.push('/social-assistance/temp-relief/new')
}

function openView(row: TempReliefListItem): void {
  void router.push(`/social-assistance/temp-relief/${row.id}/edit?mode=view`)
}

function openEdit(row: TempReliefListItem): void {
  void router.push(`/social-assistance/temp-relief/${row.id}/edit`)
}

async function refreshAll(): Promise<void> {
  await Promise.all([load(), loadSummary()])
}

async function handleConfirm(row: TempReliefListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确认要确认 [${row.applicationNo}] ${row.applicantName} 的临时救助申请生效吗？`,
      '确认生效',
      { confirmButtonText: '确认', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await confirmTempReliefApplication(row.id)
    ElMessage.success('临时救助申请已确认生效')
    await refreshAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '确认申请生效失败'))
  }
}

async function handleStop(row: TempReliefListItem): Promise<void> {
  try {
    const result = await ElMessageBox.prompt(
      `确定要终止 [${row.applicationNo}] ${row.applicantName} 的临时救助申请吗？终止后本年度仍占用唯一申请名额。`,
      '终止申请',
      {
        confirmButtonText: '终止',
        cancelButtonText: '取消',
        inputPlaceholder: '请填写终止原因',
        inputValidator: (value: string) =>
          Boolean(value && value.trim()) || '请填写终止原因',
        type: 'warning',
      },
    )
    await stopTempReliefApplication(row.id, result.value.trim())
    ElMessage.success('临时救助申请已终止')
    await refreshAll()
  } catch (error) {
    if (typeof error === 'string' && (error === 'cancel' || error === 'close')) return
    ElMessage.error(getErrorMessage(error, '终止申请失败'))
  }
}

async function handleComplete(row: TempReliefListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确认 [${row.applicationNo}] ${row.applicantName} 的临时救助已归档完成吗？`,
      '归档完成',
      { confirmButtonText: '归档', cancelButtonText: '取消', type: 'info' },
    )
  } catch {
    return
  }

  try {
    await completeTempReliefArchive(row.id)
    ElMessage.success('已登记归档完成')
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '归档失败'))
  }
}

async function handleDelete(row: TempReliefListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定要删除申请 [${row.applicationNo}] ${row.applicantName} 吗？\n此操作不可恢复。`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await deleteTempReliefApplication(row.id)
    ElMessage.success('临时救助申请已删除')
    await refreshAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除申请失败'))
  }
}

onMounted(() => {
  void load()
  void loadSummary()
})
</script>

<template>
  <div class="nc-page temp-relief-list-page">
    <NcPageHeader
      title="临时救助"
      description="急难型 / 支出型临时救助申请办理。申请人只能从导入台账与最低生活保障申请库检索选择。"
      kicker="低收入人口救助帮扶 · 临时救助"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="refreshAll()">刷新</el-button>
        <el-button type="primary" :icon="Plus" @click="handleCreate">新增申请</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat
        label="办理中（件）"
        :value="summary?.draftCount ?? '—'"
        tone="amber"
        compact
      />
      <NcStat
        label="已确认累计（件）"
        :value="summary?.confirmedCount ?? '—'"
        tone="pine"
        compact
      />
      <NcStat
        label="本年确认（人次）"
        :value="summary?.yearConfirmed ?? '—'"
        tone="seal"
        compact
      />
      <NcStat
        label="本年确认金额（元）"
        :value="summary?.yearConfirmedAmount ?? '—'"
        tone="ink"
        compact
      />
      <span v-if="summaryLoading" class="summary-loading">统计更新中…</span>
    </div>

    <NcCard padding="none" class="filter-card">
      <div class="status-tabs" role="tablist" aria-label="按状态筛选">
        <button
          v-for="tab in TABS"
          :key="tab.value || 'all'"
          type="button"
          role="tab"
          class="status-tab"
          :class="{ 'is-active': activeTab === tab.value }"
          :aria-selected="activeTab === tab.value"
          @click="switchTab(tab.value)"
        >
          <span>{{ tab.label }}</span>
          <span
            v-if="tabCount[tab.value] !== null"
            class="status-tab__count"
            :class="tab.value ? `status-tab__count--${tab.value.toLowerCase()}` : 'status-tab__count--all'"
          >
            {{ tabCount[tab.value] }}
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

    <NcCard title="临时救助列表" :subtitle="listSubtitle" padding="none" class="list-card">
      <div v-loading="loading && items.length > 0" class="list-area">
        <el-skeleton v-if="loading && items.length === 0" :rows="5" animated class="list-skeleton" />

        <template v-else-if="items.length > 0">
          <el-table v-if="!isNarrow" :data="items" row-key="id" class="relief-table">
            <el-table-column label="申请编号" min-width="132">
              <template #default="{ row }">
                <span class="app-no">{{ row.applicationNo || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="申请人" min-width="96">
              <template #default="{ row }">
                <strong class="applicant-name">{{ row.applicantName || '—' }}</strong>
              </template>
            </el-table-column>

            <el-table-column label="身份证（脱敏）" min-width="150">
              <template #default="{ row }">
                <span class="id-card">{{ row.applicantIdCardMasked || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="救助类型" width="112">
              <template #default="{ row }">{{ row.reliefTypeText || '—' }}</template>
            </el-table-column>

            <el-table-column label="家庭类别" min-width="132">
              <template #default="{ row }">{{ row.familyCategory || '—' }}</template>
            </el-table-column>

            <el-table-column label="乡镇 / 村" min-width="124">
              <template #default="{ row }">
                <span class="location" :title="locationText(row as TempReliefListItem)">
                  {{ locationText(row as TempReliefListItem) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="申请日期" width="108">
              <template #default="{ row }">{{ formatDate(row.applyDate) }}</template>
            </el-table-column>

            <el-table-column label="确定金额" width="106" align="right">
              <template #default="{ row }">
                <span class="amount">{{ formatMoney(row.confirmAmount) }}</span>
              </template>
            </el-table-column>

            <el-table-column label="状态" width="88">
              <template #default="{ row }">
                <span class="status" :class="statusClass(row as TempReliefListItem)">
                  {{ statusText(row as TempReliefListItem) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column v-if="activeTab === 'Stopped'" label="终止时间 / 原因" min-width="184">
              <template #default="{ row }">
                <div class="stop-cell">
                  <span>{{ formatDate(row.stoppedAt) }}</span>
                  <span class="stop-reason" :title="row.stopReason">{{ row.stopReason || '—' }}</span>
                </div>
              </template>
            </el-table-column>

            <el-table-column label="操作" width="260" fixed="right" align="right">
              <template #default="{ row }">
                <div class="row-actions">
                  <el-button link type="primary" @click="openView(row as TempReliefListItem)">
                    查看
                  </el-button>
                  <el-button
                    v-if="row.status === 'Draft'"
                    link
                    type="warning"
                    @click="openEdit(row as TempReliefListItem)"
                  >
                    编辑
                  </el-button>
                  <el-button
                    v-if="row.status === 'Draft'"
                    link
                    type="success"
                    @click="handleConfirm(row as TempReliefListItem)"
                  >
                    确认
                  </el-button>
                  <el-button
                    v-if="row.status === 'Confirmed'"
                    link
                    type="warning"
                    @click="handleStop(row as TempReliefListItem)"
                  >
                    终止
                  </el-button>
                  <el-button
                    v-if="row.status === 'Confirmed' || row.status === 'Stopped'"
                    link
                    type="primary"
                    @click="handleComplete(row as TempReliefListItem)"
                  >
                    归档
                  </el-button>
                  <el-button
                    v-if="row.status === 'Draft'"
                    link
                    type="danger"
                    @click="handleDelete(row as TempReliefListItem)"
                  >
                    删除
                  </el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="relief-cards">
            <article v-for="row in items" :key="row.id" class="relief-card">
              <header class="card-head">
                <div class="card-person">
                  <strong>{{ row.applicantName || '—' }}</strong>
                  <span class="status" :class="statusClass(row)">{{ statusText(row) }}</span>
                </div>
                <span class="app-no">{{ row.applicationNo || '—' }}</span>
              </header>

              <p class="id-card">{{ row.applicantIdCardMasked || '—' }}</p>

              <dl class="card-meta">
                <div>
                  <dt>救助类型</dt>
                  <dd>{{ row.reliefTypeText || '—' }}</dd>
                </div>
                <div>
                  <dt>家庭类别</dt>
                  <dd>{{ row.familyCategory || '—' }}</dd>
                </div>
                <div>
                  <dt>申请日期</dt>
                  <dd>{{ formatDate(row.applyDate) }}</dd>
                </div>
                <div>
                  <dt>确定金额</dt>
                  <dd>{{ formatMoney(row.confirmAmount) }}</dd>
                </div>
                <div class="card-meta__wide">
                  <dt>乡镇 / 村</dt>
                  <dd>{{ locationText(row) }}</dd>
                </div>
                <template v-if="row.status === 'Stopped'">
                  <div>
                    <dt>终止时间</dt>
                    <dd>{{ formatDate(row.stoppedAt) }}</dd>
                  </div>
                  <div>
                    <dt>终止原因</dt>
                    <dd :title="row.stopReason">{{ row.stopReason || '—' }}</dd>
                  </div>
                </template>
              </dl>

              <div class="card-actions">
                <el-button size="small" type="primary" plain @click="openView(row)">查看</el-button>
                <el-button
                  v-if="row.status === 'Draft'"
                  size="small"
                  type="warning"
                  @click="openEdit(row)"
                >
                  编辑
                </el-button>
                <el-button
                  v-if="row.status === 'Draft'"
                  size="small"
                  type="success"
                  @click="handleConfirm(row)"
                >
                  确认
                </el-button>
                <el-button
                  v-if="row.status === 'Confirmed'"
                  size="small"
                  type="warning"
                  @click="handleStop(row)"
                >
                  终止
                </el-button>
                <el-button
                  v-if="row.status === 'Confirmed' || row.status === 'Stopped'"
                  size="small"
                  type="primary"
                  @click="handleComplete(row)"
                >
                  归档
                </el-button>
                <el-button
                  v-if="row.status === 'Draft'"
                  size="small"
                  type="danger"
                  @click="handleDelete(row)"
                >
                  删除
                </el-button>
              </div>
            </article>
          </div>
        </template>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="emptyText">
            <el-button v-if="hasKeyword" :icon="Refresh" @click="handleReset">
              清除筛选
            </el-button>
          </el-empty>
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
.temp-relief-list-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.summary {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.summary-loading {
  grid-column: 1 / -1;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.filter-card,
.list-card {
  overflow: hidden;
}

.status-tabs {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
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

.status-tab__count--all {
  background-color: var(--nc-ink-2);
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
}

.list-skeleton {
  padding: 22px;
}

.relief-table {
  width: 100%;
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

.location,
.stop-reason {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.amount {
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  white-space: nowrap;
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

.status.is-draft {
  border-color: rgba(171, 50, 39, 0.3);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.status.is-confirmed {
  border-color: rgba(46, 71, 56, 0.35);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.status.is-stopped {
  border-color: var(--nc-line-strong);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
}

.stop-cell {
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: 2px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.stop-cell .stop-reason {
  color: var(--nc-muted);
}

.row-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 2px 6px;
}

.row-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.relief-cards {
  display: grid;
  gap: 10px;
  padding: 10px;
}

.relief-card {
  padding: 14px 15px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.card-person {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.card-person strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.card-head .app-no {
  flex: none;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 14px;
  margin: 10px 0 0;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
}

.card-meta div {
  min-width: 0;
}

.card-meta__wide {
  grid-column: 1 / -1;
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

.card-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px dashed var(--nc-line-faint);
}

.card-actions :deep(.el-button + .el-button) {
  margin-left: 0;
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

@media (max-width: 1100px) {
  .summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .status-tabs {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    padding: 12px;
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

  .card-head {
    flex-direction: column;
    align-items: stretch;
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
