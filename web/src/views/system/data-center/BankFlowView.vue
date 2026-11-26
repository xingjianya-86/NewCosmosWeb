<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Download, Refresh, Search } from '@element-plus/icons-vue'
import {
  exportBankFlow,
  listBankFlowImports,
  listBankFlowRecords,
} from '@/api/bankFlow'
import type { BankFlowImportBatch, BankFlowRecord } from '@/api/bankFlow'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { saveBlob } from '@/views/photos/photoMeta'

interface FlowBadge {
  text: string
  tone: 'danger' | 'warn'
}

const EMPTY_TEXT = '暂无核对数据，请在『数据中心 → 数据导入』选择『银行流水核对导入』导入核对表'

const isNarrow = useMediaQuery('(max-width: 1100px)')

const batches = ref<BankFlowImportBatch[]>([])
const selectedImportId = ref(0)
const keyword = ref('')
const exceedOnly = ref(false)

const page = ref(1)
const pageSize = ref(20)
const items = ref<BankFlowRecord[]>([])
const totalCount = ref(0)
const totalPages = ref(0)

const loading = ref(false)
const importsLoading = ref(false)
const exporting = ref(false)
const loadError = ref('')
const batchError = ref('')

let requestTicket = 0

const hasFilters = computed(
  () => keyword.value.trim().length > 0 || selectedImportId.value > 0 || exceedOnly.value,
)
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)
const currentExceedCount = computed(() => items.value.filter((item) => item.isExceed).length)
const currentInvalidCount = computed(() => items.value.filter((item) => !item.idCardValid).length)

function batchLabel(batch: BankFlowImportBatch): string {
  const name = batch.batchNo || `批次 #${batch.id}`
  if (!batch.sourceFileName || batch.sourceFileName === batch.batchNo) return name
  return `${name} · ${batch.sourceFileName}`
}

