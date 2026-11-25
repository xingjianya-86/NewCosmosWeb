<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Check, Delete, Edit, Plus, Printer, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  deleteRecoveryRecord,
  getRecoveryStats,
  listRecoveryRecords,
  searchStoppedPersons,
  updateRecoveryRecordStatus,
} from '@/api/recovery'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { RecoveryRecord, RecoveryStats, StoppedPersonDto } from '@/types/recovery'
import { recoverySourceTypeLabel, recoveryStatusLabel } from '@/types/recovery'
import {
  clearStoppedPerson,
  formatDateTime,
  formatMoney,
  sourceLabel,
  writeStoppedPerson,
} from './display'
import RecoveryPrintDialog from './RecoveryPrintDialog.vue'

type RecoveryTab = 'stopped' | 'records'

const route = useRoute()
const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 900px)')

const tab = ref<RecoveryTab>('stopped')
const keyword = ref('')
const searched = ref(false)

const stats = ref<RecoveryStats | null>(null)
const statsLoading = ref(false)

const stoppedRows = ref<StoppedPersonDto[]>([])
const stoppedLoading = ref(false)
const stoppedError = ref('')

const records = ref<RecoveryRecord[]>([])
const recordsLoading = ref(false)
const recordsError = ref('')

const printVisible = ref(false)
const printTarget = ref<RecoveryRecord | null>(null)

let stoppedTicket = 0
let recordsTicket = 0

const totalText = computed(() => formatCount(stats.value?.totalCount))
const ruralText = computed(() => formatCount(stats.value?.ruralSubsistenceCount))
const urbanText = computed(() => formatCount(stats.value?.urbanSubsistenceCount))
const rigidText = computed(() => formatCount(stats.value?.rigidExpenditureCount))

const stoppedEmptyText = computed(() =>
  searched.value ? '未找到匹配的停止人员' : '输入关键词搜索停止人员',
)

const printPersonName = computed(() => printTarget.value?.personName ?? '')
const printSourceDisplay = computed(() => {
  const record = printTarget.value
  if (!record) return ''
  return record.sourceTypeDisplay || recoverySourceTypeLabel(record.sourceType)
})

function formatCount(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return value.toLocaleString('zh-CN')
}

function stoppedRowKey(row: StoppedPersonDto): string {
  return `${row.sourceType}-${row.sourceId}`
}

function statusTagType(status: string): 'info' | 'success' | 'warning' {
  if (status === 'Printed') return 'success'
  if (status === 'Confirmed') return 'warning'
  return 'info'
}

function recordStatusText(record: RecoveryRecord): string {
  return record.statusDisplay || recoveryStatusLabel(record.status) || record.status || '—'
}

function recordSourceText(record: RecoveryRecord): string {
  return record.sourceTypeDisplay || recoverySourceTypeLabel(record.sourceType) || '—'
}

function recordAmount(record: RecoveryRecord): string {
  return formatMoney(record.recoveryAmount)
}

async function loadStats(): Promise<void> {
  statsLoading.value = true
  try {
    stats.value = await getRecoveryStats()
  } catch {
    // 统计块失败保持「—」，不阻断列表使用。
    stats.value = null
  } finally {
    statsLoading.value = false
  }
}

async function searchStopped(): Promise<void> {
  const search = keyword.value.trim()
  if (!search) {
    ElMessage.warning('请输入姓名或身份证号')
    return
  }

  const ticket = ++stoppedTicket
  stoppedLoading.value = true
  stoppedError.value = ''
  searched.value = true
  try {
    const rows = await searchStoppedPersons(search)
    if (ticket !== stoppedTicket) return
    stoppedRows.value = rows ?? []
    if (stoppedRows.value.length === 0) {
      ElMessage.info('未找到匹配的停止人员')
    }
  } catch (error) {
    if (ticket !== stoppedTicket) return
    stoppedRows.value = []
    stoppedError.value = getErrorMessage(error, '停止人员搜索失败')
  } finally {
    if (ticket === stoppedTicket) stoppedLoading.value = false
  }
}

