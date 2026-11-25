<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { Refresh, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  getSocialAssistanceSummary,
  listGracePeriodMembers,
  listGracePeriods,
} from '@/api/socialAssistance'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { GracePeriodListItem, GracePeriodMember } from '@/types/socialAssistance'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const keyword = ref('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<GracePeriodListItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const listLoaded = ref(false)
const loading = ref(false)
const loadError = ref('')

const activeCount = ref<number | null>(null)
const statsError = ref('')

const selectedApplicationId = ref<number | null>(null)
const members = ref<GracePeriodMember[]>([])
const membersLoading = ref(false)
const membersError = ref('')
const memberPanelRef = ref<HTMLElement | null>(null)

let requestTicket = 0
let memberTicket = 0

const hasFilters = computed(() => keyword.value.trim().length > 0)
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

/** 页头统计与旧版一致：进行中/已到期取列表 TotalCount，在享保障对象取 summary.activeCount。 */
const expiringStat = computed(() =>
  listLoaded.value ? formatCount(totalCount.value) : '—',
)
const activeStat = computed(() =>
  activeCount.value === null ? '—' : formatCount(activeCount.value),
)

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取渐退期记录…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `点击行查看家庭成员明细 · 共 ${totalCount.value} 户${suffix}`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的渐退期记录' : '暂无进行中或已到期的渐退期档案',
)

const selectedRow = computed(
  () =>
    items.value.find((item) => item.applicationId === selectedApplicationId.value) ?? null,
)

function formatCount(value: number): string {
  return value.toLocaleString('zh-CN')
}

function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—'
  return value.toLocaleString('zh-CN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1
}

function graceMonthsText(row: GracePeriodListItem): string {
  return row.gracePeriodMonths === null || row.gracePeriodMonths === undefined
    ? '—'
    : `${row.gracePeriodMonths} 个月`
}

function ageText(value: number | null): string {
  return value === null || value === undefined ? '—' : `${value} 岁`
}

function rowClassName({ row }: { row: GracePeriodListItem }): string {
  return row.applicationId === selectedApplicationId.value ? 'is-selected-row' : ''
}

function isSelected(row: GracePeriodListItem): boolean {
  return row.applicationId === selectedApplicationId.value
}

function clearSelection(): void {
  memberTicket += 1
  selectedApplicationId.value = null
  members.value = []
  membersLoading.value = false
  membersError.value = ''
}

function selectRow(row: GracePeriodListItem): void {
  if (selectedApplicationId.value === row.applicationId) return
  selectedApplicationId.value = row.applicationId
  void loadMembers(row.applicationId)
  // 手机端成员明细在卡片列表下方，滚动到面板避免用户误以为没有响应。
  if (isNarrow.value) {
    void nextTick(() => {
      memberPanelRef.value?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
    })
  }
}

/** 成员明细：失败显式提示，不吞成空表。 */
async function loadMembers(applicationId: number): Promise<void> {
  const ticket = ++memberTicket
  members.value = []
  membersError.value = ''
  membersLoading.value = true

  try {
    const response = await listGracePeriodMembers(applicationId)
    if (ticket !== memberTicket || selectedApplicationId.value !== applicationId) return
    members.value = response.items ?? []
  } catch (error) {
    if (ticket !== memberTicket || selectedApplicationId.value !== applicationId) return
    members.value = []
    membersError.value = getErrorMessage(error, '家庭成员明细加载失败')
  } finally {
    if (ticket === memberTicket) membersLoading.value = false
  }
}