function amountText(display: string, value: number): string {
  const text = (display ?? '').trim()
  if (text) return text
  return value.toLocaleString('zh-CN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}

function rowIndex(row: BankFlowRecord, index: number): number {
  return row.rowNo ?? (page.value - 1) * pageSize.value + index + 1
}

function isMachineryExceed(row: BankFlowRecord): boolean {
  return row.machineryPower !== null && row.machineryPower > 60
}

function badges(row: BankFlowRecord): FlowBadge[] {
  const list: FlowBadge[] = []
  if (row.isExceed) list.push({ text: '超30月', tone: 'danger' })
  if (!row.idCardValid) list.push({ text: '证存疑', tone: 'warn' })
  if (isMachineryExceed(row)) list.push({ text: '农机超60', tone: 'danger' })
  return list
}

async function loadImports(): Promise<void> {
  importsLoading.value = true
  batchError.value = ''
  try {
    const list = await listBankFlowImports()
    batches.value = list
    if (
      selectedImportId.value > 0 &&
      !list.some((batch) => batch.id === selectedImportId.value)
    ) {
      selectedImportId.value = 0
    }
  } catch (error) {
    batches.value = []
    batchError.value = getErrorMessage(error, '批次列表加载失败')
  } finally {
    importsLoading.value = false
  }
}

async function loadRecords(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''
  try {
    const response = await listBankFlowRecords({
      keyword: keyword.value.trim() || undefined,
      importId: selectedImportId.value > 0 ? selectedImportId.value : null,
      exceedOnly: exceedOnly.value,
      page: page.value,
      pageSize: pageSize.value,
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
    loadError.value = getErrorMessage(error, '核对数据加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

async function refreshAll(): Promise<void> {
  await loadImports()
  await loadRecords()
}

function handleFilterChange(): void {
  page.value = 1
  void loadRecords()
}

function handleSearch(): void {
  page.value = 1
  void loadRecords()
}

function handleReset(): void {
  keyword.value = ''
  selectedImportId.value = 0
  exceedOnly.value = false
  page.value = 1
  void loadRecords()
}

function handlePageChange(value: number): void {
  page.value = value
  void loadRecords()
}

function handleSizeChange(value: number): void {
  pageSize.value = value
  page.value = 1
  void loadRecords()
}

function pad2(value: number): string {
  return String(value).padStart(2, '0')
}

function exportFileName(): string {
  const now = new Date()
  return `银行流水核对_${now.getFullYear()}${pad2(now.getMonth() + 1)}${pad2(now.getDate())}${pad2(now.getHours())}${pad2(now.getMinutes())}.xlsx`
}

async function handleExport(): Promise<void> {
  exporting.value = true
  try {
    const blob = await exportBankFlow({
      keyword: keyword.value.trim() || undefined,
      importId: selectedImportId.value > 0 ? selectedImportId.value : null,
      exceedOnly: exceedOnly.value,
    })
    saveBlob(blob, exportFileName())
    ElMessage.success('导出完成')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '导出失败'))
  } finally {
    exporting.value = false
  }
}

onMounted(() => {
  void refreshAll()
})
</script>

<template>
  <div class="nc-page flow-page">
    <NcPageHeader
      title="银行流水核对"
      description="按批次查看核对结果，超阈值与证件存疑单独标记"
      kicker="系统 · 数据中心"
    >
      <template #actions>
        <el-button :icon="Download" :loading="exporting" @click="handleExport">
          导出 Excel
        </el-button>
        <el-button
          :icon="Refresh"
          :loading="loading || importsLoading"
          @click="refreshAll"
        >
          刷新
        </el-button>
      </template>
    </NcPageHeader>

    <NcCard padding="none" class="filter-card">
      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-select
          v-model="selectedImportId"
          class="filter-batch"
          placeholder="全部批次"
          filterable
          :loading="importsLoading"
          @change="handleFilterChange"
        >
          <el-option label="全部批次" :value="0" />
          <el-option
            v-for="batch in batches"
            :key="batch.id"
            :label="batchLabel(batch)"
            :value="batch.id"
          />
        </el-select>

        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="姓名 / 身份证 / 家庭地址"
          @clear="handleSearch"
        />

        <label class="switch-field">
          <el-switch v-model="exceedOnly" @change="handleFilterChange" />
          <span>仅超阈值</span>
        </label>

        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            查询
          </el-button>
          <el-button
            text
            native-type="button"
            :disabled="!hasFilters"
            @click="handleReset"
          >
            重置
          </el-button>
        </div>
      </form>

      <p v-if="batchError" class="filter-error">{{ batchError }}</p>
    </NcCard>

    <NcCard
      title="核对结果"
      :subtitle="`统计按当前页结果计算，共 ${totalCount} 人`"
      padding="none"
      class="flow-list-card"
    >
      <div class="summary-bar">
        <span class="summary-item">
          共 <strong>{{ totalCount }}</strong> 人
          <small v-if="items.length > 0 && totalCount > items.length">
            （本页 {{ items.length }} 人）
          </small>
        </span>
        <span class="summary-item summary-item--danger">
          超阈值 <strong>{{ currentExceedCount }}</strong> 人
        </span>
        <span class="summary-item summary-item--warn">
          证件存疑 <strong>{{ currentInvalidCount }}</strong> 人
        </span>
      </div>

      <el-alert
        v-if="loadError"
        class="load-error"
        type="error"
        :title="loadError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="loadRecords()">重新加载</el-button>
      </el-alert>

      <div v-loading="loading && items.length > 0" class="list-area">
        <el-skeleton v-if="loading && items.length === 0" :rows="6" animated class="list-skeleton" />

        <el-table
          v-else-if="items.length > 0 && !isNarrow"
          :data="items"
          row-key="id"
          size="small"
          class="flow-table"
        >
          <el-table-column label="序号" width="64" align="center">
            <template #default="{ row, $index }">
              {{ rowIndex(row as BankFlowRecord, $index) }}
            </template>
          </el-table-column>
          <el-table-column prop="personName" label="姓名" width="96" show-overflow-tooltip />
          <el-table-column label="身份证号" min-width="178">
            <template #default="{ row }">
              <span class="id-card">{{ row.idCard || '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column prop="relationship" label="关系" width="86" show-overflow-tooltip />
          <el-table-column label="合计余额(元)" width="128" align="right">
            <template #default="{ row }">
              <span class="flow-amount">
                {{ amountText(row.totalBalanceDisplay, row.totalBalance) }}
              </span>
            </template>
          </el-table-column>
          <el-table-column label="12月最高流水" width="136" align="right">
            <template #default="{ row }">
              <span class="flow-amount" :class="{ 'is-exceed': row.isExceed }">
                {{ amountText(row.maxFlowAmountDisplay, row.maxFlowAmount) }}
              </span>
            </template>
          </el-table-column>
          <el-table-column label="各银行(余额/最高流水)" min-width="240" show-overflow-tooltip>
            <template #default="{ row }">
              <span class="bank-summary">{{ row.bankSummary || '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column label="标记" width="200">
            <template #default="{ row }">
              <div class="flow-badges">
                <span
                  v-for="badge in badges(row as BankFlowRecord)"
                  :key="badge.text"
                  class="flow-badge"
                  :class="`is-${badge.tone}`"
                >
                  {{ badge.text }}
                </span>
                <span v-if="badges(row as BankFlowRecord).length === 0" class="flow-badges__none">
                  —
                </span>
              </div>
            </template>
          </el-table-column>
        </el-table>

        <div v-else-if="items.length > 0" class="flow-cards">
          <article v-for="(row, index) in items" :key="row.id" class="flow-card">
            <header class="flow-card__head">
              <div class="flow-card__person">
                <strong>{{ row.personName || '—' }}</strong>
                <span class="flow-card__no">序号 {{ rowIndex(row, index) }}</span>
              </div>
              <div class="flow-badges">
                <span
                  v-for="badge in badges(row)"
                  :key="badge.text"
                  class="flow-badge"
                  :class="`is-${badge.tone}`"
                >
                  {{ badge.text }}
                </span>
                <span v-if="badges(row).length === 0" class="flow-badges__none">无标记</span>
              </div>
            </header>

            <p class="flow-card__id">{{ row.idCard || '—' }}</p>

            <dl class="flow-card__meta">
              <div>
                <dt>关系</dt>
                <dd>{{ row.relationship || '—' }}</dd>
              </div>
              <div>
                <dt>合计余额(元)</dt>
                <dd class="flow-amount">
                  {{ amountText(row.totalBalanceDisplay, row.totalBalance) }}
                </dd>
              </div>
              <div>
                <dt>12月最高流水</dt>
                <dd class="flow-amount" :class="{ 'is-exceed': row.isExceed }">
                  {{ amountText(row.maxFlowAmountDisplay, row.maxFlowAmount) }}
                </dd>
              </div>
            </dl>

            <div class="flow-card__banks">
              <span>各银行(余额/最高流水)</span>
              <p>{{ row.bankSummary || '—' }}</p>
            </div>
          </article>
        </div>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="EMPTY_TEXT" :image-size="88">
            <el-button v-if="hasFilters" :icon="Refresh" @click="handleReset">
              清除筛选
            </el-button>
          </el-empty>
        </div>
      </div>

      <template #footer>
        <div v-if="totalCount > 0" class="pagination">
          <span class="pagination-note">
            第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 人
          </span>
          <el-pagination
            :current-page="page"
            :page-size="pageSize"
            :page-sizes="[20, 50, 100]"
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
.flow-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.filter-card {
  overflow: hidden;
}

.filter-bar {
  display: grid;
  grid-template-columns: 260px minmax(220px, 1fr) auto auto;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
}

.filter-bar :deep(.el-select),
.filter-bar :deep(.el-input) {
  width: 100%;
}

.switch-field {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.filter-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.filter-error {
  margin: 0;
  padding: 0 16px 12px;
  color: #b42318;
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.flow-list-card {
  overflow: hidden;
}

.flow-list-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.summary-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px 24px;
  padding: 11px 18px;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-soft);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.summary-item strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-md);
}

.summary-item small {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.summary-item--danger strong {
  color: var(--nc-seal-dark);
}

.summary-item--warn strong {
  color: var(--nc-amber-dark);
}

.load-error {
  margin: 12px 16px 0;
}

.list-area {
  min-height: 220px;
}

.list-skeleton {
  padding: 22px;
}

.flow-table {
  width: 100%;
}

.id-card {
  font-family: var(--nc-font-mono);
  font-size: 13px;
  letter-spacing: 0.01em;
  white-space: nowrap;
}

.flow-amount {
  font-family: var(--nc-font-mono);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.flow-amount.is-exceed {
  color: var(--nc-seal);
  font-weight: 700;
}

.bank-summary {
  color: var(--nc-muted-2);
  line-height: 1.6;
}

.flow-badges {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
}

.flow-badge {
  display: inline-flex;
  align-items: center;
  padding: 0 6px;
  border: 1px solid transparent;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 600;
  line-height: 18px;
  white-space: nowrap;
}

.flow-badge.is-danger {
  border-color: rgba(171, 50, 39, 0.24);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.flow-badge.is-warn {
  border-color: rgba(150, 104, 28, 0.26);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.flow-badges__none {
  color: var(--nc-disabled);
  font-size: var(--nc-text-xs);
}

.flow-cards {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px;
}

.flow-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 13px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
}

.flow-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.flow-card__person {
  display: flex;
  min-width: 0;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px 10px;
}

.flow-card__person strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 17px;
  letter-spacing: 0.03em;
}

.flow-card__no {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.flow-card__id {
  margin: 0;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: 14px;
  letter-spacing: 0.02em;
  word-break: break-all;
}

.flow-card__meta {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px 12px;
  margin: 0;
}

.flow-card__meta dt {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 11px;
  letter-spacing: 0.03em;
}

.flow-card__meta dd {
  margin: 2px 0 0;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: 13px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.flow-card__meta dd.flow-amount {
  font-size: 14px;
}

.flow-card__banks {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding-top: 7px;
  border-top: 1px dashed var(--nc-line-faint);
}

.flow-card__banks span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 11px;
  letter-spacing: 0.03em;
}

.flow-card__banks p {
  margin: 0;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
  word-break: break-all;
}

.empty-wrap {
  padding: 12px 8px;
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

@media (max-width: 1280px) {
  .filter-bar {
    grid-template-columns: 230px minmax(200px, 1fr) auto;
  }

  .filter-actions {
    grid-column: 1 / -1;
  }
}

@media (max-width: 767px) {
  .filter-bar {
    grid-template-columns: 1fr;
    padding: 12px;
  }

  .switch-field {
    padding: 2px 0;
  }

  .filter-actions {
    grid-column: auto;
  }

  .summary-bar {
    gap: 8px 16px;
    padding: 11px 14px;
  }

  .flow-card__meta {
    grid-template-columns: 1fr 1fr;
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