async function loadRecords(): Promise<void> {
  const ticket = ++recordsTicket
  recordsLoading.value = true
  recordsError.value = ''
  try {
    const rows = await listRecoveryRecords(keyword.value)
    if (ticket !== recordsTicket) return
    records.value = rows
  } catch (error) {
    if (ticket !== recordsTicket) return
    records.value = []
    recordsError.value = getErrorMessage(error, '追缴记录加载失败')
  } finally {
    if (ticket === recordsTicket) recordsLoading.value = false
  }
}

function handleSearch(): void {
  if (tab.value === 'stopped') {
    void searchStopped()
    return
  }
  void loadRecords()
}

function handleKeywordClear(): void {
  if (tab.value === 'stopped') {
    stoppedRows.value = []
    searched.value = false
    stoppedError.value = ''
    return
  }
  void loadRecords()
}

function switchTab(next: RecoveryTab): void {
  if (tab.value === next) return
  tab.value = next
  if (next === 'records') {
    void loadRecords()
  } else if (!searched.value) {
    stoppedRows.value = []
  }
}

function goBack(): void {
  void router.push('/')
}

function openManual(): void {
  clearStoppedPerson()
  void router.push('/recovery/manual')
}

function selectPerson(person: StoppedPersonDto): void {
  writeStoppedPerson(person)
  void router.push('/recovery/form')
}

async function closeRecord(record: RecoveryRecord): Promise<void> {
  if (record.status !== 'Draft') {
    ElMessage.warning('仅办理中（草稿）的记录可结案')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确认对 ${record.personName} 的追缴记录（${formatMoney(record.recoveryAmount)}）结案？\n结案后状态转为「已确认」。`,
      '结案确认',
      { confirmButtonText: '确认结案', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await updateRecoveryRecordStatus(record.id, 'Confirmed')
    ElMessage.success('已结案（已确认）')
    await loadRecords()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '结案失败'))
  }
}

function editRecord(record: RecoveryRecord): void {
  if (record.status !== 'Draft') {
    ElMessage.warning('仅草稿状态的记录可编辑')
    return
  }
  const path = record.inputMode === 'Manual' ? '/recovery/manual' : '/recovery/form'
  void router.push({ path, query: { recordId: String(record.id) } })
}

async function deleteRecord(record: RecoveryRecord): Promise<void> {
  if (record.status !== 'Draft') {
    ElMessage.warning('仅草稿状态的记录可删除')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确认删除 ${record.personName} 的追缴草稿？此操作不可恢复。`,
      '删除确认',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await deleteRecoveryRecord(record.id)
    records.value = records.value.filter((item) => item.id !== record.id)
    ElMessage.success('草稿已删除')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除失败'))
  }
}

function reprint(record: RecoveryRecord): void {
  if (record.status === 'Draft') {
    ElMessage.warning('草稿状态暂不支持补打')
    return
  }
  printTarget.value = record
  printVisible.value = true
}

onMounted(() => {
  const rawTab = Array.isArray(route.query.tab) ? route.query.tab[0] : route.query.tab
  const rawKeyword = Array.isArray(route.query.keyword)
    ? route.query.keyword[0]
    : route.query.keyword
  if (typeof rawKeyword === 'string') keyword.value = rawKeyword
  if (rawTab === 'records') tab.value = 'records'

  void loadStats()
  if (tab.value === 'records') {
    void loadRecords()
  } else if (keyword.value.trim()) {
    void searchStopped()
  }
})
</script>

