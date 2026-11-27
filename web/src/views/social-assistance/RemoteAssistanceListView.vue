<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Delete,
  Edit,
  Plus,
  Printer,
  Search,
  View,
} from '@element-plus/icons-vue'
import { deleteApplication } from '@/api/applicationForm'
import { getErrorMessage } from '@/api/http'
import { completeRemoteAssistance, listRemoteAssistance } from '@/api/remoteAssistance'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type { ApplicationListItem } from '@/types/application'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const LIST_PATH = '/social-assistance/remote-assistance'

const keyword = ref('')
const status = ref('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<ApplicationListItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')
let requestTicket = 0

const hasFilters = computed(() => Boolean(keyword.value.trim() || status.value))
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取异地协助记录…'
  return `共 ${totalCount.value} 条记录`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的记录' : '暂无异地协助记录',
)

function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return String(value)
  const pad = (part: number): string => String(part).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(
    date.getHours(),
  )}:${pad(date.getMinutes())}`
}

function statusText(row: any): string {
  if (row.status === 'RemoteAssistanceCompleted') return '异地协助已办结'
  if (row.status === 'RemoteAssistanceDraft') return '异地协助办理中'
  return row.statusText || row.status || '—'
}

function statusTone(row: any): string {
  if (row.status === 'RemoteAssistanceCompleted') return 'pine'
  return 'ink'
}

function isDraft(row: any): boolean {
  return row.status === 'RemoteAssistanceDraft'
}

function isCompleted(row: any): boolean {
  return row.status === 'RemoteAssistanceCompleted'
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listRemoteAssistance({
      keyword: keyword.value.trim() || undefined,
      status: status.value || undefined,
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
    loadError.value = getErrorMessage(error, '异地协助列表加载失败')
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

function goBack(): void {
  void router.push('/social-assistance')
}

function createEntry(): void {
  void router.push(`${LIST_PATH}/new`)
}

function editEntry(row: any): void {
  void router.push(`${LIST_PATH}/${row.id}/edit`)
}

function outputDocs(row: any): void {
  void router.push({
    path: '/archives/output',
    query: {
      businessType: 'FamilyApplication',
      businessId: String(row.id),
      docTypes: '入户调查,邻里访问',
    },
  })
}

async function completeEntry(row: any): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定办结「${row.applicantName}」的异地协助吗？办结后不可再编辑。`,
      '确认办结',
      { type: 'warning', confirmButtonText: '确认办结', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  try {
    await completeRemoteAssistance(row.id)
    ElMessage.success('异地协助已办结')
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '办结失败'))
  }
}