async function loadSummary(): Promise<void> {
  statsError.value = ''
  try {
    const summary = await getSocialAssistanceSummary()
    activeCount.value = summary.activeCount
  } catch (error) {
    activeCount.value = null
    statsError.value = getErrorMessage(error, '统计加载失败')
  }
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  clearSelection()
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
    listLoaded.value = true
  } catch (error) {
    if (ticket !== requestTicket) return
    items.value = []
    totalCount.value = 0
    totalPages.value = 0
    listLoaded.value = false
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

function refreshAll(): void {
  void load()
  void loadSummary()
}

/**
 * 重新核算：旧版跳转 ApplicationFormPage 的 Review 模式。
 * Web 端表单目前只允许草稿编辑，这里按约定打开申请档案编辑页并带 mode=review，
 * 非草稿状态由表单页按只读「查看」处理——页面文案已明示，不伪造写操作。
 */
function openReview(row: GracePeriodListItem): void {
  if (!row.applicationId) return
  void router.push({
    path: `/applications/${row.applicationId}/edit`,
    query: { mode: 'review' },
  })
}

onMounted(() => {
  void load()
  void loadSummary()
})
</script>

<template>
  <div class="nc-page grace-page">
    <NcPageHeader
      title="渐退期管理"
      description="进行中与已到期的渐退户统一管理，期满请及时复核处置。"
      kicker="低收入人口救助帮扶 · 渐退期"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="refreshAll">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="进行中/已到期（户）" :value="expiringStat" tone="ink" compact />
      <NcStat label="在享保障对象（户）" :value="activeStat" tone="pine" compact />
    </div>
    <p v-if="statsError" class="stats-note">{{ statsError }}</p>

    <NcCard padding="none" class="filter-card">
      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索申请人姓名、身份证号"
          @clear="handleSearch"
        />
        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            搜索
          </el-button>
          <el-button v-if="hasFilters" text native-type="button" @click="handleReset">
            重置
          </el-button>
        </div>
      </form>
    </NcCard>

    <el-alert
      class="review-note"
      type="info"
      :closable="false"
      show-icon
      title="重新核算将以「申请档案编辑页（经济复核）」打开；非草稿状态为只读，需在档案中完成复核与提交。"
    />

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
            :row-class-name="rowClassName"
            @row-click="selectRow"
          >
            <el-table-column label="序号" width="58" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>

            <el-table-column label="申请编号" min-width="124">
              <template #default="{ row }">
                <span class="app-no">{{ row.applicationNo || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="申请人" min-width="88">
              <template #default="{ row }">
                <strong class="applicant-name">{{ row.applicantName || '—' }}</strong>
              </template>
            </el-table-column>

            <el-table-column label="身份证号" min-width="158">
              <template #default="{ row }">
                <span class="id-card">{{ row.applicantIdCardMasked || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="渐退期" width="92">
              <template #default="{ row }">{{ graceMonthsText(row as GracePeriodListItem) }}</template>
            </el-table-column>

            <el-table-column label="状态" width="80">
              <template #default="{ row }">
                <span class="status" :class="row.status === 'Active' ? 'is-active' : 'is-expired'">
                  {{ row.statusText }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="时间长度" width="104">
              <template #default="{ row }">
                <span class="days" :class="{ 'is-expired': row.status !== 'Active' }">
                  {{ row.durationText || '—' }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="原分类" min-width="92">
              <template #default="{ row }">{{ row.originalClassification || '—' }}</template>
            </el-table-column>

            <el-table-column label="操作" width="102" align="right">
              <template #default="{ row }">
                <el-button
                  size="small"
                  type="primary"
                  @click.stop="openReview(row as GracePeriodListItem)"
                >
                  重新核算
                </el-button>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="grace-cards">
            <article
              v-for="(row, index) in items"
              :key="row.applicationId"
              class="grace-card"
              :class="{ 'is-selected': isSelected(row) }"
              role="button"
              tabindex="0"
              @click="selectRow(row)"
              @keydown.enter="selectRow(row)"
              @keydown.space.prevent="selectRow(row)"
            >
              <header class="card-head">
                <span class="app-no">序号 {{ rowIndex(index) }} · {{ row.applicationNo || '—' }}</span>
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
                  <span>渐退期</span>
                  <strong>{{ graceMonthsText(row) }}</strong>
                </div>
                <div>
                  <span>时间长度</span>
                  <strong class="days" :class="{ 'is-expired': row.status !== 'Active' }">
                    {{ row.durationText || '—' }}
                  </strong>
                </div>
                <div>
                  <span>原分类</span>
                  <strong>{{ row.originalClassification || '—' }}</strong>
                </div>
              </div>

              <footer class="card-foot">
                <span class="card-select-hint">
                  {{ isSelected(row) ? '已选中，明细见下方' : '点击卡片查看家庭成员' }}
                </span>
                <el-button size="small" type="primary" @click.stop="openReview(row)">
                  重新核算
                </el-button>
              </footer>
            </article>
          </div>
        </template>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="emptyDescription">
            <el-button v-if="hasFilters" :icon="Refresh" @click="handleReset">清除筛选</el-button>
          </el-empty>
        </div>

        <section v-if="selectedRow" ref="memberPanelRef" class="member-panel">
          <header class="member-head">
            <div class="member-heading">
              <h3>家庭成员名单</h3>
              <p>{{ selectedRow.applicantName || '—' }} · {{ selectedRow.applicationNo || '—' }}</p>
            </div>
            <el-button link type="primary" @click="clearSelection">收起</el-button>
          </header>

          <el-alert
            v-if="membersError"
            class="member-error"
            type="error"
            :title="membersError"
            :closable="false"
            show-icon
          />

          <div v-loading="membersLoading" class="member-body">
            <el-table
              v-if="!isNarrow"
              :data="members"
              row-key="id"
              size="small"
              class="member-table"
              max-height="240"
              empty-text="该户无成员明细"
            >
              <el-table-column label="姓名" min-width="100">
                <template #default="{ row }">
                  <strong class="applicant-name">{{ row.name || '—' }}</strong>
                </template>
              </el-table-column>
              <el-table-column label="关系" min-width="96">
                <template #default="{ row }">{{ row.relationshipText || '—' }}</template>
              </el-table-column>
              <el-table-column label="年龄" width="72">
                <template #default="{ row }">{{ ageText(row.age) }}</template>
              </el-table-column>
              <el-table-column label="身份证号" min-width="168">
                <template #default="{ row }">
                  <span class="id-card">{{ row.idCard || '—' }}</span>
                </template>
              </el-table-column>
              <el-table-column label="年收入（元）" min-width="118" align="right">
                <template #default="{ row }">
                  <span class="money">{{ formatMoney(row.annualIncome) }}</span>
                </template>
              </el-table-column>
            </el-table>

            <ul v-else-if="members.length > 0" class="member-list">
              <li v-for="member in members" :key="member.id" class="member-item">
                <div class="member-item-head">
                  <strong>{{ member.name || '—' }}</strong>
                  <span class="member-relation">{{ member.relationshipText || '—' }}</span>
                </div>
                <div class="member-item-line">
                  <span class="member-key">年龄</span>
                  <span>{{ ageText(member.age) }}</span>
                </div>
                <div class="member-item-line">
                  <span class="member-key">身份证号</span>
                  <span class="id-card">{{ member.idCard || '—' }}</span>
                </div>
                <div class="member-item-line">
                  <span class="member-key">年收入（元）</span>
                  <span class="money">{{ formatMoney(member.annualIncome) }}</span>
                </div>
              </li>
            </ul>

            <p v-else-if="!membersLoading && !membersError" class="member-empty">该户无成员明细</p>
          </div>
        </section>
      </div>

      <template #footer>
        <div v-if="items.length > 0" class="pagination">
          <span class="pagination-note">
            第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 户
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
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 10px;
}

.stats-note {
  margin: -6px 0 0;
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-xs);
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

.review-note,
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

.grace-table :deep(.is-selected-row > td.el-table__cell) {
  background-color: var(--nc-paper-3);
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

.days {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.days.is-expired {
  color: var(--nc-seal);
}

.money {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
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

.grace-card.is-selected {
  border-color: var(--nc-seal);
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

.card-head .app-no {
  overflow: hidden;
  text-overflow: ellipsis;
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

.card-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
}

.card-select-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.member-panel {
  padding: 14px 16px 16px;
  border-top: 1px solid var(--nc-line);
  background-color: var(--nc-paper-soft);
}

.member-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.member-heading h3 {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.member-heading p {
  margin-top: 4px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.member-error {
  margin-top: 10px;
}

.member-body {
  min-height: 64px;
  margin-top: 10px;
}

.member-table {
  width: 100%;
}

.member-list {
  display: grid;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.member-item {
  padding: 10px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
}

.member-item-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
  padding-bottom: 7px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.member-item-head strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.member-relation {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
}

.member-item-line {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
  margin-top: 6px;
  font-size: var(--nc-text-sm);
}

.member-key {
  flex: none;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.member-empty {
  padding: 16px 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
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

  .member-panel {
    padding: 12px;
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

@media (max-width: 400px) {
  .summary {
    grid-template-columns: 1fr;
  }

  .card-meta-grid {
    grid-template-columns: 1fr;
  }

  .member-item-line {
    flex-wrap: wrap;
  }
}
</style>