<template>
  <div class="nc-page recovery-list-page">
    <NcPageHeader title="后补追缴" description="停止人员追缴 · 手工录入">
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回工作台</el-button>
        <el-button type="primary" :icon="Plus" @click="openManual">手工录入</el-button>
      </template>
    </NcPageHeader>

    <div class="stats-grid">
      <NcStat label="停止人员（人）" :value="totalText" tone="ink" :class="{ 'is-loading': statsLoading }" />
      <NcStat label="农村低保（人）" :value="ruralText" tone="pine" :class="{ 'is-loading': statsLoading }" />
      <NcStat label="城市低保（人）" :value="urbanText" tone="seal" :class="{ 'is-loading': statsLoading }" />
      <NcStat label="刚性支出（人）" :value="rigidText" tone="amber" :class="{ 'is-loading': statsLoading }" />
    </div>

    <NcCard padding="none" class="filter-card">
      <div class="filter-body">
        <div class="tab-row" role="tablist" aria-label="后补追缴视图">
          <button
            type="button"
            class="tab-chip"
            :class="{ 'is-active': tab === 'stopped' }"
            role="tab"
            :aria-selected="tab === 'stopped'"
            @click="switchTab('stopped')"
          >
            停止人员
          </button>
          <button
            type="button"
            class="tab-chip"
            :class="{ 'is-active': tab === 'records' }"
            role="tab"
            :aria-selected="tab === 'records'"
            @click="switchTab('records')"
          >
            我的追缴记录
          </button>
        </div>

        <form class="search-row" @submit.prevent="handleSearch">
          <el-input
            v-model="keyword"
            class="search-input"
            clearable
            :prefix-icon="Search"
            :placeholder="
              tab === 'stopped'
                ? '输入姓名或身份证号搜索停止人员'
                : '输入姓名或身份证号搜索追缴记录'
            "
            @clear="handleKeywordClear"
          />
          <el-button type="primary" :icon="Search" native-type="submit" :loading="tab === 'stopped' ? stoppedLoading : recordsLoading">
            搜索
          </el-button>
        </form>
      </div>
    </NcCard>

    <!-- 停止人员 -->
    <template v-if="tab === 'stopped'">
      <el-alert
        v-if="stoppedError"
        class="load-error"
        type="error"
        :title="stoppedError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="searchStopped()">重新搜索</el-button>
      </el-alert>

      <NcCard padding="none" class="list-card">
        <div v-loading="stoppedLoading" class="list-area" :class="{ 'is-cards': isNarrow }">
          <el-skeleton v-if="stoppedLoading && stoppedRows.length === 0" :rows="5" animated />

          <template v-else-if="stoppedRows.length > 0">
            <el-table v-if="!isNarrow" :data="stoppedRows" :row-key="stoppedRowKey" class="recovery-table">
              <el-table-column label="序号" width="64">
                <template #default="{ $index }">{{ $index + 1 }}</template>
              </el-table-column>
              <el-table-column label="姓名" min-width="110">
                <template #default="{ row }">{{ row.personName || '—' }}</template>
              </el-table-column>
              <el-table-column label="身份证号" min-width="180">
                <template #default="{ row }">
                  <span class="mono">{{ row.idCard || '—' }}</span>
                </template>
              </el-table-column>
              <el-table-column label="来源类型" min-width="120">
                <template #default="{ row }">
                  <el-tag size="small" effect="plain">
                    {{ row.sourceDisplay || sourceLabel(row.sourceType) || '—' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="月保障额" min-width="130" align="right">
                <template #default="{ row }">{{ formatMoney(row.monthlyAmount) }}</template>
              </el-table-column>
              <el-table-column label="操作" width="100" align="right">
                <template #default="{ row }">
                  <el-button size="small" type="primary" @click="selectPerson(row as StoppedPersonDto)">
                    选择
                  </el-button>
                </template>
              </el-table-column>
            </el-table>

            <div v-else class="stopped-cards">
              <article v-for="(row, index) in stoppedRows" :key="`${row.sourceType}-${row.sourceId}`" class="stopped-card">
                <header class="card-head">
                  <div>
                    <strong>{{ row.personName || '—' }}</strong>
                    <span class="mono card-id">{{ row.idCard || '—' }}</span>
                  </div>
                  <span class="card-index">#{{ index + 1 }}</span>
                </header>
                <dl class="card-meta">
                  <div>
                    <dt>来源类型</dt>
                    <dd>{{ row.sourceDisplay || sourceLabel(row.sourceType) || '—' }}</dd>
                  </div>
                  <div>
                    <dt>月保障额</dt>
                    <dd>{{ formatMoney(row.monthlyAmount) }}</dd>
                  </div>
                </dl>
                <div class="card-actions">
                  <el-button size="small" type="primary" @click="selectPerson(row)">选择</el-button>
                </div>
              </article>
            </div>
          </template>

          <div v-else-if="!stoppedLoading" class="empty-wrap">
            <el-empty :description="stoppedEmptyText" :image-size="90" />
          </div>
        </div>

        <template v-if="stoppedRows.length > 0" #footer>
          <p class="list-note">选择人员后点击「选择」进入追缴表单。</p>
        </template>
      </NcCard>
    </template>

    <!-- 我的追缴记录 -->
    <template v-else>
      <el-alert
        v-if="recordsError"
        class="load-error"
        type="error"
        :title="recordsError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="loadRecords()">重新加载</el-button>
      </el-alert>

      <NcCard padding="none" class="list-card">
        <div v-loading="recordsLoading" class="list-area" :class="{ 'is-cards': isNarrow }">
          <el-skeleton v-if="recordsLoading && records.length === 0" :rows="5" animated />

          <template v-else-if="records.length > 0">
            <el-table v-if="!isNarrow" :data="records" row-key="id" class="recovery-table">
              <el-table-column label="姓名" min-width="110">
                <template #default="{ row }">{{ row.personName || '—' }}</template>
              </el-table-column>
              <el-table-column label="追缴分类" min-width="120">
                <template #default="{ row }">
                  <el-tag size="small" effect="plain">{{ recordSourceText(row as RecoveryRecord) }}</el-tag>
                </template>
              </el-table-column>
              <el-table-column label="应追缴总额" min-width="140" align="right">
                <template #default="{ row }">
                  <strong class="amount">{{ recordAmount(row as RecoveryRecord) }}</strong>
                </template>
              </el-table-column>
              <el-table-column label="状态" min-width="110">
                <template #default="{ row }">
                  <el-tag size="small" :type="statusTagType(row.status)" effect="plain">
                    {{ recordStatusText(row as RecoveryRecord) }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="创建时间" min-width="150">
                <template #default="{ row }">
                  <span class="time-text">{{ formatDateTime(row.createdAt) }}</span>
                </template>
              </el-table-column>
              <el-table-column label="操作" min-width="230" align="right">
                <template #default="{ row }">
                  <div class="row-actions">
                    <el-button
                      v-if="row.status === 'Draft'"
                      size="small"
                      type="success"
                      :icon="Check"
                      @click="closeRecord(row as RecoveryRecord)"
                    >
                      结案
                    </el-button>
                    <el-button
                      v-if="row.status === 'Draft'"
                      size="small"
                      type="primary"
                      :icon="Edit"
                      @click="editRecord(row as RecoveryRecord)"
                    >
                      编辑
                    </el-button>
                    <el-button
                      v-if="row.status === 'Draft'"
                      size="small"
                      type="danger"
                      :icon="Delete"
                      @click="deleteRecord(row as RecoveryRecord)"
                    >
                      删除
                    </el-button>
                    <el-button
                      v-if="row.status !== 'Draft'"
                      size="small"
                      :icon="Printer"
                      @click="reprint(row as RecoveryRecord)"
                    >
                      补打
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>

            <div v-else class="record-cards">
              <article v-for="record in records" :key="record.id" class="record-card">
                <header class="card-head">
                  <div>
                    <strong>{{ record.personName || '—' }}</strong>
                    <span class="card-id">{{ recordSourceText(record) }}</span>
                  </div>
                  <el-tag size="small" :type="statusTagType(record.status)" effect="plain">
                    {{ recordStatusText(record) }}
                  </el-tag>
                </header>
                <dl class="card-meta">
                  <div>
                    <dt>应追缴总额</dt>
                    <dd class="amount">{{ recordAmount(record) }}</dd>
                  </div>
                  <div>
                    <dt>创建时间</dt>
                    <dd>{{ formatDateTime(record.createdAt) }}</dd>
                  </div>
                </dl>
                <div class="card-actions">
                  <template v-if="record.status === 'Draft'">
                    <el-button size="small" type="success" @click="closeRecord(record)">结案</el-button>
                    <el-button size="small" type="primary" @click="editRecord(record)">编辑</el-button>
                    <el-button size="small" type="danger" @click="deleteRecord(record)">删除</el-button>
                  </template>
                  <el-button v-else size="small" :icon="Printer" @click="reprint(record)">补打</el-button>
                </div>
              </article>
            </div>
          </template>

          <div v-else-if="!recordsLoading" class="empty-wrap">
            <el-empty description="暂无追缴记录" :image-size="90" />
          </div>
        </div>

        <template v-if="records.length > 0" #footer>
          <p class="list-note">草稿可编辑/删除，已确认/已打印记录可补打。</p>
        </template>
      </NcCard>
    </template>

    <RecoveryPrintDialog
      v-model="printVisible"
      :record-id="printTarget?.id ?? null"
      :person-name="printPersonName"
      :source-display="printSourceDisplay"
      action="reprint"
    />
  </div>
</template>

<style scoped>
.recovery-list-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.stats-grid :deep(.nc-stat.is-loading .nc-stat__value) {
  opacity: 0.55;
}

.filter-card {
  overflow: hidden;
}

.filter-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 14px 16px;
}

.tab-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.tab-chip {
  padding: 6px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-pill);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-base);
  font-weight: 600;
  line-height: 1.5;
  cursor: pointer;
  transition:
    background-color var(--nc-transition),
    border-color var(--nc-transition),
    color var(--nc-transition);
}