async function removeEntry(row: any): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定删除「${row.applicantName}」${row.applicationNo ? `（${row.applicationNo}）` : ''}的异地协助记录吗？此操作不可恢复。`,
      '确认删除',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }

  try {
    await deleteApplication(row.id)
    ElMessage.success('记录已删除')
    if (items.value.length === 1 && page.value > 1) {
      page.value -= 1
    }
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除失败'))
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page remote-assistance-page">
    <NcPageHeader
      title="异地协助"
      kicker="Remote Assistance"
      description="入户调查与邻里走访，本地留存，不纳入月报统计。"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回工作台</el-button>
        <el-button type="primary" :icon="Plus" @click="createEntry">新增协助</el-button>
      </template>
    </NcPageHeader>

    <NcCard padding="none" class="filter-card">
      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索姓名、身份证号"
          @clear="handleSearch"
        />
        <el-select
          v-model="status"
          class="filter-status"
          clearable
          placeholder="全部状态"
          @change="handleSearch"
        >
          <el-option label="办理中" value="RemoteAssistanceDraft" />
          <el-option label="已办结" value="RemoteAssistanceCompleted" />
        </el-select>
        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            检 索
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

    <NcCard title="异地协助记录" :subtitle="listSubtitle" padding="none" class="list-card">
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
            class="remote-assistance-table"
          >
            <el-table-column label="姓名" min-width="110">
              <template #default="{ row }">{{ row.applicantName || '—' }}</template>
            </el-table-column>
            <el-table-column label="身份证号" min-width="170">
              <template #default="{ row }">
                <span class="mono">{{ row.applicantIdCardMasked || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="申请编号" min-width="140">
              <template #default="{ row }">{{ row.applicationNo || '—' }}</template>
            </el-table-column>
            <el-table-column label="状态" width="130">
              <template #default="{ row }">
                <span class="status-tag" :class="`tone-${statusTone(row)}`">
                  {{ statusText(row) }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="标记" width="96">
              <template #default="{ row }">
                <span v-if="row.remoteMarkerText" class="status-tag tone-amber">
                  {{ row.remoteMarkerText }}
                </span>
                <span v-else class="muted">—</span>
              </template>
            </el-table-column>
            <el-table-column label="更新时间" width="150">
              <template #default="{ row }">{{ formatDateTime(row.updatedAt) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="280" fixed="right">
              <template #default="{ row }">
                <el-button
                  v-if="isDraft(row)"
                  size="small"
                  type="primary"
                  :icon="Edit"
                  @click="editEntry(row)"
                >
                  编辑
                </el-button>
                <el-button
                  v-if="isDraft(row)"
                  size="small"
                  :icon="Printer"
                  @click="outputDocs(row)"
                >
                  出文书
                </el-button>
                <el-button
                  v-if="isDraft(row)"
                  size="small"
                  type="success"
                  :icon="View"
                  @click="completeEntry(row)"
                >
                  办结
                </el-button>
                <el-button
                  v-if="isDraft(row)"
                  size="small"
                  type="danger"
                  :icon="Delete"
                  @click="removeEntry(row)"
                >
                  删除
                </el-button>
                <el-button
                  v-if="isCompleted(row)"
                  size="small"
                  :icon="Printer"
                  @click="outputDocs(row)"
                >
                  出文书
                </el-button>
              </template>
            </el-table-column>
          </el-table>

          <!-- 移动端卡片 -->
          <div v-else class="remote-assistance-cards">
            <article v-for="row in items" :key="row.id" class="remote-card">
              <header class="remote-card__head">
                <strong>{{ row.applicantName || '—' }}</strong>
                <span class="status-tag" :class="`tone-${statusTone(row)}`">
                  {{ statusText(row) }}
                </span>
              </header>
              <p class="remote-card__id">{{ row.applicantIdCardMasked || '—' }}</p>
              <p v-if="row.remoteMarkerText" class="remote-card__marker">
                <span class="status-tag tone-amber">{{ row.remoteMarkerText }}</span>
              </p>
              <p class="remote-card__no">编号：{{ row.applicationNo || '—' }}</p>
              <div class="remote-card__actions">
                <el-button
                  v-if="isDraft(row)"
                  size="small"
                  type="primary"
                  :icon="Edit"
                  @click="editEntry(row)"
                >
                  编辑
                </el-button>
                <el-button
                  v-if="isDraft(row)"
                  size="small"
                  :icon="Printer"
                  @click="outputDocs(row)"
                >
                  出文书
                </el-button>
                <el-button
                  v-if="isDraft(row)"
                  size="small"
                  type="success"
                  :icon="View"
                  @click="completeEntry(row)"
                >
                  办结
                </el-button>
                <el-button
                  v-if="isDraft(row)"
                  size="small"
                  type="danger"
                  :icon="Delete"
                  @click="removeEntry(row)"
                >
                  删除
                </el-button>
                <el-button
                  v-if="isCompleted(row)"
                  size="small"
                  :icon="Printer"
                  @click="outputDocs(row)"
                >
                  出文书
                </el-button>
              </div>
            </article>
          </div>
        </template>

        <el-empty v-else-if="!loading && !loadError" :description="emptyDescription" />
      </div>
    </NcCard>

    <footer v-if="totalCount > 0" class="list-footer">
      <span class="footer-note">第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 条</span>
      <el-pagination
        :current-page="page"
        :page-size="pageSize"
        :page-sizes="[10, 20, 50, 100]"
        :total="totalCount"
        :layout="paginationLayout"
        background
        @current-change="handlePageChange"
        @size-change="handleSizeChange"
      />
    </footer>
  </div>
</template>

<style scoped>
.remote-assistance-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 100%;
}

/* 列表卡填满页面剩余高度，避免下方出现大片空白。 */
.remote-assistance-page .list-card {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.remote-assistance-page .list-card :deep(.nc-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.filter-card {
  padding: 14px 18px;
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.filter-search {
  width: 240px;
}

.filter-status {
  width: 160px;
}

.filter-actions {
  display: flex;
  gap: 8px;
}

.load-error {
  margin: 0;
}

.list-card :deep(.nc-card__body) {
  padding: 0;
}

.list-area {
  min-height: 120px;
  height: 100%;
}

.list-skeleton {
  padding: 16px;
}

.remote-assistance-table {
  width: 100%;
}

.remote-assistance-table :deep(.el-table__header th) {
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-weight: 600;
}

.status-tag {
  display: inline-flex;
  align-items: center;
  padding: 1px 8px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  white-space: nowrap;
}

.status-tag.tone-pine {
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.status-tag.tone-ink {
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
}

.status-tag.tone-amber {
  background-color: var(--nc-amber-soft, #f6ead0);
  color: var(--nc-amber, #9a6b00);
  border-color: var(--nc-amber-line, #e3c98d);
}

.remote-card__marker {
  margin: 4px 0 0;
}

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.remote-assistance-cards {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 14px;
}

.remote-card {
  padding: 14px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.remote-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 6px;
}

.remote-card__head strong {
  color: var(--nc-ink);
  font-size: 15px;
}

.remote-card__id {
  margin: 0 0 4px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.remote-card__no {
  margin: 0 0 10px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.remote-card__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.list-footer {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.footer-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}
</style>