.tab-chip:hover {
  border-color: var(--nc-line-strong);
  color: var(--nc-ink-2);
}

.tab-chip.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.search-row {
  display: flex;
  gap: 10px;
}

.search-input {
  flex: 1 1 auto;
  min-width: 0;
}

.load-error {
  margin: 0;
}

.list-card {
  overflow: hidden;
}

.list-area {
  padding: 0 16px 16px;
}

.recovery-table {
  width: 100%;
}

.recovery-table :deep(.el-table__header th) {
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.08em;
}

.recovery-table :deep(.el-table__row td) {
  vertical-align: middle;
}

.mono {
  font-family: var(--nc-font-mono);
}

.amount {
  font-family: var(--nc-font-mono);
  color: var(--nc-seal-dark);
}

.time-text {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.row-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 6px;
}

.row-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.list-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.empty-wrap {
  padding: 26px 0;
}

/* ── 窄屏卡片 ─────────────────────────────────────────────────── */

.stopped-cards,
.record-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 12px;
}

.stopped-card,
.record-card {
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
  min-width: 0;
}

.card-head strong {
  font-size: var(--nc-text-md);
}

.card-id,
.card-index {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  overflow-wrap: anywhere;
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
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}

.card-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

@media (max-width: 1100px) {
  .stats-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 700px) {
  .stats-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .search-row {
    flex-direction: column;
  }

  .search-row :deep(.el-button) {
    width: 100%;
    margin-left: 0;
  }

  .card-meta {
    grid-template-columns: minmax(0, 1fr);
  }

  .card-actions :deep(.el-button) {
    flex: 1 1 auto;
  }
}
</style>
